// JobMaintenanceOptions — windows + cadence for dedupe and the freshness sweep.
//
// Override via .env.local with JOB_MAINTENANCE__... keys.
//
// Refs: AIRMVP1-203

namespace Aireq.Worker.Jobs;

public sealed class JobMaintenanceOptions
{
    public const string ConfigKey = "JOB_MAINTENANCE";

    /// <summary>Cron for the maintenance job. Default: every 12h at :30 (offset
    /// from the ingestion cron so they don't fight over the same rows).</summary>
    public string Cron { get; set; } = "30 */12 * * *";

    /// <summary>A posting not re-seen within this many hours is deactivated.
    /// memory.md §7: re-scrape every 72h, deactivate if no longer found.</summary>
    public int StalenessWindowHours { get; set; } = 72;

    /// <summary>Dedup only considers postings posted within this many days, so an
    /// old re-listing doesn't collapse against a fresh one. memory.md §7: 30 days.</summary>
    public int DedupeWindowDays { get; set; } = 30;

    public TimeSpan StalenessWindow => TimeSpan.FromHours(StalenessWindowHours);
    public TimeSpan DedupeWindow => TimeSpan.FromDays(DedupeWindowDays);
}
