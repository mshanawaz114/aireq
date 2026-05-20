// JobIngestionServiceTests — orchestration + upsert behaviour.
//
// Coverage:
//   - New postings are inserted with is_active=true.
//   - Re-seen postings (same source + external id) update in place, not dup.
//   - A posting with the same external id but a DIFFERENT source is a separate
//     row (identity is the (source, external_id) pair).
//   - Disabled sources are skipped (no rows, no throw).
//   - One source throwing doesn't abort the run; others still ingest.
//
// Refs: AIRMVP1-201

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Jobs;

public sealed class JobIngestionServiceTests
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

    private static JobIngestionService Build(AireqDbContext db, IEnumerable<IJobSource> sources)
    {
        var opts = Options.Create(new JobIngestionOptions
        {
            Queries = new() { "engineer" }, // single query keeps counts predictable
            MaxResultsPerQuery = 50,
        });
        return new JobIngestionService(sources, db, opts, NullLogger<JobIngestionService>.Instance);
    }

    [Fact]
    public async Task New_postings_are_inserted_active()
    {
        var dbName = $"ingest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var src = new FakeJobSource("adzuna", new[]
        {
            FakeJobSource.Job("adzuna", "A1"),
            FakeJobSource.Job("adzuna", "A2"),
        });

        var report = await Build(db, new[] { src }).RunAsync(CancellationToken.None);

        report.TotalInserted.Should().Be(2);
        report.TotalUpdated.Should().Be(0);
        var jobs = await db.Jobs.ToListAsync();
        jobs.Should().HaveCount(2);
        jobs.Should().OnlyContain(j => j.IsActive);
    }

    [Fact]
    public async Task Reseen_posting_updates_in_place_not_duplicated()
    {
        var dbName = $"ingest-{Guid.NewGuid()}";

        // First run inserts A1.
        await using (var db1 = NewDb(dbName))
        {
            await Build(db1, new[] { new FakeJobSource("adzuna", new[] { FakeJobSource.Job("adzuna", "A1", title: "Old title") }) })
                .RunAsync(CancellationToken.None);
        }

        // Second run re-seeds A1 with a new title.
        await using (var db2 = NewDb(dbName))
        {
            var report = await Build(db2, new[] { new FakeJobSource("adzuna", new[] { FakeJobSource.Job("adzuna", "A1", title: "New title") }) })
                .RunAsync(CancellationToken.None);
            report.TotalInserted.Should().Be(0);
            report.TotalUpdated.Should().Be(1);
        }

        await using var db3 = NewDb(dbName);
        var jobs = await db3.Jobs.ToListAsync();
        jobs.Should().ContainSingle("the re-seen posting must update, not duplicate");
        jobs[0].Title.Should().Be("New title");
    }

    [Fact]
    public async Task Same_external_id_different_source_is_a_separate_row()
    {
        var dbName = $"ingest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var sources = new[]
        {
            new FakeJobSource("adzuna", new[] { FakeJobSource.Job("adzuna", "X1") }),
            new FakeJobSource("usajobs", new[] { FakeJobSource.Job("usajobs", "X1") }),
        };

        await Build(db, sources).RunAsync(CancellationToken.None);

        var jobs = await db.Jobs.ToListAsync();
        jobs.Should().HaveCount(2, "identity is (source, external_id), not external_id alone");
    }

    [Fact]
    public async Task Disabled_sources_are_skipped()
    {
        var dbName = $"ingest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var disabled = new FakeJobSource("adzuna", new[] { FakeJobSource.Job("adzuna", "A1") }, enabled: false);

        var report = await Build(db, new[] { disabled }).RunAsync(CancellationToken.None);

        report.TotalInserted.Should().Be(0);
        (await db.Jobs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task One_failing_source_does_not_abort_the_run()
    {
        var dbName = $"ingest-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var sources = new IJobSource[]
        {
            new ThrowingJobSource("badsource"),
            new FakeJobSource("usajobs", new[] { FakeJobSource.Job("usajobs", "U1") }),
        };

        var report = await Build(db, sources).RunAsync(CancellationToken.None);

        report.TotalInserted.Should().Be(1, "the healthy source still ingests");
        (await db.Jobs.CountAsync()).Should().Be(1);
    }

    private sealed class ThrowingJobSource(string name) : IJobSource
    {
        public string Name => name;
        public bool IsEnabled => true;
        public async IAsyncEnumerable<RawJob> FetchAsync(
            JobSourceQuery query,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
