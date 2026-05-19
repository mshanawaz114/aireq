// LlmBudgetOptions — per-tenant per-month token caps and per-1M-token pricing.
//
// Defaults match the numbers locked in memory.md §9:
//   Haiku  — 250k input / 50k output / month / tenant
//   Sonnet —  80k input / 20k output / month / tenant
//
// Override via .env.local with LLM__BUDGET__... keys (IConfiguration converts
// __ to : at bind time).
//
// Pricing defaults are Anthropic's published rates as of May 2026; refresh
// when they change. Used only for our internal cost estimates — actual billing
// is reconciled monthly via the Anthropic console.
//
// Refs: AIRMVP1-105

namespace Aireq.Shared.Llm;

public sealed class LlmBudgetOptions
{
    public const string ConfigKey = "LLM:BUDGET";

    public LlmModelBudget Haiku { get; set; } = new()
    {
        InputTokensPerMonth = 250_000,
        OutputTokensPerMonth = 50_000,
        InputUsdPerMillion = 0.25m,
        OutputUsdPerMillion = 1.25m,
    };

    public LlmModelBudget Sonnet { get; set; } = new()
    {
        InputTokensPerMonth = 80_000,
        OutputTokensPerMonth = 20_000,
        InputUsdPerMillion = 3m,
        OutputUsdPerMillion = 15m,
    };

    public LlmModelBudget For(LlmModel model) => model switch
    {
        LlmModel.Haiku => Haiku,
        LlmModel.Sonnet => Sonnet,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown LLM model."),
    };
}

public sealed class LlmModelBudget
{
    public long InputTokensPerMonth { get; set; }
    public long OutputTokensPerMonth { get; set; }
    public decimal InputUsdPerMillion { get; set; }
    public decimal OutputUsdPerMillion { get; set; }

    public decimal EstimateUsd(int inputTokens, int outputTokens) =>
        (inputTokens / 1_000_000m) * InputUsdPerMillion
        + (outputTokens / 1_000_000m) * OutputUsdPerMillion;
}
