// StripeSignature — verifies the Stripe-Signature header on incoming webhooks.
//
// Stripe signs `${timestamp}.${rawBody}` with HMAC-SHA256 under the endpoint's
// webhook secret and sends it as `t=<ts>,v1=<hex>` (possibly multiple v1s during
// secret rotation). We recompute and compare in constant time, and reject
// payloads outside the tolerance window to blunt replay attacks.
//
// Pure + static so it's unit-testable with no HTTP.
//
// Refs: AIRMVP1-406

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aireq.Api.Billing;

public static class StripeSignature
{
    /// <summary>Verify a Stripe webhook signature header against the raw payload.</summary>
    public static bool Verify(
        string payload, string? signatureHeader, string? secret,
        long toleranceSeconds = 300, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
            return false;

        string? t = null;
        var v1s = new List<string>();
        foreach (var part in signatureHeader.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var val = kv[1].Trim();
            if (key == "t") t = val;
            else if (key == "v1") v1s.Add(val);
        }

        if (t is null || v1s.Count == 0) return false;
        if (!long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts)) return false;

        // Replay window.
        var nowSec = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        if (Math.Abs(nowSec - ts) > toleranceSeconds) return false;

        var signedPayload = $"{t}.{payload}";
        var expected = Hmac(secret!, signedPayload);

        foreach (var candidate in v1s)
        {
            if (FixedTimeEqualsHex(candidate, expected)) return true;
        }
        return false;
    }

    private static string Hmac(string secret, string data)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = h.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexStringLower(hash);
    }

    private static bool FixedTimeEqualsHex(string a, string b)
    {
        var ab = Encoding.ASCII.GetBytes(a);
        var bb = Encoding.ASCII.GetBytes(b);
        return ab.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
