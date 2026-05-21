// EmailSubmissionChannel — Tier C. Last automated resort: email the application
// to a contact harvested from the JD (recruiter address or careers@…).
//
// Handles ANY source (Tier 2), so in the tier-ordered fallback it only runs
// after Tier A (API) and Tier B (Playwright) decline/fail. Requires a recipient
// email discoverable in the JD — if none, returns "failed" so the orchestrator
// records a Manual fallback.
//
// Composes a short cover note via the LLM (Groq), attaches the tailored PDF, and
// sends through the throttled/dry-run IEmailSender.
//
// Refs: AIRMVP1-305

using System.Text.RegularExpressions;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Email;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Worker.Submission;

public sealed partial class EmailSubmissionChannel(
    AireqDbContext db,
    IEmailSender email,
    ILlmGateway llm,
    ILogger<EmailSubmissionChannel> log) : ISubmissionChannel
{
    public SubmissionChannel Kind => SubmissionChannel.Email;
    public int Tier => 2; // after API + Playwright

    // Handles everything — it's the catch-all automated tier. Whether it can
    // actually send depends on finding a recipient (checked in SubmitAsync).
    public bool CanHandle(string jobSource) => true;

    public async Task<SubmissionOutcome> SubmitAsync(SubmissionRequest r, bool live, CancellationToken ct)
    {
        // Recipient: first email address in the JD description.
        var jobDescription = await db.Jobs
            .Where(j => j.Source == r.JobSource && j.SourceExternalId == r.JobExternalId)
            .Select(j => j.Description)
            .FirstOrDefaultAsync(ct);

        var recipient = ExtractEmail(jobDescription);
        if (recipient is null)
        {
            log.LogInformation("Email channel: no recipient found in JD for match {MatchId}.", r.MatchId);
            return SubmissionOutcome.Failed(Kind, "{\"reason\":\"no recipient email in JD\"}");
        }

        // Cover note via LLM (short, professional). Failure is non-fatal — fall
        // back to a plain note so we still send.
        string coverNote;
        try
        {
            var resp = await llm.CompleteAsync(new LlmRequest(
                TenantId: r.TenantId,
                Model: LlmModel.Haiku,
                Purpose: "email.cover",
                SystemPrompt: "Write a concise (<120 word) professional cover note for a job application email. Plain text, no salutation placeholders like [Name].",
                UserPrompt: $"Applicant: {r.FirstName} {r.LastName}. Applying via email; resume attached. Keep it warm and specific to a software/staffing role."),
                ct);
            coverNote = string.IsNullOrWhiteSpace(resp.Text)
                ? DefaultNote(r) : resp.Text.Trim();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cover-note generation failed for match {MatchId}; using default.", r.MatchId);
            coverNote = DefaultNote(r);
        }

        var html = $"<p>{System.Net.WebUtility.HtmlEncode(coverNote).Replace("\n", "<br/>")}</p>";
        var result = await email.SendAsync(new EmailMessage(
            TenantId: r.TenantId,
            To: recipient,
            Subject: $"Application — {r.FirstName} {r.LastName}",
            HtmlBody: html,
            Purpose: "apply",
            Attachment: r.ResumePdf,
            AttachmentName: r.ResumeFileName), live, ct);

        var payload = $"{{\"recipient\":\"{recipient}\",\"emailStatus\":\"{result.Status}\"}}";
        return result.Status switch
        {
            "sent" => SubmissionOutcome.Received(Kind, payload),
            "dry_run" => SubmissionOutcome.DryRun(Kind, payload),
            _ => SubmissionOutcome.Failed(Kind, payload), // throttled / failed
        };
    }

    private static string DefaultNote(SubmissionRequest r) =>
        $"Hello,\n\nI'm interested in this role and have attached my resume. " +
        $"I'd welcome the chance to discuss how my background fits.\n\nBest regards,\n{r.FirstName} {r.LastName}";

    private static string? ExtractEmail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = EmailRegex().Match(text);
        return m.Success ? m.Value : null;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();
}
