// IJobCandidateFinder — returns the nearest active+canonical jobs for a
// consultant, by pgvector cosine distance.
//
// Split out from MatchingService so the vector query (Npgsql-only — pgvector's
// `<=>` operator can't run on the EF InMemory provider) is isolated behind an
// interface. MatchingService's filtering + scoring + upsert logic is then
// unit-testable with a fake finder.
//
// Refs: AIRMVP1-204

namespace Aireq.Worker.Matching;

public interface IJobCandidateFinder
{
    /// <summary>
    /// Nearest <paramref name="limit"/> active, canonical, embedded jobs to the
    /// consultant's resume embedding, ascending by cosine distance. Empty if the
    /// consultant has no embedded resume yet.
    /// </summary>
    Task<IReadOnlyList<JobCandidate>> FindForConsultantAsync(
        Guid consultantId, int limit, CancellationToken ct);
}

/// <summary>A candidate job + its cosine distance (0 = identical, 2 = opposite).</summary>
public sealed record JobCandidate(Guid JobId, string? JobLocation, double CosineDistance);
