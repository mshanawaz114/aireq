// ReplyClassifier — reads each newly-threaded inbound recruiter reply, asks the
// LLM (Haiku, the cheap classifier) to judge sentiment + intent, and acts on it:
//
//   1. RecruiterThread.Sentiment / RequiresHuman are updated, and LastClassifiedAt
//      is advanced to the thread's LastInboundAt watermark (so a thread is only
//      re-classified once a *newer* reply arrives).
//   2. Match.Status advances on the conversation: rejection -> Rejected,
//      interview_request -> Interview, anything else -> Reply (never regressing a
//      terminal/ahead status).
//   3. When the reply needs a human (interview, info/salary/scheduling), an
//      Escalation is raised — at most one open Escalation per match, so a chatty
//      thread doesn't spam the dashboard.
//
// Malformed model output leaves the thread untouched (re-tried next pass) — we
// never persist junk, mirroring MatchScorer.
//
// Refs: AIRMVP1-402

using System.Text.Json;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Inbound;

public sealed class ReplyClassifier(
    AireqDbContext db,
    ILlmGateway llm,
    IOptions<ReplyClassificationOptions> options,
    ILogger<ReplyClassifier> log)
{
    private const string SystemPrompt =
        """
        You triage inbound replies a recruiter or employer sent in response to a
        candidate's job application. Return ONLY a single JSON object (no prose, no
        Markdown fences):

        {
          "sentiment": "positive" | "neutral" | "negative",
          "intent": "interview_request" | "rejection" | "info_request" | "salary_question" | "scheduling" | "other",
          "requiresHuman": <true|false>,
          "summary": "<one short sentence a busy recruiter can act on>"
        }

        Guidance:
        - interview_request: they want to schedule/advance — requiresHuman = true.
        - info_request / salary_question / scheduling: they need a human reply —
          requiresHuman = true.
        - rejection: a clear no. requiresHuman = false (nothing to do).
        - other / pure auto-acknowledgements: requiresHuman = false unless a real
          question is asked.
        Be conservative: only set requiresHuman = true when a person genuinely
        needs to act.
        """;

    // Statuses we won't regress away from when a reply lands.
    private static readonly HashSet<MatchStatus> Terminal =
        [MatchStatus.Interview, MatchStatus.Rejected, MatchStatus.Closed];

    private static readonly HashSet<string> Sentiments = ["positive", "neutral", "negative"];
    private static readonly HashSet<string> Intents =
        ["interview_request", "rejection", "info_request", "salary_question", "scheduling", "other"];

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var opts = options.Value;

        // Threads with a reply newer than their last classification.
        var threads = await db.RecruiterThreads
            .IgnoreQueryFilters()
            .Where(t => t.LastInboundAt != null
                        && (t.LastClassifiedAt == null || t.LastClassifiedAt < t.LastInboundAt))
            .OrderBy(t => t.LastInboundAt)
            .Take(opts.BatchSize)
            .ToListAsync(ct);

        if (threads.Count == 0) return 0;

        // Preload the matches (for tenant + status) and jobs (for context).
        var matchIds = threads.Select(t => t.MatchId).Distinct().ToList();
        var matchById = await db.Matches.IgnoreQueryFilters()
            .Where(m => matchIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);
        var jobIds = matchById.Values.Select(m => m.JobId).Distinct().ToList();
        var jobById = await db.Jobs.IgnoreQueryFilters()
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, ct);

        // Matches that already have an open escalation — so we don't pile on.
        var openEscalationMatchIds = (await db.Escalations.IgnoreQueryFilters()
            .Where(e => matchIds.Contains(e.MatchId) && e.ResolvedAt == null)
            .Select(e => e.MatchId)
            .ToListAsync(ct)).ToHashSet();

        var classified = 0;
        foreach (var thread in threads)
        {
            if (ct.IsCancellationRequested) break;
            if (!matchById.TryGetValue(thread.MatchId, out var match)) continue;

            // Latest inbound message on this thread (the one that bumped the cursor).
            var latest = await db.Messages.IgnoreQueryFilters()
                .Where(m => m.ThreadId == thread.Id && m.Direction == MessageDirection.Inbound)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync(ct);
            if (latest is null) continue;

            jobById.TryGetValue(match.JobId, out var job);
            var userPrompt =
                $"## ROLE\n{job?.Title ?? "(unknown role)"} at {job?.Company ?? "(unknown)"}\n\n" +
                $"## REPLY\nSubject: {latest.Subject ?? "(none)"}\n\n{Clamp(latest.Body, opts.MaxBodyChars)}";

            try
            {
                var resp = await llm.CompleteAsync(new LlmRequest(
                    TenantId: match.TenantId,
                    Model: LlmModel.Haiku,
                    Purpose: "reply.classify",
                    SystemPrompt: SystemPrompt,
                    UserPrompt: userPrompt), ct);

                if (!TryParse(resp.Text, out var c))
                {
                    log.LogWarning("Thread {ThreadId}: classifier returned non-conforming JSON; leaving for retry.", thread.Id);
                    continue;
                }

                // Apply to the thread.
                thread.Sentiment = c!.Sentiment;
                thread.RequiresHuman = c.RequiresHuman;
                thread.LastClassifiedAt = thread.LastInboundAt;

                // Advance the match (never regress past a terminal/ahead status).
                if (!Terminal.Contains(match.Status))
                {
                    match.Status = c.Intent switch
                    {
                        "rejection" => MatchStatus.Rejected,
                        "interview_request" => MatchStatus.Interview,
                        _ => MatchStatus.Reply,
                    };
                }

                // Raise an escalation if a human must act and none is open yet.
                if (c.RequiresHuman && openEscalationMatchIds.Add(match.Id))
                {
                    db.Escalations.Add(new Escalation
                    {
                        MatchId = match.Id,
                        Reason = c.Intent,
                        Summary = Clamp(c.Summary, 500),
                        CreatedAt = DateTimeOffset.UtcNow,
                    });
                }

                classified++;
            }
            catch (LlmBudgetExceededException)
            {
                log.LogWarning("LLM budget exhausted mid-classification; stopping at {Classified}.", classified);
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Thread {ThreadId}: classification failed; will retry next pass.", thread.Id);
            }
        }

        if (classified > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Reply classification: {Classified}/{Batch} threads handled.", classified, threads.Count);
        return classified;
    }

    private static bool TryParse(string text, out ReplyClassification? result)
    {
        result = null;
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
            var parsed = JsonSerializer.Deserialize<ReplyClassification>(json, JsonOpts);
            if (parsed is null) return false;

            var sentiment = (parsed.Sentiment ?? "").Trim().ToLowerInvariant();
            var intent = (parsed.Intent ?? "").Trim().ToLowerInvariant();
            if (!Sentiments.Contains(sentiment)) sentiment = "neutral";
            if (!Intents.Contains(intent)) intent = "other";
            if (string.IsNullOrWhiteSpace(parsed.Summary)) return false;

            result = parsed with { Sentiment = sentiment, Intent = intent };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Clamp(string s, int max) => s.Length <= max ? s : s[..max];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
