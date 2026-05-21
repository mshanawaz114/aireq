// IAtsPortalTemplate — per-ATS automation strategy. One template per ATS layout
// (Greenhouse-hosted, Lever-hosted, Workday, iCIMS…), NOT per company — the same
// template drives every company on that ATS, which is what makes ~5 templates
// cover ~80% of portals (memory.md §8 Tier B).
//
// Splitting URL building + selector logic out of the channel keeps it pure-
// testable (Matches + BuildApplyUrl) while the actual page interaction lives in
// FillAsync / SubmitAsync.
//
// Refs: AIRMVP1-304

using Microsoft.Playwright;

namespace Aireq.Worker.Submission.Playwright;

public interface IAtsPortalTemplate
{
    /// <summary>ATS name (for logging/screenshots), e.g. "greenhouse-hosted".</summary>
    string Name { get; }

    /// <summary>True if this template handles the given job source.</summary>
    bool Matches(string jobSource);

    /// <summary>Public apply URL for the posting (board token + external id).</summary>
    string BuildApplyUrl(SubmissionRequest request);

    /// <summary>Fill the form fields (name, email, resume upload). Does NOT submit.</summary>
    Task FillAsync(IPage page, SubmissionRequest request, string resumeFilePath);

    /// <summary>Click the submit control. Only called in live mode.</summary>
    Task SubmitAsync(IPage page);
}
