// ThreadServiceTests — Inbox read model: tenant scoping + message ordering.
// Refs: AIRMVP1-401 (read side)

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Threads;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Threads;

public sealed class ThreadServiceTests
{
    private static AireqDbContext NewDb(string dbName, StubTenantContext tenant) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    [Fact]
    public async Task Lists_thread_with_messages_oldest_first_and_is_tenant_scoped()
    {
        var dbName = $"thr-{Guid.NewGuid()}";

        // Tenant A: a thread with two messages. Tenant B: nothing.
        var tenantA = new StubTenantContext();
        Guid threadId;
        await using (var seed = NewDb(dbName, new StubTenantContext()))
        {
            var tenant = new Tenant { Name = "Acme" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
            var job = new Job { Source = "gh", SourceExternalId = "G1", Title = "Eng", Company = "bigco", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
            var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id };
            var thread = new RecruiterThread { MatchId = match.Id, RecruiterEmail = "r@bigco.test", RecruiterName = "Rita", Sentiment = "positive", LastInboundAt = DateTimeOffset.UtcNow };
            var older = new Message { ThreadId = thread.Id, Direction = MessageDirection.Outbound, Body = "Application", SentAt = DateTimeOffset.UtcNow.AddDays(-2) };
            var newer = new Message { ThreadId = thread.Id, Direction = MessageDirection.Inbound, Body = "Let's talk", SentAt = DateTimeOffset.UtcNow.AddDays(-1) };
            seed.AddRange(tenant, consultant, job, match, thread, older, newer);
            await seed.SaveChangesAsync();
            tenantA.TenantId = tenant.Id; tenantA.UserId = Guid.NewGuid();
            threadId = thread.Id;
        }

        await using (var db = NewDb(dbName, tenantA))
        {
            var threads = await new ThreadService(db).ListAsync(CancellationToken.None);
            threads.Should().ContainSingle();
            var th = threads[0];
            th.Id.Should().Be(threadId);
            th.Sentiment.Should().Be("positive");
            th.Messages.Should().HaveCount(2);
            th.Messages[0].Body.Should().Be("Application", "messages are oldest-first");
            th.Messages[1].Direction.Should().Be("Inbound");
        }

        // A different tenant sees nothing on the same store.
        var tenantB = new StubTenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await using (var db = NewDb(dbName, tenantB))
            (await new ThreadService(db).ListAsync(CancellationToken.None)).Should().BeEmpty();
    }
}
