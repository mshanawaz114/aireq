// DigestRunner — Hangfire entry point for the daily email digest pass.
// Refs: AIRMVP1-403

using Hangfire;

namespace Aireq.Worker.Notifications;

public interface IDigestRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class DigestRunner(
    DigestService digest,
    ILogger<DigestRunner> log) : IDigestRunner
{
    [Queue("email")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Daily digest pass triggered.");
        var n = await digest.RunAsync(ct);
        log.LogInformation("Daily digest pass done — {Sent} digests sent.", n);
    }
}
