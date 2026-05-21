// NotificationServiceTests — create persists + is tenant-scoped, feed orders
// unread-first with an exact unread count, and mark-read paths work. SignalR
// push is stubbed (NoOpHubContext); persistence is the assertion.
//
// Refs: AIRMVP1-403

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Notifications;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Notifications;

public sealed class NotificationServiceTests
{
    private static AireqDbContext NewDb(string dbName, StubTenantContext tenant) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    private static NotificationService Build(AireqDbContext db, StubTenantContext tenant) =>
        new(db, tenant, new NoOpHubContext<NotificationsHub>());

    [Fact]
    public async Task Create_persists_and_is_returned_unread()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() };
        var dbName = $"notif-{Guid.NewGuid()}";
        await using var db = NewDb(dbName, tenant);

        var dto = await Build(db, tenant).CreateAsync("reply", "New reply (positive)", "body", "/matches/x", Guid.NewGuid());

        dto.Read.Should().BeFalse();
        dto.Type.Should().Be("reply");
        var row = await db.Notifications.IgnoreQueryFilters().SingleAsync();
        row.TenantId.Should().Be(tenantId);
        row.ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task Feed_orders_unread_first_and_counts_unread()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() };
        var dbName = $"notif-{Guid.NewGuid()}";
        await using var db = NewDb(dbName, tenant);
        var svc = Build(db, tenant);

        var first = await svc.CreateAsync("reply", "first");
        await svc.CreateAsync("reply", "second");
        await svc.MarkReadAsync(first.Id, CancellationToken.None); // read the older one

        var feed = await svc.GetFeedAsync(CancellationToken.None);

        feed.UnreadCount.Should().Be(1);
        feed.Items.Should().HaveCount(2);
        feed.Items[0].Title.Should().Be("second", "unread sorts ahead of read");
        feed.Items[0].Read.Should().BeFalse();
    }

    [Fact]
    public async Task Mark_all_read_clears_unread()
    {
        var tenant = new StubTenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var dbName = $"notif-{Guid.NewGuid()}";
        await using var db = NewDb(dbName, tenant);
        var svc = Build(db, tenant);

        await svc.CreateAsync("reply", "a");
        await svc.CreateAsync("escalation", "b");

        (await svc.MarkAllReadAsync(CancellationToken.None)).Should().Be(2);
        (await svc.GetFeedAsync(CancellationToken.None)).UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task Feed_is_tenant_scoped()
    {
        var dbName = $"notif-{Guid.NewGuid()}";

        // Tenant A creates a notification.
        var tenantA = new StubTenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await using (var dbA = NewDb(dbName, tenantA))
            await Build(dbA, tenantA).CreateAsync("reply", "A's note");

        // Tenant B sees an empty feed on the same store.
        var tenantB = new StubTenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await using var dbB = NewDb(dbName, tenantB);
        var feed = await Build(dbB, tenantB).GetFeedAsync(CancellationToken.None);

        feed.Items.Should().BeEmpty();
        feed.UnreadCount.Should().Be(0);
    }
}
