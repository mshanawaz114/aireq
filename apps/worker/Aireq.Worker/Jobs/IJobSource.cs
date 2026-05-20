// IJobSource — one discovery source (Adzuna, USAJobs, a Greenhouse board, …).
//
// Design contract:
//   - Sources are config-keyed and SELF-DISABLE when their key is missing
//     (IsEnabled => false). The ingestion service skips disabled sources with
//     a log line rather than throwing — so you can drop keys into .env.local
//     incrementally and only the configured sources run.
//   - FetchAsync streams normalized RawJob records; the source owns paging,
//     rate-limit backoff, and mapping the provider's payload into RawJob.
//   - Sources never touch the database. JobIngestionService owns persistence,
//     dedupe, and freshness so every source benefits from the same pipeline.
//
// Refs: AIRMVP1-201

namespace Aireq.Worker.Jobs;

public interface IJobSource
{
    /// <summary>Stable lowercase id stored on jobs.source (e.g. "adzuna", "usajobs").</summary>
    string Name { get; }

    /// <summary>False when the source isn't configured (missing key). Skipped, not errored.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// True for keyword-search sources (Adzuna, USAJobs) — the ingestion service
    /// runs them once per configured query. False for full-board sources (ATS
    /// endpoints return a company's entire board regardless of keywords) — the
    /// service runs them once per ingestion pass to avoid re-fetching boards N
    /// times. Defaults to true; ATS sources override to false.
    /// </summary>
    bool IsKeywordDriven => true;

    /// <summary>Stream matching postings for the query. Implementations page internally.</summary>
    IAsyncEnumerable<RawJob> FetchAsync(JobSourceQuery query, CancellationToken ct);
}

/// <summary>A normalized posting from any source, pre-persistence.</summary>
/// <param name="RawJson">Original provider payload, kept verbatim for forensics + re-derivation.</param>
public sealed record RawJob(
    string Source,
    string SourceExternalId,
    string Title,
    string Company,
    string? Location,
    string? Description,
    DateTimeOffset? PostedAt,
    DateTimeOffset? ExpiresAt,
    string RawJson);

/// <summary>What to search for. Ingestion runs each configured query against each source.</summary>
public sealed record JobSourceQuery(
    string Keywords,
    string? Location = null,
    int MaxResults = 50);
