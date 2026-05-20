// EmbeddingOptions — batch size + cadence for the embedding pass.
// Refs: AIRMVP1-204

namespace Aireq.Worker.Matching;

public sealed class EmbeddingOptions
{
    public const string ConfigKey = "EMBEDDING";

    /// <summary>Cron for the embedding pass. Default: hourly (free-tier friendly).</summary>
    public string Cron { get; set; } = "15 * * * *";

    /// <summary>Max rows embedded per pass per entity, to stay inside free-tier
    /// rate limits (Gemini free: ~1500 req/day). 100/hour ≈ 2400/day across
    /// jobs + resumes — tune down if you hit limits.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Max characters fed to the embedder (cost + token-limit guard).</summary>
    public int MaxChars { get; set; } = 8_000;
}
