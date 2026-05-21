// DigestService — composes and sends a once-a-day activity digest per tenant.
//
// For each tenant with an owner, counts the last LookbackHours of activity:
// new matches, submissions, recruiter replies, and the current open-escalation
// backlog. Tenants with no activity AND no open escalations are skipped (no
// zero-content spam). The email goes through the throttled/dry-run IEmailSender
// with purpose "digest" — so it's audited in EmailLog like every other send.
//
// Runs in the worker, which has no per-request tenant context, so every query
// is explicitly scoped by tenant id with IgnoreQueryFilters (the worker
// convention).
//
// Refs: AIRMVP1-403

using System.Net;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Notifications;

public sealed class DigestService(
    AireqDbContext db,
    IEmailSender email,
    IOptions<DigestOptions> options,
    ILogger<DigestService> log)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var since = DateTimeOffset.UtcNow.AddHours(-Math.Abs(opts.LookbackHours));

        var tenants = await db.Tenants.IgnoreQueryFilters().ToListAsync(ct);
        var sent = 0;

        foreach (var tenant in tenants)
        {
            if (ct.IsCancellationRequested) break;

            // Owner (preferred) or earliest user as the digest recipient.
            var recipient = await db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId == tenant.Id)
                .OrderByDescending(u => u.Role == "owner")
                .ThenBy(u => u.CreatedAt)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
            if (recipient is null) continue;

            var newMatches = await db.Matches.IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == tenant.Id && m.CreatedAt >= since, ct);

            var submissions = await (
                from s in db.Submissions.IgnoreQueryFilters()
                join m in db.Matches.IgnoreQueryFilters() on s.MatchId equals m.Id
                where m.TenantId == tenant.Id && s.SubmittedAt >= since
                select s.Id).CountAsync(ct);

            var replies = await (
                from msg in db.Messages.IgnoreQueryFilters()
                join t in db.RecruiterThreads.IgnoreQueryFilters() on msg.ThreadId equals t.Id
                join m in db.Matches.IgnoreQueryFilters() on t.MatchId equals m.Id
                where m.TenantId == tenant.Id
                      && msg.Direction == MessageDirection.Inbound
                      && msg.SentAt >= since
                select msg.Id).CountAsync(ct);

            var openEscalations = await (
                from e in db.Escalations.IgnoreQueryFilters()
                join m in db.Matches.IgnoreQueryFilters() on e.MatchId equals m.Id
                where m.TenantId == tenant.Id && e.ResolvedAt == null
                select e.Id).CountAsync(ct);

            if (newMatches == 0 && submissions == 0 && replies == 0 && openEscalations == 0)
                continue; // nothing worth an email

            var html = BuildHtml(tenant.Name, opts.LookbackHours, newMatches, submissions, replies, openEscalations);
            var subject = $"Aireq daily digest — {newMatches} new match{(newMatches == 1 ? "" : "es")}, " +
                          $"{replies} repl{(replies == 1 ? "y" : "ies")}, {openEscalations} to action";

            var result = await email.SendAsync(new EmailMessage(
                TenantId: tenant.Id,
                To: recipient,
                Subject: subject,
                HtmlBody: html,
                Purpose: "digest"), opts.SendLive, ct);

            if (result.Status is "sent" or "dry_run") sent++;
        }

        log.LogInformation("Daily digest pass: {Sent}/{Tenants} tenants emailed (live={Live}).",
            sent, tenants.Count, opts.SendLive);
        return sent;
    }

    private static string BuildHtml(
        string tenantName, int hours, int newMatches, int submissions, int replies, int openEscalations)
    {
        string Row(string label, int n) =>
            $"<tr><td style=\"padding:6px 12px;\">{WebUtility.HtmlEncode(label)}</td>" +
            $"<td style=\"padding:6px 12px;font-weight:600;text-align:right;\">{n}</td></tr>";

        return
            $"<div style=\"font-family:system-ui,Segoe UI,Arial,sans-serif;max-width:560px;\">" +
            $"<h2 style=\"margin:0 0 4px;\">Your Aireq digest</h2>" +
            $"<p style=\"color:#555;margin:0 0 16px;\">{WebUtility.HtmlEncode(tenantName)} · last {hours} hours</p>" +
            $"<table style=\"border-collapse:collapse;width:100%;border:1px solid #eee;\">" +
            Row("New matches", newMatches) +
            Row("Applications submitted", submissions) +
            Row("Recruiter replies", replies) +
            Row("Open items needing you", openEscalations) +
            $"</table>" +
            $"<p style=\"color:#888;font-size:12px;margin-top:16px;\">" +
            $"You're receiving this because you own an Aireq workspace.</p></div>";
    }
}
