// AtsJobSourceBase — shared loop for ATS board sources (Greenhouse, Lever,
// Ashby). Each concrete source supplies its company list + the per-company
// fetch/parse; the base handles iteration, graceful per-company error
// isolation (a 404 / moved board skips that company, never aborts the run),
// and the IJobSource plumbing.
//
// ATS sources are full-board (IsKeywordDriven=false): they return a company's
// entire active board, so the ingestion service runs them once per pass.
//
// Refs: AIRMVP1-202

using System.Runtime.CompilerServices;

namespace Aireq.Worker.Jobs.Sources;

public abstract class AtsJobSourceBase(ILogger log) : IJobSource
{
    public abstract string Name { get; }

    /// <summary>Company board identifiers to ingest (token / slug / board name).</summary>
    protected abstract IReadOnlyList<string> Companies { get; }

    public bool IsEnabled => Companies.Count > 0;

    public bool IsKeywordDriven => false;

    /// <summary>Fetch + parse one company's board. Throw to signal a fetch error
    /// (the base logs + skips that company). Return empty for an empty board.</summary>
    protected abstract Task<IReadOnlyList<RawJob>> FetchCompanyAsync(string company, CancellationToken ct);

    public async IAsyncEnumerable<RawJob> FetchAsync(
        JobSourceQuery query, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var company in Companies)
        {
            if (ct.IsCancellationRequested) yield break;

            IReadOnlyList<RawJob> jobs;
            try
            {
                jobs = await FetchCompanyAsync(company, ct);
            }
            catch (Exception ex)
            {
                // Stale token, moved board, transient 5xx — skip this company.
                log.LogWarning(ex, "{Source}: failed to fetch board '{Company}'; skipping.",
                    Name, company);
                continue;
            }

            foreach (var job in jobs)
                yield return job;
        }
    }
}
