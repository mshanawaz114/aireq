// AnthropicLlmGatewayTests — the LlmGateway is a P0 control surface
// (cost cap + audit log + single chokepoint), so the rules of the road get
// tested in isolation against a fake HTTP handler.
//
// Coverage:
//   - Happy path: POSTs to /v1/messages with x-api-key + anthropic-version,
//     parses Anthropic's response shape, returns usage-annotated LlmResponse.
//   - Audit log row is written with prompt + response + token counts.
//   - Cost estimate uses the configured per-million-token rates.
//   - Tenant over its monthly budget throws LlmBudgetExceededException
//     BEFORE the HTTP call is issued.
//   - Tenant = null bypasses budget enforcement (system calls).
//   - Anthropic non-2xx → HttpRequestException (caller can retry).
//
// Refs: AIRMVP1-105

using System.Net;
using System.Text.Json;
using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Shared.Llm;
using Aireq.Worker.Llm;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Llm;

public sealed class AnthropicLlmGatewayTests
{
    private const string SampleResponseJson = """
        {
          "id": "msg_01",
          "type": "message",
          "role": "assistant",
          "content": [{ "type": "text", "text": "{\"fullName\": \"Alice\"}" }],
          "usage": { "input_tokens": 123, "output_tokens": 45 }
        }
        """;

    private static (AireqDbContext db, HttpClient http, FakeHttpMessageHandler handler, AnthropicLlmGateway gw)
        BuildSut(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null,
            LlmBudgetOptions? budget = null,
            string? apiKey = "test-key")
    {
        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase($"llm-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AireqDbContext(options, new StubTenantContext());

        var handler = new FakeHttpMessageHandler(
            respond ?? ((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleResponseJson, System.Text.Encoding.UTF8, "application/json"),
            })));

        var http = new HttpClient(handler);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = apiKey,
                ["ANTHROPIC_MODEL_FAST"] = "claude-haiku-4-5",
                ["ANTHROPIC_MODEL_STRONG"] = "claude-sonnet-4-6",
            })
            .Build();

        var gw = new AnthropicLlmGateway(
            http, db,
            Options.Create(budget ?? new LlmBudgetOptions()),
            config,
            NullLogger<AnthropicLlmGateway>.Instance);

        return (db, http, handler, gw);
    }

    [Fact]
    public async Task Happy_path_posts_request_returns_response_and_audit_logs()
    {
        // Capture the request body DURING the call — the gateway wraps its
        // request in `using`, so it's disposed by the time the assertions run;
        // reading req.Content afterwards would throw ObjectDisposedException.
        string? capturedBody = null;
        var (db, _, handler, gw) = BuildSut(respond: async (req, ct) =>
        {
            capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleResponseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var tenantId = Guid.NewGuid();

        var resp = await gw.CompleteAsync(new LlmRequest(
            tenantId,
            LlmModel.Haiku,
            Purpose: "resume.parse",
            SystemPrompt: "system",
            UserPrompt: "user"), CancellationToken.None);

        // ---- request shape ----------------------------------------------
        // Method/URI/headers stay readable on a disposed request — only the
        // Content stream is gone, which is why we captured the body above.
        handler.Requests.Should().ContainSingle();
        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsoluteUri.Should().Be("https://api.anthropic.com/v1/messages");
        req.Headers.GetValues("x-api-key").Single().Should().Be("test-key");
        req.Headers.GetValues("anthropic-version").Single().Should().Be("2023-06-01");

        capturedBody.Should().NotBeNull();
        var sentBody = JsonDocument.Parse(capturedBody!).RootElement;
        sentBody.GetProperty("model").GetString().Should().Be("claude-haiku-4-5");
        sentBody.GetProperty("system").GetString().Should().Be("system");
        sentBody.GetProperty("messages")[0].GetProperty("role").GetString().Should().Be("user");
        sentBody.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be("user");

        // ---- response shape ---------------------------------------------
        resp.Text.Should().Be("{\"fullName\": \"Alice\"}");
        resp.InputTokens.Should().Be(123);
        resp.OutputTokens.Should().Be(45);
        resp.ModelName.Should().Be("claude-haiku-4-5");
        // Default Haiku pricing: $0.25/M input + $1.25/M output
        // = 123/1e6 * 0.25 + 45/1e6 * 1.25 = 0.00003075 + 0.0000563 = ~0.0000869
        resp.CostUsdEstimate.Should().BeApproximately(0.0000869m, 0.0000001m);

        // ---- audit log ---------------------------------------------------
        var call = await db.LlmCalls.SingleAsync();
        call.TenantId.Should().Be(tenantId);
        call.Model.Should().Be("claude-haiku-4-5");
        call.Purpose.Should().Be("resume.parse");
        call.InputTokens.Should().Be(123);
        call.OutputTokens.Should().Be(45);
        call.PromptText.Should().Contain("system").And.Contain("user");
        call.ResponseText.Should().Be("{\"fullName\": \"Alice\"}");
    }

    [Fact]
    public async Task Over_budget_tenant_is_refused_before_calling_anthropic()
    {
        var tenantId = Guid.NewGuid();

        // Build the SUT with a tight budget the seed will breach.
        var budget = new LlmBudgetOptions
        {
            Haiku = new LlmModelBudget
            {
                InputTokensPerMonth = 100,
                OutputTokensPerMonth = 100,
                InputUsdPerMillion = 0.25m,
                OutputUsdPerMillion = 1.25m,
            },
        };
        var (db, _, handler, gw) = BuildSut(budget: budget);

        // Seed prior usage right at the cap.
        db.LlmCalls.Add(new LlmCall
        {
            TenantId = tenantId,
            Model = "claude-haiku-4-5",
            Purpose = "resume.parse",
            InputTokens = 100,
            OutputTokens = 0,
            CostUsdEstimate = 0,
            PromptText = "x",
            ResponseText = "y",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Act + assert.
        var act = async () => await gw.CompleteAsync(new LlmRequest(
            tenantId, LlmModel.Haiku, "resume.parse", "sys", "usr"), CancellationToken.None);
        await act.Should().ThrowAsync<LlmBudgetExceededException>();
        handler.Requests.Should().BeEmpty("network call must not be issued when over budget");
    }

    [Fact]
    public async Task Null_tenant_bypasses_budget_check()
    {
        // Set the budget at 0 for both — would block any real tenant. The null
        // tenant must still be allowed through.
        var budget = new LlmBudgetOptions
        {
            Haiku = new LlmModelBudget
            {
                InputTokensPerMonth = 0,
                OutputTokensPerMonth = 0,
                InputUsdPerMillion = 0.25m,
                OutputUsdPerMillion = 1.25m,
            },
        };
        var (_, _, handler, gw) = BuildSut(budget: budget);

        var resp = await gw.CompleteAsync(new LlmRequest(
            TenantId: null,
            LlmModel.Haiku, "system.classify", "sys", "usr"), CancellationToken.None);

        resp.Should().NotBeNull();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Anthropic_5xx_surfaces_as_HttpRequestException()
    {
        var (_, _, _, gw) = BuildSut(respond: (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream blew up"),
            }));

        var act = async () => await gw.CompleteAsync(new LlmRequest(
            null, LlmModel.Haiku, "test", "sys", "usr"), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Missing_api_key_throws_meaningful_error()
    {
        var (_, _, _, gw) = BuildSut(apiKey: null);
        var act = async () => await gw.CompleteAsync(new LlmRequest(
            null, LlmModel.Haiku, "test", "sys", "usr"), CancellationToken.None);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("ANTHROPIC_API_KEY");
    }
}
