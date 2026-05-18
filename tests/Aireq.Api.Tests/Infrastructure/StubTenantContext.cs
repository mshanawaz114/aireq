using Aireq.Api.Auth;

namespace Aireq.Api.Tests.Infrastructure;

/// <summary>
/// Configurable ITenantContext for tests. Lets each test assertion pin the
/// tenant identity to a known value without standing up the HTTP pipeline.
/// </summary>
public sealed class StubTenantContext : ITenantContext
{
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string? Role { get; set; }
    public bool IsAuthenticated => TenantId is not null && UserId is not null;
}
