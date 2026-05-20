// MatchingServiceTests — scoring, score floor, location filter, upsert, and the
// no-clobber rule. Uses a fake candidate finder so the pgvector query (Npgsql-
// only) is out of scope; this exercises the orchestration that IS testable.
//
// Refs: AIRMVP1-204

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.Matching;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Matching;

public sealed class MatchingServiceTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private sealed class FakeFinder(IReadOnlyList<JobCandidate> candidates) : IJobCandidateFinder
    {
        public Task<IReadOnlyList<JobCandidate>> FindForConsultantAsync(
            Guid consultantId, int limit, CancellationToken ct) =>
            Task.FromResult(candidates);
    }

    private static MatchingService Build(AireqDbContext db, IReadOnlyList<JobCandidate> candidates, int minScore = 50)
    {
        var opts = Options.Create(new MatchingOptions { TopN = 50, MinScore = minScore });
        return new MatchingService(db, new FakeFinder(candidates), opts, NullLogger<MatchingService>.Instance);
    }

    private static async Task<(Consultant consultant, Job job)> SeedAsync(AireqDbContext db, string? jobLocation = "Remote")
    {
        var tenant = new Tenant { Name = "T" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice", Location = "Austin, TX" };
        var job = new Job
        {
            Source = "greenhouse", SourceExternalId = "G1", Title = "Engineer",
            Company = "Acme", Location = jobLocation, IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
        };
        db.AddRange(tenant, consultant, job);
        await db.SaveChangesAsync();
        return (consultant, job);
    }

    [Theory]
    [InlineData(0.0, 100)]   // identical
    [InlineData(0.2, 80)]
    [InlineData(0.5, 50)]
    [InlineData(1.0, 0)]     // orthogonal
    [InlineData(1.5, 0)]     // clamped
    public void ToScore_maps_cosine_distance(double distance, int expected)
    {
        MatchingService.ToScore(distance).Should().Be(expected);
    }

    [Theory]
    [InlineData("Austin, TX", "Remote", true)]        // remote always ok
    [InlineData("Austin, TX", "Austin, TX", true)]    // shared token
    [InlineData("Austin, TX", "New York, NY", false)] // no overlap
    [InlineData(null, "New York, NY", true)]          // no preference
    [InlineData("Austin, TX", null, true)]            // unknown job location
    public void LocationCompatible_heuristic(string? consultant, string? job, bool expected)
    {
        MatchingService.LocationCompatible(consultant, job).Should().Be(expected);
    }

    [Fact]
    public async Task Creates_match_above_threshold()
    {
        var dbName = $"match-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (consultant, job) = await SeedAsync(db);

        // distance 0.1 -> score 90, above MinScore 50.
        var created = await Build(db, new[] { new JobCandidate(job.Id, job.Location, 0.1) })
            .MatchConsultantAsync(consultant, CancellationToken.None);

        created.Should().Be(1);
        var match = await db.Matches.IgnoreQueryFilters().SingleAsync();
        match.TenantId.Should().Be(consultant.TenantId);
        match.JobId.Should().Be(job.Id);
        match.Score.Should().Be(90);
        match.Status.Should().Be(MatchStatus.New);
    }

    [Fact]
    public async Task Skips_match_below_threshold()
    {
        var dbName = $"match-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (consultant, job) = await SeedAsync(db);

        // distance 0.7 -> score 30, below MinScore 50.
        var created = await Build(db, new[] { new JobCandidate(job.Id, job.Location, 0.7) })
            .MatchConsultantAsync(consultant, CancellationToken.None);

        created.Should().Be(0);
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Skips_location_incompatible_job()
    {
        var dbName = $"match-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (consultant, job) = await SeedAsync(db, jobLocation: "New York, NY"); // consultant is Austin

        var created = await Build(db, new[] { new JobCandidate(job.Id, "New York, NY", 0.05) })
            .MatchConsultantAsync(consultant, CancellationToken.None);

        created.Should().Be(0, "high vector score but location-incompatible");
    }

    [Fact]
    public async Task Upsert_refreshes_score_on_new_match_but_not_actioned_ones()
    {
        var dbName = $"match-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (consultant, job) = await SeedAsync(db);

        // Pre-existing match the user already tailored (must NOT be clobbered).
        db.Matches.Add(new Match
        {
            TenantId = consultant.TenantId, ConsultantId = consultant.Id, JobId = job.Id,
            Score = 70, Status = MatchStatus.Tailored,
        });
        await db.SaveChangesAsync();

        var created = await Build(db, new[] { new JobCandidate(job.Id, job.Location, 0.05) }) // score 95
            .MatchConsultantAsync(consultant, CancellationToken.None);

        created.Should().Be(0, "match already exists");
        var match = await db.Matches.IgnoreQueryFilters().SingleAsync();
        match.Score.Should().Be(70, "an actioned (Tailored) match's score is not overwritten");
        match.Status.Should().Be(MatchStatus.Tailored);
    }

    [Fact]
    public async Task Upsert_updates_score_on_still_new_match()
    {
        var dbName = $"match-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var (consultant, job) = await SeedAsync(db);
        db.Matches.Add(new Match
        {
            TenantId = consultant.TenantId, ConsultantId = consultant.Id, JobId = job.Id,
            Score = 60, Status = MatchStatus.New,
        });
        await db.SaveChangesAsync();

        await Build(db, new[] { new JobCandidate(job.Id, job.Location, 0.1) }) // score 90
            .MatchConsultantAsync(consultant, CancellationToken.None);

        var match = await db.Matches.IgnoreQueryFilters().SingleAsync();
        match.Score.Should().Be(90, "a still-New match's score is refreshed");
    }
}
