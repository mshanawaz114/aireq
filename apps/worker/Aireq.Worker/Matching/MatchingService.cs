// MatchingService — turns nearest-job candidates into scored Match rows.
//
// For one consultant:
//   1. Ask the candidate finder for the nearest active jobs (pgvector).
//   2. Map cosine distance -> 0–100 vector score; drop anything below MinScore.
//   3. Apply rule filters (location compatibility today; see notes below).
//   4. Upsert Match by (ConsultantId, JobId): create new ones, refresh the
//      score on still-New matches, never clobber matches the user has already
//      acted on (Tailored / Submitted / …).
//
// Scoring note: this is the *vector* score. AIRMVP1-205 layers an LLM scorer
// (with reasoning + ATS gap analysis) on top and overwrites Score with its
// judgement; until then the cosine score is what the UI shows.
//
// Rule-filter note: work-authorization and rate filters need structured
// requirements parsed out of the JD, which we don't extract yet — enforcing
// them on free-text now would wrongly drop good matches. They're deliberately
// deferred (documented, not faked). Location uses a best-effort heuristic.
//
// Refs: AIRMVP1-204

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Matching;

public sealed class MatchingService(
    AireqDbContext db,
    IJobCandidateFinder finder,
    IOptions<MatchingOptions> options,
    ILogger<MatchingService> log)
{
    public async Task<int> MatchConsultantAsync(Consultant consultant, CancellationToken ct)
    {
        var opts = options.Value;
        var candidates = await finder.FindForConsultantAsync(consultant.Id, opts.TopN, ct);
        if (candidates.Count == 0) return 0;

        // Existing matches for this consultant, so we upsert rather than dup
        // (the (ConsultantId, JobId) unique index would otherwise throw).
        var jobIds = candidates.Select(c => c.JobId).ToList();
        var existing = await db.Matches
            .IgnoreQueryFilters()
            .Where(m => m.ConsultantId == consultant.Id && jobIds.Contains(m.JobId))
            .ToDictionaryAsync(m => m.JobId, ct);

        var created = 0;
        foreach (var candidate in candidates)
        {
            var score = ToScore(candidate.CosineDistance);
            if (score < opts.MinScore) continue;
            if (!LocationCompatible(consultant.Location, candidate.JobLocation)) continue;

            if (existing.TryGetValue(candidate.JobId, out var match))
            {
                // Only refresh score on matches the user hasn't acted on yet.
                if (match.Status == MatchStatus.New) match.Score = score;
            }
            else
            {
                db.Matches.Add(new Match
                {
                    TenantId = consultant.TenantId,
                    ConsultantId = consultant.Id,
                    JobId = candidate.JobId,
                    Score = score,
                    Status = MatchStatus.New,
                });
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation(
            "Matched consultant {ConsultantId}: {Created} new match(es) from {Candidates} candidate(s).",
            consultant.Id, created, candidates.Count);
        return created;
    }

    /// <summary>Cosine distance (0..2) -> 0–100 score. similarity = 1 - distance.</summary>
    public static int ToScore(double cosineDistance)
    {
        var similarity = 1.0 - cosineDistance;
        var clamped = Math.Clamp(similarity, 0.0, 1.0);
        return (int)Math.Round(clamped * 100);
    }

    /// <summary>
    /// Best-effort location compatibility. Remote jobs always pass; if either
    /// side is unknown we don't exclude (cosine already ranked it). Otherwise we
    /// require a shared token (city/state). Heuristic — tightens once we parse
    /// structured location out of the JD.
    /// </summary>
    public static bool LocationCompatible(string? consultantLocation, string? jobLocation)
    {
        if (string.IsNullOrWhiteSpace(jobLocation)) return true;
        if (jobLocation.Contains("remote", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(consultantLocation)) return true;

        var jobTokens = Tokenize(jobLocation);
        var consultantTokens = Tokenize(consultantLocation);
        return jobTokens.Overlaps(consultantTokens);
    }

    private static HashSet<string> Tokenize(string s) =>
        s.Split(new[] { ' ', ',', '/', '-', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
         .Select(t => t.ToLowerInvariant())
         .Where(t => t.Length > 1)
         .ToHashSet();
}
