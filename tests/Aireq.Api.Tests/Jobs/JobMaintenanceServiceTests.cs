// JobMaintenanceServiceTests — dedupe + freshness sweep behaviour.
//
// Coverage:
//   - Dedup: same ContentHash collapses to one canonical; ATS source wins over
//     aggregator; non-canonical rows get CanonicalJobId set.
//   - Dedup ignores postings older than the dedupe window.
//   - Sweep: postings not seen within the staleness window are deactivated;
//     recently-seen ones stay active.
//
// Refs: AIRMVP1-203

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Jobs;

public sealed class JobMaintenanceServiceTests
{
    private static AireqDbContext NewDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AireqDbContext(options, new StubTenantContext());
    }

    private static JobMaintenanceService Build(AireqDbContext db,
        int stalenessHours = 72, int dedupeDays = 30)
    {
        var opts = Options.Create(new JobMaintenanceOptions
        {
            StalenessWindowHours = stalenessHours,
            DedupeWindowDays = dedupeDays,
        });
        return new JobMaintenanceService(db, opts, NullLogger<JobMaintenanceService>.Instance);
    }

    private static Job NewJob(string source, string extId, string hash,
        DateTimeOffset? postedAt = null, DateTimeOffset? lastSeen = null, bool active = true) =>
        new()
        {
            Source = source,
            SourceExternalId = extId,
            Title = "Engineer",
            Company = "Acme",
            ContentHash = hash,
            PostedAt = postedAt ?? DateTimeOffset.UtcNow,
            LastSeenAt = lastSeen ?? DateTimeOffset.UtcNow,
            IsActive = active,
        };

    [Fact]
    public async Task Dedup_collapses_same_hash_preferring_ats_source()
    {
        var dbName = $"maint-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);

        // Same posting from an aggregator (created first) and an ATS board.
        var aggregator = NewJob("adzuna", "AD1", "hash-A",
            postedAt: DateTimeOffset.UtcNow.AddDays(-1));
        var ats = NewJob("greenhouse", "GH1", "hash-A",
            postedAt: DateTimeOffset.UtcNow.AddDays(-1));
        db.Jobs.AddRange(aggregator, ats);
        await db.SaveChangesAsync();

        var marked = await Build(db).DedupeAsync(CancellationToken.None);

        marked.Should().Be(1);
        var atsRow = await db.Jobs.SingleAsync(j => j.Source == "greenhouse");
        var aggRow = await db.Jobs.SingleAsync(j => j.Source == "adzuna");
        atsRow.CanonicalJobId.Should().BeNull("ATS source is preferred as canonical");
        aggRow.CanonicalJobId.Should().Be(atsRow.Id, "the aggregator copy points at the ATS canonical");
    }

    [Fact]
    public async Task Dedup_ignores_distinct_hashes()
    {
        var dbName = $"maint-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        db.Jobs.AddRange(NewJob("adzuna", "A1", "hash-A"), NewJob("adzuna", "A2", "hash-B"));
        await db.SaveChangesAsync();

        var marked = await Build(db).DedupeAsync(CancellationToken.None);

        marked.Should().Be(0);
        (await db.Jobs.CountAsync(j => j.CanonicalJobId != null)).Should().Be(0);
    }

    [Fact]
    public async Task Dedup_ignores_postings_outside_window()
    {
        var dbName = $"maint-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        // Both same hash, but one posted 40 days ago — outside the 30-day window.
        db.Jobs.AddRange(
            NewJob("adzuna", "A1", "hash-A", postedAt: DateTimeOffset.UtcNow.AddDays(-1)),
            NewJob("greenhouse", "G1", "hash-A", postedAt: DateTimeOffset.UtcNow.AddDays(-40)));
        await db.SaveChangesAsync();

        var marked = await Build(db, dedupeDays: 30).DedupeAsync(CancellationToken.None);

        marked.Should().Be(0, "the 40-day-old posting is outside the dedupe window so no group forms");
    }

    [Fact]
    public async Task Sweep_deactivates_stale_keeps_fresh()
    {
        var dbName = $"maint-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var fresh = NewJob("adzuna", "FRESH", "h1", lastSeen: DateTimeOffset.UtcNow.AddHours(-1));
        var stale = NewJob("adzuna", "STALE", "h2", lastSeen: DateTimeOffset.UtcNow.AddHours(-100));
        db.Jobs.AddRange(fresh, stale);
        await db.SaveChangesAsync();

        var swept = await Build(db, stalenessHours: 72).SweepStaleAsync(CancellationToken.None);

        swept.Should().Be(1);
        (await db.Jobs.SingleAsync(j => j.SourceExternalId == "FRESH")).IsActive.Should().BeTrue();
        (await db.Jobs.SingleAsync(j => j.SourceExternalId == "STALE")).IsActive.Should().BeFalse();
    }
}
