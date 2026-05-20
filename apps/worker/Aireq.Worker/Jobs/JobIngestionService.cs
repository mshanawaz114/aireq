// JobIngestionService — runs every enabled source across every configured
// query and upserts results into the jobs table.
//
// Persistence rules:
//   - Identity is (source, source_external_id) — the unique index on Job.
//   - Existing row: refresh mutable fields (title/company/location/desc/
//     expiry/raw) and re-activate (is_active=true) so a re-seen posting is
//     considered fresh. Embedding is left untouched (AIRMVP1-204 owns it).
//   - New row: insert with is_active=true. Embedding stays null until 204.
//   - The 30-day-window dedupe + the staleness sweep (is_active=false for
//     postings not re-seen) land in AIRMVP1-203; this story establishes the
//     upsert-on-identity foundation they build on.
//
// Refs: AIRMVP1-201

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Jobs;

public sealed class JobIngestionService(
    IEnumerable<IJobSource> sources,
    AireqDbContext db,
    IOptions<JobIngestionOptions> options,
    ILogger<JobIngestionService> log)
{
    public async Task<IngestionReport> RunAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var report = new IngestionReport();

        var enabled = sources.Where(s => s.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            log.LogWarning(
                "Job ingestion ran but NO sources are enabled. Add provider keys to .env.local " +
                "(ADZUNA_APP_ID/KEY, USAJOBS_AUTH_KEY/USER_AGENT, …).");
            return report;
        }

        log.LogInformation(
            "Job ingestion starting — {SourceCount} source(s), {QueryCount} query(ies).",
            enabled.Count, opts.Queries.Count);

        foreach (var source in enabled)
        {
            // Keyword sources run once per query; full-board sources (ATS) run
            // once per pass since they ignore keywords and return the whole board.
            var queries = source.IsKeywordDriven
                ? opts.Queries.Select(k => new JobSourceQuery(k, opts.Location, opts.MaxResultsPerQuery))
                : new[] { new JobSourceQuery("*", opts.Location, opts.MaxResultsPerQuery) }.AsEnumerable();

            foreach (var query in queries)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var (inserted, updated) = await IngestOneAsync(source, query, ct);
                    report.Add(source.Name, inserted, updated);
                }
                catch (Exception ex)
                {
                    // One bad (source, query) must not abort the whole run.
                    log.LogError(ex, "Ingestion failed for source={Source} query={Query}.",
                        source.Name, query.Keywords);
                    report.AddError(source.Name);
                }
            }
        }

        log.LogInformation("Job ingestion done — {Summary}", report);
        return report;
    }

    private async Task<(int inserted, int updated)> IngestOneAsync(
        IJobSource source, JobSourceQuery query, CancellationToken ct)
    {
        // Collect the page of raw jobs first (bounded by MaxResultsPerQuery).
        var raws = new List<RawJob>();
        await foreach (var raw in source.FetchAsync(query, ct))
            raws.Add(raw);

        if (raws.Count == 0) return (0, 0);

        // Load existing rows for these external ids in one query (this source only).
        var ids = raws.Select(r => r.SourceExternalId).Distinct().ToList();
        var existing = await db.Jobs
            .Where(j => j.Source == source.Name && ids.Contains(j.SourceExternalId))
            .ToDictionaryAsync(j => j.SourceExternalId, ct);

        var inserted = 0;
        var updated = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var r in raws)
        {
            if (existing.TryGetValue(r.SourceExternalId, out var job))
            {
                // Refresh + re-activate. Embedding intentionally untouched.
                // Title/Company are required non-null on Job; RawJob guarantees
                // them non-null, so Trim() can't return null here.
                job.Title = Trim(r.Title, 300)!;
                job.Company = Trim(r.Company, 200)!;
                job.Location = Trim(r.Location, 200);
                job.Description = Trim(r.Description, 50_000);
                job.ExpiresAt = r.ExpiresAt;
                job.RawJson = r.RawJson;
                job.IsActive = true;
                updated++;
            }
            else
            {
                db.Jobs.Add(new Job
                {
                    Source = r.Source,
                    SourceExternalId = r.SourceExternalId,
                    Title = Trim(r.Title, 300)!,
                    Company = Trim(r.Company, 200)!,
                    Location = Trim(r.Location, 200),
                    Description = Trim(r.Description, 50_000),
                    PostedAt = r.PostedAt ?? now,
                    ExpiresAt = r.ExpiresAt,
                    RawJson = r.RawJson,
                    IsActive = true,
                });
                inserted++;
            }
        }

        await db.SaveChangesAsync(ct);
        return (inserted, updated);
    }

    private static string? Trim(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];
}

/// <summary>Per-run tally, surfaced in logs and (later) the admin dashboard.</summary>
public sealed class IngestionReport
{
    private readonly Dictionary<string, (int inserted, int updated, int errors)> _bySource = new();

    public void Add(string source, int inserted, int updated)
    {
        var cur = _bySource.GetValueOrDefault(source);
        _bySource[source] = (cur.inserted + inserted, cur.updated + updated, cur.errors);
    }

    public void AddError(string source)
    {
        var cur = _bySource.GetValueOrDefault(source);
        _bySource[source] = (cur.inserted, cur.updated, cur.errors + 1);
    }

    public int TotalInserted => _bySource.Values.Sum(v => v.inserted);
    public int TotalUpdated => _bySource.Values.Sum(v => v.updated);
    public IReadOnlyDictionary<string, (int inserted, int updated, int errors)> BySource => _bySource;

    public override string ToString() =>
        $"inserted={TotalInserted} updated={TotalUpdated} " +
        string.Join(" ", _bySource.Select(kv =>
            $"[{kv.Key}: +{kv.Value.inserted}/~{kv.Value.updated}/!{kv.Value.errors}]"));
}
