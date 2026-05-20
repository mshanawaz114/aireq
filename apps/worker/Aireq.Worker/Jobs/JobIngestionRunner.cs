// JobIngestionRunner — the Hangfire entry point for recurring job ingestion.
//
// Hangfire serializes a call to IJobIngestionRunner.RunAsync and resolves the
// implementation from DI when the schedule fires. Kept as a thin wrapper over
// JobIngestionService so the service stays free of Hangfire concerns and is
// unit-testable on its own.
//
// Queue: "discovery" (declared on the Hangfire server in Program.cs).
//
// Refs: AIRMVP1-201

using Hangfire;

namespace Aireq.Worker.Jobs;

public interface IJobIngestionRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class JobIngestionRunner(
    JobIngestionService service,
    ILogger<JobIngestionRunner> log) : IJobIngestionRunner
{
    [Queue("discovery")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Recurring job ingestion triggered.");
        await service.RunAsync(ct);
    }
}
