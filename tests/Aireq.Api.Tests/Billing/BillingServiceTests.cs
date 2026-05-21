// BillingServiceTests — trial entitlement derivation + webhook upsert. EF
// InMemory; Stripe HTTP is never hit by these paths (status + webhook).
//
// Refs: AIRMVP1-406

using System.Text.Json;
using Aireq.Api.Auth;
using Aireq.Api.Billing;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Billing;

public sealed class BillingServiceTests
{
    private static AireqDbContext NewDb(string dbName, StubTenantContext tenant) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    private static BillingService Build(AireqDbContext db, StubTenantContext tenant)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var opts = Options.Create(new BillingOptions { TrialDays = 14 });
        var stripe = new StripeClient(new HttpClient(), config, opts, NullLogger<StripeClient>.Instance);
        return new BillingService(db, tenant, stripe, opts, NullLogger<BillingService>.Instance);
    }

    private static async Task<(StubTenantContext, Guid)> SeedTenantAsync(AireqDbContext db, int createdDaysAgo)
    {
        var tenant = new Tenant { Name = "Acme", CreatedAt = DateTimeOffset.UtcNow.AddDays(-createdDaysAgo) };
        db.Add(tenant);
        await db.SaveChangesAsync();
        return (new StubTenantContext { TenantId = tenant.Id, UserId = Guid.NewGuid() }, tenant.Id);
    }

    [Fact]
    public async Task New_tenant_is_trialing_and_entitled()
    {
        var dbName = $"bill-{Guid.NewGuid()}";
        var tenantCtx = new StubTenantContext();
        await using var db = NewDb(dbName, tenantCtx);
        var (ctx, _) = await SeedTenantAsync(db, createdDaysAgo: 2);
        tenantCtx.TenantId = ctx.TenantId;
        tenantCtx.UserId = ctx.UserId;

        var status = await Build(db, tenantCtx).GetStatusAsync(CancellationToken.None);

        status.Status.Should().Be("trialing");
        status.Entitled.Should().BeTrue();
        status.TrialEndsAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Expired_trial_with_no_subscription_is_not_entitled()
    {
        var dbName = $"bill-{Guid.NewGuid()}";
        var tenantCtx = new StubTenantContext();
        await using var db = NewDb(dbName, tenantCtx);
        var (ctx, _) = await SeedTenantAsync(db, createdDaysAgo: 30);
        tenantCtx.TenantId = ctx.TenantId;
        tenantCtx.UserId = ctx.UserId;

        var status = await Build(db, tenantCtx).GetStatusAsync(CancellationToken.None);

        status.Status.Should().Be("trial_expired");
        status.Entitled.Should().BeFalse();
    }

    [Fact]
    public async Task Active_subscription_is_entitled_even_after_trial()
    {
        var dbName = $"bill-{Guid.NewGuid()}";
        var tenantCtx = new StubTenantContext();
        await using var db = NewDb(dbName, tenantCtx);
        var (ctx, tenantId) = await SeedTenantAsync(db, createdDaysAgo: 30);
        tenantCtx.TenantId = ctx.TenantId;
        tenantCtx.UserId = ctx.UserId;
        db.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId, StripeCustomerId = "cus_1", StripeSubscriptionId = "sub_1",
            Status = "active", CurrentPeriodEnd = DateTimeOffset.UtcNow.AddDays(20),
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var status = await Build(db, tenantCtx).GetStatusAsync(CancellationToken.None);

        status.Status.Should().Be("active");
        status.Entitled.Should().BeTrue();
        status.HasStripeCustomer.Should().BeTrue();
    }

    [Fact]
    public async Task Webhook_subscription_updated_upserts_state_by_tenant_metadata()
    {
        var dbName = $"bill-{Guid.NewGuid()}";
        var tenantCtx = new StubTenantContext();
        await using var db = NewDb(dbName, tenantCtx);
        var (ctx, tenantId) = await SeedTenantAsync(db, createdDaysAgo: 1);
        tenantCtx.TenantId = ctx.TenantId;
        tenantCtx.UserId = ctx.UserId;

        var periodEnd = DateTimeOffset.UtcNow.AddDays(25).ToUnixTimeSeconds();
        var evt = JsonDocument.Parse($$"""
            {
              "type": "customer.subscription.updated",
              "data": { "object": {
                "id": "sub_42", "customer": "cus_42", "status": "active",
                "current_period_end": {{periodEnd}},
                "metadata": { "tenant_id": "{{tenantId}}" },
                "items": { "data": [ { "price": { "id": "price_pro" } } ] }
              } }
            }
            """);

        var changed = await Build(db, tenantCtx).ApplyWebhookAsync(evt, CancellationToken.None);

        changed.Should().BeTrue();
        var sub = await db.BillingSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId);
        sub.Status.Should().Be("active");
        sub.StripeSubscriptionId.Should().Be("sub_42");
        sub.StripeCustomerId.Should().Be("cus_42");
        sub.PriceId.Should().Be("price_pro");
        sub.CurrentPeriodEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task Webhook_subscription_deleted_marks_canceled()
    {
        var dbName = $"bill-{Guid.NewGuid()}";
        var tenantCtx = new StubTenantContext();
        await using var db = NewDb(dbName, tenantCtx);
        var (ctx, tenantId) = await SeedTenantAsync(db, createdDaysAgo: 1);
        tenantCtx.TenantId = ctx.TenantId;
        tenantCtx.UserId = ctx.UserId;
        db.BillingSubscriptions.Add(new BillingSubscription
        {
            TenantId = tenantId, StripeCustomerId = "cus_9", StripeSubscriptionId = "sub_9",
            Status = "active", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var evt = JsonDocument.Parse($$"""
            { "type": "customer.subscription.deleted",
              "data": { "object": { "id": "sub_9", "customer": "cus_9", "status": "canceled",
                "metadata": { "tenant_id": "{{tenantId}}" } } } }
            """);

        await Build(db, tenantCtx).ApplyWebhookAsync(evt, CancellationToken.None);

        var sub = await db.BillingSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenantId);
        sub.Status.Should().Be("canceled");
        sub.CanceledAt.Should().NotBeNull();
    }
}
