// DigestServiceTests — per-tenant activity counting, zero-activity skip, and
// recipient/owner selection. Email is captured via FakeEmailSender (dry-run).
//
// Refs: AIRMVP1-403

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Notifications;

public sealed class DigestServiceTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static DigestService Build(AireqDbContext db, FakeEmailSender email) =>
        new(db, email, Options.Create(new DigestOptions { LookbackHours = 24, SendLive = false }),
            NullLogger<DigestService>.Instance);

    [Fact]
    public async Task Sends_one_digest_per_tenant_with_activity_to_the_owner()
    {
        var dbName = $"digest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);

        var tenant = new Tenant { Name = "Acme" };
        db.Add(tenant);
        db.Add(new User { TenantId = tenant.Id, Email = "viewer@acme.test", PasswordHash = "x", Role = "viewer" });
        db.Add(new User { TenantId = tenant.Id, Email = "owner@acme.test", PasswordHash = "x", Role = "owner" });
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var job = new Job { Source = "gh", SourceExternalId = "G1", Title = "Eng", Company = "co", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
        db.AddRange(consultant, job);
        db.Add(new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        var sent = await Build(db, email).RunAsync(CancellationToken.None);

        sent.Should().Be(1);
        email.Sent.Should().ContainSingle();
        email.Sent[0].Message.To.Should().Be("owner@acme.test");
        email.Sent[0].Message.Purpose.Should().Be("digest");
        email.Sent[0].Live.Should().BeFalse("SendLive defaults off");
    }

    [Fact]
    public async Task Tenant_with_no_activity_is_skipped()
    {
        var dbName = $"digest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);

        var tenant = new Tenant { Name = "Quiet" };
        db.Add(tenant);
        db.Add(new User { TenantId = tenant.Id, Email = "owner@quiet.test", PasswordHash = "x", Role = "owner" });
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        var sent = await Build(db, email).RunAsync(CancellationToken.None);

        sent.Should().Be(0);
        email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_escalation_alone_warrants_a_digest()
    {
        var dbName = $"digest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);

        var tenant = new Tenant { Name = "Acme" };
        db.Add(tenant);
        db.Add(new User { TenantId = tenant.Id, Email = "owner@acme.test", PasswordHash = "x", Role = "owner" });
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var job = new Job { Source = "gh", SourceExternalId = "G1", Title = "Eng", Company = "co", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
        // Match created well outside the lookback window (so it isn't "new").
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) };
        db.AddRange(consultant, job, match);
        db.Add(new Escalation { MatchId = match.Id, Reason = "interview_request", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) });
        await db.SaveChangesAsync();

        var sent = await Build(db, new FakeEmailSender()).RunAsync(CancellationToken.None);
        sent.Should().Be(1);
    }
}
