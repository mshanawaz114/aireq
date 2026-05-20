// MatchEndpoints — read API for the Matches UI.
//
//   GET /api/matches?minScore=&status=   → tenant-scoped scored matches.
//
// Refs: AIRMVP1-206

using Aireq.Api.Data.Entities;
using Aireq.Api.Matches;
using Microsoft.AspNetCore.Mvc;

namespace Aireq.Api.Endpoints;

public static class MatchEndpoints
{
    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/matches", async (
                [FromQuery] int? minScore,
                [FromQuery] string? status,
                MatchListService svc,
                CancellationToken ct) =>
            {
                MatchStatus? parsed = null;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    if (!Enum.TryParse<MatchStatus>(status, ignoreCase: true, out var s))
                        return Results.BadRequest(new { error = $"Unknown status '{status}'." });
                    parsed = s;
                }

                var matches = await svc.ListAsync(minScore, parsed, ct);
                return Results.Ok(matches);
            })
            .WithTags("matches")
            .RequireAuthorization()
            .WithSummary("List the current tenant's scored job matches.");

        return app;
    }
}
