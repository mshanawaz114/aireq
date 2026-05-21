// ReplyClassifierRunner — Hangfire entry point for the reply classification pass.
// Refs: AIRMVP1-402

using Hangfire;

namespace Aireq.Worker.Inbound;

public interface IReplyClassifierRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class ReplyClassifierRunner(
    ReplyClassifier classifier,
    ILogger<ReplyClassifierRunner> log) : IReplyClassifierRunner
{
    [Queue("email")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Reply classification pass triggered.");
        var n = await classifier.RunAsync(ct);
        log.LogInformation("Reply classification pass done — {Classified} threads classified.", n);
    }
}
