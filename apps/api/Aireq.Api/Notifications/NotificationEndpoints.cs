// NotificationEndpoints — the in-app notification feed API (REST companion to
// the SignalR push).
//
//   GET  /api/notifications            → feed (unread-first) + unread count.
//   POST /api/notifications/{id}/read  → mark one read.
//   POST /api/notifications/read-all   → mark all read.
//
// Refs: AIRMVP1-403

using Aireq.Api.Notifications;

namespace Aireq.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("notifications").RequireAuthorization();

        group.MapGet("", async (NotificationService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetFeedAsync(ct)))
            .WithSummary("Notification feed (unread-first) with the unread count.");

        group.MapPost("/{id:guid}/read", async (Guid id, NotificationService svc, CancellationToken ct) =>
                await svc.MarkReadAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .WithSummary("Mark a notification read.");

        group.MapPost("/read-all", async (NotificationService svc, CancellationToken ct) =>
                Results.Ok(new { marked = await svc.MarkAllReadAsync(ct) }))
            .WithSummary("Mark all notifications read.");

        return app;
    }
}
