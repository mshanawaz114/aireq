// GroqLlmGateway — free-tier implementation of ILlmGateway, talks to Groq's
// OpenAI-compatible Chat Completions endpoint.
//
// Why Groq for MVP: their free tier (30 req/min, 6000 req/day on free models)
// is more than enough to run the owner-as-only-user flow at zero spend until
// paying customers arrive. Quality on JSON-extraction tasks (resume parsing,
// match scoring, classification) is close enough to Claude Haiku to be
// acceptable; flip LLM__PROVIDER=anthropic when there's revenue.
//
// Model mapping:
//   LlmModel.Haiku   → llama-3.1-8b-instant   (default; override GROQ_MODEL_FAST)
//   LlmModel.Sonnet  → llama-3.3-70b-versatile (default; override GROQ_MODEL_STRONG)
//
// Config: GROQ_API_KEY (required), GROQ_MODEL_FAST, GROQ_MODEL_STRONG.
//
// NOTE (history): this gateway + the LLM__PROVIDER switch were authored in
// AIRMVP1-105 but dropped from that PR's merge; restored on the AIRMVP1-201
// branch so the free-tier path works for W2 match scoring.
//
// Refs: AIRMVP1-105, AIRMVP1-201

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Llm;

public sealed class GroqLlmGateway(
    HttpClient http,
    AireqDbContext db,
    IOptions<LlmBudgetOptions> budget,
    IConfiguration config,
    ILogger<GroqLlmGateway> log) : ILlmGateway
{
    private const string DefaultFast = "llama-3.1-8b-instant";
    private const string DefaultStrong = "llama-3.3-70b-versatile";
    private const string BaseUrl = "https://api.groq.com/openai/v1/chat/completions";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var modelName = ResolveModelName(request.Model);
        var modelBudget = budget.Value.For(request.Model);

        if (request.TenantId is Guid tenantId)
        {
            var monthStart = StartOfCurrentMonthUtc();
            var usage = await db.LlmCalls
                .Where(c => c.TenantId == tenantId && c.Model == modelName && c.CreatedAt >= monthStart)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Input = g.Sum(c => (long)c.InputTokens),
                    Output = g.Sum(c => (long)c.OutputTokens),
                })
                .FirstOrDefaultAsync(ct);

            var inputUsed = usage?.Input ?? 0;
            var outputUsed = usage?.Output ?? 0;
            if (inputUsed >= modelBudget.InputTokensPerMonth || outputUsed >= modelBudget.OutputTokensPerMonth)
            {
                throw new LlmBudgetExceededException(
                    tenantId, request.Model,
                    inputUsed, modelBudget.InputTokensPerMonth,
                    outputUsed, modelBudget.OutputTokensPerMonth);
            }
        }

        var apiKey = config["GROQ_API_KEY"]
            ?? throw new InvalidOperationException(
                "GROQ_API_KEY is not set. Sign up at console.groq.com (free) and add it to .env.local.");

        var payload = new GroqRequestBody(
            modelName,
            request.MaxOutputTokens,
            new[]
            {
                new GroqMessage("system", request.SystemPrompt),
                new GroqMessage("user", request.UserPrompt),
            });

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var httpResp = await http.SendAsync(httpReq, ct);
        if (!httpResp.IsSuccessStatusCode)
        {
            var body = await httpResp.Content.ReadAsStringAsync(ct);
            log.LogError("Groq call failed: {Status} {Reason} — body={Body}",
                (int)httpResp.StatusCode, httpResp.ReasonPhrase, body);
            throw new HttpRequestException(
                $"Groq returned {(int)httpResp.StatusCode} ({httpResp.ReasonPhrase}).",
                inner: null, statusCode: httpResp.StatusCode);
        }

        var body2 = await httpResp.Content.ReadFromJsonAsync<GroqResponseBody>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Groq returned an empty body.");

        var text = body2.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        var inputTokens = body2.Usage?.PromptTokens ?? 0;
        var outputTokens = body2.Usage?.CompletionTokens ?? 0;
        var cost = modelBudget.EstimateUsd(inputTokens, outputTokens);

        db.LlmCalls.Add(new LlmCall
        {
            TenantId = request.TenantId,
            Model = modelName,
            Purpose = request.Purpose,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsdEstimate = cost,
            PromptText = Truncate($"## SYSTEM\n{request.SystemPrompt}\n\n## USER\n{request.UserPrompt}"),
            ResponseText = Truncate(text),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "LLM call OK (Groq) — tenant={TenantId} model={Model} purpose={Purpose} in={In} out={Out}",
            request.TenantId, modelName, request.Purpose, inputTokens, outputTokens);

        return new LlmResponse(text, inputTokens, outputTokens, cost, modelName);
    }

    private string ResolveModelName(LlmModel model) => model switch
    {
        LlmModel.Haiku => config["GROQ_MODEL_FAST"] ?? DefaultFast,
        LlmModel.Sonnet => config["GROQ_MODEL_STRONG"] ?? DefaultStrong,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown model."),
    };

    private static string Truncate(string s) =>
        s.Length <= LlmCall.MaxPayloadChars ? s : s[..LlmCall.MaxPayloadChars];

    private static DateTimeOffset StartOfCurrentMonthUtc()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record GroqRequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<GroqMessage> Messages);

    private sealed record GroqMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class GroqResponseBody
    {
        [JsonPropertyName("choices")] public List<GroqChoice>? Choices { get; set; }
        [JsonPropertyName("usage")] public GroqUsage? Usage { get; set; }
    }

    private sealed class GroqChoice
    {
        [JsonPropertyName("message")] public GroqMessage? Message { get; set; }
    }

    private sealed class GroqUsage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    }
}
