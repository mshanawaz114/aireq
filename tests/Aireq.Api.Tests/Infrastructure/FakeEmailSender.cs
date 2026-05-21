// FakeEmailSender — records the EmailMessages it was asked to send and returns
// a configurable result. Used by digest + other email-driven tests.
//
// Refs: AIRMVP1-403

using Aireq.Shared.Email;

namespace Aireq.Api.Tests.Infrastructure;

public sealed class FakeEmailSender(string status = "dry_run") : IEmailSender
{
    public List<(EmailMessage Message, bool Live)> Sent { get; } = new();

    public Task<EmailResult> SendAsync(EmailMessage message, bool live, CancellationToken ct = default)
    {
        Sent.Add((message, live));
        return Task.FromResult(new EmailResult(status, status == "sent" ? "fake-id" : null));
    }
}
