// Shared helpers for job-source configuration.
// Refs: AIRMVP1-201

namespace Aireq.Worker.Jobs;

public static class JobSourceConfig
{
    /// <summary>
    /// A config value counts as "set" only when it's non-blank and not one of the
    /// placeholder sentinels we ship in .env.example. This is what lets a source
    /// self-disable cleanly until you drop in a real key.
    /// </summary>
    public static bool IsSet(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.Equals("REPLACE_ME", StringComparison.OrdinalIgnoreCase)
        && !value.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase);
}
