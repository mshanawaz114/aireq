// AtsAnalysisService — loads a match's job description + the consultant's latest
// parsed resume, then runs the deterministic extractor. Tenant-scoped via the
// Match global query filter (a match in another tenant is invisible -> NotFound).
//
// Refs: AIRMVP1-301

using Aireq.Api.Data;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Ats;

public sealed class AtsAnalysisService(AireqDbContext db)
{
    /// <summary>Returns null when the match isn't visible to the current tenant.</summary>
    public async Task<AtsAnalysis?> AnalyzeAsync(Guid matchId, CancellationToken ct)
    {
        // Match is tenant-scoped by the global filter.
        var match = await db.Matches
            .Where(m => m.Id == matchId)
            .Select(m => new { m.Id, m.ConsultantId, JobDescription = m.Job.Description, JobTitle = m.Job.Title })
            .SingleOrDefaultAsync(ct);

        if (match is null) return null;

        // Latest parsed resume for the consultant = the matching profile text.
        var resumeText = await db.Resumes
            .IgnoreQueryFilters()
            .Where(r => r.ConsultantId == match.ConsultantId && r.ParsedJson != null)
            .OrderByDescending(r => r.Version)
            .Select(r => r.ParsedJson)
            .FirstOrDefaultAsync(ct);

        // Title + description give the JD its keywords.
        var jobText = $"{match.JobTitle}\n{match.JobDescription}";
        return AtsKeywordExtractor.Analyze(match.Id, jobText, resumeText);
    }
}
