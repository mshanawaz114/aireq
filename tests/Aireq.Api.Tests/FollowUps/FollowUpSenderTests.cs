// FollowUpSenderTests — Approved -> Sent through the (fake) email sender,
// reply-race cancellation, and throttle/failure handling.
//
// Refs: AIRMVP1-404

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.FollowUps;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.FollowUps;

public sealed class FollowUpSenderTests
{
    private const string Recruiter = "recruiter@bigco.test";

    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static FollowUpSender Build(AireqDbContext db, FakeEmailSender email) =>
        new(db, email, Options.Create(new FollowUpOptions { SendBatchSize = 50, SendLive = false }),
            NullLogger<FollowUpSender>.Instance);

    private static async Task<(Guid TenantId, Guid MatchId, Guid FollowUpId)> SeedApprovedAsync(AireqDbContext db)
    {
        var tenant = new Tenant { Name = "Acme" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var job = new Job { Source = "gh", SourceExternalId = "G1", Title = "Eng", Company = "co", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Status = MatchStatus.Submitted };
        var f = new FollowUp
        {
            TenantId = tenant.Id, MatchId = match.Id, Recipient = Recruiter,
            DraftSubject = "Following up", DraftBody = "Just checking in.", Sequence = 1,
            Status = FollowUpStatus.Approved, ApprovedAt = DateTimeOffset.UtcNow,
        };
        db.AddRange(tenant, consultant, job, match, f);
        await db.SaveChangesAsync();
        return (tenant.Id, match.Id, f.Id);
    }

    [Fact]
    public async Task Sends_approved_followup_and_marks_sent()
    {
        var dbName = $"fups-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (_, matchId, _) = await SeedApprovedAsync(db);
        var email = new FakeEmailSender(); // dry_run

        var sent = await Build(db, email).RunAsync(CancellationToken.None);

        sent.Should().Be(1);
        var f = await db.FollowUps.IgnoreQueryFilters().SingleAsync();
        f.Status.Should().Be(FollowUpStatus.Sent);
        f.SentAt.Should().NotBeNull();
        email.Sent.Should().ContainSingle();
        email.Sent[0].Message.Purpose.Should().Be("followup");
        email.Sent[0].Message.CorrelationMatchId.Should().Be(matchId);
    }

    [Fact]
    public async Task Cancels_when_a_reply_arrived_before_send()
    {
        var dbName = $"fups-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (_, matchId, _) = await SeedApprovedAsync(db);
        db.RecruiterThreads.Add(new RecruiterThread
        {
            MatchId = matchId, RecruiterEmail = Recruiter, LastInboundAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var email = new FakeEmailSender();

        var sent = await Build(db, email).RunAsync(CancellationToken.None);

        sent.Should().Be(0);
        (await db.FollowUps.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(FollowUpStatus.Cancelled);
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Throttled_send_marks_failed()
    {
        var dbName = $"fups-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedApprovedAsync(db);
        var email = new FakeEmailSender(status: "throttled");

        var sent = await Build(db, email).RunAsync(CancellationToken.None);

        sent.Should().Be(0);
        var f = await db.FollowUps.IgnoreQueryFilters().SingleAsync();
        f.Status.Should().Be(FollowUpStatus.Failed);
        f.FailureReason.Should().Be("throttled");
    }
}
