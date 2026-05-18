// Health endpoints — liveness (always 200 if process is up) + readiness
// (200 only when downstream deps like Postgres are reachable).
// Refs: AIRMVP1-101

using Aireq.Shared.Contracts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Reflection;

namespace Aireq.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        // Liveness — does NOT touch the DB. Used by container orchestrators
        // to decide whether to restart the process.
        app.MapGet("/health/live", () => Results.Ok(new HealthResponse(
            Status: "ok",
            Service: "api",
            Version: version,
            DependenciesHealthy: null,
            Timestamp: DateTimeOffset.UtcNow)))
            .WithName("HealthLive")
            .WithSummary("Process liveness — returns 200 if the API is running.")
            .AllowAnonymous();

        // Readiness — DOES touch downstream deps tagged "ready" (e.g. Postgres).
        // Used by load balancers to decide whether to route traffic.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteReadinessResponse,
            AllowCachingResponses = false,
        }).AllowAnonymous();

        // Convenience alias — `/health` returns readiness.
        app.MapGet("/health", () => Results.Redirect("/health/ready"))
            .AllowAnonymous();

        return app;
    }

    private static async Task WriteReadinessResponse(HttpContext ctx, HealthReport report)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        ctx.Response.ContentType = "application/json";

        var payload = new HealthResponse(
            Status: report.Status switch
            {
                HealthStatus.Healthy => "ok",
                HealthStatus.Degraded => "degraded",
                _ => "down",
            },
            Service: "api",
            Version: version,
            DependenciesHealthy: report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Status == HealthStatus.Healthy),
            Timestamp: DateTimeOffset.UtcNow);

        await ctx.Response.WriteAsJsonAsync(payload);
    }
}
