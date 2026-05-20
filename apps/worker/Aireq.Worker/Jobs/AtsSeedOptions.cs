// AtsSeedOptions — the per-ATS lists of company board identifiers to ingest.
//
// ATS boards are per-company public endpoints, so unlike Adzuna/USAJobs there's
// no single API to query — we iterate a curated company list. These defaults
// are a starter set of companies known to host public boards on each ATS;
// curate freely via JOB_INGESTION__ATS__... in .env.local. Unknown/stale tokens
// just 404 and are skipped with a log line (the source degrades gracefully),
// so a wrong entry never breaks ingestion.
//
// Token formats:
//   Greenhouse — the board token in boards.greenhouse.io/<token> (e.g. "stripe")
//   Lever      — the company slug in jobs.lever.co/<slug>        (e.g. "netflix")
//   Ashby      — the board name in jobs.ashbyhq.com/<board>      (e.g. "ramp")
//
// Refs: AIRMVP1-202

namespace Aireq.Worker.Jobs;

public sealed class AtsSeedOptions
{
    public const string ConfigKey = "JOB_INGESTION:ATS";

    public List<string> Greenhouse { get; set; } = new()
    {
        "stripe", "airbnb", "coinbase", "databricks", "robinhood",
        "instacart", "doordash", "lyft", "reddit", "gitlab",
        "figma", "discord", "brex", "plaid", "chime",
    };

    public List<string> Lever { get; set; } = new()
    {
        "netflix", "spotify", "ramp", "notion", "kickstarter",
    };

    public List<string> Ashby { get; set; } = new()
    {
        "ramp", "linear", "vanta", "posthog", "deel",
    };
}
