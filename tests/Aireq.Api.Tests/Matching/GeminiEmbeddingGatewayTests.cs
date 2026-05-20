// GeminiEmbeddingGatewayTests — request shape, response parsing, dimension guard,
// and self-disable without a key.
//
// Refs: AIRMVP1-204
using System.Net;
using Aireq.Api.Tests.Llm; // FakeHttpMessageHandler
using Aireq.Shared.Llm;
using Aireq.Worker.Llm;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Matching;

public sealed class GeminiEmbeddingGatewayTests
{
    private static string Vector(int n)
    {
        var vals = string.Join(",", Enumerable.Range(0, n).Select(i => (i / 1000.0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return $$"""{ "embedding": { "values": [ {{vals}} ] } }""";
    }

    private static GeminiEmbeddingGateway Build(FakeHttpMessageHandler handler, string? key = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GEMINI_API_KEY"] = key })
            .Build();
        return new GeminiEmbeddingGateway(new HttpClient(handler), config, NullLogger<GeminiEmbeddingGateway>.Instance);
    }

    [Fact]
    public void Not_configured_without_key()
    {
        var gw = Build(FakeHttpMessageHandler.RespondingWith("{}"), key: null);
        gw.IsConfigured.Should().BeFalse();
        gw.Dimensions.Should().Be(EmbeddingConfig.Dimensions);
    }

    [Fact]
    public async Task Parses_embedding_values()
    {
        var handler = FakeHttpMessageHandler.RespondingWith(Vector(EmbeddingConfig.Dimensions));
        var gw = Build(handler);

        gw.IsConfigured.Should().BeTrue();
        var vec = await gw.EmbedAsync("hello", CancellationToken.None);

        vec.Should().HaveCount(EmbeddingConfig.Dimensions);
    }

    [Fact]
    public async Task Wrong_dimension_throws()
    {
        // Provider returns 512 dims but schema expects EmbeddingConfig.Dimensions.
        var handler = FakeHttpMessageHandler.RespondingWith(Vector(512));
        var gw = Build(handler);

        var act = async () => await gw.EmbedAsync("hello", CancellationToken.None);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("dims");
    }

    [Fact]
    public async Task Non_2xx_throws_http()
    {
        var handler = FakeHttpMessageHandler.RespondingWith("nope", HttpStatusCode.TooManyRequests);
        var gw = Build(handler);

        var act = async () => await gw.EmbedAsync("hello", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
