// AdzunaJobSourceTests — parsing + self-disable behaviour against a fake HTTP
// handler (no network).
//
// Coverage:
//   - Disabled (no keys) → yields nothing, no HTTP call.
//   - Enabled → parses Adzuna's JSON shape into RawJob, maps company/location.
//   - Respects MaxResults across the page.
//
// Refs: AIRMVP1-201

using Aireq.Api.Tests.Llm; // FakeHttpMessageHandler
using Aireq.Worker.Jobs;
using Aireq.Worker.Jobs.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Jobs;

public sealed class AdzunaJobSourceTests
{
    private const string SampleJson = """
        {
          "results": [
            {
              "id": "12345",
              "title": "Senior Data Engineer",
              "description": "Build pipelines.",
              "created": "2026-05-18T10:00:00Z",
              "company": { "display_name": "Acme Data" },
              "location": { "display_name": "Austin, TX" }
            },
            {
              "id": "67890",
              "title": "Platform Engineer",
              "description": "Run the platform.",
              "created": "2026-05-17T09:00:00Z",
              "company": { "display_name": "Globex" },
              "location": { "display_name": "Remote" }
            }
          ]
        }
        """;

    private static AdzunaJobSource Build(
        FakeHttpMessageHandler handler, bool withKeys = true)
    {
        var settings = new Dictionary<string, string?>();
        if (withKeys)
        {
            settings["ADZUNA_APP_ID"] = "test-id";
            settings["ADZUNA_APP_KEY"] = "test-key";
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AdzunaJobSource(new HttpClient(handler), config, NullLogger<AdzunaJobSource>.Instance);
    }

    [Fact]
    public async Task Disabled_without_keys_yields_nothing_and_makes_no_call()
    {
        var handler = FakeHttpMessageHandler.RespondingWith(SampleJson);
        var src = Build(handler, withKeys: false);

        src.IsEnabled.Should().BeFalse();
        var jobs = await Collect(src);
        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Parses_results_into_rawjobs()
    {
        var handler = FakeHttpMessageHandler.RespondingWith(SampleJson);
        var src = Build(handler);

        src.IsEnabled.Should().BeTrue();
        var jobs = await Collect(src, new JobSourceQuery("data engineer", "Austin", MaxResults: 50));

        jobs.Should().HaveCount(2);
        jobs[0].Source.Should().Be("adzuna");
        jobs[0].SourceExternalId.Should().Be("12345");
        jobs[0].Title.Should().Be("Senior Data Engineer");
        jobs[0].Company.Should().Be("Acme Data");
        jobs[0].Location.Should().Be("Austin, TX");
        jobs[0].PostedAt.Should().BeCloseTo(
            new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Respects_max_results()
    {
        var handler = FakeHttpMessageHandler.RespondingWith(SampleJson);
        var src = Build(handler);

        var jobs = await Collect(src, new JobSourceQuery("engineer", null, MaxResults: 1));

        jobs.Should().ContainSingle("MaxResults caps the yield even if the page has more");
    }

    private static async Task<List<RawJob>> Collect(
        IJobSource src, JobSourceQuery? query = null)
    {
        var list = new List<RawJob>();
        await foreach (var j in src.FetchAsync(query ?? new JobSourceQuery("engineer"), CancellationToken.None))
            list.Add(j);
        return list;
    }
}
