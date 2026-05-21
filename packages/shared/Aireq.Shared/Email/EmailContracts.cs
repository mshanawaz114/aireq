// Email contracts (AIRMVP1-305). Shared so any project can describe an email to
// send; the actual sender + throttle live in the worker.
//
// Refs: AIRMVP1-305

namespace Aireq.Shared.Email;

/// <param name="TenantId">Tenant the send is attributed + throttled against. Null for system mail.</param>
/// <param name="Purpose">Call-site tag: "apply" | "followup" | "digest".</param>
/// <param name="CorrelationMatchId">Match this email relates to, so an inbound reply threads back to it.</param>
public sealed record EmailMessage(
    Guid? TenantId,
    string To,
    string Subject,
    string HtmlBody,
    string Purpose,
    byte[]? Attachment = null,
    string? AttachmentName = null,
    Guid? CorrelationMatchId = null);

/// <param name="Status">sent | dry_run | throttled | failed.</param>
public sealed record EmailResult(string Status, string? ProviderMessageId)
{
    public static EmailResult Sent(string? id) => new("sent", id);
    public static EmailResult DryRun() => new("dry_run", null);
    public static EmailResult Throttled() => new("throttled", null);
    public static EmailResult Failed() => new("failed", null);
}

public interface IEmailSender
{
    /// <summary>
    /// Send (or, when not live / not configured, dry-run) the message, enforcing
    /// the per-tenant warmup daily cap and writing an EmailLog audit row.
    /// </summary>
    Task<EmailResult> SendAsync(EmailMessage message, bool live, CancellationToken ct = default);
}
