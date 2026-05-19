// ILlmGateway — single chokepoint for every LLM call in the system.
//
// Hard guardrail (memory.md §11): never bypass this — direct SDK calls escape
// cost caps, escape audit logging, and break our P0 promise that the per-tenant
// budget cap is enforced.
//
// Implementations:
//   - AnthropicLlmGateway (apps/worker) — production path against api.anthropic.com.
//   - FakeLlmGateway (tests) — deterministic responses.
//
// Refs: AIRMVP1-105

namespace Aireq.Shared.Llm;

public interface ILlmGateway
{
    /// <summary>
    /// Send a prompt to the configured LLM, enforce the tenant's monthly budget
    /// before the call, audit-log the input + output afterwards, and return the
    /// usage-annotated response.
    /// </summary>
    /// <exception cref="LlmBudgetExceededException">
    /// Thrown when the tenant has already used its monthly token budget for the
    /// requested model class. The call is NOT sent to the provider.
    /// </exception>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
