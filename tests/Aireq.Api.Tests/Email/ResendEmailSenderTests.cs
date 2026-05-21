// ResendEmailSenderTests — dry-run gating, warmup throttle, real send via a
// fake HTTP handler, and EmailLog audit rows.
//
// Refs: AIRMVP1-305

using System.Net;
using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Llm; // FakeHttpMessageHandler
using Aireq.Shared.Email;
using Aireq.Worker.Email;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Email;

public sealed class ResendEmailSenderTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static ResendEmailSender Build(
        AireqDbContext db, FakeHttpMessageHandler handler, string? apiKey = "re_test", int dailyCap = 50)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RESEND_API_KEY"] = apiKey,
            ["RESEND_FROM"] = "Aireq <noreply@aireq.test>",
        }).Build();
        return new ResendEmailSender(new HttpClient(handler),
            db, Options.Create(new EmailOptions { DailyCap = dailyCap }), config,
            NullLogger<ResendEmailSender>.Instance);
    }

    private static EmailMessage Msg(Guid tenantId) =>
        new(tenantId, "recruiter@co.test", "Application", "<p>hi</p>", "apply");

    [Fact]
    public async Task Not_live_is_dry_run_and_sends_nothing()
    {
        var dbName = $"email-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var handler = FakeHttpMessageHandler.RespondingWith("{\"id\":\"x\"}");
        var tenantId = Guid.NewGuid();

        var result = await Build(db, handler).SendAsync(Msg(tenantId), live: false, CancellationToken.None);

        result.Status.Should().Be("dry_run");
        handler.Requests.Should().BeEmpty("no HTTP call in dry-run");
        (await db.EmailLogs.SingleAsync()).Status.Should().Be("dry_run");
    }

    [Fact]
    public async Task Missing_api_key_forces_dry_run_even_when_live()
    {
        var dbName = $"email-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var handler = FakeHttpMessageHandler.RespondingWith("{\"id\":\"x\"}");

        var result = await Build(db, handler, apiKey: null).SendAsync(Msg(Guid.NewGuid()), live: true, CancellationToken.None);

        result.Status.Should().Be("dry_run");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Live_send_posts_to_resend_and_logs_sent()
    {
        var dbName = $"email-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var handler = FakeHttpMessageHandler.RespondingWith("{\"id\":\"email_123\"}");
        var tenantId = Guid.NewGuid();

        var result = await Build(db, handler).SendAsync(Msg(tenantId), live: true, CancellationToken.None);

        result.Status.Should().Be("sent");
        result.ProviderMessageId.Should().Be("email_123");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.AbsoluteUri.Should().Be("https://api.resend.com/emails");
        var log = await db.EmailLogs.SingleAsync();
        log.Status.Should().Be("sent");
        log.ProviderMessageId.Should().Be("email_123");
    }

    [Fact]
    public async Task Throttles_when_daily_cap_reached()
    {
        var dbName = $"email-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var tenantId = Guid.NewGuid();

        // Seed today's sends at the cap.
        for (var i = 0; i < 2; i++)
            db.EmailLogs.Add(new EmailLog
            {
                TenantId = tenantId, ToAddress = "x@y.z", Subject = "s", Purpose = "apply",
                Status = "sent", CreatedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();

        var handler = FakeHttpMessageHandler.RespondingWith("{\"id\":\"x\"}");
        var result = await Build(db, handler, dailyCap: 2).SendAsync(Msg(tenantId), live: true, CancellationToken.None);

        result.Status.Should().Be("throttled");
        handler.Requests.Should().BeEmpty("over cap -> no send");
        (await db.EmailLogs.CountAsync(e => e.Status == "throttled")).Should().Be(1);
    }

    [Fact]
    public async Task Resend_failure_logs_failed()
    {
        var dbName = $"email-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var handler = FakeHttpMessageHandler.RespondingWith("nope", HttpStatusCode.UnprocessableEntity);

        var result = await Build(db, handler).SendAsync(Msg(Guid.NewGuid()), live: true, CancellationToken.None);

        result.Status.Should().Be("failed");
        (await db.EmailLogs.SingleAsync()).Status.Should().Be("failed");
    }
}
