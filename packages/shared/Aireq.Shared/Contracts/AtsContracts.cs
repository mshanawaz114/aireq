// ATS analysis DTO (AIRMVP1-301). The full keyword-coverage breakdown for one
// match, consumed by the missing-keywords panel and (next) the resume rewriter.
//
// Refs: AIRMVP1-301

namespace Aireq.Shared.Contracts;

/// <param name="CoveragePercent">Share of the JD's ATS keywords present in the resume (0–100).</param>
public sealed record AtsAnalysis(
    Guid MatchId,
    int CoveragePercent,
    int JobKeywordCount,
    IReadOnlyList<string> PresentKeywords,
    IReadOnlyList<string> MissingKeywords);
