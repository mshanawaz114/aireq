// TailoredResumeRendererTests — smoke test: renders a real PDF (valid header,
// non-trivial size). QuestPDF license is set in the test fixture.
//
// Refs: AIRMVP1-302

using Aireq.Shared.Contracts;
using Aireq.Worker.Tailoring;
using FluentAssertions;
using Xunit;

namespace Aireq.Api.Tests.Tailoring;

public sealed class TailoredResumeRendererTests
{
    static TailoredResumeRendererTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    [Fact]
    public void Renders_a_valid_pdf()
    {
        var content = new ResumeContent(
            "Alice Architect", "Sr. Salesforce Architect", "Austin, TX",
            "alice@example.com", "555-1234", "20 years building CRM platforms.",
            new[] { new ResumeSkill("Salesforce", 12), new ResumeSkill("Apex", 10) },
            new[]
            {
                new ResumeExperience("Acme", "Lead Engineer", "2018-01", null,
                    new[] { "Built X.", "Scaled Y." }),
            },
            new[] { new ResumeEducation("MIT", "BSc", "Computer Science", "2008") },
            new[] { "Salesforce Certified Architect" });

        var pdf = TailoredResumeRenderer.Render(content);

        pdf.Should().NotBeNullOrEmpty();
        pdf.Length.Should().BeGreaterThan(1000, "a real PDF has non-trivial size");
        // PDF magic header "%PDF".
        System.Text.Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Renders_with_minimal_content_without_throwing()
    {
        var content = new ResumeContent(
            "Bob", null, null, null, null, null,
            Array.Empty<ResumeSkill>(), Array.Empty<ResumeExperience>(),
            Array.Empty<ResumeEducation>(), Array.Empty<string>());

        var act = () => TailoredResumeRenderer.Render(content);
        act.Should().NotThrow();
    }
}
