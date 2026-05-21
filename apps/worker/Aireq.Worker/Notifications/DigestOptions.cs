// DigestOptions — tuning for the daily per-tenant email digest.
// Bound from the DIGEST configuration section.
// Refs: AIRMVP1-403

namespace Aireq.Worker.Notifications;

public sealed class DigestOptions
{
    public const string ConfigKey = "DIGEST";

    /// <summary>Cron for the digest send. Default: 13:00 UTC daily (~morning US).</summary>
    public string Cron { get; set; } = "0 13 * * *";

    /// <summary>Activity window summarised in the digest (hours back from send).</summary>
    public int LookbackHours { get; set; } = 24;

    /// <summary>
    /// Whether digests are sent for real. False (default) -> the email sender
    /// dry-runs (audited, nothing leaves). Flip on once the sending domain is
    /// warmed and a real RESEND_API_KEY is set.
    /// </summary>
    public bool SendLive { get; set; }
}
