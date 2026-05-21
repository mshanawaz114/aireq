// InboundMessageProcessorTests — the correlation + threading core of the Gmail
// inbound feature, exercised on EF InMemory with no network.
//
// Covers: sender->Match correlation via the apply EmailLog, thread create +
// append, idempotent dedupe by Gmail message id, and the no-correlation skip.
//
// Refs: AIRMVP1-401

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.Inbound;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Inbound;

public sealed class InboundMessageProcessorTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static InboundMessageProcessor Build(AireqDbContext db) =>
        new(db, NullLogger<InboundMessageProcessor>.Instance);

    private const string Recruiter = "recruiter@bigco.test";

    /// <summary>Seed a tenant + match + the apply EmailLog that ties the recruiter
    /// address to that match. Returns (tenantId, matchId).</summary>
    private static async Task<(Guid TenantId, Guid MatchId)> SeedAppliedAsync(AireqDbContext db)
    {
        var tenant = new Tenant { Name = "Acme" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var job = new Job
        {
            Source = "greenhouse", SourceExternalId = "G1", Title = "Eng", Company = "bigco",
            IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
        };
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Score = 90 };
        db.AddRange(tenant, consultant, job, match);
        db.EmailLogs.Add(new EmailLog
        {
            TenantId = tenant.Id, ToAddress = Recruiter, Subject = "Application — Alice",
            Purpose = "apply", Status = "sent", CorrelationMatchId = match.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();
        return (tenant.Id, match.Id);
    }

    private static GmailAccount Account(Guid tenantId) => new()
    {
        TenantId = tenantId, EmailAddress = "owner@acme.test", RefreshToken = "rt",
    };

    private static InboundEmail Reply(string id, string from = Recruiter, string? name = "Rita Recruiter") =>
        new(ProviderMessageId: id, ThreadId: "t1", FromEmail: from, FromName: name,
            Subject: "Re: Application — Alice", Body: "Thanks, let's chat.", ReceivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Correlates_reply_to_match_and_creates_thread_with_inbound_message()
    {
        var dbName = $"inbound-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (tenantId, matchId) = await SeedAppliedAsync(db);

        var threaded = await Build(db).ProcessAsync(Account(tenantId), new[] { Reply("m1") }, CancellationToken.None);

        threaded.Should().Be(1);
        var thread = await db.RecruiterThreads.IgnoreQueryFilters().SingleAsync();
        thread.MatchId.Should().Be(matchId);
        thread.RecruiterEmail.Should().Be(Recruiter);
        thread.RecruiterName.Should().Be("Rita Recruiter");
        thread.LastInboundAt.Should().NotBeNull();

        var msg = await db.Messages.IgnoreQueryFilters().SingleAsync();
        msg.Direction.Should().Be(MessageDirection.Inbound);
        msg.ProviderMessageId.Should().Be("m1");
        msg.GeneratedByAi.Should().BeFalse();
        msg.ThreadId.Should().Be(thread.Id);
    }

    [Fact]
    public async Task Second_reply_appends_to_the_same_thread()
    {
        var dbName = $"inbound-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (tenantId, _) = await SeedAppliedAsync(db);
        var proc = Build(db);

        await proc.ProcessAsync(Account(tenantId), new[] { Reply("m1") }, CancellationToken.None);
        var threaded2 = await proc.ProcessAsync(Account(tenantId), new[] { Reply("m2") }, CancellationToken.None);

        threaded2.Should().Be(1);
        (await db.RecruiterThreads.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.Messages.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Reprocessing_same_message_id_is_idempotent()
    {
        var dbName = $"inbound-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (tenantId, _) = await SeedAppliedAsync(db);
        var proc = Build(db);

        await proc.ProcessAsync(Account(tenantId), new[] { Reply("m1") }, CancellationToken.None);
        var again = await proc.ProcessAsync(Account(tenantId), new[] { Reply("m1") }, CancellationToken.None);

        again.Should().Be(0, "the same Gmail message id must not be threaded twice");
        (await db.Messages.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reply_from_unknown_sender_is_ignored()
    {
        var dbName = $"inbound-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (tenantId, _) = await SeedAppliedAsync(db);

        var threaded = await Build(db).ProcessAsync(
            Account(tenantId), new[] { Reply("m1", from: "stranger@spam.test", name: null) }, CancellationToken.None);

        threaded.Should().Be(0);
        (await db.RecruiterThreads.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.Messages.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Correlation_is_scoped_to_the_account_tenant()
    {
        var dbName = $"inbound-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedAppliedAsync(db); // tenant A applied to this recruiter

        // A different tenant's mailbox receives a reply from the same address —
        // it must NOT thread against tenant A's match.
        var otherTenant = Guid.NewGuid();
        var threaded = await Build(db).ProcessAsync(
            Account(otherTenant), new[] { Reply("m1") }, CancellationToken.None);

        threaded.Should().Be(0);
        (await db.RecruiterThreads.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }
}
