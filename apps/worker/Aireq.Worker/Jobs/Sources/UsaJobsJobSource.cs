// UsaJobsJobSource — USAJobs.gov Search API (free, federal openings).
//
// Docs: https://developer.usajobs.gov/api-reference/get-api-search  •  GET
//   https://data.usajobs.gov/api/search?Keyword=...&LocationName=...&ResultsPerPage=50
// Headers: Host: data.usajobs.gov · User-Agent: <your email> · Authorization-Key: <key>
//
// Config:
//   USAJOBS_AUTH_KEY      — required, else IsEnabled=false
//   USAJOBS_USER_AGENT    — your registered email (required by their API)
//
// Refs: AIRMVP1-201

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aireq.Worker.Jobs.Sources;

public sealed class UsaJobsJobSource(
    HttpClient http,
    IConfiguration config,
    ILogger<UsaJobsJobSource> log) : IJobSource
{
    public string Name => "usajobs";

    private string? AuthKey => config["USAJOBS_AUTH_KEY"];
    private string? UserAgent => config["USAJOBS_USER_AGENT"];

    public bool IsEnabled => JobSourceConfig.IsSet(AuthKey) && JobSourceConfig.IsSet(UserAgent);

    public async IAsyncEnumerable<RawJob> FetchAsync(
        JobSourceQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsEnabled)
        {
            log.LogInformation("USAJobs source disabled (USAJOBS_AUTH_KEY/USER_AGENT not set). Skipping.");
            yield break;
        }

        var url =
            "https://data.usajobs.gov/api/search" +
            $"?Keyword={Uri.EscapeDataString(query.Keywords)}" +
            (string.IsNullOrWhiteSpace(query.Location)
                ? ""
                : $"&LocationName={Uri.EscapeDataString(query.Location!)}") +
            $"&ResultsPerPage={Math.Clamp(query.MaxResults, 1, 500)}";

        UsaJobsResponse? body;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Host", "data.usajobs.gov");
            req.Headers.Add("User-Agent", UserAgent);
            req.Headers.Add("Authorization-Key", AuthKey);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("USAJobs returned {Status}; skipping this query.", (int)resp.StatusCode);
                yield break;
            }
            body = await resp.Content.ReadFromJsonAsync<UsaJobsResponse>(JsonOpts, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "USAJobs fetch failed; skipping this query.");
            yield break;
        }

        var items = body?.SearchResult?.SearchResultItems;
        if (items is null) yield break;

        foreach (var item in items)
        {
            var d = item.MatchedObjectDescriptor;
            if (d is null || string.IsNullOrWhiteSpace(d.PositionId) || string.IsNullOrWhiteSpace(d.PositionTitle))
                continue;

            yield return new RawJob(
                Source: Name,
                SourceExternalId: d.PositionId!,
                Title: d.PositionTitle!.Trim(),
                Company: d.OrganizationName?.Trim() ?? "U.S. Federal Government",
                Location: d.PositionLocationDisplay?.Trim(),
                Description: d.UserArea?.Details?.JobSummary?.Trim(),
                PostedAt: d.PublicationStartDate,
                ExpiresAt: d.ApplicationCloseDate,
                RawJson: JsonSerializer.Serialize(d, JsonOpts));
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class UsaJobsResponse
    {
        [JsonPropertyName("SearchResult")] public SearchResultBody? SearchResult { get; set; }
    }

    private sealed class SearchResultBody
    {
        [JsonPropertyName("SearchResultItems")] public List<SearchResultItem>? SearchResultItems { get; set; }
    }

    private sealed class SearchResultItem
    {
        [JsonPropertyName("MatchedObjectDescriptor")] public MatchedObject? MatchedObjectDescriptor { get; set; }
    }

    private sealed class MatchedObject
    {
        [JsonPropertyName("PositionID")] public string? PositionId { get; set; }
        [JsonPropertyName("PositionTitle")] public string? PositionTitle { get; set; }
        [JsonPropertyName("OrganizationName")] public string? OrganizationName { get; set; }
        [JsonPropertyName("PositionLocationDisplay")] public string? PositionLocationDisplay { get; set; }
        [JsonPropertyName("PublicationStartDate")] public DateTimeOffset? PublicationStartDate { get; set; }
        [JsonPropertyName("ApplicationCloseDate")] public DateTimeOffset? ApplicationCloseDate { get; set; }
        [JsonPropertyName("UserArea")] public UserAreaBody? UserArea { get; set; }
    }

    private sealed class UserAreaBody
    {
        [JsonPropertyName("Details")] public DetailsBody? Details { get; set; }
    }

    private sealed class DetailsBody
    {
        [JsonPropertyName("JobSummary")] public string? JobSummary { get; set; }
    }
}
