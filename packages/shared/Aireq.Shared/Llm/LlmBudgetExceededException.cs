// Thrown by ILlmGateway implementations when a tenant has already used its
// monthly budget for the requested model class. Carries the numbers so the
// caller can decide whether to surface them or fail silently.
//
// Refs: AIRMVP1-105

namespace Aireq.Shared.Llm;

public sealed class LlmBudgetExceededException : Exception
{
    public LlmBudgetExceededException(
        Guid? tenantId,
        LlmModel model,
        long inputTokensUsed,
        long inputTokensCap,
        long outputTokensUsed,
        long outputTokensCap)
        : base(
            $"Tenant {tenantId?.ToString() ?? "(unattributed)"} has exceeded its monthly " +
            $"{model} budget — input {inputTokensUsed}/{inputTokensCap}, " +
            $"output {outputTokensUsed}/{outputTokensCap}.")
    {
        TenantId = tenantId;
        Model = model;
        InputTokensUsed = inputTokensUsed;
        InputTokensCap = inputTokensCap;
        OutputTokensUsed = outputTokensUsed;
        OutputTokensCap = outputTokensCap;
    }

    public Guid? TenantId { get; }
    public LlmModel Model { get; }
    public long InputTokensUsed { get; }
    public long InputTokensCap { get; }
    public long OutputTokensUsed { get; }
    public long OutputTokensCap { get; }
}
