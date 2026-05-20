// JobContentHashTests — the dedup fingerprint must be stable across cosmetic
// formatting differences and distinct across real content differences.
//
// Refs: AIRMVP1-203

using Aireq.Worker.Jobs;
using FluentAssertions;
using Xunit;

namespace Aireq.Api.Tests.Jobs;

public sealed class JobContentHashTests
{
    [Fact]
    public void Same_content_different_formatting_hashes_equal()
    {
        var a = JobContentHash.Compute("Acme Corp", "Senior Engineer", "Austin, TX", "Build things.");
        var b = JobContentHash.Compute("  acme   corp ", "SENIOR  engineer", "austin, tx", "build things.");
        a.Should().Be(b, "normalization collapses case + whitespace so cross-source copies collide");
    }

    [Fact]
    public void Different_company_hashes_differ()
    {
        var a = JobContentHash.Compute("Acme", "Engineer", "Remote", "JD");
        var b = JobContentHash.Compute("Globex", "Engineer", "Remote", "JD");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Only_first_500_description_chars_matter()
    {
        var prefix = new string('x', 500);
        var a = JobContentHash.Compute("Acme", "Engineer", "Remote", prefix + "AAA");
        var b = JobContentHash.Compute("Acme", "Engineer", "Remote", prefix + "BBB");
        a.Should().Be(b, "differences past the 500-char prefix are ignored");
    }

    [Fact]
    public void Hash_is_64_hex_chars()
    {
        var h = JobContentHash.Compute("Acme", "Engineer", null, null);
        h.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
