// JobMaintenanceRunner — Hangfire entry point for the dedupe + freshness pass.
// Dedup runs before the sweep so a duplicate of a stale posting is collapsed
// before either gets deactivated.
//
// Refs: AIRMVP1-203

using Hangfire;

namespace Aireq.Worker.Jobs;

public interface IJobMaintenanceRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class JobMaintenanceRunner(
    JobMaintenanceService service,
    ILogger<JobMaintenanceRunner> log) : IJobMaintenanceRunner
{
    [Queue("discovery")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Job maintenance triggered.");
        var deduped = await service.DedupeAsync(ct);
        var swept = await service.SweepStaleAsync(ct);
        log.LogInformation("Job maintenance done — {Deduped} deduped, {Swept} deactivated.",
            deduped, swept);
    }
}
