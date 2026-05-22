// ThreadEndpoints — recruiter Inbox read API.
//
//   GET /api/threads → tenant's recruiter threads (newest activity first) with
//                      their messages.
//
// Refs: AIRMVP1-401 (read side)

using Aireq.Api.Threads;

namespace Aireq.Api.Endpoints;

public static class ThreadEndpoints
{
    public static IEndpointRouteBuilder MapThreadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/threads", async (ThreadService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(ct)))
            .WithTags("inbox")
            .RequireAuthorization()
            .WithSummary("List recruiter threads with their messages.");

        return app;
    }
}
