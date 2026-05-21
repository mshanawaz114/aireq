// StripeSignatureTests — webhook signature verification (valid, tampered,
// missing, replay-window). Pure HMAC, no HTTP.
//
// Refs: AIRMVP1-406

using System.Security.Cryptography;
using System.Text;
using Aireq.Api.Billing;
using FluentAssertions;
using Xunit;

namespace Aireq.Api.Tests.Billing;

public sealed class StripeSignatureTests
{
    private const string Secret = "whsec_test_secret";

    private static string Header(string payload, long ts, string secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexStringLower(h.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}")));
        return $"t={ts},v1={hex}";
    }

    [Fact]
    public void Valid_signature_passes()
    {
        var payload = "{\"id\":\"evt_1\"}";
        var now = DateTimeOffset.UtcNow;
        var header = Header(payload, now.ToUnixTimeSeconds(), Secret);

        StripeSignature.Verify(payload, header, Secret, now: now).Should().BeTrue();
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var header = Header("{\"id\":\"evt_1\"}", now.ToUnixTimeSeconds(), Secret);

        StripeSignature.Verify("{\"id\":\"evil\"}", header, Secret, now: now).Should().BeFalse();
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        var payload = "{\"id\":\"evt_1\"}";
        var now = DateTimeOffset.UtcNow;
        var header = Header(payload, now.ToUnixTimeSeconds(), "whsec_other");

        StripeSignature.Verify(payload, header, Secret, now: now).Should().BeFalse();
    }

    [Fact]
    public void Outside_tolerance_window_fails()
    {
        var payload = "{\"id\":\"evt_1\"}";
        var now = DateTimeOffset.UtcNow;
        var oldTs = now.AddMinutes(-10).ToUnixTimeSeconds();
        var header = Header(payload, oldTs, Secret);

        StripeSignature.Verify(payload, header, Secret, toleranceSeconds: 300, now: now).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=123")]
    public void Malformed_or_missing_header_fails(string header)
    {
        StripeSignature.Verify("{}", header, Secret).Should().BeFalse();
    }

    [Fact]
    public void Missing_secret_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var header = Header("{}", now.ToUnixTimeSeconds(), Secret);
        StripeSignature.Verify("{}", header, null, now: now).Should().BeFalse();
    }
}
