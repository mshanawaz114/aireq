// EmailOptions — Resend sender config + warmup throttle.
// Refs: AIRMVP1-305

namespace Aireq.Worker.Email;

public sealed class EmailOptions
{
    public const string ConfigKey = "EMAIL";

    /// <summary>From header. Falls back to RESEND_FROM if unset here.</summary>
    public string? From { get; set; }

    /// <summary>Max real sends per tenant per UTC day (warmup protection).
    /// memory.md §14: keep a new domain under ~50/day for the first 2 weeks.</summary>
    public int DailyCap { get; set; } = 50;
}
