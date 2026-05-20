// GeminiEmbeddingGateway — Google Gemini text-embedding-004 (free tier).
//
//   POST https://generativelanguage.googleapis.com/v1beta/models/{model}:embedContent?key={key}
//   body { "model": "models/text-embedding-004", "content": { "parts": [ { "text": "..." } ] } }
//   → { "embedding": { "values": [ <768 floats> ] } }
//
// Config:
//   GEMINI_API_KEY            — required, else IsConfigured=false (embedders skip)
//   GEMINI_EMBEDDING_MODEL    — default text-embedding-004
//
// Refs: AIRMVP1-204
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Aireq.Shared.Llm;

namespace Aireq.Worker.Llm;

public sealed class GeminiEmbeddingGateway(
    HttpClient http,
    IConfiguration config,
    ILogger<GeminiEmbeddingGateway> log) : IEmbeddingGateway
{
    private const string DefaultModel = "text-embedding-004";

    public int Dimensions => EmbeddingConfig.Dimensions;

    private string? ApiKey => config["GEMINI_API_KEY"];
    private string Model => config["GEMINI_EMBEDDING_MODEL"] ?? DefaultModel;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey!.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "GEMINI_API_KEY is not set. Get a free key at aistudio.google.com and add it to .env.local.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent?key={Uri.EscapeDataString(ApiKey!)}";
        var payload = new EmbedRequest($"models/{Model}", new Content(new[] { new Part(text) }));

        using var resp = await http.PostAsJsonAsync(url, payload, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogError("Gemini embed failed: {Status} — {Body}", (int)resp.StatusCode, body);
            throw new HttpRequestException(
                $"Gemini embedding returned {(int)resp.StatusCode}.", null, resp.StatusCode);
        }

        var parsed = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
        var values = parsed?.Embedding?.Values;
        if (values is null || values.Length == 0)
            throw new InvalidOperationException("Gemini returned an empty embedding.");
        if (values.Length != Dimensions)
            throw new InvalidOperationException(
                $"Gemini returned {values.Length} dims but schema expects {Dimensions}. " +
                "Did the embedding model change? Update EmbeddingConfig.Dimensions + migrate.");

        return values;
    }

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("content")] Content Content);

    private sealed record Content([property: JsonPropertyName("parts")] Part[] Parts);

    private sealed record Part([property: JsonPropertyName("text")] string Text);

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embedding")] public EmbeddingBody? Embedding { get; set; }
    }

    private sealed class EmbeddingBody
    {
        [JsonPropertyName("values")] public float[]? Values { get; set; }
    }
}
