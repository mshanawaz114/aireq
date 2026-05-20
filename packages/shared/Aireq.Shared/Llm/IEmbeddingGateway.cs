// IEmbeddingGateway — turns text into a dense vector for pgvector matching.
//
// Separate from ILlmGateway because embeddings are a different provider, model,
// and billing surface. MVP uses Google Gemini text-embedding-004 (free tier,
// 768 dims). Swap providers behind this interface without touching callers —
// but note the vector COLUMN dimension (EmbeddingConfig.Dimensions) is baked
// into the schema, so a provider with a different dimension needs a migration.
//
// Refs: AIRMVP1-204

namespace Aireq.Shared.Llm;

public interface IEmbeddingGateway
{
    /// <summary>Embedding length this provider returns; must equal EmbeddingConfig.Dimensions.</summary>
    int Dimensions { get; }

    /// <summary>False when the provider isn't configured (no API key) — embedders
    /// skip with a log line instead of throwing, so you can add the key later.</summary>
    bool IsConfigured { get; }

    /// <summary>Embed a single text into a fixed-length vector.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

public static class EmbeddingConfig
{
    /// <summary>
    /// Vector dimension stored in pgvector columns. 768 = Gemini text-embedding-004.
    /// Changing this requires an ALTER on jobs.embedding + resumes.embedding.
    /// </summary>
    public const int Dimensions = 768;
}
