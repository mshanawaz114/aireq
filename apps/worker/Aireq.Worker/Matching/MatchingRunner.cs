// MatchingRunner — Hangfire entry point. Runs matching for every consultant
// that has an embedded resume.
//
// Refs: AIRMVP1-204

using Aireq.Api.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Worker.Matching;

public interface IMatchingRunner
{
    Task RunAsync(CancellationToken ct = default);
}

public sealed class MatchingRunner(
    AireqDbContext db,
    MatchingService matching,
    ILogger<MatchingRunner> log) : IMatchingRunner
{
    [Queue("discovery")]
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Consultants with at least one embedded resume — others have nothing to
        // match against yet. EmbeddedAt is mapped on every provider so this is a
        // plain relational query.
        var consultants = await db.Consultants
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.Resumes.Any(r => r.EmbeddedAt != null))
            .ToListAsync(ct);

        log.LogInformation("Matching pass triggered for {Count} consultant(s).", consultants.Count);

        var total = 0;
        foreach (var consultant in consultants)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                total += await matching.MatchConsultantAsync(consultant, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Matching failed for consultant {ConsultantId}.", consultant.Id);
            }
        }

        log.LogInformation("Matching pass done — {Total} new match(es).", total);
    }
}
