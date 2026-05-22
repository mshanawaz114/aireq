// DevSeedEndpoints — DEV-ONLY demo data so the recruiter-CRM screens (Inbox,
// Escalations, Follow-ups, Notifications) can be seen populated without waiting
// on a real Gmail reply.
//
// Mapped ONLY when app.Environment.IsDevelopment() (see Program.cs), and scoped
// to the caller's tenant. Idempotent: re-running won't duplicate (it keys off a
// well-known demo recruiter address).
//
// Refs: AIRMVP1-408 (dev tooling)

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Dev;

public static class DevSeedEndpoints
{
    private const string DemoRecruiter = "demo.recruiter@example.com";
    private const string DemoRecruiter2 = "talent@democo.example.com";

    public static IEndpointRouteBuilder MapDevSeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/seed-crm", SeedAsync)
            .RequireAuthorization()
            .WithTags("dev")
            .WithSummary("DEV ONLY — seed demo recruiter-CRM data for the current tenant.");

        return app;
    }

    private static async Task<IResult> SeedAsync(AireqDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        if (tenant.TenantId is not { } tenantId) return Results.Unauthorized();

        // Idempotent: bail if the demo thread already exists for this tenant.
        var alreadySeeded = await db.RecruiterThreads.IgnoreQueryFilters()
            .AnyAsync(t => t.RecruiterEmail == DemoRecruiter
                        && db.Matches.IgnoreQueryFilters().Any(m => m.Id == t.MatchId && m.TenantId == tenantId), ct);
        if (alreadySeeded)
            return Results.Ok(new { seeded = false, message = "Demo data already present." });

        var now = DateTimeOffset.UtcNow;

        // A consultant to hang the matches off (reuse the tenant's first, else make one).
        var consultantId = await db.Consultants.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).Select(c => c.Id).FirstOrDefaultAsync(ct);
        if (consultantId == Guid.Empty)
        {
            var consultant = new Consultant { TenantId = tenantId, FullName = "Demo Candidate", Location = "Remote" };
            db.Consultants.Add(consultant);
            consultantId = consultant.Id;
        }

        // Two demo jobs + matches.
        var jobA = new Job { Source = "greenhouse", SourceExternalId = $"DEMO-A-{Guid.NewGuid():N}", Title = "Senior Salesforce Architect", Company = "DemoCorp", Location = "Remote", IsActive = true, LastSeenAt = now };
        var jobB = new Job { Source = "lever", SourceExternalId = $"DEMO-B-{Guid.NewGuid():N}", Title = "Platform Engineer", Company = "DemoCo", Location = "Remote", IsActive = true, LastSeenAt = now };
        var matchA = new Match { TenantId = tenantId, ConsultantId = consultantId, JobId = jobA.Id, Score = 91, Status = MatchStatus.Interview };
        var matchB = new Match { TenantId = tenantId, ConsultantId = consultantId, JobId = jobB.Id, Score = 84, Status = MatchStatus.Submitted };
        db.AddRange(jobA, jobB, matchA, matchB);

        // Apply emails (correlation source).
        db.EmailLogs.AddRange(
            new EmailLog { TenantId = tenantId, ToAddress = DemoRecruiter, Subject = "Application — Demo Candidate", Purpose = "apply", Status = "sent", CorrelationMatchId = matchA.Id, CreatedAt = now.AddDays(-4) },
            new EmailLog { TenantId = tenantId, ToAddress = DemoRecruiter2, Subject = "Application — Demo Candidate", Purpose = "apply", Status = "sent", CorrelationMatchId = matchB.Id, CreatedAt = now.AddDays(-5) });

        // Thread A: an interview-request reply (classified), with messages.
        var thread = new RecruiterThread
        {
            MatchId = matchA.Id, RecruiterEmail = DemoRecruiter, RecruiterName = "Rita Recruiter",
            Sentiment = "positive", RequiresHuman = true, LastInboundAt = now.AddHours(-2),
            LastClassifiedAt = now.AddHours(-2),
        };
        db.RecruiterThreads.Add(thread);
        db.Messages.AddRange(
            new Message { ThreadId = thread.Id, Direction = MessageDirection.Outbound, Subject = "Application — Demo Candidate", Body = "Hello,\n\nI'm interested in the Senior Salesforce Architect role and have attached my resume.\n\nBest,\nDemo Candidate", SentAt = now.AddDays(-4), GeneratedByAi = true, AiModel = "demo" },
            new Message { ThreadId = thread.Id, Direction = MessageDirection.Inbound, Subject = "Re: Application — Demo Candidate", Body = "Hi! Your background looks great — are you available for a 30-minute call this week? Tuesday or Wednesday afternoon work on our end.", SentAt = now.AddHours(-2), ProviderMessageId = $"demo-{Guid.NewGuid():N}" });

        // Escalation for thread A.
        db.Escalations.Add(new Escalation { MatchId = matchA.Id, Reason = "interview_request", Summary = "Recruiter wants a 30-min call Tue/Wed afternoon.", CreatedAt = now.AddHours(-2) });

        // A pending follow-up on the quiet match B.
        db.FollowUps.Add(new FollowUp
        {
            TenantId = tenantId, MatchId = matchB.Id, Recipient = DemoRecruiter2,
            DraftSubject = "Following up on my application", DraftBody = "Hi,\n\nJust following up on my application for the Platform Engineer role — still very interested and happy to share more. Thanks!\n\nDemo Candidate",
            Sequence = 1, Status = FollowUpStatus.Pending, CreatedAt = now.AddMinutes(-30),
        });

        // Notifications (unread).
        db.Notifications.AddRange(
            new Notification { TenantId = tenantId, Type = "escalation", Title = "Action needed: interview request", Body = "Senior Salesforce Architect at DemoCorp — wants a call this week.", Link = $"/matches/{matchA.Id}", MatchId = matchA.Id, CreatedAt = now.AddHours(-2) },
            new Notification { TenantId = tenantId, Type = "followup", Title = "Follow-up ready to approve: Platform Engineer at DemoCo", Body = "Following up on my application", Link = $"/matches/{matchB.Id}", MatchId = matchB.Id, CreatedAt = now.AddMinutes(-30) });

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            seeded = true,
            threads = 1,
            escalations = 1,
            followUps = 1,
            notifications = 2,
            message = "Demo recruiter-CRM data created. Open Inbox / Escalations / Follow-ups.",
        });
    }
}
