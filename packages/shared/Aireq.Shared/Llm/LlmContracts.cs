// LLM request / response DTOs + model enum.
//
// The gateway intentionally accepts only a single user prompt in v1 — multi-turn
// and tool-use slot in later. The model enum maps to whatever the operator has
// configured in env vars (ANTHROPIC_MODEL_FAST / STRONG), defaulting to the
// versions pinned in memory.md (haiku-4-5 + sonnet-4-6).
//
// Refs: AIRMVP1-105

namespace Aireq.Shared.Llm;

public enum LlmModel
{
    /// <summary>Cheap, structured-output workhorse. Resume parsing, classification.</summary>
    Haiku,

    /// <summary>Quality model. Resume rewriting, cold email drafting.</summary>
    Sonnet,
}

/// <summary>
/// One request to the LLM. Multi-turn conversations and tool use slot in later;
/// v1 keeps it to a single user message + system prompt.
/// </summary>
/// <param name="TenantId">
/// Tenant the call is billed against. <c>null</c> for system / unattributed
/// calls (e.g. ingestion classifiers). Per-tenant budget caps apply only when
/// this is set.
/// </param>
/// <param name="Purpose">
/// Stable identifier of the calling code path, for cost analytics
/// (e.g. <c>"resume.parse"</c>, <c>"match.score"</c>, <c>"email.draft"</c>).
/// </param>
public sealed record LlmRequest(
    Guid? TenantId,
    LlmModel Model,
    string Purpose,
    string SystemPrompt,
    string UserPrompt,
    int MaxOutputTokens = 4096);

/// <param name="CostUsdEstimate">
/// Estimated USD cost based on the model's published per-token pricing.
/// Stored for cost analytics and budget enforcement; actual Anthropic billing
/// is reconciled monthly via the console.
/// </param>
public sealed record LlmResponse(
    string Text,
    int InputTokens,
    int OutputTokens,
    decimal CostUsdEstimate,
    string ModelName);
