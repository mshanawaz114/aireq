// FollowUp — a planned nudge email to a recruiter on a submitted application
// that hasn't replied yet.
//
// Owner-approval is the default: the planner drafts the nudge and parks it in
// Pending; a human approves it (API) before it sends. Only when the auto-send
// flag is on does the planner create it pre-Approved. The sender pass turns
// Approved -> Sent through the throttled/dry-run IEmailSender.
//
// Rate limiting lives in the planner (max nudges per match + min gap between
// outbound), backed by the per-tenant warmup cap in the email sender.
//
// Tenant-scoped via the global query filter, like Match.
//
// Refs: AIRMVP1-404

namespace Aireq.Api.Data.Entities;

public sealed class FollowUp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MatchId { get; set; }

    /// <summary>Recipient (the address we applied to).</summary>
    public required string Recipient { get; set; }

    public required string DraftSubject { get; set; }
    public required string DraftBody { get; set; }

    /// <summary>1 for the first nudge, 2 for the second, … (rate-limit key).</summary>
    public int Sequence { get; set; }

    public FollowUpStatus Status { get; set; } = FollowUpStatus.Pending;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>Set when a send attempt failed, for the dashboard.</summary>
    public string? FailureReason { get; set; }

    public const int MaxBodyChars = 10_000;

    // Navigation
    public Match Match { get; set; } = null!;
}

public enum FollowUpStatus
{
    /// <summary>Drafted, awaiting owner approval.</summary>
    Pending = 0,

    /// <summary>Approved (by a human, or auto) — the sender will pick it up.</summary>
    Approved = 1,

    /// <summary>Successfully sent (or dry-run sent).</summary>
    Sent = 2,

    /// <summary>Owner declined / no longer relevant (e.g. a reply arrived).</summary>
    Cancelled = 3,

    /// <summary>Send attempt failed (throttled / provider error).</summary>
    Failed = 4,
}
