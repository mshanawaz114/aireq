// PgVectorJobCandidateFinder — production candidate finder using pgvector.
//
// Loads the consultant's latest embedded resume vector, then orders active +
// canonical + embedded jobs by cosine distance (`<=>`) and takes the top N.
// All Npgsql-side; not exercised by the InMemory test suite (the matching
// orchestration is tested via a fake finder instead).
//
// Refs: AIRMVP1-204

using Aireq.Api.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Aireq.Worker.Matching;

public sealed class PgVectorJobCandidateFinder(
    AireqDbContext db,
    ILogger<PgVectorJobCandidateFinder> log) : IJobCandidateFinder
{
    public async Task<IReadOnlyList<JobCandidate>> FindForConsultantAsync(
        Guid consultantId, int limit, CancellationToken ct)
    {
        // Latest embedded resume = the consultant's matching vector.
        var resume = await db.Resumes
            .IgnoreQueryFilters()
            .Where(r => r.ConsultantId == consultantId && r.EmbeddedAt != null && r.Embedding != null)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        if (resume?.Embedding is null)
        {
            log.LogInformation("No embedded resume for consultant {ConsultantId}; no candidates.", consultantId);
            return Array.Empty<JobCandidate>();
        }

        var query = resume.Embedding;

        // Nearest active, canonical (non-duplicate), embedded jobs.
        var candidates = await db.Jobs
            .Where(j => j.IsActive && j.CanonicalJobId == null && j.Embedding != null)
            .OrderBy(j => j.Embedding!.CosineDistance(query))
            .Take(limit)
            .Select(j => new JobCandidate(j.Id, j.Location, j.Embedding!.CosineDistance(query)))
            .ToListAsync(ct);

        return candidates;
    }
}
