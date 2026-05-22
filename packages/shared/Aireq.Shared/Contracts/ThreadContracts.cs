// Recruiter thread DTOs for the Inbox UI (AIRMVP1-401 read side).
// Refs: AIRMVP1-401

namespace Aireq.Shared.Contracts;

/// <param name="Direction">Inbound | Outbound.</param>
public sealed record ThreadMessageResponse(
    Guid Id,
    string Direction,
    string? Subject,
    string Body,
    DateTimeOffset SentAt,
    bool GeneratedByAi);

/// <param name="Sentiment">positive | neutral | negative — null until classified.</param>
public sealed record ThreadResponse(
    Guid Id,
    Guid MatchId,
    string JobTitle,
    string Company,
    string RecruiterEmail,
    string? RecruiterName,
    string? Sentiment,
    bool RequiresHuman,
    DateTimeOffset? LastInboundAt,
    IReadOnlyList<ThreadMessageResponse> Messages);
