// SubmissionServiceTests — the orchestration + the §12/§14 safety contract.
//
// Coverage:
//   - Dry-run records a Submission but does NOT advance the match (so a real
//     submit can still happen later).
//   - Live "received" advances the match to Submitted.
//   - No channel for the job source -> a Manual (Tier D) submission row.
//   - A non-Tailored match is rejected (no submission).
//   - Channel selection by job source.
//
// Refs: AIRMVP1-303

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Resumes; // FakeBlobStorage
using Aireq.Worker.Submission;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Submission;

public sealed class SubmissionServiceTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    // A controllable channel that returns whatever outcome the test wants.
    private sealed class StubChannel(
        string source,
        Func<SubmissionRequest, bool, SubmissionOutcome> respond,
        int tier = 0,
        SubmissionChannel kind = SubmissionChannel.Api)
        : ISubmissionChannel
    {
        public SubmissionChannel Kind => kind;
        public int Tier => tier;
        public bool CanHandle(string jobSource) => string.Equals(jobSource, source, StringComparison.OrdinalIgnoreCase);
        public Task<SubmissionOutcome> SubmitAsync(SubmissionRequest r, bool live, CancellationToken ct) =>
            Task.FromResult(respond(r, live));
    }

    private static async Task<(Match match, FakeBlobStorage blobs)> SeedAsync(
        string dbName, AireqDbContext db, MatchStatus status = MatchStatus.Tailored, string jobSource = "greenhouse")
    {
        var tenant = new Tenant { Name = "T" };
        var owner = new User { TenantId = tenant.Id, Email = "owner@acme.test", PasswordHash = "x", Role = "owner" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice Architect" };
        var job = new Job
        {
            Source = jobSource, SourceExternalId = "JOB1", Title = "Engineer", Company = "stripe",
            IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
        };
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Score = 80, Status = status };
        var tailored = new TailoredResume { MatchId = match.Id, BlobUrl = "https://fake-blob.local/x", AtsScore = 88 };
        db.AddRange(tenant, owner, consultant, job, match, tailored);
        await db.SaveChangesAsync();

        // Put the tailored PDF where SubmissionService reconstructs the path.
        var blobs = new FakeBlobStorage();
        var path = $"tenants/{tenant.Id}/consultants/{consultant.Id}/tailored/{match.Id}/{tailored.Id}.pdf";
        using var pdf = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 }); // "%PDF.."
        await blobs.UploadAsync(path, pdf, "application/pdf");

        return (match, blobs);
    }

    private static SubmissionService Build(
        AireqDbContext db, FakeBlobStorage blobs, IEnumerable<ISubmissionChannel> channels, bool live)
        => new(db, channels, blobs, Options.Create(new SubmissionOptions { EnableLiveSubmit = live }),
            NullLogger<SubmissionService>.Instance);

    [Fact]
    public async Task DryRun_records_submission_but_does_not_advance_match()
    {
        var dbName = $"submit-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (match, blobs) = await SeedAsync(dbName, db);
        var channel = new StubChannel("greenhouse", (_, live) =>
        {
            live.Should().BeFalse("dry-run must pass live=false to the channel");
            return SubmissionOutcome.DryRun(SubmissionChannel.Api, "{}");
        });

        await Build(db, blobs, new[] { channel }, live: false).SubmitAsync(match.Id, CancellationToken.None);

        var sub = await db.Submissions.SingleAsync();
        sub.ResponseStatus.Should().Be("dry_run");
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(MatchStatus.Tailored, "dry-run leaves the match submittable");
    }

    [Fact]
    public async Task Live_received_advances_match_to_submitted()
    {
        var dbName = $"submit-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (match, blobs) = await SeedAsync(dbName, db);
        var channel = new StubChannel("greenhouse", (_, live) =>
        {
            live.Should().BeTrue();
            return SubmissionOutcome.Received(SubmissionChannel.Api, "{\"status\":201}");
        });

        await Build(db, blobs, new[] { channel }, live: true).SubmitAsync(match.Id, CancellationToken.None);

        (await db.Submissions.SingleAsync()).ResponseStatus.Should().Be("received");
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(MatchStatus.Submitted);
    }

    [Fact]
    public async Task No_channel_records_manual_fallback()
    {
        var dbName = $"submit-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (match, blobs) = await SeedAsync(dbName, db, jobSource: "usajobs"); // no Tier-A channel

        await Build(db, blobs, new[] { new StubChannel("greenhouse", (_, _) => SubmissionOutcome.DryRun(SubmissionChannel.Api, "{}")) }, live: false)
            .SubmitAsync(match.Id, CancellationToken.None);

        var sub = await db.Submissions.SingleAsync();
        sub.Channel.Should().Be(SubmissionChannel.Manual);
        sub.ResponseStatus.Should().Be("pending_manual");
    }

    [Fact]
    public async Task Falls_through_to_higher_tier_when_lower_tier_fails()
    {
        var dbName = $"submit-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (match, blobs) = await SeedAsync(dbName, db);

        // Tier 0 (API) fails; Tier 1 (Portal) succeeds. Both handle "greenhouse".
        var tierA = new StubChannel("greenhouse", (_, _) => SubmissionOutcome.Failed(SubmissionChannel.Api, "{}"), tier: 0);
        var tierB = new StubChannel("greenhouse", (_, _) => SubmissionOutcome.Received(SubmissionChannel.Portal, "{}"),
            tier: 1, kind: SubmissionChannel.Portal);

        // Register out of order to prove ordering is by Tier, not registration.
        await Build(db, blobs, new ISubmissionChannel[] { tierB, tierA }, live: true)
            .SubmitAsync(match.Id, CancellationToken.None);

        var sub = await db.Submissions.SingleAsync();
        sub.Channel.Should().Be(SubmissionChannel.Portal, "Tier A failed, fell through to Tier B");
        sub.ResponseStatus.Should().Be("received");
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(MatchStatus.Submitted);
    }

    [Fact]
    public async Task Non_tailored_match_is_not_submitted()
    {
        var dbName = $"submit-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (match, blobs) = await SeedAsync(dbName, db, status: MatchStatus.New);

        await Build(db, blobs, new[] { new StubChannel("greenhouse", (_, _) => SubmissionOutcome.Received(SubmissionChannel.Api, "{}")) }, live: true)
            .SubmitAsync(match.Id, CancellationToken.None);

        (await db.Submissions.CountAsync()).Should().Be(0, "a New (un-tailored) match cannot be submitted");
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(MatchStatus.New);
    }
}
