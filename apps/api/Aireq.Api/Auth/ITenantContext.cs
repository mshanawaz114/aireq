// ITenantContext — the ambient "who am I and what tenant do I belong to" for
// the current request. Read by:
//   - EF Core query filters in AireqDbContext (auto-scope queries)
//   - Endpoint handlers that need to stamp tenant_id on new entities
//   - Authorization handlers
//
// Implementations:
//   - HttpTenantContext: reads from HttpContext.User claims (the normal path)
//   - StubTenantContext: for tests, lets us set the values directly
//
// Refs: AIRMVP1-103

namespace Aireq.Api.Auth;

public interface ITenantContext
{
    /// <summary>Tenant for the current request. <c>null</c> on unauthenticated requests.</summary>
    Guid? TenantId { get; }

    /// <summary>User for the current request. <c>null</c> on unauthenticated requests.</summary>
    Guid? UserId { get; }

    /// <summary>Role claim — "owner" | "admin" | "viewer". <c>null</c> on unauthenticated requests.</summary>
    string? Role { get; }

    /// <summary>True when both <see cref="TenantId"/> and <see cref="UserId"/> are present.</summary>
    bool IsAuthenticated { get; }
}
