// RecruiterReplyE2ETests — the W4 chaos/integration test. Stitches the Week-4
// recruiter-CRM services together end-to-end on one shared InMemory db, the way
// PipelineE2ETests does for the W3 discover->apply loop:
//
//   inbound reply (correlate + thread)  ->  classify (sentiment/intent)  ->
//   advance match + raise escalation + raise notification
//   and, in parallel, a quiet application  ->  follow-up planned.
//
// This is the automated half of AIRMVP1-407's MVP bug-bash; the live, Gmail/LLM
// half is the manual runbook (docs/RUNBOOK-uat.md).
//
// Refs: AIRMVP1-407

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Matching; // FakeLlmGateway
using Aireq.Worker.FollowUps;
using Aireq.Worker.Inbound;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.E2E;

public sealed class RecruiterReplyE2ETests
{
    private const string Recruiter = "rita@bigco.test";

    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private const string ClassifyJson =
        "{\"sentiment\":\"positive\",\"intent\":\"interview_request\",\"requiresHuman\":true,\"summary\":\"Wants to schedule a call.\"}";
    private const string DraftJson =
        "{\"subject\":\"Following up\",\"body\":\"Hi, still very interested — thanks! Alice\"}";

    [Fact]
    public async Task Full_w4_loop_reply_classify_escalate_notify_and_followup()
    {
        var dbName = $"w4e2e-{Guid.NewGuid()}";

        // ---- Seed: tenant + owner + two applied matches --------------------
        // A: will receive a reply.  B: stays quiet (follow-up candidate).
        Guid tenantId, matchA, matchB;
        await using (var seed = NewDb(dbName))
        {
            var tenant = new Tenant { Name = "Acme Staffing" };
            var owner = new User { TenantId = tenant.Id, Email = "owner@acme.test", PasswordHash = "x", Role = "owner" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice Architect" };
            var jobA = new Job { Source = "greenhouse", SourceExternalId = "A1", Title = "Salesforce Architect", Company = "bigco", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
            var jobB = new Job { Source = "lever", SourceExternalId = "B1", Title = "Platform Engineer", Company = "smallco", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
            var mA = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = jobA.Id, Status = MatchStatus.Submitted, Score = 90 };
            var mB = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = jobB.Id, Status = MatchStatus.Submitted, Score = 85 };
            seed.AddRange(tenant, owner, consultant, jobA, jobB, mA, mB);
            // Apply emails (the correlation source) — both sent 5 days ago.
            seed.EmailLogs.AddRange(
                new EmailLog { TenantId = tenant.Id, ToAddress = Recruiter, Subject = "Application — Alice", Purpose = "apply", Status = "sent", CorrelationMatchId = mA.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) },
                new EmailLog { TenantId = tenant.Id, ToAddress = "talent@smallco.test", Subject = "Application — Alice", Purpose = "apply", Status = "sent", CorrelationMatchId = mB.Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) });
            await seed.SaveChangesAsync();
            tenantId = tenant.Id; matchA = mA.Id; matchB = mB.Id;
        }

        var account = new GmailAccount { TenantId = tenantId, EmailAddress = "owner@acme.test", RefreshToken = "rt" };

        // ---- 1. Inbound reply from A's recruiter -> threaded ----------------
        await using (var db = NewDb(dbName))
        {
            var reply = new InboundEmail("m1", "t1", Recruiter, "Rita", "Re: Application — Alice",
                "Loved your background — can we talk Tuesday?", DateTimeOffset.UtcNow);
            var processor = new InboundMessageProcessor(db, NullLogger<InboundMessageProcessor>.Instance);
            (await processor.ProcessAsync(account, new[] { reply }, CancellationToken.None)).Should().Be(1);

            var thread = await db.RecruiterThreads.IgnoreQueryFilters().SingleAsync();
            thread.MatchId.Should().Be(matchA);
            thread.LastInboundAt.Should().NotBeNull();
        }

        // ---- 2. Classify the reply -> escalate + notify + advance match -----
        await using (var db = NewDb(dbName))
        {
            var classifier = new ReplyClassifier(db, new FakeLlmGateway(ClassifyJson),
                Options.Create(new ReplyClassificationOptions { BatchSize = 25 }),
                NullLogger<ReplyClassifier>.Instance);
            (await classifier.RunAsync(CancellationToken.None)).Should().Be(1);

            (await db.Matches.IgnoreQueryFilters().SingleAsync(m => m.Id == matchA)).Status
                .Should().Be(MatchStatus.Interview);
            (await db.Escalations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
            (await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.Type == "escalation")).Should().Be(1);
        }

        // ---- 3. Follow-up planning -> nudge B (quiet), skip A (replied) -----
        await using (var db = NewDb(dbName))
        {
            var planner = new FollowUpPlanner(db, new FakeLlmGateway(DraftJson),
                Options.Create(new FollowUpOptions { FirstNudgeAfterDays = 3, MaxFollowUps = 2 }),
                NullLogger<FollowUpPlanner>.Instance);
            (await planner.RunAsync(CancellationToken.None)).Should().Be(1, "only the quiet application B is nudged");

            var followUp = await db.FollowUps.IgnoreQueryFilters().SingleAsync();
            followUp.MatchId.Should().Be(matchB);
            followUp.Status.Should().Be(FollowUpStatus.Pending);
        }

        // ---- 4. Send the approved follow-up (after owner approval) ----------
        await using (var db = NewDb(dbName))
        {
            var followUp = await db.FollowUps.IgnoreQueryFilters().SingleAsync();
            followUp.Status = FollowUpStatus.Approved;
            followUp.ApprovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var email = new FakeEmailSender(); // dry_run
            var sender = new FollowUpSender(db, email,
                Options.Create(new FollowUpOptions { SendBatchSize = 50, SendLive = false }),
                NullLogger<FollowUpSender>.Instance);
            (await sender.RunAsync(CancellationToken.None)).Should().Be(1);

            (await db.FollowUps.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(FollowUpStatus.Sent);
            email.Sent.Should().ContainSingle();
            email.Sent[0].Message.Purpose.Should().Be("followup");
            email.Sent[0].Message.CorrelationMatchId.Should().Be(matchB);
        }
    }
}
