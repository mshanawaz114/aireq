// InboundMessageProcessor — correlate inbound Gmail replies to the Match they
// belong to and thread them.
//
// Pure persistence logic (db only, no network) so it's fully unit-testable on
// EF InMemory. The GmailInboundPoller fetches messages via IGmailClient and
// hands them here.
//
// Correlation rule: when we email an application out (EmailSubmissionChannel),
// the EmailLog row records CorrelationMatchId + the recruiter's address as
// ToAddress. A reply arrives FROM that address into the tenant owner's connected
// mailbox — so we resolve the Match by finding the most-recent apply EmailLog
// for this tenant whose recipient equals the reply's sender. No match => the
// message isn't a reply to one of our applies, so we leave it alone.
//
// Idempotent: every inbound is keyed by its Gmail message id (Message.
// ProviderMessageId); a message already threaded is skipped, so re-polling the
// same window never double-inserts.
//
// Refs: AIRMVP1-401

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Worker.Inbound;

public sealed class InboundMessageProcessor(
    AireqDbContext db,
    ILogger<InboundMessageProcessor> log)
{
    /// <summary>
    /// Correlate + thread a batch of inbound messages for one connected mailbox.
    /// Returns the count newly threaded (after dedupe + correlation filtering).
    /// </summary>
    public async Task<int> ProcessAsync(
        GmailAccount account, IReadOnlyList<InboundEmail> emails, CancellationToken ct = default)
    {
        var tenantId = account.TenantId;
        var threaded = 0;

        foreach (var email in emails)
        {
            ct.ThrowIfCancellationRequested();

            // 1. Dedupe — already threaded this Gmail message?
            var seen = await db.Messages.IgnoreQueryFilters()
                .AnyAsync(m => m.ProviderMessageId == email.ProviderMessageId, ct);
            if (seen) continue;

            // 2. Correlate sender -> Match via the apply EmailLog we wrote.
            var matchId = await db.EmailLogs
                .Where(e => e.TenantId == tenantId
                            && e.CorrelationMatchId != null
                            && e.ToAddress == email.FromEmail)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => e.CorrelationMatchId)
                .FirstOrDefaultAsync(ct);

            if (matchId is null)
            {
                log.LogDebug("Inbound from {From} has no correlating apply email for tenant {Tenant}; skipping.",
                    email.FromEmail, tenantId);
                continue;
            }

            // 3. Guard: the Match must still belong to this tenant.
            var matchOk = await db.Matches.IgnoreQueryFilters()
                .AnyAsync(m => m.Id == matchId && m.TenantId == tenantId, ct);
            if (!matchOk)
            {
                log.LogWarning("Correlated match {Match} not found for tenant {Tenant}; skipping inbound {Msg}.",
                    matchId, tenantId, email.ProviderMessageId);
                continue;
            }

            // 4. Upsert the thread for (match, recruiter address).
            var thread = await db.RecruiterThreads.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.MatchId == matchId && t.RecruiterEmail == email.FromEmail, ct);
            if (thread is null)
            {
                thread = new RecruiterThread
                {
                    MatchId = matchId.Value,
                    RecruiterEmail = email.FromEmail,
                    RecruiterName = email.FromName,
                };
                db.RecruiterThreads.Add(thread);
            }
            else if (thread.RecruiterName is null && email.FromName is not null)
            {
                thread.RecruiterName = email.FromName;
            }
            thread.LastInboundAt = email.ReceivedAt;

            // 5. Append the inbound message (audit + provenance).
            db.Messages.Add(new Message
            {
                ThreadId = thread.Id,
                Direction = MessageDirection.Inbound,
                Subject = Truncate(email.Subject, 500),
                Body = string.IsNullOrWhiteSpace(email.Body)
                    ? "(empty body)"
                    : email.Body.Length <= 50_000 ? email.Body : email.Body[..50_000],
                SentAt = email.ReceivedAt,
                ProviderMessageId = email.ProviderMessageId,
                GeneratedByAi = false,
            });
            threaded++;
        }

        if (threaded > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Threaded {Count} inbound repl{Suffix} for tenant {Tenant}.",
                threaded, threaded == 1 ? "y" : "ies", tenantId);
        }

        return threaded;
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];
}
