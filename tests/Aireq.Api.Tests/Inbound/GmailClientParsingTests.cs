// GmailClientParsingTests — the pure wire-parsing helpers on GmailClient
// (From-header splitting + base64url body decode). No network.
//
// Refs: AIRMVP1-401

using System.Text;
using Aireq.Worker.Inbound;
using FluentAssertions;
using Xunit;

namespace Aireq.Api.Tests.Inbound;

public sealed class GmailClientParsingTests
{
    [Theory]
    [InlineData("Jane Doe <jane@x.com>", "jane@x.com", "Jane Doe")]
    [InlineData("\"Doe, Jane\" <Jane@X.com>", "jane@x.com", "Doe, Jane")]
    [InlineData("bare@x.com", "bare@x.com", null)]
    [InlineData("<only@x.com>", "only@x.com", null)]
    public void ParseFrom_splits_email_and_name(string header, string email, string? name)
    {
        var (parsedEmail, parsedName) = GmailClient.ParseFrom(header);
        parsedEmail.Should().Be(email);
        parsedName.Should().Be(name);
    }

    [Fact]
    public void ParseFrom_empty_is_empty()
    {
        var (email, name) = GmailClient.ParseFrom("");
        email.Should().BeEmpty();
        name.Should().BeNull();
    }

    [Fact]
    public void DecodeBase64Url_round_trips_utf8()
    {
        const string text = "Thanks — let's chat! 你好";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        GmailClient.DecodeBase64Url(b64).Should().Be(text);
    }

    [Fact]
    public void DecodeBase64Url_handles_null_and_garbage()
    {
        GmailClient.DecodeBase64Url(null).Should().BeEmpty();
        GmailClient.DecodeBase64Url("!!!not base64!!!").Should().BeEmpty();
    }
}
