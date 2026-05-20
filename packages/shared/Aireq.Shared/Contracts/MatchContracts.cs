// Match DTOs for the Matches UI (AIRMVP1-206). Flattens the Match + its Job +
// the deserialized MatchReasoning into one row the web list renders.
//
// Refs: AIRMVP1-206

namespace Aireq.Shared.Contracts;

/// <param name="Score">0–100. LLM score once reasoned, else the cosine vector score.</param>
/// <param name="Reasoned">True once the LLM scorer produced reasoning; false = vector-only.</param>
public sealed record MatchResponse(
    Guid Id,
    Guid JobId,
    string JobTitle,
    string Company,
    string? Location,
    DateTimeOffset PostedAt,
    int Score,
    string Status,
    bool Reasoned,
    string? Summary,
    IReadOnlyList<string> Rationale,
    IReadOnlyList<string> MissingKeywords);
