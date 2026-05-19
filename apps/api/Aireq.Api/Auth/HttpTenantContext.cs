// HttpTenantContext — production implementation that reads the JWT claims off
// the current HttpContext. Registered as Scoped so each request gets its own
// instance and EF Core's query filters see the right tenant.
//
// Claims read:
//   - sub          → UserId
//   - tenant_id    → TenantId
//   - role         → Role
//
// Refs: AIRMVP1-103

using System.Security.Claims;

namespace Aireq.Api.Auth;

public sealed class HttpTenantContext(IHttpContextAccessor http) : ITenantContext
{
    public Guid? TenantId => ReadGuidClaim(AireqClaimTypes.TenantId);
    public Guid? UserId => ReadGuidClaim(ClaimTypes.NameIdentifier) ?? ReadGuidClaim("sub");
    public string? Role => http.HttpContext?.User?.FindFirstValue(ClaimTypes.Role)
                          ?? http.HttpContext?.User?.FindFirstValue("role");
    public bool IsAuthenticated => TenantId is not null && UserId is not null;

    private Guid? ReadGuidClaim(string type)
    {
        var raw = http.HttpContext?.User?.FindFirstValue(type);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}

public static class AireqClaimTypes
{
    public const string TenantId = "tenant_id";
}
