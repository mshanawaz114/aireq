// JobIngestionOptions — what to search for and how often.
//
// Jobs are NOT tenant-scoped (the same posting matches many consultants), so
// ingestion runs globally against a configured set of broad queries. Per-
// consultant relevance happens later at match time (AIRMVP1-204).
//
// Override via .env.local with JOB_INGESTION__... keys. Defaults give a sane
// tech-staffing starter set so a fresh install discovers real jobs immediately
// once a source key is added.
//
// Refs: AIRMVP1-201

namespace Aireq.Worker.Jobs;

public sealed class JobIngestionOptions
{
    public const string ConfigKey = "JOB_INGESTION";

    /// <summary>Cron for the recurring ingestion job. Default: every 6 hours.</summary>
    public string Cron { get; set; } = "0 */6 * * *";

    /// <summary>Max postings to pull per (source, query). Keeps free-tier quotas safe.</summary>
    public int MaxResultsPerQuery { get; set; } = 50;

    /// <summary>Broad search terms run against every enabled source.</summary>
    public List<string> Queries { get; set; } = new()
    {
        "software engineer",
        "data engineer",
        "salesforce",
        "devops",
        "project manager",
        "business analyst",
    };

    /// <summary>Optional location filter applied to every query (null = nationwide).</summary>
    public string? Location { get; set; }
}
