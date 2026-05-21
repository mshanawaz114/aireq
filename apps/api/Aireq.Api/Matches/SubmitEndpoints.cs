// SubmitEndpoints — enqueue an application submission for a match.
//
//   POST /api/matches/{matchId}/submit  → 202 Accepted (worker submits).
//
// This POST IS the explicit per-match approval (§12/§14). The match must be
// Tailored (a tailored resume exists) before it can be submitted. Whether the
// worker actually sends or dry-runs depends on FEATURES__ENABLE_LIVE_SUBMIT.
//
// Refs: AIRMVP1-303

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Endpoints;

public static class SubmitEndpoints
{
    public static IEndpointRouteBuilder MapSubmitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/matches/{matchId:guid}/submit", async (
                Guid matchId,
                AireqDbContext db,
                IBackgroundJobClient jobs,
                CancellationToken ct) =>
            {
                // Tenant-scoped lookup (Match global filter).
                var status = await db.Matches
                    .Where(m => m.Id == matchId)
                    .Select(m => (MatchStatus?)m.Status)
                    .SingleOrDefaultAsync(ct);

                if (status is null)
                    return Results.NotFound(new { error = "Match not found." });
                if (status != MatchStatus.Tailored)
                    return Results.Conflict(new
                    {
                        error = "Match must be tailored before submitting. Tailor it first.",
                    });

                var jobId = jobs.Enqueue<ISubmissionJob>(j => j.SubmitAsync(matchId, CancellationToken.None));
                return Results.Accepted($"/api/matches/{matchId}", new { enqueued = jobId });
            })
            .WithTags("matches")
            .RequireAuthorization()
            .WithSummary("Approve + enqueue an application submission for a tailored match.");

        return app;
    }
}
