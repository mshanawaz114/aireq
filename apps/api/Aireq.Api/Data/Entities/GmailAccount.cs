// GmailAccount — a tenant's connected Gmail mailbox + OAuth tokens.
//
// One row per tenant (the owner connects their inbox once). Stores the refresh
// token (long-lived) and a cached access token; the worker refreshes the access
// token when it's near expiry. LastHistoryId is Gmail's incremental-sync cursor
// so each poll only fetches what's new.
//
// Tokens are secrets at rest — encrypted by the storage layer in production
// (Key Vault / column encryption); for MVP they live in this row. PII purge +
// at-rest encryption tighten in the GA security pass.
//
// Refs: AIRMVP1-401

namespace Aireq.Api.Data.Entities;

public sealed class GmailAccount : Common.ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>The connected mailbox address (from the OAuth profile).</summary>
    public required string EmailAddress { get; set; }

    /// <summary>Long-lived OAuth refresh token (offline access).</summary>
    public required string RefreshToken { get; set; }

    /// <summary>Most-recent access token; refreshed when near expiry.</summary>
    public string? AccessToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    /// <summary>Gmail incremental-sync cursor; null until the first poll.</summary>
    public string? LastHistoryId { get; set; }

    /// <summary>When the poller last ran for this account.</summary>
    public DateTimeOffset? LastPolledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
