// Notification — a durable in-app notification for a tenant.
//
// The DB row is the source of truth (so an unread badge survives reconnects and
// the worker — a separate process from the SignalR hub — can raise one by simply
// inserting a row). The API's NotificationsHub streams rows it creates to live
// clients; worker-created rows surface on the client's next fetch/reconnect.
//
// Tenant-scoped via the global query filter, like Match.
//
// Refs: AIRMVP1-403

namespace Aireq.Api.Data.Entities;

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>escalation | reply | submission | digest | system.</summary>
    public required string Type { get; set; }

    public required string Title { get; set; }
    public string? Body { get; set; }

    /// <summary>Relative app link to act on it (e.g. "/matches/{id}").</summary>
    public string? Link { get; set; }

    /// <summary>The Match this notification concerns, when applicable.</summary>
    public Guid? MatchId { get; set; }

    /// <summary>Null until the user marks it read.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public const int MaxBodyChars = 2_000;
}
