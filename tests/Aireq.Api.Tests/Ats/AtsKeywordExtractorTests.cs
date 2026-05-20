// AtsKeywordExtractorTests — deterministic keyword coverage logic.
//
// Refs: AIRMVP1-301

using Aireq.Api.Ats;
using FluentAssertions;
using Xunit;

namespace Aireq.Api.Tests.Ats;

public sealed class AtsKeywordExtractorTests
{
    private static readonly Guid MatchId = Guid.NewGuid();

    [Fact]
    public void Splits_present_and_missing_with_coverage()
    {
        var job = "We need a Senior C# engineer with Azure, Kubernetes and PostgreSQL experience.";
        var resume = "10 years of C# and PostgreSQL. Some Docker.";

        var a = AtsKeywordExtractor.Analyze(MatchId, job, resume);

        a.PresentKeywords.Should().Contain("c#").And.Contain("postgresql");
        a.MissingKeywords.Should().Contain("azure").And.Contain("kubernetes");
        a.JobKeywordCount.Should().Be(a.PresentKeywords.Count + a.MissingKeywords.Count);
        // 2 of 4 present -> 50%.
        a.CoveragePercent.Should().Be(50);
    }

    [Fact]
    public void Word_boundary_prevents_substring_false_positives()
    {
        // "java" must NOT be detected from "javascript".
        var job = "Strong JavaScript and TypeScript skills required.";
        var a = AtsKeywordExtractor.Analyze(MatchId, job, "");

        a.MissingKeywords.Should().Contain("javascript").And.Contain("typescript");
        a.MissingKeywords.Should().NotContain("java");
    }

    [Fact]
    public void Special_char_terms_match()
    {
        var job = "Looking for .NET, C++ and CI/CD experience.";
        var a = AtsKeywordExtractor.Analyze(MatchId, job, "Built CI/CD pipelines.");

        a.PresentKeywords.Should().Contain("ci/cd");
        a.MissingKeywords.Should().Contain(".net").And.Contain("c++");
    }

    [Fact]
    public void Multi_word_phrases_match()
    {
        var job = "Machine learning role using spring boot.";
        var a = AtsKeywordExtractor.Analyze(MatchId, job, "Did machine learning at scale.");

        a.PresentKeywords.Should().Contain("machine learning");
        a.MissingKeywords.Should().Contain("spring boot");
    }

    [Fact]
    public void No_keywords_in_jd_is_full_coverage()
    {
        var a = AtsKeywordExtractor.Analyze(MatchId, "We value great communicators and team players.", "");
        a.JobKeywordCount.Should().Be(0);
        a.CoveragePercent.Should().Be(100, "nothing to miss");
    }
}
