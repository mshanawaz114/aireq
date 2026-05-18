// JwtTokenService — issues short-lived access tokens.
//
// Claims:
//   sub        — user id (Guid)
//   tenant_id  — tenant id (Guid)  — custom claim consumed by HttpTenantContext
//   role       — role string
//   email      — convenience
//   iat / exp  — issued at / expires at
//
// AIRMVP1-103 issues stateless access tokens only (60 min default). Refresh-
// token rotation lands in AIRMVP1-103b; until then users re-log in once an
// hour, which is acceptable for dev / first-tenant testing.
//
// Refs: AIRMVP1-103

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aireq.Api.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Aireq.Api.Auth;

public sealed class JwtTokenService(IConfiguration config)
{
    // .NET's IConfiguration normalises env-var '__' separators to ':'. The
    // .env.local file uses 'JWT__SIGNING_KEY' (the env-var form); inside C#
    // we read the hierarchical form 'JWT:SIGNING_KEY'.
    private readonly string _key = config["JWT:SIGNING_KEY"]
        ?? throw new InvalidOperationException(
            "JWT:SIGNING_KEY not configured. Set JWT__SIGNING_KEY in .env.local " +
            "(any 32+ char random string; generate one with `openssl rand -base64 48`).");
    private readonly string _issuer = config["JWT:ISSUER"] ?? "aireq";
    private readonly string _audience = config["JWT:AUDIENCE"] ?? "aireq-web";
    private readonly int _lifetimeMinutes = int.TryParse(
        config["JWT:LIFETIME_MINUTES"], out var m) ? m : 60;

    public IssuedToken Issue(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_lifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(AireqClaimTypes.TenantId, user.TenantId.ToString()),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new IssuedToken(encoded, expires);
    }
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
