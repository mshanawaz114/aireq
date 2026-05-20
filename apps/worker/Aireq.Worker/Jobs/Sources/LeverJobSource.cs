// LeverJobSource — public Lever postings API (keyless).
//
//   GET https://api.lever.co/v0/postings/{company}?mode=json
//   → [ { "id": "uuid", "text": "Job Title",
//         "categories": {"location","team","commitment"},
//         "descriptionPlain", "hostedUrl", "createdAt": <epoch ms> } ]
//
// Company list from AtsSeedOptions.Lever. Lever ids are UUIDs (unique).
//
// Refs: AIRMVP1-202

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Jobs.Sources;

public sealed class LeverJobSource(
    HttpClient http,
    IOptions<AtsSeedOptions> seed,
    ILogger<LeverJobSource> log) : AtsJobSourceBase(log)
{
    public override string Name => "lever";
    protected override IReadOnlyList<string> Companies => seed.Value.Lever;

    protected override async Task<IReadOnlyList<RawJob>> FetchCompanyAsync(string company, CancellationToken ct)
    {
        var url = $"https://api.lever.co/v0/postings/{Uri.EscapeDataString(company)}?mode=json";
        using var resp = await http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogInformation("Lever board '{Company}' not found (404); skipping.", company);
            return Array.Empty<RawJob>();
        }
        resp.EnsureSuccessStatusCode();

        var postings = await resp.Content.ReadFromJsonAsync<List<LeverPosting>>(JsonOpts, ct);
        if (postings is null || postings.Count == 0) return Array.Empty<RawJob>();

        var list = new List<RawJob>(postings.Count);
        foreach (var p in postings)
        {
            if (string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Text)) continue;
            list.Add(new RawJob(
                Source: Name,
                SourceExternalId: p.Id!,
                Title: p.Text!.Trim(),
                Company: company,
                Location: p.Categories?.Location?.Trim(),
                Description: p.DescriptionPlain?.Trim(),
                PostedAt: p.CreatedAt is long ms
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                    : null,
                ExpiresAt: null,
                RawJson: JsonSerializer.Serialize(p, JsonOpts)));
        }
        return list;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class LeverPosting
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("descriptionPlain")] public string? DescriptionPlain { get; set; }
        [JsonPropertyName("hostedUrl")] public string? HostedUrl { get; set; }
        [JsonPropertyName("createdAt")] public long? CreatedAt { get; set; }
        [JsonPropertyName("categories")] public LeverCategories? Categories { get; set; }
    }

    private sealed class LeverCategories
    {
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("team")] public string? Team { get; set; }
        [JsonPropertyName("commitment")] public string? Commitment { get; set; }
    }
}
