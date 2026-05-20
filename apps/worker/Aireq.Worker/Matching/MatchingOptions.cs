// MatchingOptions — candidate breadth, score floor, cadence.
// Refs: AIRMVP1-204

namespace Aireq.Worker.Matching;

public sealed class MatchingOptions
{
    public const string ConfigKey = "MATCHING";

    /// <summary>Cron for the matching pass. Default: every 2h at :45 (after the
    /// hourly embedding pass has had a chance to run).</summary>
    public string Cron { get; set; } = "45 */2 * * *";

    /// <summary>Nearest jobs pulled per consultant before filtering.</summary>
    public int TopN { get; set; } = 50;

    /// <summary>Minimum 0–100 vector score to create/keep a match. Below this the
    /// pairing is too weak to be worth the consultant's attention.</summary>
    public int MinScore { get; set; } = 50;
}
