// ConsultantServiceTests — tenant-scoped CRUD behaviour at the service layer.
//
// Coverage:
//   - Create stamps the current tenant + returns the projection.
//   - Create without a tenant context is Unauthenticated.
//   - Create with blank name is Invalid.
//   - List only returns the current tenant's consultants.
//   - Get / Update of another tenant's consultant returns NotFound (no leak).
//   - Update mutates fields + returns the new projection.
//
// Refs: AIRMVP1-106

using Aireq.Api.Auth;
using Aireq.Api.Consultants;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Shared.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Consultants;

public sealed class ConsultantServiceTests
{
    private static AireqDbContext NewDb(ITenantContext tenant, string dbName)
    {
        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AireqDbContext(options, tenant);
    }

    private static UpsertConsultantRequest Sample(string name = "Alice Architect") =>
        new(name, "Sr. Salesforce Architect · 20 yrs", "Austin, TX", "US Citizen", 145m);

    [Fact]
    public async Task Create_stamps_tenant_and_returns_projection()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() };
        var dbName = $"consultant-{Guid.NewGuid()}";
        await using var db = NewDb(tenant, dbName);
        var svc = new ConsultantService(db, tenant);

        var result = await svc.CreateAsync(Sample(), CancellationToken.None);

        result.Kind.Should().Be(CreateConsultantKind.Ok);
        result.Consultant!.FullName.Should().Be("Alice Architect");
        result.Consultant.WorkAuth.Should().Be("US Citizen");
        result.Consultant.ResumeCount.Should().Be(0);

        var saved = await db.Consultants.IgnoreQueryFilters().SingleAsync();
        saved.TenantId.Should().Be(tenantId, "create must stamp the current tenant");
    }

    [Fact]
    public async Task Create_without_tenant_is_unauthenticated()
    {
        var tenant = new StubTenantContext(); // null tenant
        await using var db = NewDb(tenant, $"consultant-{Guid.NewGuid()}");
        var svc = new ConsultantService(db, tenant);

        var result = await svc.CreateAsync(Sample(), CancellationToken.None);

        result.Kind.Should().Be(CreateConsultantKind.Unauthenticated);
    }

    [Fact]
    public async Task Create_with_blank_name_is_invalid()
    {
        var tenant = new StubTenantContext { TenantId = Guid.NewGuid(), UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, $"consultant-{Guid.NewGuid()}");
        var svc = new ConsultantService(db, tenant);

        var result = await svc.CreateAsync(Sample(name: "   "), CancellationToken.None);

        result.Kind.Should().Be(CreateConsultantKind.Invalid);
        result.Error.Should().Contain("required");
    }

    [Fact]
    public async Task List_only_returns_current_tenants_consultants()
    {
        var dbName = $"consultant-{Guid.NewGuid()}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // Seed one consultant per tenant via separate contexts (per-tenant pattern).
        var ctxA = new StubTenantContext { TenantId = tenantA, UserId = Guid.NewGuid() };
        await using (var dbA = NewDb(ctxA, dbName))
        {
            await new ConsultantService(dbA, ctxA)
                .CreateAsync(Sample("Alice from A"), CancellationToken.None);
        }
        var ctxB = new StubTenantContext { TenantId = tenantB, UserId = Guid.NewGuid() };
        await using (var dbB = NewDb(ctxB, dbName))
        {
            await new ConsultantService(dbB, ctxB)
                .CreateAsync(Sample("Bob from B"), CancellationToken.None);
        }

        // Read as tenant A.
        var tenant = new StubTenantContext { TenantId = tenantA, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var list = await new ConsultantService(db, tenant).ListAsync(CancellationToken.None);

        list.Should().ContainSingle();
        list[0].FullName.Should().Be("Alice from A");
    }

    [Fact]
    public async Task Update_of_another_tenants_consultant_returns_NotFound()
    {
        var dbName = $"consultant-{Guid.NewGuid()}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Guid bobId;
        var ctxB = new StubTenantContext { TenantId = tenantB, UserId = Guid.NewGuid() };
        await using (var dbB = NewDb(ctxB, dbName))
        {
            var created = await new ConsultantService(dbB, ctxB)
                .CreateAsync(Sample("Bob from B"), CancellationToken.None);
            bobId = created.Consultant!.Id;
        }

        // Tenant A tries to update Bob.
        var tenant = new StubTenantContext { TenantId = tenantA, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var result = await new ConsultantService(db, tenant)
            .UpdateAsync(bobId, Sample("Hijacked"), CancellationToken.None);

        result.Kind.Should().Be(UpdateConsultantKind.NotFound);
    }

    [Fact]
    public async Task Update_mutates_fields()
    {
        var tenantId = Guid.NewGuid();
        var dbName = $"consultant-{Guid.NewGuid()}";
        var tenant = new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var svc = new ConsultantService(db, tenant);

        var created = await svc.CreateAsync(Sample(), CancellationToken.None);
        var updated = await svc.UpdateAsync(
            created.Consultant!.Id,
            new UpsertConsultantRequest("Alice Renamed", "New headline", "Remote", "H1B", 160m),
            CancellationToken.None);

        updated.Kind.Should().Be(UpdateConsultantKind.Ok);
        updated.Consultant!.FullName.Should().Be("Alice Renamed");
        updated.Consultant.WorkAuth.Should().Be("H1B");
        updated.Consultant.RateTargetUsdHourly.Should().Be(160m);
    }
}
