// PlaywrightSubmissionChannel — Tier B. Drives the ATS-hosted application form
// in a headless browser when there's no clean API (or the API failed).
//
// Flow per submission:
//   1. Pick the template matching the job source; build the apply URL.
//   2. Launch headless Chromium, navigate, write the resume PDF to a temp file,
//      let the template fill the form.
//   3. Screenshot the filled form, upload it to blob (audit/debug evidence).
//   4. DRY-RUN (default): stop here — never click submit. Return "dry_run" with
//      the screenshot URL.
//      LIVE: template.SubmitAsync, return "received".
//
// Requires browser binaries: run `playwright install chromium` once (or the
// generated bin/.../playwright.ps1 install). Not unit-tested here — the tier
// orchestration + template URL/match logic are tested instead; full-browser
// verification is the AIRMVP1-307 chaos-test job.
//
// Refs: AIRMVP1-304

using System.Text.Json;
using Aireq.Api.Data.Entities;
using Aireq.Api.Storage;
using Microsoft.Playwright;

namespace Aireq.Worker.Submission.Playwright;

public sealed class PlaywrightSubmissionChannel(
    IEnumerable<IAtsPortalTemplate> templates,
    IBlobStorage blobs,
    ILogger<PlaywrightSubmissionChannel> log) : ISubmissionChannel
{
    public SubmissionChannel Kind => SubmissionChannel.Portal;
    public int Tier => 1; // after Tier A APIs

    public bool CanHandle(string jobSource) => templates.Any(t => t.Matches(jobSource));

    public async Task<SubmissionOutcome> SubmitAsync(SubmissionRequest r, bool live, CancellationToken ct)
    {
        var template = templates.FirstOrDefault(t => t.Matches(r.JobSource));
        if (template is null)
            return SubmissionOutcome.Failed(Kind, "{\"reason\":\"no template\"}");

        var url = template.BuildApplyUrl(r);
        var tempPdf = Path.Combine(Path.GetTempPath(), $"aireq-{r.MatchId}-{Guid.NewGuid():N}.pdf");

        try
        {
            await File.WriteAllBytesAsync(tempPdf, r.ResumePdf, ct);

            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.NetworkIdle });

            await template.FillAsync(page, r, tempPdf);

            // Screenshot the filled form as evidence (both dry-run + live).
            var shot = await page.ScreenshotAsync(new() { FullPage = true });
            var shotPath = $"tenants/{Guid.Empty}/submissions/{r.MatchId}/{Guid.NewGuid():N}.png";
            using var shotStream = new MemoryStream(shot);
            var shotUri = await blobs.UploadAsync(shotPath, shotStream, "image/png", ct);

            var payload = JsonSerializer.Serialize(new
            {
                channel = "playwright",
                template = template.Name,
                url,
                screenshot = shotUri.ToString(),
            }, JsonOpts);

            if (!live)
            {
                log.LogInformation("[DRY-RUN] Playwright filled {Template} form for match {MatchId}; screenshot {Shot}.",
                    template.Name, r.MatchId, shotUri);
                return SubmissionOutcome.DryRun(Kind, payload);
            }

            await template.SubmitAsync(page);
            log.LogInformation("Playwright submitted {Template} form for match {MatchId}.", template.Name, r.MatchId);
            return SubmissionOutcome.Received(Kind, payload);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Playwright submit failed for match {MatchId} ({Template}).", r.MatchId, template.Name);
            return SubmissionOutcome.Failed(Kind, JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts));
        }
        finally
        {
            try { if (File.Exists(tempPdf)) File.Delete(tempPdf); } catch { /* best effort */ }
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
