// NotificationsHub — SignalR hub that streams in-app notifications to the
// authenticated tenant's connected clients.
//
// On connect, the client is added to a per-tenant group ("tenant:{id}") derived
// from its JWT tenant_id claim, so NotificationService can push to exactly the
// right tenant. The hub itself carries no business logic — pushes originate in
// NotificationService.CreateAsync.
//
// Auth: the hub requires a valid bearer. SignalR's WebSocket transport can't set
// an Authorization header, so Program.cs wires JwtBearer to also read the token
// from the `access_token` query string on /hubs paths.
//
// Refs: AIRMVP1-403

using Aireq.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Aireq.Api.Notifications;

[Authorize]
public sealed class NotificationsHub : Hub
{
    /// <summary>SignalR group name for a tenant's connections.</summary>
    public static string GroupFor(Guid tenantId) => $"tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst(AireqClaimTypes.TenantId)?.Value;
        if (Guid.TryParse(tenantId, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(id));
        await base.OnConnectedAsync();
    }
}
