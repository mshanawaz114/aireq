// EmbeddingRunner — Hangfire entry point for the embedding pass. Embeds new
// jobs and newly-parsed resumes so they're ready for vector matching (204b).
//
// Refs: AIRMVP1-204

using Hangfire;

namespace Aireq.Worker.Matching;

public interface IEmbeddingRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class EmbeddingRunner(
    JobEmbedder jobs,
    ResumeEmbedder resumes,
    ILogger<EmbeddingRunner> log) : IEmbeddingRunner
{
    [Queue("discovery")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        log.LogInformation("Embedding pass triggered.");
        var j = await jobs.RunAsync(ct);
        var r = await resumes.RunAsync(ct);
        log.LogInformation("Embedding pass done — {Jobs} jobs, {Resumes} resumes.", j, r);
    }
}
