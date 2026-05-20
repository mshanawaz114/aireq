// MatchReasoning — the structured rationale the LLM scorer produces and the
// Matches UI (AIRMVP1-206) consumes. Stored verbatim as JSON in
// Match.ReasoningJson; lives in shared so producer (worker) and consumer
// (API/web) agree on the shape.
//
// Refs: AIRMVP1-205

namespace Aireq.Shared.Contracts;

public sealed record MatchReasoning(
    int Score,
    string Summary,
    IReadOnlyList<string> Rationale,
    IReadOnlyList<string> MissingKeywords);
