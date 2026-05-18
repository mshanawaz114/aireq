// Auth DTOs shared between API and (eventually) generated TS types.
// Refs: AIRMVP1-103

namespace Aireq.Shared.Contracts;

public sealed record SignupRequest(
    string TenantName,
    string Email,
    string Password,
    string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthUser User);

public sealed record AuthUser(
    Guid Id,
    Guid TenantId,
    string Email,
    string? DisplayName,
    string Role,
    string TenantName,
    string TenantPlan);
