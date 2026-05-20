// GreenhouseJobSource — public Greenhouse board API (keyless, freshest source).
//
//   GET https://boards-api.greenhouse.io/v1/boards/{token}/jobs?content=true
//   → { "jobs": [ { "id": 12345, "title", "updated_at", "location": {"name"},
//                   "content": "<html>", "absolute_url" } ] }
//
// Company list from AtsSeedOptions.Greenhouse. Job ids are globally unique
// within Greenhouse, so jobs.source_external_id = id is collision-free.
//
// Refs: AIRMVP1-202

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Jobs.Sources;

public sealed class GreenhouseJobSource(
    HttpClient http,
    IOptions<AtsSeedOptions> seed,
    ILogger<GreenhouseJobSource> log) : AtsJobSourceBase(log)
{
    public override string Name => "greenhouse";
    protected override IReadOnlyList<string> Companies => seed.Value.Greenhouse;

    protected override async Task<IReadOnlyList<RawJob>> FetchCompanyAsync(string company, CancellationToken ct)
    {
        var url = $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(company)}/jobs?content=true";
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogInformation("Greenhouse board '{Company}' not found (404); skipping.", company);
            return Array.Empty<RawJob>();
        }
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<GreenhouseResponse>(JsonOpts, ct);
        var jobs = body?.Jobs;
        if (jobs is null || jobs.Count == 0) return Array.Empty<RawJob>();

        var list = new List<RawJob>(jobs.Count);
        foreach (var j in jobs)
        {
            if (j.Id is null || string.IsNullOrWhiteSpace(j.Title)) continue;
            list.Add(new RawJob(
                Source: Name,
                SourceExternalId: j.Id.Value.ToString(),
                Title: j.Title!.Trim(),
                Company: company,
                Location: j.Location?.Name?.Trim(),
                Description: WebUtility.HtmlDecode(j.Content ?? "")?.Trim(),
                PostedAt: j.UpdatedAt,
                ExpiresAt: null,
                RawJson: JsonSerializer.Serialize(j, JsonOpts)));
        }
        return list;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class GreenhouseResponse
    {
        [JsonPropertyName("jobs")] public List<GreenhouseJob>? Jobs { get; set; }
    }

    private sealed class GreenhouseJob
    {
        [JsonPropertyName("id")] public long? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("absolute_url")] public string? AbsoluteUrl { get; set; }
        [JsonPropertyName("location")] public GreenhouseLocation? Location { get; set; }
    }

    private sealed class GreenhouseLocation
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
