// ReplyClassificationOptions — tuning for the inbound reply classifier pass.
// Bound from the REPLY_CLASSIFICATION configuration section.
// Refs: AIRMVP1-402

namespace Aireq.Worker.Inbound;

public sealed class ReplyClassificationOptions
{
    public const string ConfigKey = "REPLY_CLASSIFICATION";

    /// <summary>Cron for the recurring pass. Default: every 5 minutes (just
    /// behind the inbound poll, so freshly-threaded replies get classified soon).</summary>
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>Max threads classified per pass (one LLM call each).</summary>
    public int BatchSize { get; set; } = 25;

    /// <summary>Reply body is clamped to this before going to the model.</summary>
    public int MaxBodyChars { get; set; } = 4_000;
}
