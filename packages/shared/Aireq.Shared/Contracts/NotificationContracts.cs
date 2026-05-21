// Notification DTOs for the in-app feed + SignalR push payload (AIRMVP1-403).
// Lives in shared so the API (producer), the SignalR client, and the web feed
// agree on the shape.
//
// Refs: AIRMVP1-403

namespace Aireq.Shared.Contracts;

/// <param name="Type">escalation | reply | submission | digest | system.</param>
/// <param name="Link">Relative app link to act on it, if any.</param>
/// <param name="Read">True once the user has marked it read.</param>
public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string? Body,
    string? Link,
    Guid? MatchId,
    bool Read,
    DateTimeOffset CreatedAt);

/// <param name="UnreadCount">Unread notifications for the current tenant.</param>
public sealed record NotificationFeed(
    IReadOnlyList<NotificationResponse> Items,
    int UnreadCount);
