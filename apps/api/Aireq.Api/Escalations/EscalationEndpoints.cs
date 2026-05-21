// EscalationEndpoints — the "needs you" queue API.
//
//   GET  /api/escalations?open=true        → tenant escalations, newest first.
//   POST /api/escalations/{id}/resolve      → mark one resolved.
//
// Refs: AIRMVP1-402

using Aireq.Api.Escalations;

namespace Aireq.Api.Endpoints;

public static class EscalationEndpoints
{
    public static IEndpointRouteBuilder MapEscalationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/escalations").WithTags("escalations").RequireAuthorization();

        group.MapGet("", async (bool? open, EscalationService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(openOnly: open ?? true, ct)))
            .WithSummary("List escalations needing human attention (open by default).");

        group.MapPost("/{id:guid}/resolve", async (Guid id, EscalationService svc, CancellationToken ct) =>
                await svc.ResolveAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Mark an escalation resolved.");

        return app;
    }
}
