// GreenhouseJobSourceTests — parsing, 404 graceful skip, multi-company iteration.
//
// Refs: AIRMVP1-202

using System.Net;
using Aireq.Api.Tests.Llm; // FakeHttpMessageHandler
using Aireq.Worker.Jobs;
using Aireq.Worker.Jobs.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Jobs;

public sealed class GreenhouseJobSourceTests
{
    private const string TwoJobs = """
        {
          "jobs": [
            {
              "id": 100,
              "title": "Staff Engineer",
              "updated_at": "2026-05-18T08:00:00Z",
              "content": "Build &amp; ship.",
              "location": { "name": "San Francisco, CA" },
              "absolute_url": "https://boards.greenhouse.io/acme/jobs/100"
            },
            {
              "id": 101,
              "title": "Product Designer",
              "updated_at": "2026-05-17T08:00:00Z",
              "content": "Design things.",
              "location": { "name": "Remote" }
            }
          ]
        }
        """;

    private static GreenhouseJobSource Build(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        params string[] companies)
    {
        var seed = Options.Create(new AtsSeedOptions { Greenhouse = companies.ToList() });
        var handler = new FakeHttpMessageHandler(respond);
        return new GreenhouseJobSource(new HttpClient(handler), seed, NullLogger<GreenhouseJobSource>.Instance);
    }

    [Fact]
    public void Is_full_board_not_keyword_driven()
    {
        var src = Build((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), "acme");
        ((IJobSource)src).IsKeywordDriven.Should().BeFalse();
        src.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Disabled_when_no_companies_seeded()
    {
        var src = Build((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        src.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Parses_jobs_and_decodes_html_content()
    {
        var src = Build(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TwoJobs, System.Text.Encoding.UTF8, "application/json"),
            }),
            "acme");

        var jobs = await Collect(src);

        jobs.Should().HaveCount(2);
        jobs[0].Source.Should().Be("greenhouse");
        jobs[0].SourceExternalId.Should().Be("100");
        jobs[0].Title.Should().Be("Staff Engineer");
        jobs[0].Company.Should().Be("acme");
        jobs[0].Location.Should().Be("San Francisco, CA");
        jobs[0].Description.Should().Be("Build & ship.", "HTML entities are decoded");
    }

    [Fact]
    public async Task A_404_company_is_skipped_without_aborting_others()
    {
        // First company 404s, second returns jobs.
        var src = Build(
            (req, _) =>
            {
                var is404Company = req.RequestUri!.AbsoluteUri.Contains("/gone/");
                return Task.FromResult(is404Company
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(TwoJobs, System.Text.Encoding.UTF8, "application/json"),
                    });
            },
            "gone", "acme");

        var jobs = await Collect(src);

        jobs.Should().HaveCount(2, "the 404 company is skipped, the healthy one still yields");
        jobs.Should().OnlyContain(j => j.Company == "acme");
    }

    private static async Task<List<RawJob>> Collect(IJobSource src)
    {
        var list = new List<RawJob>();
        await foreach (var j in src.FetchAsync(new JobSourceQuery("*"), CancellationToken.None))
            list.Add(j);
        return list;
    }
}
