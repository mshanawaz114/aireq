// ResendEmailSender — IEmailSender backed by Resend, with warmup throttling +
// dry-run + audit logging. The single outbound-email chokepoint (reused by
// Tier C apply, follow-ups, and digests).
//
// Behaviour:
//   - Not live OR no RESEND_API_KEY -> log EmailLog(dry_run), send nothing.
//   - Live: count today's *sent* rows for the tenant; if >= DailyCap, log
//     EmailLog(throttled), send nothing. Otherwise POST to Resend, log the
//     outcome (sent + provider id, or failed).
//   - Dry-run + throttled rows do NOT count toward the cap (only "sent" does).
//
// Config: RESEND_API_KEY, RESEND_FROM (or EMAIL__FROM), EMAIL__DAILYCAP.
//
// Refs: AIRMVP1-305

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Email;

public sealed class ResendEmailSender(
    HttpClient http,
    AireqDbContext db,
    IOptions<EmailOptions> options,
    IConfiguration config,
    ILogger<ResendEmailSender> log) : IEmailSender
{
    private const string Endpoint = "https://api.resend.com/emails";

    public async Task<EmailResult> SendAsync(EmailMessage msg, bool live, CancellationToken ct = default)
    {
        var apiKey = config["RESEND_API_KEY"];
        var configured = !string.IsNullOrWhiteSpace(apiKey) && apiKey != "re_REPLACE_ME";

        if (!live || !configured)
        {
            await LogAsync(msg, "dry_run", null, ct);
            log.LogInformation("[DRY-RUN] Email to {To} ({Purpose}) — not sent (live={Live}, configured={Configured}).",
                msg.To, msg.Purpose, live, configured);
            return EmailResult.DryRun();
        }

        // Warmup throttle — count today's real sends for this tenant.
        // Explicit UTC-midnight DateTimeOffset (DateTimeOffset.UtcNow.Date returns
        // a DateTime, whose mixed comparison shifts by the local offset).
        var n = DateTimeOffset.UtcNow;
        var dayStart = new DateTimeOffset(n.Year, n.Month, n.Day, 0, 0, 0, TimeSpan.Zero);
        var sentToday = await db.EmailLogs
            .Where(e => e.TenantId == msg.TenantId && e.Status == "sent" && e.CreatedAt >= dayStart)
            .CountAsync(ct);
        if (sentToday >= options.Value.DailyCap)
        {
            await LogAsync(msg, "throttled", null, ct);
            log.LogWarning("Email throttled for tenant {TenantId}: {Sent}/{Cap} today.",
                msg.TenantId, sentToday, options.Value.DailyCap);
            return EmailResult.Throttled();
        }

        var from = options.Value.From ?? config["RESEND_FROM"] ?? "Aireq <noreply@aireq.com>";
        var payload = new ResendRequest(
            from, new[] { msg.To }, msg.Subject, msg.HtmlBody,
            msg.Attachment is null ? null : new[]
            {
                new ResendAttachment(msg.AttachmentName ?? "resume.pdf", Convert.ToBase64String(msg.Attachment)),
            });

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(payload, options: JsonOpts),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                log.LogWarning("Resend failed {Status}: {Body}", (int)resp.StatusCode, body);
                await LogAsync(msg, "failed", null, ct);
                return EmailResult.Failed();
            }

            var parsed = await resp.Content.ReadFromJsonAsync<ResendResponse>(JsonOpts, ct);
            await LogAsync(msg, "sent", parsed?.Id, ct);
            log.LogInformation("Email sent to {To} ({Purpose}), provider id {Id}.", msg.To, msg.Purpose, parsed?.Id);
            return EmailResult.Sent(parsed?.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Resend send errored for {To}.", msg.To);
            await LogAsync(msg, "failed", null, ct);
            return EmailResult.Failed();
        }
    }

    private async Task LogAsync(EmailMessage msg, string status, string? providerId, CancellationToken ct)
    {
        db.EmailLogs.Add(new EmailLog
        {
            TenantId = msg.TenantId,
            ToAddress = msg.To,
            Subject = msg.Subject,
            Purpose = msg.Purpose,
            Status = status,
            ProviderMessageId = providerId,
            Body = Truncate(msg.HtmlBody),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s) => s.Length <= EmailLog.MaxBodyChars ? s : s[..EmailLog.MaxBodyChars];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record ResendRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("attachments")] ResendAttachment[]? Attachments);

    private sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ResendResponse
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }
}
