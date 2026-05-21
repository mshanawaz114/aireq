// ReplyClassifierTests — sentiment/intent classification of threaded replies,
// match status advancement, escalation raising + dedupe, and the re-classify
// watermark. EF InMemory + FakeLlmGateway, no network.
//
// Refs: AIRMVP1-402

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Matching; // FakeLlmGateway
using Aireq.Worker.Inbound;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Inbound;

public sealed class ReplyClassifierTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static ReplyClassifier Build(AireqDbContext db, string json) =>
        new(db, new FakeLlmGateway(json),
            Options.Create(new ReplyClassificationOptions { BatchSize = 25 }),
            NullLogger<ReplyClassifier>.Instance);

    private static string Json(string sentiment, string intent, bool requiresHuman, string summary = "do the thing") =>
        $"{{\"sentiment\":\"{sentiment}\",\"intent\":\"{intent}\",\"requiresHuman\":{(requiresHuman ? "true" : "false")},\"summary\":\"{summary}\"}}";

    /// <summary>Seed a tenant+match+thread with one inbound reply needing classification.</summary>
    private static async Task<(Guid MatchId, Guid ThreadId)> SeedThreadAsync(
        AireqDbContext db, MatchStatus status = MatchStatus.Submitted, DateTimeOffset? inboundAt = null)
    {
        var at = inboundAt ?? DateTimeOffset.UtcNow;
        var tenant = new Tenant { Name = "Acme" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var job = new Job
        {
            Source = "greenhouse", SourceExternalId = "G1", Title = "Eng", Company = "bigco",
            IsActive = true, LastSeenAt = at,
        };
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Status = status };
        var thread = new RecruiterThread { MatchId = match.Id, RecruiterEmail = "r@bigco.test", LastInboundAt = at };
        var msg = new Message
        {
            ThreadId = thread.Id, Direction = MessageDirection.Inbound,
            Subject = "Re: Application", Body = "Are you free Tuesday for a call?", SentAt = at,
        };
        db.AddRange(tenant, consultant, job, match, thread, msg);
        await db.SaveChangesAsync();
        return (match.Id, thread.Id);
    }

    [Fact]
    public async Task Interview_request_advances_match_and_raises_escalation()
    {
        var dbName = $"cls-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (matchId, threadId) = await SeedThreadAsync(db);

        var n = await Build(db, Json("positive", "interview_request", true, "Wants a call Tuesday."))
            .RunAsync(CancellationToken.None);

        n.Should().Be(1);
        var thread = await db.RecruiterThreads.IgnoreQueryFilters().SingleAsync(t => t.Id == threadId);
        thread.Sentiment.Should().Be("positive");
        thread.RequiresHuman.Should().BeTrue();
        thread.LastClassifiedAt.Should().Be(thread.LastInboundAt);

        (await db.Matches.IgnoreQueryFilters().SingleAsync(m => m.Id == matchId)).Status
            .Should().Be(MatchStatus.Interview);

        var esc = await db.Escalations.IgnoreQueryFilters().SingleAsync();
        esc.MatchId.Should().Be(matchId);
        esc.Reason.Should().Be("interview_request");
    }

    [Fact]
    public async Task Rejection_sets_status_rejected_and_raises_no_escalation()
    {
        var dbName = $"cls-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (matchId, _) = await SeedThreadAsync(db);

        await Build(db, Json("negative", "rejection", false)).RunAsync(CancellationToken.None);

        (await db.Matches.IgnoreQueryFilters().SingleAsync(m => m.Id == matchId)).Status
            .Should().Be(MatchStatus.Rejected);
        (await db.Escalations.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Already_classified_thread_is_skipped()
    {
        var dbName = $"cls-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedThreadAsync(db);

        var classifier = Build(db, Json("neutral", "other", false));
        (await classifier.RunAsync(CancellationToken.None)).Should().Be(1);
        // Second pass: nothing new since LastClassifiedAt == LastInboundAt.
        (await classifier.RunAsync(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Only_one_open_escalation_per_match()
    {
        var dbName = $"cls-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (matchId, threadId) = await SeedThreadAsync(db);

        // First classification raises an escalation.
        await Build(db, Json("neutral", "info_request", true)).RunAsync(CancellationToken.None);
        (await db.Escalations.IgnoreQueryFilters().CountAsync()).Should().Be(1);

        // A newer reply arrives; re-classify — must NOT raise a second open one.
        var thread = await db.RecruiterThreads.IgnoreQueryFilters().SingleAsync(t => t.Id == threadId);
        var newer = thread.LastInboundAt!.Value.AddMinutes(10);
        thread.LastInboundAt = newer;
        db.Messages.Add(new Message
        {
            ThreadId = threadId, Direction = MessageDirection.Inbound,
            Subject = "Re: Application", Body = "Any update?", SentAt = newer,
        });
        await db.SaveChangesAsync();

        await Build(db, Json("neutral", "salary_question", true)).RunAsync(CancellationToken.None);
        (await db.Escalations.IgnoreQueryFilters().CountAsync(e => e.ResolvedAt == null)).Should().Be(1);
    }

    [Fact]
    public async Task Terminal_status_is_not_regressed()
    {
        var dbName = $"cls-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (matchId, _) = await SeedThreadAsync(db, status: MatchStatus.Interview);

        // A neutral "other" reply would map to Reply, but Interview is ahead.
        await Build(db, Json("neutral", "other", false)).RunAsync(CancellationToken.None);

        (await db.Matches.IgnoreQueryFilters().SingleAsync(m => m.Id == matchId)).Status
            .Should().Be(MatchStatus.Interview);
    }

    [Fact]
    public async Task Malformed_json_leaves_thread_unclassified()
    {
        var dbName = $"cls-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedThreadAsync(db);

        var n = await Build(db, "not json at all").RunAsync(CancellationToken.None);

        n.Should().Be(0);
        (await db.RecruiterThreads.IgnoreQueryFilters().SingleAsync()).Sentiment.Should().BeNull();
    }
}
