// FollowUp DTOs for the approval queue UI (AIRMVP1-404).
// Refs: AIRMVP1-404

namespace Aireq.Shared.Contracts;

/// <param name="Status">Pending | Approved | Sent | Cancelled | Failed.</param>
/// <param name="Sequence">1 = first nudge, 2 = second, …</param>
public sealed record FollowUpResponse(
    Guid Id,
    Guid MatchId,
    string JobTitle,
    string Company,
    string Recipient,
    string DraftSubject,
    string DraftBody,
    int Sequence,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);
