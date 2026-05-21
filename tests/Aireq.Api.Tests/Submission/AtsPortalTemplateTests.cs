// AtsPortalTemplateTests — the pure parts of the Playwright templates (source
// matching + apply-URL construction) + the channel's CanHandle/Tier. The actual
// browser interaction needs `playwright install` browser binaries and is
// exercised by the AIRMVP1-307 chaos test, not here.
//
// Refs: AIRMVP1-304

using Aireq.Api.Tests.Resumes; // FakeBlobStorage
using Aireq.Worker.Submission;
using Aireq.Worker.Submission.Playwright;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Submission;

public sealed class AtsPortalTemplateTests
{
    private static SubmissionRequest Req(string source, string board, string ext) =>
        new(Guid.NewGuid(), Guid.NewGuid(), source, ext, board, "Alice", "Architect", "a@x.com", null,
            new byte[] { 1, 2, 3 }, "resume.pdf");

    [Fact]
    public void Greenhouse_template_matches_and_builds_url()
    {
        var t = new GreenhouseHostedTemplate();
        t.Matches("greenhouse").Should().BeTrue();
        t.Matches("lever").Should().BeFalse();
        t.BuildApplyUrl(Req("greenhouse", "stripe", "12345"))
            .Should().Be("https://boards.greenhouse.io/stripe/jobs/12345");
    }

    [Fact]
    public void Lever_template_matches_and_builds_url()
    {
        var t = new LeverHostedTemplate();
        t.Matches("lever").Should().BeTrue();
        t.Matches("greenhouse").Should().BeFalse();
        t.BuildApplyUrl(Req("lever", "netflix", "abc-uuid"))
            .Should().Be("https://jobs.lever.co/netflix/abc-uuid/apply");
    }

    [Fact]
    public void Channel_handles_known_sources_and_is_tier_1()
    {
        var channel = new PlaywrightSubmissionChannel(
            new IAtsPortalTemplate[] { new GreenhouseHostedTemplate(), new LeverHostedTemplate() },
            new FakeBlobStorage(),
            NullLogger<PlaywrightSubmissionChannel>.Instance);

        channel.Tier.Should().Be(1);
        channel.Kind.Should().Be(Aireq.Api.Data.Entities.SubmissionChannel.Portal);
        channel.CanHandle("greenhouse").Should().BeTrue();
        channel.CanHandle("lever").Should().BeTrue();
        channel.CanHandle("workday").Should().BeFalse("no Workday template yet");
    }
}
