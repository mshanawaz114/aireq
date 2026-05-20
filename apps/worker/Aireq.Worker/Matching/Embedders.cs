// JobEmbedder + ResumeEmbedder — populate pgvector embeddings for matching.
//
// Both find rows with EmbeddedAt == null, embed a representative text via
// IEmbeddingGateway, store the vector, and stamp EmbeddedAt. EmbeddedAt is the
// queryable marker (the Embedding column itself is Npgsql-only / Ignored on the
// test provider), so "needs embedding" is determinable everywhere.
//
// Both skip cleanly when the embedding provider isn't configured.
//
// Refs: AIRMVP1-204

using Aireq.Api.Data;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace Aireq.Worker.Matching;

public sealed class JobEmbedder(
    AireqDbContext db,
    IEmbeddingGateway embeddings,
    IOptions<EmbeddingOptions> options,
    ILogger<JobEmbedder> log)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (!embeddings.IsConfigured)
        {
            log.LogInformation("Job embedding skipped — embedding provider not configured (GEMINI_API_KEY).");
            return 0;
        }

        var opts = options.Value;
        var batch = await db.Jobs
            .Where(j => j.IsActive && j.EmbeddedAt == null)
            .OrderBy(j => j.CreatedAt)
            .Take(opts.BatchSize)
            .ToListAsync(ct);

        var done = 0;
        foreach (var job in batch)
        {
            if (ct.IsCancellationRequested) break;
            var text = Clamp($"{job.Title}\n{job.Company}\n{job.Location}\n{job.Description}", opts.MaxChars);
            try
            {
                var vec = await embeddings.EmbedAsync(text, ct);
                job.Embedding = new Vector(vec);
                job.EmbeddedAt = DateTimeOffset.UtcNow;
                done++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Embedding failed for job {JobId}; will retry next pass.", job.Id);
            }
        }

        if (done > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Job embedding: {Done}/{Batch} embedded.", done, batch.Count);
        return done;
    }

    private static string Clamp(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class ResumeEmbedder(
    AireqDbContext db,
    IEmbeddingGateway embeddings,
    IOptions<EmbeddingOptions> options,
    ILogger<ResumeEmbedder> log)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (!embeddings.IsConfigured)
        {
            log.LogInformation("Resume embedding skipped — embedding provider not configured (GEMINI_API_KEY).");
            return 0;
        }

        var opts = options.Value;
        // Only resumes that have been parsed (ParsedJson set) are worth embedding;
        // a raw upload with no extracted content gives a useless vector.
        var batch = await db.Resumes
            .IgnoreQueryFilters()
            .Where(r => r.ParsedJson != null && r.EmbeddedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(opts.BatchSize)
            .ToListAsync(ct);

        var done = 0;
        foreach (var resume in batch)
        {
            if (ct.IsCancellationRequested) break;
            var text = Clamp(resume.ParsedJson ?? "", opts.MaxChars);
            if (string.IsNullOrWhiteSpace(text)) continue;
            try
            {
                var vec = await embeddings.EmbedAsync(text, ct);
                resume.Embedding = new Vector(vec);
                resume.EmbeddedAt = DateTimeOffset.UtcNow;
                done++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Embedding failed for resume {ResumeId}; will retry next pass.", resume.Id);
            }
        }

        if (done > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Resume embedding: {Done}/{Batch} embedded.", done, batch.Count);
        return done;
    }

    private static string Clamp(string s, int max) => s.Length <= max ? s : s[..max];
}
