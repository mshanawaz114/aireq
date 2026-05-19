// AnthropicLlmGateway — production implementation of ILlmGateway.
//
// Behaviour:
//   1. Sum existing LlmCall tokens for (tenant, model, current calendar month).
//   2. If usage already exceeds the configured cap, throw LlmBudgetExceededException
//      BEFORE issuing the network call. Caller can swallow → escalate → fall back.
//   3. POST to https://api.anthropic.com/v1/messages — raw HTTP rather than the
//      SDK so we keep one HTTP client to instrument with Polly + OTel later
//      (AIRMVP1-130 era) and so unit tests can hand-roll responses.
//   4. Persist a LlmCall row with the prompts (truncated to LlmCall.MaxPayloadChars),
//      tokens, and estimated cost.
//   5. Return the response with usage annotated.
//
// Config keys (all via IConfiguration → can be env vars, .env.local, or KeyVault):
//   ANTHROPIC_API_KEY            — required at first call
//   ANTHROPIC_MODEL_FAST         — model id for LlmModel.Haiku (default claude-haiku-4-5)
//   ANTHROPIC_MODEL_STRONG       — model id for LlmModel.Sonnet (default claude-sonnet-4-6)
//   LLM__BUDGET__HAIKU__*        — bound to LlmBudgetOptions.Haiku
//   LLM__BUDGET__SONNET__*       — bound to LlmBudgetOptions.Sonnet
//
// Refs: AIRMVP1-105

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Llm;

public sealed class AnthropicLlmGateway(
    HttpClient http,
    AireqDbContext db,
    IOptions<LlmBudgetOptions> budget,
    IConfiguration config,
    ILogger<AnthropicLlmGateway> log) : ILlmGateway
{
    private const string DefaultHaiku = "claude-haiku-4-5";
    private const string DefaultSonnet = "claude-sonnet-4-6";
    private const string AnthropicVersion = "2023-06-01";
    private const string BaseUrl = "https://api.anthropic.com/v1/messages";

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        var modelName = ResolveModelName(request.Model);
        var modelBudget = budget.Value.For(request.Model);

        // ---- 1. Budget check --------------------------------------------------
        if (request.TenantId is Guid tenantId)
        {
            var monthStart = StartOfCurrentMonthUtc();
            var usage = await db.LlmCalls
                .Where(c => c.TenantId == tenantId
                            && c.Model == modelName
                            && c.CreatedAt >= monthStart)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Input = g.Sum(c => (long)c.InputTokens),
                    Output = g.Sum(c => (long)c.OutputTokens),
                })
                .FirstOrDefaultAsync(ct);

            var inputUsed = usage?.Input ?? 0;
            var outputUsed = usage?.Output ?? 0;
            if (inputUsed >= modelBudget.InputTokensPerMonth
                || outputUsed >= modelBudget.OutputTokensPerMonth)
            {
                log.LogWarning(
                    "LLM budget exceeded for tenant {TenantId} model {Model} — refusing call. " +
                    "Input {InputUsed}/{InputCap}, Output {OutputUsed}/{OutputCap}.",
                    tenantId, modelName,
                    inputUsed, modelBudget.InputTokensPerMonth,
                    outputUsed, modelBudget.OutputTokensPerMonth);
                throw new LlmBudgetExceededException(
                    tenantId, request.Model,
                    inputUsed, modelBudget.InputTokensPerMonth,
                    outputUsed, modelBudget.OutputTokensPerMonth);
            }
        }

        // ---- 2. Issue HTTP call ----------------------------------------------
        var apiKey = config["ANTHROPIC_API_KEY"]
            ?? throw new InvalidOperationException(
                "ANTHROPIC_API_KEY is not set. Configure it in .env.local or Key Vault.");

        var payload = new AnthropicRequestBody(
            modelName,
            request.MaxOutputTokens,
            request.SystemPrompt,
            new[] { new AnthropicMessage("user", request.UserPrompt) });

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        httpReq.Headers.Add("x-api-key", apiKey);
        httpReq.Headers.Add("anthropic-version", AnthropicVersion);

        using var httpResp = await http.SendAsync(httpReq, ct);
        if (!httpResp.IsSuccessStatusCode)
        {
            var body = await httpResp.Content.ReadAsStringAsync(ct);
            log.LogError(
                "Anthropic call failed: {Status} {Reason} — body={Body}",
                (int)httpResp.StatusCode, httpResp.ReasonPhrase, body);
            throw new HttpRequestException(
                $"Anthropic returned {(int)httpResp.StatusCode} ({httpResp.ReasonPhrase}).",
                inner: null,
                statusCode: httpResp.StatusCode);
        }

        var body2 = await httpResp.Content.ReadFromJsonAsync<AnthropicResponseBody>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Anthropic returned an empty body.");

        var text = body2.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? "";
        var inputTokens = body2.Usage?.InputTokens ?? 0;
        var outputTokens = body2.Usage?.OutputTokens ?? 0;
        var cost = modelBudget.EstimateUsd(inputTokens, outputTokens);

        // ---- 3. Audit log ----------------------------------------------------
        db.LlmCalls.Add(new LlmCall
        {
            TenantId = request.TenantId,
            Model = modelName,
            Purpose = request.Purpose,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsdEstimate = cost,
            PromptText = Truncate(BuildPromptRecord(request)),
            ResponseText = Truncate(text),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "LLM call OK — tenant={TenantId} model={Model} purpose={Purpose} " +
            "in={InputTokens} out={OutputTokens} cost~=${Cost:F4}",
            request.TenantId, modelName, request.Purpose, inputTokens, outputTokens, cost);

        return new LlmResponse(text, inputTokens, outputTokens, cost, modelName);
    }

    private string ResolveModelName(LlmModel model) => model switch
    {
        LlmModel.Haiku => config["ANTHROPIC_MODEL_FAST"] ?? DefaultHaiku,
        LlmModel.Sonnet => config["ANTHROPIC_MODEL_STRONG"] ?? DefaultSonnet,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown model."),
    };

    private static string BuildPromptRecord(LlmRequest req) =>
        $"## SYSTEM\n{req.SystemPrompt}\n\n## USER\n{req.UserPrompt}";

    private static string Truncate(string s) =>
        s.Length <= LlmCall.MaxPayloadChars ? s : s[..LlmCall.MaxPayloadChars];

    private static DateTimeOffset StartOfCurrentMonthUtc()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ---- Wire types -------------------------------------------------------

    private sealed record AnthropicRequestBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class AnthropicResponseBody
    {
        [JsonPropertyName("content")] public List<AnthropicContent>? Content { get; set; }
        [JsonPropertyName("usage")] public AnthropicUsage? Usage { get; set; }
    }

    private sealed class AnthropicContent
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
    }
}
