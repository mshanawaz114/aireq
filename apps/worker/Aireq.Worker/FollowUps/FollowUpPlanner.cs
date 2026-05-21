// FollowUpPlanner — finds submitted applications that have gone quiet and drafts
// a polite nudge for each, subject to rate limits.
//
// An application is eligible when, for a match that isn't terminal (Reply /
// Interview / Rejected / Closed) and has no inbound recruiter reply:
//   - we have an apply EmailLog with a recipient address, and
//   - it's been >= FirstNudgeAfterDays (first nudge) or >= GapDays (subsequent)
//     since the last outbound, and
//   - we've sent fewer than MaxFollowUps nudges, and
//   - there's no Pending/Approved follow-up already queued for it.
//
// Each draft is parked Pending (owner approval) unless AutoSend is on, in which
// case it's created pre-Approved. A Pending draft also raises a notification so
// the owner knows there's something to approve.
//
// The actual send is FollowUpSender's job.
//
// Refs: AIRMVP1-404

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.FollowUps;

public sealed class FollowUpPlanner(
    AireqDbContext db,
    ILlmGateway llm,
    IOptions<FollowUpOptions> options,
    ILogger<FollowUpPlanner> log)
{
    // Outbound purposes that count toward the "last outbound" + nudge tally.
    private static readonly string[] OutboundPurposes = ["apply", "followup"];
    private static readonly string[] SentStatuses = ["sent", "dry_run"];

    private static readonly HashSet<MatchStatus> Terminal =
        [MatchStatus.Reply, MatchStatus.Interview, MatchStatus.Rejected, MatchStatus.Closed];

    private const string SystemPrompt =
        """
        Write a brief, warm, professional follow-up email nudging a recruiter about
        a job application the candidate already submitted. Return ONLY a single JSON
        object (no prose, no fences):

        { "subject": "<concise subject>", "body": "<plain-text body, < 120 words>" }

        Rules: reference that they applied and are still interested, be courteous and
        low-pressure, no placeholders like [Name] or [Company], no salutation the
        sender can't fill, sign off with the candidate's name.
        """;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var now = DateTimeOffset.UtcNow;

        // Candidate matches: non-terminal, with at least one apply email logged.
        // We pull the apply EmailLog set for correlation in one go.
        var applyLogs = await db.EmailLogs
            .Where(e => e.Purpose == "apply" && e.CorrelationMatchId != null
                        && SentStatuses.Contains(e.Status))
            .Select(e => new { MatchId = e.CorrelationMatchId!.Value, e.ToAddress, e.CreatedAt })
            .ToListAsync(ct);
        if (applyLogs.Count == 0) return 0;

        var candidateMatchIds = applyLogs.Select(a => a.MatchId).Distinct().ToList();

        // Matches that are still eligible (non-terminal).
        var matches = await db.Matches.IgnoreQueryFilters()
            .Where(m => candidateMatchIds.Contains(m.Id))
            .Include(m => m.Job)
            .Include(m => m.Consultant)
            .ToListAsync(ct);
        var matchById = matches.ToDictionary(m => m.Id);

        // Replies received -> exclude (the reply/escalation flow owns these).
        var repliedMatchIds = (await db.RecruiterThreads.IgnoreQueryFilters()
            .Where(t => candidateMatchIds.Contains(t.MatchId) && t.LastInboundAt != null)
            .Select(t => t.MatchId).Distinct().ToListAsync(ct)).ToHashSet();

        // Existing nudges: count sent ones + flag those with an open (Pending/
        // Approved) draft already queued.
        var followUps = await db.FollowUps.IgnoreQueryFilters()
            .Where(f => candidateMatchIds.Contains(f.MatchId))
            .Select(f => new { f.MatchId, f.Status })
            .ToListAsync(ct);
        var sentCountByMatch = followUps
            .Where(f => f.Status == FollowUpStatus.Sent)
            .GroupBy(f => f.MatchId)
            .ToDictionary(g => g.Key, g => g.Count());
        var openDraftMatchIds = followUps
            .Where(f => f.Status is FollowUpStatus.Pending or FollowUpStatus.Approved)
            .Select(f => f.MatchId).ToHashSet();

        // Group apply logs per match for recipient + last-outbound time.
        var planned = 0;
        foreach (var matchId in candidateMatchIds)
        {
            if (ct.IsCancellationRequested) break;
            if (planned >= opts.PlanBatchSize) break;
            if (repliedMatchIds.Contains(matchId)) continue;
            if (openDraftMatchIds.Contains(matchId)) continue;
            if (!matchById.TryGetValue(matchId, out var match)) continue;
            if (Terminal.Contains(match.Status)) continue;

            var sentSoFar = sentCountByMatch.GetValueOrDefault(matchId, 0);
            if (sentSoFar >= opts.MaxFollowUps) continue;

            // Recipient = the address we applied to (latest apply log for the match).
            var apply = applyLogs.Where(a => a.MatchId == matchId)
                .OrderByDescending(a => a.CreatedAt).First();
            if (string.IsNullOrWhiteSpace(apply.ToAddress)) continue;

            // Last outbound across apply + prior follow-ups.
            var lastOutbound = await db.EmailLogs
                .Where(e => e.CorrelationMatchId == matchId
                            && OutboundPurposes.Contains(e.Purpose)
                            && SentStatuses.Contains(e.Status))
                .MaxAsync(e => (DateTimeOffset?)e.CreatedAt, ct) ?? apply.CreatedAt;

            var requiredGap = sentSoFar == 0 ? opts.FirstNudgeAfterDays : opts.GapDays;
            if ((now - lastOutbound).TotalDays < requiredGap) continue;

            // Draft the nudge.
            string subject, body;
            try
            {
                var resp = await llm.CompleteAsync(new LlmRequest(
                    TenantId: match.TenantId,
                    Model: LlmModel.Haiku,
                    Purpose: "followup.draft",
                    SystemPrompt: SystemPrompt,
                    UserPrompt: $"Candidate: {match.Consultant.FullName}. Role: {match.Job.Title} at {match.Job.Company}. " +
                                $"This is follow-up #{sentSoFar + 1}."), ct);
                if (!TryParseDraft(resp.Text, out subject, out body))
                {
                    log.LogWarning("Follow-up draft for match {MatchId} was non-conforming; skipping this pass.", matchId);
                    continue;
                }
            }
            catch (LlmBudgetExceededException)
            {
                log.LogWarning("LLM budget exhausted mid follow-up planning; stopping at {Planned}.", planned);
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Follow-up draft failed for match {MatchId}; will retry next pass.", matchId);
                continue;
            }

            var followUp = new FollowUp
            {
                TenantId = match.TenantId,
                MatchId = matchId,
                Recipient = apply.ToAddress,
                DraftSubject = Clamp(subject, 500),
                DraftBody = Clamp(body, FollowUp.MaxBodyChars),
                Sequence = sentSoFar + 1,
                Status = opts.AutoSend ? FollowUpStatus.Approved : FollowUpStatus.Pending,
                ApprovedAt = opts.AutoSend ? now : null,
                CreatedAt = now,
            };
            db.FollowUps.Add(followUp);

            // Owner heads-up when it needs approval.
            if (!opts.AutoSend)
            {
                db.Notifications.Add(new Notification
                {
                    TenantId = match.TenantId,
                    Type = "followup",
                    Title = $"Follow-up ready to approve: {match.Job.Title} at {match.Job.Company}",
                    Body = Clamp(subject, Notification.MaxBodyChars),
                    Link = $"/matches/{matchId}",
                    MatchId = matchId,
                    CreatedAt = now,
                });
            }

            openDraftMatchIds.Add(matchId); // guard against a duplicate within this pass
            planned++;
        }

        if (planned > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Follow-up planning: {Planned} nudge(s) drafted (autoSend={Auto}).", planned, opts.AutoSend);
        return planned;
    }

    private static bool TryParseDraft(string text, out string subject, out string body)
    {
        subject = ""; body = "";
        var json = text.Trim();
        if (json.StartsWith("```"))
        {
            var nl = json.IndexOf('\n');
            if (nl > 0) json = json[(nl + 1)..];
            if (json.EndsWith("```")) json = json[..^3];
            json = json.Trim();
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            subject = root.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
            body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return !string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string Clamp(string s, int max) => s.Length <= max ? s : s[..max];
}
