// EmailSubmissionChannelTests — Tier C behaviour: recipient discovery from the
// JD, dry-run outcome, and the no-recipient -> failed path (which the service
// turns into Manual).
//
// Refs: AIRMVP1-305

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Matching; // FakeLlmGateway
using Aireq.Shared.Email;
using Aireq.Worker.Submission;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Submission;

public sealed class EmailSubmissionChannelTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    // Records what it was asked to send + returns a configurable status.
    private sealed class FakeEmailSender(string status) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task<EmailResult> SendAsync(EmailMessage m, bool live, CancellationToken ct = default)
        {
            Sent.Add(m);
            return Task.FromResult(new EmailResult(status, status == "sent" ? "id_1" : null));
        }
    }

    private static async Task<Job> SeedJobAsync(AireqDbContext db, string? description)
    {
        var job = new Job
        {
            Source = "workday", SourceExternalId = "W1", Title = "Engineer", Company = "Acme",
            Description = description, IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static SubmissionRequest Req(Job job) =>
        new(Guid.NewGuid(), Guid.NewGuid(), job.Source, job.SourceExternalId, job.Company,
            "Alice", "Architect", "alice@me.test", null, new byte[] { 1, 2, 3 }, "resume.pdf");

    [Fact]
    public async Task Sends_to_recipient_extracted_from_jd_dry_run()
    {
        var dbName = $"emailch-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var job = await SeedJobAsync(db, "Apply by emailing careers@acme.example with your resume.");
        var sender = new FakeEmailSender("dry_run");

        var channel = new EmailSubmissionChannel(db, sender, new FakeLlmGateway("Short cover note."),
            NullLogger<EmailSubmissionChannel>.Instance);
        var outcome = await channel.SubmitAsync(Req(job), live: false, CancellationToken.None);

        outcome.Status.Should().Be("dry_run");
        sender.Sent.Should().ContainSingle();
        sender.Sent[0].To.Should().Be("careers@acme.example");
        sender.Sent[0].Attachment.Should().NotBeNull("the tailored resume is attached");
        sender.Sent[0].Purpose.Should().Be("apply");
    }

    [Fact]
    public async Task No_recipient_in_jd_is_failed()
    {
        var dbName = $"emailch-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var job = await SeedJobAsync(db, "Great role, no contact email here.");
        var sender = new FakeEmailSender("sent");

        var channel = new EmailSubmissionChannel(db, sender, new FakeLlmGateway("note"),
            NullLogger<EmailSubmissionChannel>.Instance);
        var outcome = await channel.SubmitAsync(Req(job), live: true, CancellationToken.None);

        outcome.Status.Should().Be("failed", "no recipient -> orchestrator records Manual");
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Channel_is_tier_2_email_and_handles_anything()
    {
        var channel = new EmailSubmissionChannel(null!, null!, null!, NullLogger<EmailSubmissionChannel>.Instance);
        channel.Tier.Should().Be(2);
        channel.Kind.Should().Be(SubmissionChannel.Email);
        channel.CanHandle("anything").Should().BeTrue();
    }
}
