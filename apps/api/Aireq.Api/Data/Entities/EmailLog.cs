// EmailLog — audit row for every outbound email the system sends or would send.
//
// Serves three jobs:
//   - Audit (memory.md §11: log every AI-generated outbound email — who, when,
//     what, status).
//   - Warmup throttling: count today's *sent* rows per tenant against the daily
//     cap to protect a new sending domain's reputation (memory.md §14).
//   - Deliverability tracking (provider message id for later webhook reconcile).
//
// Reused beyond submissions by follow-ups + digests in Week 4.
//
// Refs: AIRMVP1-305

namespace Aireq.Api.Data.Entities;

public sealed class EmailLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant the send is attributed to. Null for system mail.</summary>
    public Guid? TenantId { get; set; }

    public required string ToAddress { get; set; }
    public required string Subject { get; set; }

    /// <summary>Call-site purpose: "apply" | "followup" | "digest" | ...</summary>
    public required string Purpose { get; set; }

    /// <summary>sent | dry_run | throttled | failed.</summary>
    public required string Status { get; set; }

    /// <summary>Provider (Resend) message id when sent; null otherwise.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>The Match this email relates to (apply emails), so an inbound
    /// reply from the recipient can be threaded back to it. (AIRMVP1-401)</summary>
    public Guid? CorrelationMatchId { get; set; }

    /// <summary>Body, truncated to <see cref="MaxBodyChars"/> for the audit row.</summary>
    public string? Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public const int MaxBodyChars = 20_000;
}
