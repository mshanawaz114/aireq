// FollowUpPlannerTests — eligibility + rate limiting for auto follow-up nudges.
// EF InMemory + FakeLlmGateway (returns a draft JSON), no network.
//
// Refs: AIRMVP1-404

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Matching; // FakeLlmGateway
using Aireq.Worker.FollowUps;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.FollowUps;

public sealed class FollowUpPlannerTests
{
    private const string DraftJson = "{\"subject\":\"Following up on my application\",\"body\":\"Hi, just checking in — still very interested. Thanks, Alice.\"}";

    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static FollowUpPlanner Build(AireqDbContext db, bool autoSend = false) =>
        new(db, new FakeLlmGateway(DraftJson),
            Options.Create(new FollowUpOptions { FirstNudgeAfterDays = 3, GapDays = 3, MaxFollowUps = 2, AutoSend = autoSend }),
            NullLogger<FollowUpPlanner>.Instance);

    private const string Recruiter = "recruiter@bigco.test";

    /// <summary>Seed a tenant+match with an apply email logged `appliedDaysAgo` ago.</summary>
    private static async Task<Guid> SeedAppliedAsync(
        AireqDbContext db, int appliedDaysAgo = 5, MatchStatus status = MatchStatus.Submitted)
    {
        var tenant = new Tenant { Name = "Acme" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice Architect" };
        var job = new Job { Source = "gh", SourceExternalId = "G1", Title = "Eng", Company = "bigco", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Status = status };
        db.AddRange(tenant, consultant, job, match);
        db.EmailLogs.Add(new EmailLog
        {
            TenantId = tenant.Id, ToAddress = Recruiter, Subject = "Application", Purpose = "apply",
            Status = "sent", CorrelationMatchId = match.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-appliedDaysAgo),
        });
        await db.SaveChangesAsync();
        return match.Id;
    }

    [Fact]
    public async Task Plans_a_pending_nudge_with_a_notification()
    {
        var dbName = $"fup-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var matchId = await SeedAppliedAsync(db);

        var n = await Build(db).RunAsync(CancellationToken.None);

        n.Should().Be(1);
        var f = await db.FollowUps.IgnoreQueryFilters().SingleAsync();
        f.MatchId.Should().Be(matchId);
        f.Recipient.Should().Be(Recruiter);
        f.Sequence.Should().Be(1);
        f.Status.Should().Be(FollowUpStatus.Pending);
        (await db.Notifications.IgnoreQueryFilters().CountAsync(x => x.Type == "followup")).Should().Be(1);
    }

    [Fact]
    public async Task Auto_send_creates_an_approved_nudge_without_notification()
    {
        var dbName = $"fup-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedAppliedAsync(db);

        await Build(db, autoSend: true).RunAsync(CancellationToken.None);

        var f = await db.FollowUps.IgnoreQueryFilters().SingleAsync();
        f.Status.Should().Be(FollowUpStatus.Approved);
        f.ApprovedAt.Should().NotBeNull();
        (await db.Notifications.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Too_recent_application_is_not_nudged()
    {
        var dbName = $"fup-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedAppliedAsync(db, appliedDaysAgo: 1); // < FirstNudgeAfterDays (3)

        (await Build(db).RunAsync(CancellationToken.None)).Should().Be(0);
        (await db.FollowUps.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Match_with_a_reply_is_not_nudged()
    {
        var dbName = $"fup-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var matchId = await SeedAppliedAsync(db);
        db.RecruiterThreads.Add(new RecruiterThread
        {
            MatchId = matchId, RecruiterEmail = Recruiter, LastInboundAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        (await Build(db).RunAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Max_followups_cap_is_respected()
    {
        var dbName = $"fup-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var matchId = await SeedAppliedAsync(db);
        for (var i = 1; i <= 2; i++)
            db.FollowUps.Add(new FollowUp
            {
                TenantId = (await db.Matches.IgnoreQueryFilters().SingleAsync()).TenantId,
                MatchId = matchId, Recipient = Recruiter, DraftSubject = "s", DraftBody = "b",
                Sequence = i, Status = FollowUpStatus.Sent, SentAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        await db.SaveChangesAsync();

        (await Build(db).RunAsync(CancellationToken.None)).Should().Be(0, "already at MaxFollowUps");
    }

    [Fact]
    public async Task Existing_open_draft_blocks_a_duplicate()
    {
        var dbName = $"fup-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var matchId = await SeedAppliedAsync(db);
        db.FollowUps.Add(new FollowUp
        {
            TenantId = (await db.Matches.IgnoreQueryFilters().SingleAsync()).TenantId,
            MatchId = matchId, Recipient = Recruiter, DraftSubject = "s", DraftBody = "b",
            Sequence = 1, Status = FollowUpStatus.Pending,
        });
        await db.SaveChangesAsync();

        (await Build(db).RunAsync(CancellationToken.None)).Should().Be(0);
        (await db.FollowUps.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }
}
