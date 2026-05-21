// WaitlistEndpoints — anonymous marketing waitlist signup.
//
//   POST /api/waitlist  { email, persona?, source? }  → idempotent join.
//
// Public + unauthenticated (it runs before any tenant exists). Email is
// normalized + validated; a repeat submit is a no-op (AlreadyJoined=true) rather
// than an error, so the landing form is friendly on a double-tap.
//
// Refs: AIRMVP1-405

using Aireq.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Aireq.Api.Marketing;

public static class WaitlistEndpoints
{
    public static IEndpointRouteBuilder MapWaitlistEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/waitlist", JoinAsync)
            .AllowAnonymous()
            .WithTags("marketing")
            .WithSummary("Join the marketing waitlist (idempotent on email).");

        return app;
    }

    private static async Task<IResult> JoinAsync(
        [FromBody] WaitlistRequest req, WaitlistService svc, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        if (email.Length is < 3 or > 254 || !email.Contains('@') || !email.Contains('.'))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["A valid email is required."],
            });

        return Results.Ok(await svc.JoinAsync(email, req.Persona, req.Source, ct));
    }
}
