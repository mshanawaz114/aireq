// IGmailClient — the seam over Gmail's REST API for inbound polling.
//
// Two responsibilities, deliberately behind an interface so the poller's
// orchestration is testable with a fake and the raw-HTTPS plumbing lives in
// one place (GmailClient):
//   - EnsureAccessTokenAsync: refresh the short-lived access token from the
//     long-lived refresh token when it's missing or near expiry.
//   - PollAsync: fetch inbound messages newer than the account's cursor and
//     return them parsed, plus the advanced cursor.
//
// Correlation (sender -> Match) and persistence are NOT here — that's the pure,
// db-only InboundMessageProcessor, which this feeds.
//
// Refs: AIRMVP1-401

using Aireq.Api.Data.Entities;

namespace Aireq.Worker.Inbound;

/// <param name="ProviderMessageId">Gmail message id — the inbound dedupe key.</param>
/// <param name="ThreadId">Gmail thread id (provider-side), kept for diagnostics.</param>
/// <param name="FromEmail">Parsed sender address, lower-cased.</param>
/// <param name="FromName">Sender display name, if the From header carried one.</param>
/// <param name="Subject">Subject header (may be empty).</param>
/// <param name="Body">Best-effort plain-text body (decoded from the payload).</param>
/// <param name="ReceivedAt">Gmail internalDate (when the mailbox received it).</param>
public sealed record InboundEmail(
    string ProviderMessageId,
    string ThreadId,
    string FromEmail,
    string? FromName,
    string Subject,
    string Body,
    DateTimeOffset ReceivedAt);

/// <param name="Messages">Inbound messages fetched this poll, oldest first.</param>
/// <param name="NewCursor">Cursor to persist on the account (max received epoch,
/// as a string), or null to leave the cursor unchanged.</param>
public sealed record GmailPollResult(IReadOnlyList<InboundEmail> Messages, string? NewCursor);

public interface IGmailClient
{
    /// <summary>
    /// Returns a currently-valid access token for the account, refreshing it via
    /// the refresh token when null or within the expiry skew. Mutates the passed
    /// <paramref name="account"/> (AccessToken / AccessTokenExpiresAt) so the
    /// caller can persist the refreshed token.
    /// </summary>
    Task<string> EnsureAccessTokenAsync(GmailAccount account, CancellationToken ct = default);

    /// <summary>
    /// Fetch inbound messages newer than the account's cursor (LastHistoryId,
    /// holding a unix-seconds marker). On the first poll (null cursor) it scans a
    /// short recent window so freshly-arrived replies aren't missed.
    /// </summary>
    Task<GmailPollResult> PollAsync(GmailAccount account, int maxMessages, CancellationToken ct = default);
}
