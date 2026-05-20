// AshbyJobSource — public Ashby job-board API (keyless).
//
//   GET https://api.ashbyhq.com/posting-api/job-board/{board}?includeCompensation=true
//   → { "jobs": [ { "id": "uuid", "title", "location",
//                   "descriptionPlain", "publishedAt", "jobUrl", "isListed" } ] }
//
// Company list from AtsSeedOptions.Ashby. Ashby ids are UUIDs (unique).
//
// Refs: AIRMVP1-202

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Jobs.Sources;

public sealed class AshbyJobSource(
    HttpClient http,
    IOptions<AtsSeedOptions> seed,
    ILogger<AshbyJobSource> log) : AtsJobSourceBase(log)
{
    public override string Name => "ashby";
    protected override IReadOnlyList<string> Companies => seed.Value.Ashby;

    protected override async Task<IReadOnlyList<RawJob>> FetchCompanyAsync(string company, CancellationToken ct)
    {
        var url = $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(company)}?includeCompensation=true";
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogInformation("Ashby board '{Company}' not found (404); skipping.", company);
            return Array.Empty<RawJob>();
        }
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<AshbyResponse>(JsonOpts, ct);
        var jobs = body?.Jobs;
        if (jobs is null || jobs.Count == 0) return Array.Empty<RawJob>();

        var list = new List<RawJob>(jobs.Count);
        foreach (var j in jobs)
        {
            if (string.IsNullOrWhiteSpace(j.Id) || string.IsNullOrWhiteSpace(j.Title)) continue;
            // Skip postings the company has unlisted.
            if (j.IsListed is false) continue;
            list.Add(new RawJob(
                Source: Name,
                SourceExternalId: j.Id!,
                Title: j.Title!.Trim(),
                Company: company,
                Location: j.Location?.Trim(),
                Description: j.DescriptionPlain?.Trim(),
                PostedAt: j.PublishedAt,
                ExpiresAt: null,
                RawJson: JsonSerializer.Serialize(j, JsonOpts)));
        }
        return list;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class AshbyResponse
    {
        [JsonPropertyName("jobs")] public List<AshbyJob>? Jobs { get; set; }
    }

    private sealed class AshbyJob
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; set; }
        [JsonPropertyName("publishedAt")] public DateTimeOffset? PublishedAt { get; set; }
        [JsonPropertyName("jobUrl")] public string? JobUrl { get; set; }
        [JsonPropertyName("isListed")] public bool? IsListed { get; set; }
    }
}
