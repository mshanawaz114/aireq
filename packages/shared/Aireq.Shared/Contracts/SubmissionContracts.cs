// Submission DTO for the tracker UI (AIRMVP1-306).
// Refs: AIRMVP1-306

namespace Aireq.Shared.Contracts;

public sealed record SubmissionResponse(
    Guid Id,
    Guid MatchId,
    string JobTitle,
    string Company,
    string Channel,
    string? ResponseStatus,
    DateTimeOffset SubmittedAt,
    string? ResponsePayloadJson);
