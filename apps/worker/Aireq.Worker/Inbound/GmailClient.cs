// GmailClient — raw-HTTPS implementation of IGmailClient.
//
// Deliberately NOT the Google.Apis.Gmail SDK: the surface we need is tiny (token
// refresh + list + get), the SDK drags a large dependency tree, and raw calls
// keep the exact wire behaviour auditable. All endpoints are the public Gmail
// REST v1 + OAuth2 token endpoints.
//
//   Token refresh : POST https://oauth2.googleapis.com/token
//   List messages : GET  https://gmail.googleapis.com/gmail/v1/users/me/messages?q=...
//   Get message   : GET  https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=full
//
// Config: GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET (shared with the API's OAuth
// connect flow). The client self-disables (returns empty) when they're unset so
// the poller is a no-op until Gmail is wired up.
//
// Refs: AIRMVP1-401

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aireq.Api.Data.Entities;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Inbound;

public sealed class GmailClient(
    HttpClient http,
    IConfiguration config,
    IOptions<GmailInboundOptions> options,
    ILogger<GmailClient> log) : IGmailClient
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string MessagesEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages";

    // Refresh a little before the token actually expires so an in-flight poll
    // never races the boundary.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(2);

    private string? ClientId => config["GOOGLE_CLIENT_ID"];
    private string? ClientSecret => config["GOOGLE_CLIENT_SECRET"];

    private bool Configured =>
        !string.IsNullOrWhiteSpace(ClientId) && !ClientId!.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ClientSecret) && !ClientSecret!.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase);

    public async Task<string> EnsureAccessTokenAsync(GmailAccount account, CancellationToken ct = default)
    {
        if (!Configured)
            throw new InvalidOperationException("Gmail OAuth not configured (GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET).");

        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(account.AccessToken)
            && account.AccessTokenExpiresAt is { } exp
            && exp - ExpirySkew > now)
        {
            return account.AccessToken!;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId!,
                ["client_secret"] = ClientSecret!,
                ["refresh_token"] = account.RefreshToken,
                ["grant_type"] = "refresh_token",
            }),
        };

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Gmail token refresh failed ({(int)resp.StatusCode}): {body}");
        }

        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts, ct)
                    ?? throw new InvalidOperationException("Gmail token refresh returned an empty body.");

        account.AccessToken = token.AccessToken;
        account.AccessTokenExpiresAt = now.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3000);
        return account.AccessToken!;
    }

    public async Task<GmailPollResult> PollAsync(GmailAccount account, int maxMessages, CancellationToken ct = default)
    {
        if (!Configured)
        {
            log.LogInformation("Gmail client disabled (GOOGLE_CLIENT_ID / SECRET not set); skipping poll.");
            return new GmailPollResult([], null);
        }

        var token = await EnsureAccessTokenAsync(account, ct);

        // Cursor: LastHistoryId holds the unix-seconds of the newest message we
        // processed. First poll (null) scans a short recent window so replies
        // that arrived between connect and first poll aren't lost.
        var query = long.TryParse(account.LastHistoryId, out var sinceEpoch)
            ? $"in:inbox after:{sinceEpoch}"
            : $"in:inbox newer_than:{Math.Max(1, options.Value.InitialScanDays)}d";

        var listUrl =
            $"{MessagesEndpoint}?maxResults={Math.Clamp(maxMessages, 1, 100)}&q={Uri.EscapeDataString(query)}";

        ListResponse? list;
        using (var listReq = new HttpRequestMessage(HttpMethod.Get, listUrl))
        {
            listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var listResp = await http.SendAsync(listReq, ct);
            if (!listResp.IsSuccessStatusCode)
            {
                var body = await listResp.Content.ReadAsStringAsync(ct);
                log.LogWarning("Gmail messages.list failed ({Status}): {Body}", (int)listResp.StatusCode, body);
                return new GmailPollResult([], null);
            }
            list = await listResp.Content.ReadFromJsonAsync<ListResponse>(JsonOpts, ct);
        }

        var ids = list?.Messages;
        if (ids is null || ids.Count == 0)
            return new GmailPollResult([], null);

        var parsed = new List<InboundEmail>(ids.Count);
        foreach (var stub in ids)
        {
            if (string.IsNullOrEmpty(stub.Id)) continue;
            var msg = await GetMessageAsync(stub.Id!, token, ct);
            if (msg is not null) parsed.Add(msg);
        }

        // Oldest-first so threads append in arrival order.
        parsed.Sort((a, b) => a.ReceivedAt.CompareTo(b.ReceivedAt));

        // Advance the cursor to one second past the newest message we saw, so the
        // next inclusive `after:` query doesn't re-list the same boundary message
        // (the processor dedupes by id regardless, this just trims fetches).
        string? newCursor = parsed.Count == 0
            ? null
            : (parsed[^1].ReceivedAt.ToUnixTimeSeconds() + 1).ToString();

        return new GmailPollResult(parsed, newCursor);
    }

    private async Task<InboundEmail?> GetMessageAsync(string id, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{MessagesEndpoint}/{id}?format=full");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("Gmail messages.get {Id} failed ({Status}).", id, (int)resp.StatusCode);
            return null;
        }

        var m = await resp.Content.ReadFromJsonAsync<MessageResource>(JsonOpts, ct);
        if (m?.Payload is null) return null;

        var headers = m.Payload.Headers ?? [];
        var fromRaw = HeaderValue(headers, "From") ?? "";
        var (fromEmail, fromName) = ParseFrom(fromRaw);
        if (string.IsNullOrEmpty(fromEmail)) return null;

        var subject = HeaderValue(headers, "Subject") ?? "";
        var received = m.InternalDate is { } ms && long.TryParse(ms, out var epochMs)
            ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs)
            : DateTimeOffset.UtcNow;

        var body = ExtractBody(m.Payload);
        if (string.IsNullOrWhiteSpace(body)) body = m.Snippet ?? "";

        return new InboundEmail(
            ProviderMessageId: m.Id ?? id,
            ThreadId: m.ThreadId ?? "",
            FromEmail: fromEmail,
            FromName: fromName,
            Subject: subject,
            Body: body,
            ReceivedAt: received);
    }

    private static string? HeaderValue(IReadOnlyList<Header> headers, string name) =>
        headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>Split a From header ("Jane Doe &lt;jane@x.com&gt;" or "jane@x.com")
    /// into (email lower-cased, display name).</summary>
    public static (string Email, string? Name) ParseFrom(string from)
    {
        if (string.IsNullOrWhiteSpace(from)) return ("", null);
        var lt = from.IndexOf('<');
        var gt = from.IndexOf('>');
        if (lt >= 0 && gt > lt)
        {
            var email = from[(lt + 1)..gt].Trim().ToLowerInvariant();
            var name = from[..lt].Trim().Trim('"');
            return (email, string.IsNullOrWhiteSpace(name) ? null : name);
        }
        return (from.Trim().ToLowerInvariant(), null);
    }

    /// <summary>Walk the MIME tree for the first text/plain body; fall back to the
    /// top-level body. Gmail base64url-encodes the data.</summary>
    internal static string ExtractBody(Payload payload)
    {
        var plain = FindPart(payload, "text/plain");
        var data = plain?.Body?.Data ?? payload.Body?.Data;
        return DecodeBase64Url(data);
    }

    private static Payload? FindPart(Payload p, string mime)
    {
        if (string.Equals(p.MimeType, mime, StringComparison.OrdinalIgnoreCase) && p.Body?.Data is not null)
            return p;
        if (p.Parts is null) return null;
        foreach (var child in p.Parts)
        {
            var hit = FindPart(child, mime);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Decode a Gmail base64url payload to UTF-8 text (empty on garbage).</summary>
    public static string DecodeBase64Url(string? data)
    {
        if (string.IsNullOrEmpty(data)) return "";
        var s = data.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch (FormatException) { return ""; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ---- Wire DTOs ---------------------------------------------------------
    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class ListResponse
    {
        [JsonPropertyName("messages")] public List<MessageStub>? Messages { get; set; }
    }

    private sealed class MessageStub
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("threadId")] public string? ThreadId { get; set; }
    }

    private sealed class MessageResource
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("threadId")] public string? ThreadId { get; set; }
        [JsonPropertyName("snippet")] public string? Snippet { get; set; }
        [JsonPropertyName("internalDate")] public string? InternalDate { get; set; }
        [JsonPropertyName("payload")] public Payload? Payload { get; set; }
    }

    internal sealed class Payload
    {
        [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
        [JsonPropertyName("headers")] public List<Header>? Headers { get; set; }
        [JsonPropertyName("body")] public PayloadBody? Body { get; set; }
        [JsonPropertyName("parts")] public List<Payload>? Parts { get; set; }
    }

    internal sealed class Header
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    internal sealed class PayloadBody
    {
        [JsonPropertyName("data")] public string? Data { get; set; }
    }
}
