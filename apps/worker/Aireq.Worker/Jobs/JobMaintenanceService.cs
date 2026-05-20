// JobMaintenanceService — dedupe + freshness sweep over the jobs table.
//
// Dedupe: postings sharing a ContentHash (same company/title/location/JD-prefix)
//   within the dedupe window collapse to one canonical row; the rest get their
//   CanonicalJobId set and are excluded from matching. Canonical preference is
//   ATS sources first (freshest, employer-authoritative), then earliest seen.
//
// Freshness: postings not re-seen within the staleness window are deactivated.
//   Ingestion bumps LastSeenAt on every pass, so a posting that disappears from
//   all sources ages out and stops surfacing in matches.
//
// Both operations load-then-save (rather than ExecuteUpdate) so they run
// identically on Postgres and the EF InMemory provider used in tests; MVP job
// volume is well within what that comfortably handles.
//
// Refs: AIRMVP1-203

using Aireq.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Jobs;

public sealed class JobMaintenanceService(
    AireqDbContext db,
    IOptions<JobMaintenanceOptions> options,
    ILogger<JobMaintenanceService> log)
{
    /// <summary>
    /// Collapse same-content postings to a single canonical row. Returns the
    /// number of rows newly marked as duplicates.
    /// </summary>
    public async Task<int> DedupeAsync(CancellationToken ct)
    {
        var windowStart = DateTimeOffset.UtcNow - options.Value.DedupeWindow;

        var candidates = await db.Jobs
            .Where(j => j.IsActive && j.ContentHash != null && j.PostedAt >= windowStart)
            .ToListAsync(ct);

        var marked = 0;
        foreach (var group in candidates.GroupBy(j => j.ContentHash))
        {
            if (group.Count() < 2) continue;

            var canonical = group
                .OrderBy(j => SourceRank(j.Source))
                .ThenBy(j => j.CreatedAt)
                .ThenBy(j => j.Id)
                .First();

            foreach (var job in group)
            {
                if (job.Id == canonical.Id)
                {
                    // The canonical row must not point at anything.
                    if (job.CanonicalJobId is not null) job.CanonicalJobId = null;
                }
                else if (job.CanonicalJobId != canonical.Id)
                {
                    job.CanonicalJobId = canonical.Id;
                    marked++;
                }
            }
        }

        if (marked > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Dedupe: {Marked} duplicate posting(s) collapsed across {Groups} group(s).",
            marked, candidates.GroupBy(j => j.ContentHash).Count(g => g.Count() > 1));
        return marked;
    }

    /// <summary>
    /// Deactivate active postings not re-seen within the staleness window.
    /// Returns the number deactivated.
    /// </summary>
    public async Task<int> SweepStaleAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - options.Value.StalenessWindow;

        var stale = await db.Jobs
            .Where(j => j.IsActive && j.LastSeenAt < cutoff)
            .ToListAsync(ct);

        foreach (var job in stale) job.IsActive = false;

        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Freshness sweep: {Count} stale posting(s) deactivated (cutoff {Cutoff:o}).",
            stale.Count, cutoff);
        return stale.Count;
    }

    /// <summary>
    /// Canonical preference: employer ATS boards are the freshest, most
    /// authoritative copy, so they win over aggregators when the same posting
    /// appears in both.
    /// </summary>
    private static int SourceRank(string source) => source switch
    {
        "greenhouse" or "lever" or "ashby" => 0,
        "adzuna" or "usajobs" or "jsearch" => 1,
        _ => 2,
    };
}
