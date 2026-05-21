// WaitlistServiceTests — first join inserts; a repeat is idempotent.
// Refs: AIRMVP1-405

using Aireq.Api.Data;
using Aireq.Api.Marketing;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Marketing;

public sealed class WaitlistServiceTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    [Fact]
    public async Task First_join_persists_the_entry()
    {
        var dbName = $"wl-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);

        var res = await new WaitlistService(db).JoinAsync("founder@agency.test", "agency", "launch", CancellationToken.None);

        res.Joined.Should().BeTrue();
        res.AlreadyJoined.Should().BeFalse();
        var row = await db.WaitlistEntries.SingleAsync();
        row.Email.Should().Be("founder@agency.test");
        row.Persona.Should().Be("agency");
        row.Source.Should().Be("launch");
    }

    [Fact]
    public async Task Repeat_join_is_idempotent()
    {
        var dbName = $"wl-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var svc = new WaitlistService(db);

        await svc.JoinAsync("dup@x.test", null, null, CancellationToken.None);
        var second = await svc.JoinAsync("dup@x.test", null, null, CancellationToken.None);

        second.AlreadyJoined.Should().BeTrue();
        (await db.WaitlistEntries.CountAsync()).Should().Be(1);
    }
}
