// ResumeTailorJob — Hangfire implementation of IResumeTailorJob. Thin wrapper
// over ResumeTailor so the service stays Hangfire-agnostic + testable.
//
// Refs: AIRMVP1-302

using Aireq.Shared.Jobs;
using Hangfire;

namespace Aireq.Worker.Tailoring;

public sealed class ResumeTailorJob(ResumeTailor tailor, ILogger<ResumeTailorJob> log) : IResumeTailorJob
{
    [Queue("tailor")]
    public async Task TailorAsync(Guid matchId, CancellationToken ct = default)
    {
        log.LogInformation("Resume tailoring job started for match {MatchId}.", matchId);
        await tailor.TailorAsync(matchId, ct);
    }
}
