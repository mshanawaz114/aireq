// FakeJobSource — deterministic IJobSource for ingestion-service tests.
// Refs: AIRMVP1-201

using Aireq.Worker.Jobs;

namespace Aireq.Api.Tests.Jobs;

public sealed class FakeJobSource(
    string name,
    IReadOnlyList<RawJob> jobs,
    bool enabled = true,
    bool keywordDriven = true) : IJobSource
{
    public string Name => name;
    public bool IsEnabled => enabled;
    public bool IsKeywordDriven => keywordDriven;

    /// <summary>How many times FetchAsync was enumerated — used to assert
    /// keyword sources run per-query and full-board sources run once.</summary>
    public int FetchCount { get; private set; }

    public async IAsyncEnumerable<RawJob> FetchAsync(
        JobSourceQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        FetchCount++;
        foreach (var j in jobs)
        {
            ct.ThrowIfCancellationRequested();
            yield return j;
            await Task.Yield();
        }
    }

    public static RawJob Job(string source, string externalId, string title = "Engineer", string company = "Acme") =>
        new(source, externalId, title, company, "Remote", "JD text", DateTimeOffset.UtcNow, null, "{}");
}
