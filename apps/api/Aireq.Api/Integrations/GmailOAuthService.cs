// GmailOAuthService — server side of the Gmail "connect your inbox" flow.
//
//   BuildAuthorizationUrl(tenantId) → the Google consent URL the browser is sent
//       to. The tenant id is carried in a signed `state` (HMAC over the JWT
//       signing key) so the stateless callback can trust which tenant it's for
//       without a server-side session.
//
//   HandleCallbackAsync(code, state) → verifies the state, exchanges the auth
//       code for tokens, reads the mailbox address from the Gmail profile, and
//       upserts the tenant's single GmailAccount (refresh token + cursor reset).
//
// Credentials (GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET) are read flat so the API
// and the worker's GmailClient share one pair. The flow self-disables (IsEnabled
// = false) until they're set, so the connect endpoint 503s cleanly in dev.
//
// Refs: AIRMVP1-401

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Api.Integrations;

public sealed class GmailOAuthService(
    HttpClient http,
    AireqDbContext db,
    IConfiguration config,
    IOptions<GmailOAuthOptions> options,
    ILogger<GmailOAuthService> log)
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string ProfileEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/profile";

    private GmailOAuthOptions Opts => options.Value;
    private string? ClientId => config["GOOGLE_CLIENT_ID"] ?? Opts.ClientId;
    private string? ClientSecret => config["GOOGLE_CLIENT_SECRET"] ?? Opts.ClientSecret;
    private string SigningKey => config["JWT:SIGNING_KEY"] ?? "dev-only-do-not-use-in-prod-32bytes!!";

    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !ClientId!.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ClientSecret)
        && !ClientSecret!.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where to bounce the browser after a successful connect.</summary>
    public string PostConnectRedirect => Opts.PostConnectRedirect;

    public string BuildAuthorizationUrl(Guid tenantId)
    {
        var state = SignState(tenantId);
        var q = new Dictionary<string, string?>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = Opts.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Opts.Scope,
            // offline + consent guarantees a refresh_token is issued every time.
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
        };
        var query = string.Join('&', q
            .Where(kv => kv.Value is not null)
            .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));
        return $"{AuthEndpoint}?{query}";
    }

    /// <summary>
    /// Complete the OAuth dance: verify state, exchange code, read the mailbox
    /// address, upsert the GmailAccount. Returns the connected address.
    /// </summary>
    public async Task<string> HandleCallbackAsync(string code, string state, CancellationToken ct = default)
    {
        if (!TryReadState(state, out var tenantId))
            throw new InvalidOperationException("Invalid or tampered OAuth state.");

        // 1. Exchange the auth code for tokens.
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = ClientId!,
                ["client_secret"] = ClientSecret!,
                ["redirect_uri"] = Opts.RedirectUri,
                ["grant_type"] = "authorization_code",
            }),
        };
        using var tokenResp = await http.SendAsync(tokenReq, ct);
        if (!tokenResp.IsSuccessStatusCode)
        {
            var body = await tokenResp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Token exchange failed ({(int)tokenResp.StatusCode}): {body}");
        }
        var token = await tokenResp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                    ?? throw new InvalidOperationException("Token exchange returned an empty body.");
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new InvalidOperationException(
                "Google did not return a refresh_token. Revoke prior access and reconnect (prompt=consent).");

        // 2. Read the connected mailbox address from the Gmail profile.
        using var profReq = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
        profReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var profResp = await http.SendAsync(profReq, ct);
        profResp.EnsureSuccessStatusCode();
        var profile = await profResp.Content.ReadFromJsonAsync<ProfileResponse>(cancellationToken: ct);
        var address = profile?.EmailAddress ?? "unknown";

        // 3. Upsert the tenant's single connected mailbox.
        var account = await db.GmailAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.TenantId == tenantId, ct);
        if (account is null)
        {
            account = new GmailAccount
            {
                TenantId = tenantId,
                EmailAddress = address,
                RefreshToken = token.RefreshToken!,
            };
            db.GmailAccounts.Add(account);
        }
        else
        {
            account.EmailAddress = address;
            account.RefreshToken = token.RefreshToken!;
            // New grant — drop the old access token + sync cursor so the next
            // poll re-establishes from scratch.
            account.LastHistoryId = null;
        }
        account.AccessToken = token.AccessToken;
        account.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3000);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Connected Gmail mailbox {Address} for tenant {Tenant}.", address, tenantId);
        return address;
    }

    // ---- Signed state (HMAC over JWT signing key) --------------------------
    private string SignState(Guid tenantId)
    {
        var payload = tenantId.ToString("N");
        var sig = Convert.ToBase64String(Hmac(payload));
        return $"{payload}.{Base64Url(sig)}";
    }

    private bool TryReadState(string? state, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(state)) return false;
        var dot = state.IndexOf('.');
        if (dot <= 0) return false;
        var payload = state[..dot];
        var providedSig = state[(dot + 1)..];
        var expectedSig = Base64Url(Convert.ToBase64String(Hmac(payload)));
        // Constant-time comparison to avoid leaking signature bytes via timing.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedSig), Encoding.UTF8.GetBytes(expectedSig)))
            return false;
        return Guid.TryParseExact(payload, "N", out tenantId);
    }

    private byte[] Hmac(string data)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        return h.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Base64Url(string s) =>
        s.Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class ProfileResponse
    {
        [JsonPropertyName("emailAddress")] public string? EmailAddress { get; set; }
    }
}
