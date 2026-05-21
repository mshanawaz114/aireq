// GmailInboundPoller — recurring Hangfire job that pulls inbound recruiter
// replies for every connected mailbox and threads them.
//
// Per pass, for each GmailAccount:
//   1. IGmailClient.PollAsync → refresh token + fetch messages since the cursor.
//   2. InboundMessageProcessor.ProcessAsync → correlate + thread (idempotent).
//   3. Persist the advanced cursor + refreshed token + LastPolledAt.
//
// One account's failure (revoked token, transient Gmail 5xx) is logged and
// skipped so it never stalls the others. Self-no-ops when Gmail OAuth isn't
// configured (the client returns empty).
//
// Refs: AIRMVP1-401

using Aireq.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Inbound;

public interface IGmailInboundRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class GmailInboundPoller(
    AireqDbContext db,
    IGmailClient gmail,
    InboundMessageProcessor processor,
    IOptions<GmailInboundOptions> options,
    ILogger<GmailInboundPoller> log) : IGmailInboundRunner
{
    [Queue("email")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var accounts = await db.GmailAccounts.IgnoreQueryFilters().ToListAsync(ct);
        if (accounts.Count == 0)
        {
            log.LogInformation("Gmail inbound poll: no connected mailboxes.");
            return;
        }

        var max = options.Value.MaxPerPoll;
        var totalThreaded = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await gmail.PollAsync(account, max, ct);
                if (result.Messages.Count > 0)
                    totalThreaded += await processor.ProcessAsync(account, result.Messages, ct);

                if (result.NewCursor is not null)
                    account.LastHistoryId = result.NewCursor;
                account.LastPolledAt = DateTimeOffset.UtcNow;

                // Persist the refreshed access token + cursor + poll timestamp.
                // (Token fields were mutated in-place by EnsureAccessTokenAsync.)
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Gmail inbound poll failed for {Email} (tenant {Tenant}); continuing.",
                    account.EmailAddress, account.TenantId);
            }
        }

        log.LogInformation("Gmail inbound poll done — {Accounts} mailbox(es), {Threaded} new repl(ies) threaded.",
            accounts.Count, totalThreaded);
    }
}
