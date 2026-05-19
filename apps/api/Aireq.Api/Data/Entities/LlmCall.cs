// LlmCall — audit row for one ILlmGateway invocation.
//
// Required by memory.md §11 (security & no-leaks):
//   "Every AI-generated outbound email logged (who, when, what model,
//    prompt, response)."
//
// Also serves as the source of truth for per-tenant token budgets — the gateway
// sums recent rows for the tenant + model before each call.
//
// Refs: AIRMVP1-105

namespace Aireq.Api.Data.Entities;

public sealed class LlmCall
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant the call is billed against. <c>null</c> for system calls.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Concrete model string ("claude-haiku-4-5", "claude-sonnet-4-6").</summary>
    public required string Model { get; set; }

    /// <summary>Call-site identifier ("resume.parse", "match.score", "email.draft").</summary>
    public required string Purpose { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>Estimated USD spend (Anthropic pricing × tokens). Reconciled monthly.</summary>
    public decimal CostUsdEstimate { get; set; }

    /// <summary>
    /// Full system+user prompt as sent. Truncated to <see cref="MaxPayloadChars"/>
    /// to keep the table manageable. PII handling: only the audit log retains
    /// long-term; PII purge job (AIRMVP1-403 era) clears these for free-tier
    /// users after 90 days.
    /// </summary>
    public required string PromptText { get; set; }
    public required string ResponseText { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Hard ceiling on prompt/response text stored in the audit row. 100 KB is
    /// plenty for resumes (~25 KB pre-extraction) and roomy for cold emails.
    /// </summary>
    public const int MaxPayloadChars = 100_000;
}
