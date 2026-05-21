// FollowUpSender — ships Approved follow-ups through the throttled/dry-run
// IEmailSender, then marks them Sent (or Failed).
//
// The email carries CorrelationMatchId so a reply threads straight back to the
// match (same correlation the apply email uses). A throttled/failed send marks
// the follow-up Failed with a reason for the dashboard rather than silently
// dropping it.
//
// A last-moment guard cancels an Approved nudge if a reply has since arrived for
// the match — so an approval racing an inbound reply never sends a pointless
// nudge.
//
// Refs: AIRMVP1-404

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.FollowUps;

public sealed class FollowUpSender(
    AireqDbContext db,
    IEmailSender email,
    IOptions<FollowUpOptions> options,
    ILogger<FollowUpSender> log)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var opts = options.Value;

        var approved = await db.FollowUps.IgnoreQueryFilters()
            .Where(f => f.Status == FollowUpStatus.Approved)
            .OrderBy(f => f.ApprovedAt)
            .Take(opts.SendBatchSize)
            .ToListAsync(ct);
        if (approved.Count == 0) return 0;

        var sent = 0;
        foreach (var f in approved)
        {
            if (ct.IsCancellationRequested) break;

            // Race guard: a reply arrived since approval -> cancel, don't nudge.
            var replied = await db.RecruiterThreads.IgnoreQueryFilters()
                .AnyAsync(t => t.MatchId == f.MatchId && t.LastInboundAt != null, ct);
            if (replied)
            {
                f.Status = FollowUpStatus.Cancelled;
                f.FailureReason = "Reply received before send.";
                continue;
            }

            var html = $"<p>{System.Net.WebUtility.HtmlEncode(f.DraftBody).Replace("\n", "<br/>")}</p>";
            var result = await email.SendAsync(new EmailMessage(
                TenantId: f.TenantId,
                To: f.Recipient,
                Subject: f.DraftSubject,
                HtmlBody: html,
                Purpose: "followup",
                CorrelationMatchId: f.MatchId), opts.SendLive, ct);

            switch (result.Status)
            {
                case "sent":
                case "dry_run":
                    f.Status = FollowUpStatus.Sent;
                    f.SentAt = DateTimeOffset.UtcNow;
                    sent++;
                    break;
                default: // throttled | failed
                    f.Status = FollowUpStatus.Failed;
                    f.FailureReason = result.Status;
                    log.LogWarning("Follow-up {Id} send {Status} for match {MatchId}.", f.Id, result.Status, f.MatchId);
                    break;
            }
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Follow-up send: {Sent}/{Batch} shipped (live={Live}).", sent, approved.Count, opts.SendLive);
        return sent;
    }
}
