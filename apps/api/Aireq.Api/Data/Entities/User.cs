using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// A human operator inside a Tenant. The ASP.NET Identity user table lives
/// separately in AIRMVP1-103 — this entity is the domain-side mirror that
/// other tables foreign-key to. The two are kept in sync by Identity's
/// post-registration hook.
/// </summary>
public sealed class User : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public required string Email { get; set; }

    /// <summary>
    /// PHC-formatted password hash produced by <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>.
    /// Never logged. Never compared with == — use <c>PasswordHasher.VerifyHashedPassword</c>.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Display name shown in the UI (not used for auth).</summary>
    public string? DisplayName { get; set; }

    /// <summary>owner | admin | viewer.</summary>
    public string Role { get; set; } = "owner";

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
