// NotificationService — create + read in-app notifications for the current
// tenant, pushing live ones over SignalR.
//
// CreateAsync persists the row (durable source of truth) and pushes the DTO to
// the tenant's SignalR group ("Notification" client method). Read paths are
// tenant-scoped automatically by the Notification query filter.
//
// Note: this is the API-process path. The worker raises notifications by writing
// rows directly (it's a separate process with no hub); those surface on the
// client's next fetch/reconnect.
//
// Refs: AIRMVP1-403

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Notifications;

public sealed class NotificationService(
    AireqDbContext db,
    ITenantContext tenant,
    IHubContext<NotificationsHub> hub)
{
    private const int FeedLimit = 50;

    public async Task<NotificationResponse> CreateAsync(
        string type, string title, string? body = null, string? link = null,
        Guid? matchId = null, CancellationToken ct = default)
    {
        var tenantId = tenant.TenantId
            ?? throw new InvalidOperationException("Cannot create a notification without a tenant.");

        var n = new Notification
        {
            TenantId = tenantId,
            Type = type,
            Title = title,
            Body = Truncate(body, Notification.MaxBodyChars),
            Link = link,
            MatchId = matchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);

        var dto = ToDto(n);
        await hub.Clients.Group(NotificationsHub.GroupFor(tenantId))
            .SendAsync("Notification", dto, ct);
        return dto;
    }

    public async Task<NotificationFeed> GetFeedAsync(CancellationToken ct)
    {
        // Unread first, then newest. Capped feed; unread count is exact.
        var items = await db.Notifications
            .OrderBy(n => n.ReadAt == null ? 0 : 1)
            .ThenByDescending(n => n.CreatedAt)
            .Take(FeedLimit)
            .Select(n => new NotificationResponse(
                n.Id, n.Type, n.Title, n.Body, n.Link, n.MatchId, n.ReadAt != null, n.CreatedAt))
            .ToListAsync(ct);

        var unread = await db.Notifications.CountAsync(n => n.ReadAt == null, ct);
        return new NotificationFeed(items, unread);
    }

    /// <summary>Mark one read. False when it doesn't exist for the tenant.</summary>
    public async Task<bool> MarkReadAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return false;
        if (n.ReadAt is null)
        {
            n.ReadAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    /// <summary>Mark all the tenant's unread notifications read. Returns the count.</summary>
    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var unread = await db.Notifications.Where(n => n.ReadAt == null).ToListAsync(ct);
        foreach (var n in unread) n.ReadAt = now;
        if (unread.Count > 0) await db.SaveChangesAsync(ct);
        return unread.Count;
    }

    private static NotificationResponse ToDto(Notification n) => new(
        n.Id, n.Type, n.Title, n.Body, n.Link, n.MatchId, n.ReadAt != null, n.CreatedAt);

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];
}
