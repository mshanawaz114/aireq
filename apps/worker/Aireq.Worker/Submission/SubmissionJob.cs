// SubmissionJob — Hangfire implementation of ISubmissionJob.
// Refs: AIRMVP1-303

using Aireq.Shared.Jobs;
using Hangfire;

namespace Aireq.Worker.Submission;

public sealed class SubmissionJob(SubmissionService service, ILogger<SubmissionJob> log) : ISubmissionJob
{
    [Queue("apply")]
    public async Task SubmitAsync(Guid matchId, CancellationToken ct = default)
    {
        log.LogInformation("Submission job started for match {MatchId}.", matchId);
        await service.SubmitAsync(matchId, ct);
    }
}
