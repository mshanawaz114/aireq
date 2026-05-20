// AdzunaJobSource — Adzuna aggregator API (free tier: ~1k calls/mo).
//
// Docs: https://developer.adzuna.com/  •  GET
//   https://api.adzuna.com/v1/api/jobs/{country}/search/{page}
//     ?app_id=...&app_key=...&results_per_page=50&what=...&where=...
//
// Config:
//   ADZUNA_APP_ID, ADZUNA_APP_KEY   — both required, else IsEnabled=false
//   ADZUNA_COUNTRY                  — ISO country (default "us")
//
// Refs: AIRMVP1-201

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aireq.Worker.Jobs.Sources;

public sealed class AdzunaJobSource(
    HttpClient http,
    IConfiguration config,
    ILogger<AdzunaJobSource> log) : IJobSource
{
    public string Name => "adzuna";

    private string? AppId => config["ADZUNA_APP_ID"];
    private string? AppKey => config["ADZUNA_APP_KEY"];
    private string Country => config["ADZUNA_COUNTRY"] ?? "us";

    public bool IsEnabled => JobSourceConfig.IsSet(AppId) && JobSourceConfig.IsSet(AppKey);

    public async IAsyncEnumerable<RawJob> FetchAsync(
        JobSourceQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsEnabled)
        {
            log.LogInformation("Adzuna source disabled (ADZUNA_APP_ID/KEY not set). Skipping.");
            yield break;
        }

        const int perPage = 50;
        var remaining = query.MaxResults;
        var page = 1;

        while (remaining > 0 && !ct.IsCancellationRequested)
        {
            var take = Math.Min(perPage, remaining);
            var url =
                $"https://api.adzuna.com/v1/api/jobs/{Country}/search/{page}" +
                $"?app_id={Uri.EscapeDataString(AppId!)}" +
                $"&app_key={Uri.EscapeDataString(AppKey!)}" +
                $"&results_per_page={take}" +
                $"&what={Uri.EscapeDataString(query.Keywords)}" +
                (string.IsNullOrWhiteSpace(query.Location)
                    ? ""
                    : $"&where={Uri.EscapeDataString(query.Location!)}") +
                "&content-type=application/json";

            AdzunaSearchResponse? body;
            try
            {
                using var resp = await http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    log.LogWarning("Adzuna page {Page} returned {Status}; stopping this query.",
                        page, (int)resp.StatusCode);
                    yield break;
                }
                body = await resp.Content.ReadFromJsonAsync<AdzunaSearchResponse>(JsonOpts, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                log.LogWarning(ex, "Adzuna fetch failed on page {Page}; stopping this query.", page);
                yield break;
            }

            var results = body?.Results;
            if (results is null || results.Count == 0) yield break;

            foreach (var r in results)
            {
                if (string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Title)) continue;
                yield return new RawJob(
                    Source: Name,
                    SourceExternalId: r.Id!,
                    Title: r.Title!.Trim(),
                    Company: r.Company?.DisplayName?.Trim() ?? "Unknown",
                    Location: r.Location?.DisplayName?.Trim(),
                    Description: r.Description?.Trim(),
                    PostedAt: r.Created,
                    ExpiresAt: null,
                    RawJson: JsonSerializer.Serialize(r, JsonOpts));
                remaining--;
                if (remaining <= 0) yield break;
            }

            // A page returning fewer rows than we asked for is the last page —
            // every paginated API signals end-of-results this way. Stops the
            // loop instead of re-requesting the same final page forever.
            if (results.Count < take) yield break;

            page++;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class AdzunaSearchResponse
    {
        [JsonPropertyName("results")] public List<AdzunaResult>? Results { get; set; }
    }

    private sealed class AdzunaResult
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("created")] public DateTimeOffset? Created { get; set; }
        [JsonPropertyName("company")] public AdzunaCompany? Company { get; set; }
        [JsonPropertyName("location")] public AdzunaLocation? Location { get; set; }
        [JsonPropertyName("redirect_url")] public string? RedirectUrl { get; set; }
    }

    private sealed class AdzunaCompany
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }

    private sealed class AdzunaLocation
    {
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }
}
