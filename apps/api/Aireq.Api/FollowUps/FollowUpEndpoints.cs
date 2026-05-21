// FollowUpEndpoints — the follow-up approval queue API.
//
//   GET  /api/followups?pending=true       → drafts (pending by default).
//   POST /api/followups/{id}/approve         → approve (sender ships it next pass).
//   POST /api/followups/{id}/cancel          → decline / drop.
//
// Refs: AIRMVP1-404

using Aireq.Api.FollowUps;

namespace Aireq.Api.Endpoints;

public static class FollowUpEndpoints
{
    public static IEndpointRouteBuilder MapFollowUpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/followups").WithTags("followups").RequireAuthorization();

        group.MapGet("", async (bool? pending, FollowUpService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(pendingOnly: pending ?? true, ct)))
            .WithSummary("List follow-up nudges (pending approval by default).");

        group.MapPost("/{id:guid}/approve", async (Guid id, FollowUpService svc, CancellationToken ct) =>
                await svc.ApproveAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Approve a follow-up so it sends on the next pass.");

        group.MapPost("/{id:guid}/cancel", async (Guid id, FollowUpService svc, CancellationToken ct) =>
                await svc.CancelAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Cancel a planned follow-up.");

        return app;
    }
}
