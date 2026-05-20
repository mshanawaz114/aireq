// MetricsService — aggregates admin metrics from the existing tables.
//
// Scoping:
//   - Jobs: GLOBAL. The discovery pool is shared across tenants (jobs aren't
//     tenant-scoped), so these are pipeline-health numbers, not per-tenant.
//   - Matches: tenant-scoped automatically (global query filter on Match).
//   - Resumes: scoped via Consultants (which IS tenant-filtered).
//   - LLM: llm_calls isn't tenant-filtered by design, so we filter by the
//     current tenant id explicitly.
//
// Refs: AIRMVP1-207

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Metrics;

public sealed class MetricsService(AireqDbContext db, ITenantContext tenant)
{
    public async Task<MetricsResponse> GetAsync(CancellationToken ct)
    {
        // ---- Jobs (global pool) ----
        var jobTotal = await db.Jobs.CountAsync(ct);
        var jobActive = await db.Jobs.CountAsync(j => j.IsActive, ct);
        var jobEmbedded = await db.Jobs.CountAsync(j => j.EmbeddedAt != null, ct);
        var jobBySource = await db.Jobs
            .GroupBy(j => j.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Source, x => x.Count, ct);

        // ---- Matches (tenant-scoped by global filter) ----
        var matchTotal = await db.Matches.CountAsync(ct);
        var matchNew = await db.Matches.CountAsync(m => m.Status == MatchStatus.New, ct);
        var matchReasoned = await db.Matches.CountAsync(m => m.ReasoningJson != null, ct);
        var avgScore = matchTotal == 0
            ? 0.0
            : await db.Matches.AverageAsync(m => (double)m.Score, ct);

        // ---- Resumes (scoped via tenant-filtered Consultants) ----
        var resumesQuery = db.Consultants.SelectMany(c => c.Resumes);
        var resumeTotal = await resumesQuery.CountAsync(ct);
        var resumeParsed = await resumesQuery.CountAsync(r => r.ParsedJson != null, ct);
        var resumeEmbedded = await resumesQuery.CountAsync(r => r.EmbeddedAt != null, ct);

        // ---- LLM spend (explicit tenant filter — llm_calls isn't auto-scoped) ----
        var tenantId = tenant.TenantId;
        var llmQuery = db.LlmCalls.Where(c => c.TenantId == tenantId);
        var llmCalls = await llmQuery.CountAsync(ct);
        var llmCost = llmCalls == 0 ? 0m : await llmQuery.SumAsync(c => c.CostUsdEstimate, ct);
        var llmByPurpose = await llmQuery
            .GroupBy(c => c.Purpose)
            .Select(g => new { Purpose = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Purpose, x => x.Count, ct);

        return new MetricsResponse(
            new JobMetrics(jobTotal, jobActive, jobEmbedded, jobBySource),
            new MatchMetrics(matchTotal, matchNew, matchReasoned, Math.Round(avgScore, 1)),
            new ResumeMetrics(resumeTotal, resumeParsed, resumeEmbedded),
            new LlmMetrics(llmCalls, llmCost, llmByPurpose),
            DateTimeOffset.UtcNow);
    }
}
