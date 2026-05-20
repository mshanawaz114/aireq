// AtsKeywordExtractor — pure ATS coverage analysis.
//
// Extract the JD's vocabulary keywords, then split them by whether each appears
// in the resume text. Coverage = present / total. No I/O, no LLM — fully unit
// testable and instant.
//
// Refs: AIRMVP1-301

using Aireq.Shared.Contracts;

namespace Aireq.Api.Ats;

public static class AtsKeywordExtractor
{
    public static AtsAnalysis Analyze(Guid matchId, string? jobText, string? resumeText)
    {
        var jobKeywords = SkillsVocabulary.ExtractFrom(jobText);
        if (jobKeywords.Count == 0)
            return new AtsAnalysis(matchId, 100, 0, Array.Empty<string>(), Array.Empty<string>());

        var resumeHaystack = (resumeText ?? "").ToLowerInvariant();

        var present = new List<string>();
        var missing = new List<string>();
        foreach (var kw in jobKeywords)
        {
            if (SkillsVocabulary.Contains(resumeHaystack, kw)) present.Add(kw);
            else missing.Add(kw);
        }

        var coverage = (int)Math.Round(100.0 * present.Count / jobKeywords.Count);
        return new AtsAnalysis(matchId, coverage, jobKeywords.Count, present, missing);
    }
}
