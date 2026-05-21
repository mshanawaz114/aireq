// LeverHostedTemplate — drives the Lever-hosted application form at
// jobs.lever.co/{company}/{postingId}/apply. Lever forms use name-attribute
// inputs (name, email, phone) and a resume file input, consistent across cos.
//
// Refs: AIRMVP1-304

using Microsoft.Playwright;

namespace Aireq.Worker.Submission.Playwright;

public sealed class LeverHostedTemplate : IAtsPortalTemplate
{
    public string Name => "lever-hosted";

    public bool Matches(string jobSource) =>
        string.Equals(jobSource, "lever", StringComparison.OrdinalIgnoreCase);

    public string BuildApplyUrl(SubmissionRequest r) =>
        $"https://jobs.lever.co/{r.BoardToken}/{r.JobExternalId}/apply";

    public async Task FillAsync(IPage page, SubmissionRequest r, string resumeFilePath)
    {
        await FillFirst(page, new[] { "input[name='name']" }, $"{r.FirstName} {r.LastName}");
        await FillFirst(page, new[] { "input[name='email']" }, r.Email);
        if (!string.IsNullOrWhiteSpace(r.Phone))
            await FillFirst(page, new[] { "input[name='phone']" }, r.Phone!);

        var fileInput = page.Locator("input[type='file']").First;
        if (await fileInput.CountAsync() > 0)
            await fileInput.SetInputFilesAsync(resumeFilePath);
    }

    public async Task SubmitAsync(IPage page)
    {
        var submit = page.Locator("button[type='submit'], input[type='submit'], .template-btn-submit").First;
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
