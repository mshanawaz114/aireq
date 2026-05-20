// Admin metrics DTOs (AIRMVP1-207). Aggregated from existing tables — there's
// no separate events table in v1; the domain rows + the llm_calls audit log
// already are the event record, so metrics are derived counts.
//
// Scope: job pool is global (shared across tenants); matches, resumes, and LLM
// spend are scoped to the current tenant.
//
// Refs: AIRMVP1-207

namespace Aireq.Shared.Contracts;

public sealed record MetricsResponse(
    JobMetrics Jobs,
    MatchMetrics Matches,
    ResumeMetrics Resumes,
    LlmMetrics Llm,
    DateTimeOffset GeneratedAt);

public sealed record JobMetrics(
    int Total,
    int Active,
    int Embedded,
    IReadOnlyDictionary<string, int> BySource);

public sealed record MatchMetrics(
    int Total,
    int New,
    int Reasoned,
    double AvgScore);

public sealed record ResumeMetrics(
    int Total,
    int Parsed,
    int Embedded);

public sealed record LlmMetrics(
    int Calls,
    decimal CostUsd,
    IReadOnlyDictionary<string, int> ByPurpose);
