// GreenhouseSubmissionChannel — Tier A submit via the Greenhouse Job Board API.
//
//   POST https://boards-api.greenhouse.io/v1/boards/{board}/jobs/{jobId}
//   multipart/form-data: first_name, last_name, email, phone, resume (file)
//
// Note: some boards require a board API key (HTTP Basic auth) for application
// submission; public boards accept it unauthenticated. We send the key if
// GREENHOUSE_BOARD_KEY is configured, otherwise attempt unauthenticated. A 401/
// 422 is recorded as a "failed" outcome (not thrown) so the pipeline continues.
//
// DRY-RUN (default): builds + logs the multipart payload, contacts no one.
//
// Refs: AIRMVP1-303

using System.Text;
using System.Text.Json;
using Aireq.Api.Data.Entities;

namespace Aireq.Worker.Submission;

public sealed class GreenhouseSubmissionChannel(
    HttpClient http,
    IConfiguration config,
    ILogger<GreenhouseSubmissionChannel> log) : ISubmissionChannel
{
    public SubmissionChannel Kind => SubmissionChannel.Api;

    public bool CanHandle(string jobSource) =>
        string.Equals(jobSource, "greenhouse", StringComparison.OrdinalIgnoreCase);

    public async Task<SubmissionOutcome> SubmitAsync(SubmissionRequest r, bool live, CancellationToken ct)
    {
        var url = $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(r.BoardToken)}/jobs/{Uri.EscapeDataString(r.JobExternalId)}";
        var summary = JsonSerializer.Serialize(new
        {
            channel = "greenhouse-api",
            url,
            applicant = new { r.FirstName, r.LastName, r.Email, r.Phone },
            resumeBytes = r.ResumePdf.Length,
            resumeFile = r.ResumeFileName,
        }, JsonOpts);

        if (!live)
        {
            log.LogInformation("[DRY-RUN] Greenhouse submit for match {MatchId}: {Summary}", r.MatchId, summary);
            return SubmissionOutcome.DryRun(Kind, summary);
        }

        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(r.FirstName), "first_name" },
                { new StringContent(r.LastName), "last_name" },
                { new StringContent(r.Email), "email" },
            };
            if (!string.IsNullOrWhiteSpace(r.Phone))
                form.Add(new StringContent(r.Phone!), "phone");

            var resume = new ByteArrayContent(r.ResumePdf);
            resume.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(resume, "resume", r.ResumeFileName);

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            var boardKey = config["GREENHOUSE_BOARD_KEY"];
            if (!string.IsNullOrWhiteSpace(boardKey) && boardKey != "REPLACE_ME")
            {
                // Greenhouse Basic auth: API key as username, blank password.
                var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{boardKey}:"));
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
            }

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Serialize(new { status = (int)resp.StatusCode, body = Truncate(body) }, JsonOpts);

            if (resp.IsSuccessStatusCode)
            {
                log.LogInformation("Greenhouse submit OK for match {MatchId} ({Status}).", r.MatchId, (int)resp.StatusCode);
                return SubmissionOutcome.Received(Kind, payload);
            }

            log.LogWarning("Greenhouse submit failed for match {MatchId}: {Status}.", r.MatchId, (int)resp.StatusCode);
            return SubmissionOutcome.Failed(Kind, payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Greenhouse submit errored for match {MatchId}.", r.MatchId);
            return SubmissionOutcome.Failed(Kind, JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts));
        }
    }

    private static string Truncate(string s) => s.Length <= 2000 ? s : s[..2000];
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
