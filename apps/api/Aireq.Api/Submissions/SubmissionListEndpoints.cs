// SubmissionListEndpoints — submission tracker read API.
//
//   GET /api/submissions  → tenant-scoped submission attempts, newest first.
//
// Refs: AIRMVP1-306

using Aireq.Api.Submissions;

namespace Aireq.Api.Endpoints;

public static class SubmissionListEndpoints
{
    public static IEndpointRouteBuilder MapSubmissionListEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/submissions", async (SubmissionListService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(ct)))
            .WithTags("submissions")
            .RequireAuthorization()
            .WithSummary("List the current tenant's application submissions with audit detail.");

        return app;
    }
}
