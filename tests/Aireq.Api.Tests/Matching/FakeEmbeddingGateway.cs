// FakeEmbeddingGateway — deterministic IEmbeddingGateway for embedder/matching
// tests. Records each text it was asked to embed and returns a fixed vector.
//
// Refs: AIRMVP1-204

using Aireq.Shared.Llm;

namespace Aireq.Api.Tests.Matching;

public sealed class FakeEmbeddingGateway(bool configured = true) : IEmbeddingGateway
{
    public int Dimensions => EmbeddingConfig.Dimensions;
    public bool IsConfigured => configured;

    public List<string> Embedded { get; } = new();

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        Embedded.Add(text);
        var vec = new float[Dimensions];
        // Cheap deterministic content so different inputs differ a little.
        vec[0] = text.Length;
        return Task.FromResult(vec);
    }
}
