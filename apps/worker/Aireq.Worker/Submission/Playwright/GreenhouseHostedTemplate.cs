// GreenhouseHostedTemplate — drives the Greenhouse-hosted application form at
// boards.greenhouse.io/{board}/jobs/{id}. Greenhouse forms share a stable DOM
// across companies (first_name / last_name / email inputs by id, a resume file
// input, and a #submit_app button), so one template covers every Greenhouse co.
//
// Refs: AIRMVP1-304

using Microsoft.Playwright;

namespace Aireq.Worker.Submission.Playwright;

public sealed class GreenhouseHostedTemplate : IAtsPortalTemplate
{
    public string Name => "greenhouse-hosted";

    public bool Matches(string jobSource) =>
        string.Equals(jobSource, "greenhouse", StringComparison.OrdinalIgnoreCase);

    public string BuildApplyUrl(SubmissionRequest r) =>
        $"https://boards.greenhouse.io/{r.BoardToken}/jobs/{r.JobExternalId}";

    public async Task FillAsync(IPage page, SubmissionRequest r, string resumeFilePath)
    {
        await FillFirst(page, new[] { "#first_name", "input[name='first_name']" }, r.FirstName);
        await FillFirst(page, new[] { "#last_name", "input[name='last_name']" }, r.LastName);
        await FillFirst(page, new[] { "#email", "input[name='email']" }, r.Email);
        if (!string.IsNullOrWhiteSpace(r.Phone))
            await FillFirst(page, new[] { "#phone", "input[name='phone']" }, r.Phone!);

        // Resume upload — Greenhouse exposes a file input (often hidden behind a
        // styled button). SetInputFiles works on the underlying input.
        var fileInput = page.Locator("input[type='file']").First;
        if (await fileInput.CountAsync() > 0)
            await fileInput.SetInputFilesAsync(resumeFilePath);
    }

    public async Task SubmitAsync(IPage page)
    {
        var submit = page.Locator("#submit_app, button[type='submit'], input[type='submit']").First;
        await submit.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task FillFirst(IPage page, string[] selectors, string value)
    {
        foreach (var sel in selectors)
        {
            var loc = page.Locator(sel).First;
            if (await loc.CountAsync() > 0)
            {
                await loc.FillAsync(value);
                return;
            }
        }
    }
}
