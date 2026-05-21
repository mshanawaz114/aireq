// FollowUpOptions — tuning + safety rails for auto follow-up nudges.
// Bound from the FOLLOWUP configuration section.
// Refs: AIRMVP1-404

namespace Aireq.Worker.FollowUps;

public sealed class FollowUpOptions
{
    public const string ConfigKey = "FOLLOWUP";

    /// <summary>Cron for the plan+send pass. Default: hourly.</summary>
    public string Cron { get; set; } = "0 * * * *";

    /// <summary>Days of silence after the application before the first nudge.</summary>
    public int FirstNudgeAfterDays { get; set; } = 3;

    /// <summary>Minimum days between consecutive outbound messages on a match.</summary>
    public int GapDays { get; set; } = 3;

    /// <summary>Hard cap on nudges per match (excludes the original application).</summary>
    public int MaxFollowUps { get; set; } = 2;

    /// <summary>Max matches planned per pass (one LLM draft each).</summary>
    public int PlanBatchSize { get; set; } = 25;

    /// <summary>Max nudges actually sent per pass (independent of the per-tenant
    /// warmup cap enforced in the email sender).</summary>
    public int SendBatchSize { get; set; } = 50;

    /// <summary>
    /// When false (default) drafts are parked Pending for owner approval. When
    /// true the planner creates them pre-Approved so the sender ships them
    /// automatically. Mirrors FEATURES__SEND_EMAILS_WITHOUT_APPROVAL.
    /// </summary>
    public bool AutoSend { get; set; }

    /// <summary>Whether sends go out for real. False -> the email sender dry-runs.</summary>
    public bool SendLive { get; set; }
}
