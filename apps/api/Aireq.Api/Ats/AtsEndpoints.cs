// AtsEndpoints — on-demand ATS coverage for a match.
//
//   GET /api/matches/{matchId}/ats  → AtsAnalysis, or 404 if not visible.
//
// Refs: AIRMVP1-301

using Aireq.Api.Ats;

namespace Aireq.Api.Endpoints;

public static class AtsEndpoints
{
    public static IEndpointRouteBuilder MapAtsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/matches/{matchId:guid}/ats", async (
                Guid matchId, AtsAnalysisService svc, CancellationToken ct) =>
            {
                var analysis = await svc.AnalyzeAsync(matchId, ct);
                return analysis is null
                    ? Results.NotFound(new { error = "Match not found." })
                    : Results.Ok(analysis);
            })
            .WithTags("matches")
            .RequireAuthorization()
            .WithSummary("ATS keyword coverage for a match (resume vs JD).");

        return app;
    }
}
