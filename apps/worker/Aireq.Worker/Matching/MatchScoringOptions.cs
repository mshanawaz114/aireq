// MatchScoringOptions — batch size + cadence for the LLM scorer.
// Refs: AIRMVP1-205

namespace Aireq.Worker.Matching;

public sealed class MatchScoringOptions
{
    public const string ConfigKey = "MATCH_SCORING";

    /// <summary>Cron for the scoring pass. Default: hourly at :50 (after matching).</summary>
    public string Cron { get; set; } = "50 * * * *";

    /// <summary>Max matches scored per pass. Each is one LLM call, so this is the
    /// main cost lever. Best (highest vector score) matches are scored first.</summary>
    public int BatchSize { get; set; } = 25;

    /// <summary>Characters of resume + JD fed to the model (cost guard).</summary>
    public int MaxResumeChars { get; set; } = 6_000;
    public int MaxJobChars { get; set; } = 6_000;
}
