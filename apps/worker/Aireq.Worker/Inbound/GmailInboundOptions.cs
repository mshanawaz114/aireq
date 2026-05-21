// GmailInboundOptions — tuning for the inbound reply poller.
// Bound from the GMAIL_INBOUND configuration section.
// Refs: AIRMVP1-401

namespace Aireq.Worker.Inbound;

public sealed class GmailInboundOptions
{
    public const string ConfigKey = "GMAIL_INBOUND";

    /// <summary>Cron for the recurring poll. Default: every 5 minutes.</summary>
    public string Cron { get; set; } = "*/5 * * * *";

    /// <summary>Max messages fetched per account per poll (Gmail page cap is 100).</summary>
    public int MaxPerPoll { get; set; } = 50;

    /// <summary>On the first poll for an account (no cursor yet), scan inbox this
    /// many days back so replies between connect and first poll aren't missed.</summary>
    public int InitialScanDays { get; set; } = 3;
}
