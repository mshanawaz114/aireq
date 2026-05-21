// LeverSubmissionChannel — Tier A submit for Lever postings.
//
// EXPERIMENTAL. Lever has no clean, documented public application API the way
// Greenhouse's Job Board API does — the real apply flow is the hosted web form
// at jobs.lever.co/{company}/{postingId}/apply. We POST the form fields here as
// a best effort, but the robust path for Lever is Tier B Playwright (AIRMVP1-304).
//
// Because of that uncertainty, this channel is conservative: dry-run logs the
// intended payload; live attempts the form POST and records whatever comes back
// as received/failed without claiming success it can't verify.
//
// Refs: AIRMVP1-303, AIRMVP1-304

using System.Text.Json;
using Aireq.Api.Data.Entities;

namespace Aireq.Worker.Submission;

public sealed class LeverSubmissionChannel(
    HttpClient http,
    ILogger<LeverSubmissionChannel> log) : ISubmissionChannel
{
    public SubmissionChannel Kind => SubmissionChannel.Api;

    public bool CanHandle(string jobSource) =>
        string.Equals(jobSource, "lever", StringComparison.OrdinalIgnoreCase);

    public async Task<SubmissionOutcome> SubmitAsync(SubmissionRequest r, bool live, CancellationToken ct)
    {
        var url = $"https://jobs.lever.co/{Uri.EscapeDataString(r.BoardToken)}/{Uri.EscapeDataString(r.JobExternalId)}/apply";
        var summary = JsonSerializer.Serialize(new
        {
            channel = "lever-form",
            url,
            experimental = true,
            applicant = new { r.FirstName, r.LastName, r.Email, r.Phone },
            resumeBytes = r.ResumePdf.Length,
        }, JsonOpts);

        if (!live)
        {
            log.LogInformation("[DRY-RUN] Lever submit (experimental) for match {MatchId}: {Summary}", r.MatchId, summary);
            return SubmissionOutcome.DryRun(Kind, summary);
        }

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent($"{r.FirstName} {r.LastName}"), "name" },
                { new StringContent(r.Email), "email" },
            };
            if (!string.IsNullOrWhiteSpace(r.Phone))
                form.Add(new StringContent(r.Phone!), "phone");
            var resume = new ByteArrayContent(r.ResumePdf);
            resume.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(resume, "resume", r.ResumeFileName);

            using var resp = await http.PostAsync(url, form, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Serialize(new
            {
                status = (int)resp.StatusCode, experimental = true, body = Truncate(body),
            }, JsonOpts);

            return resp.IsSuccessStatusCode
                ? SubmissionOutcome.Received(Kind, payload)
                : SubmissionOutcome.Failed(Kind, payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Lever submit errored for match {MatchId}.", r.MatchId);
            return SubmissionOutcome.Failed(Kind, JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts));
        }
    }

    private static string Truncate(string s) => s.Length <= 2000 ? s : s[..2000];
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
