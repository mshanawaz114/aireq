// MetricsEndpoints — admin metrics read API.
//
//   GET /api/admin/metrics  → aggregated pipeline + tenant metrics.
//
// Refs: AIRMVP1-207

using Aireq.Api.Metrics;

namespace Aireq.Api.Endpoints;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/metrics", async (MetricsService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetAsync(ct)))
            .WithTags("admin")
            .RequireAuthorization()
            .WithSummary("Aggregated discovery-pipeline + tenant metrics for the dashboard.");

        return app;
    }
}
