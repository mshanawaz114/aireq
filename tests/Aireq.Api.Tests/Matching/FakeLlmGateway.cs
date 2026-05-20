// FakeLlmGateway — deterministic ILlmGateway for scorer tests. Returns a
// configurable text response and records the requests it received.
//
// Refs: AIRMVP1-205

using Aireq.Shared.Llm;

namespace Aireq.Api.Tests.Matching;

public sealed class FakeLlmGateway(string responseText) : ILlmGateway
{
    public List<LlmRequest> Requests { get; } = new();

    /// <summary>Optionally throw on the Nth call (1-based) to simulate budget exhaustion.</summary>
    public Exception? ThrowOnCall { get; set; }
    public int ThrowAtCallNumber { get; set; } = -1;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (ThrowOnCall is not null && Requests.Count == ThrowAtCallNumber)
            throw ThrowOnCall;
        return Task.FromResult(new LlmResponse(responseText, 100, 50, 0m, "fake-model"));
    }
}
