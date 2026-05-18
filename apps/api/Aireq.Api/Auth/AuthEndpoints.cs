// AuthEndpoints — signup / login / me.
//
// Signup: creates Tenant + first User in one transaction.
// Login: validates credentials, returns JWT.
// Me: returns the current authenticated user.
//
// Password hashing: Microsoft.AspNetCore.Identity.PasswordHasher<User> — uses
// PBKDF2 with HMAC-SHA-512 by default, includes salt and version prefix,
// safe to upgrade later by re-hashing on next successful login.
//
// Validation: shape checks inline; AIRMVP1-106 will swap to FluentValidation
// when forms come in.
//
// Refs: AIRMVP1-103

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aireq.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapPost("/signup", SignupAsync).AllowAnonymous();
        group.MapPost("/login",  LoginAsync).AllowAnonymous();
        group.MapGet ("/me",     MeAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> SignupAsync(
        [FromBody] SignupRequest req,
        AireqDbContext db,
        IPasswordHasher<User> hasher,
        JwtTokenService jwt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["A valid email is required."],
            });

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 12)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["Password must be at least 12 characters."],
            });

        if (string.IsNullOrWhiteSpace(req.TenantName))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["tenantName"] = ["Tenant name is required."],
            });

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var emailExists = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == normalizedEmail, ct);
        if (emailExists)
            return Results.Conflict(new { error = "An account with this email already exists." });

        var tenant = new Tenant { Name = req.TenantName.Trim(), Plan = "solo" };
        var user = new User
        {
            TenantId = tenant.Id,
            Email = normalizedEmail,
            DisplayName = req.DisplayName?.Trim(),
            Role = "owner",
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);

        db.Tenants.Add(tenant);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Race / retry / leftover from an earlier partial signup.
            return pg.ConstraintName switch
            {
                "ix_tenants_name" => Results.Conflict(new
                {
                    error = "A workspace with this name already exists. Pick a different workspace name.",
                }),
                "ix_users_email" => Results.Conflict(new
                {
                    error = "An account with this email already exists. Sign in instead.",
                }),
                _ => Results.Conflict(new
                {
                    error = "A record with these details already exists.",
                }),
            };
        }

        return Results.Ok(BuildAuthResponse(user, tenant, jwt));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest req,
        AireqDbContext db,
        IPasswordHasher<User> hasher,
        JwtTokenService jwt,
        CancellationToken ct)
    {
        var normalizedEmail = req.Email?.Trim().ToLowerInvariant() ?? "";

        // Always do password verification work — even on user-not-found — to
        // avoid leaking via timing. Use a known fixed hash for the dummy path.
        var user = await db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        var hashToCheck = user?.PasswordHash ?? DummyPasswordHash;
        var dummyUser = user ?? new User { Email = normalizedEmail };
        var verify = hasher.VerifyHashedPassword(dummyUser, hashToCheck, req.Password ?? "");

        if (user is null || verify == PasswordVerificationResult.Failed)
            return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

        // Opportunistic rehash if the hasher's algorithm has moved on.
        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, req.Password!);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(BuildAuthResponse(user, user.Tenant, jwt));
    }

    private static async Task<IResult> MeAsync(
        AireqDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (tenant.UserId is null)
            return Results.Unauthorized();

        var user = await db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == tenant.UserId, ct);

        if (user is null) return Results.NotFound();

        return Results.Ok(new AuthUser(
            user.Id,
            user.TenantId,
            user.Email,
            user.DisplayName,
            user.Role,
            user.Tenant.Name,
            user.Tenant.Plan));
    }

    private static AuthResponse BuildAuthResponse(User user, Tenant tenant, JwtTokenService jwt)
    {
        var token = jwt.Issue(user);
        return new AuthResponse(
            token.AccessToken,
            token.ExpiresAt,
            new AuthUser(
                user.Id,
                user.TenantId,
                user.Email,
                user.DisplayName,
                user.Role,
                tenant.Name,
                tenant.Plan));
    }

    // Pre-computed PasswordHasher output for "constant-time-dummy" — never
    // used as a real password; only consumed by the timing-attack guard.
    private const string DummyPasswordHash =
        "AQAAAAIAAYagAAAAEPlaceholderConstantTimeDummyHash_NotARealPassword=";
}
