// MatchScoringRunner — Hangfire entry point for the LLM scoring pass.
// Refs: AIRMVP1-205

using Hangfire;

namespace Aireq.Worker.Matching;

public interface IMatchScoringRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class MatchScoringRunner(
    MatchScorer scorer,
    ILogger<MatchScoringRunner> log) : IMatchScoringRunner
{
    [Queue("tailor")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Match scoring pass triggered.");
        var n = await scorer.RunAsync(ct);
        log.LogInformation("Match scoring pass done — {Scored} scored.", n);
    }
}
