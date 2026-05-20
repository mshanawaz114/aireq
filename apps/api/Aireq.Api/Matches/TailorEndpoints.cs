// TailorEndpoints — kick off resume tailoring for a match.
//
//   POST /api/matches/{matchId}/tailor  → 202 Accepted, enqueues the worker job.
//
// We verify the match is visible to the caller's tenant (global query filter)
// before enqueuing, so one tenant can't trigger work on another's match.
//
// Refs: AIRMVP1-302

using Aireq.Api.Data;
using Aireq.Shared.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Endpoints;

public static class TailorEndpoints
{
    public static IEndpointRouteBuilder MapTailorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/matches/{matchId:guid}/tailor", async (
                Guid matchId,
                AireqDbContext db,
                IBackgroundJobClient jobs,
                CancellationToken ct) =>
            {
                // Tenant-scoped existence check (Match global filter).
                var exists = await db.Matches.AnyAsync(m => m.Id == matchId, ct);
                if (!exists) return Results.NotFound(new { error = "Match not found." });

                var jobId = jobs.Enqueue<IResumeTailorJob>(j => j.TailorAsync(matchId, CancellationToken.None));
                return Results.Accepted($"/api/matches/{matchId}", new { enqueued = jobId });
            })
            .WithTags("matches")
            .RequireAuthorization()
            .WithSummary("Enqueue resume tailoring for a match (rewrite + PDF, runs on the worker).");

        return app;
    }
}
