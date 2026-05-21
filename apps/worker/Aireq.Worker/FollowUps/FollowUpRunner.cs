// FollowUpRunner — Hangfire entry point for the follow-up pass: plan new nudges,
// then send any that are Approved (auto-approved by the planner, or approved by
// the owner via the API since the last pass).
//
// Refs: AIRMVP1-404

using Hangfire;

namespace Aireq.Worker.FollowUps;

public interface IFollowUpRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class FollowUpRunner(
    FollowUpPlanner planner,
    FollowUpSender sender,
    ILogger<FollowUpRunner> log) : IFollowUpRunner
{
    [Queue("email")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Follow-up pass triggered.");
        var planned = await planner.RunAsync(ct);
        var sent = await sender.RunAsync(ct);
        log.LogInformation("Follow-up pass done — {Planned} planned, {Sent} sent.", planned, sent);
    }
}
