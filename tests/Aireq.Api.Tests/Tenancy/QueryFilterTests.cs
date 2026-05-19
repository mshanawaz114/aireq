// Cross-tenant isolation — the contract AIRMVP1-103 establishes. THIS TEST
// SUITE MUST STAY GREEN FOR THE LIFE OF THE PROJECT. If a future refactor
// breaks the global query filter, these tests fail BEFORE the regression
// escapes to production.
//
// Strategy:
//   - Boot AireqDbContext against EF InMemory using a stable database name
//     shared by multiple DbContexts inside one test.
//   - Seed Tenant A + Tenant B + their consultants/matches in a seed context
//     (null tenant = admin pass-through), then dispose.
//   - For each tenant view, open a FRESH DbContext with the tenant pinned at
//     construction — mirroring the per-request-scoped lifetime in production
//     (HttpTenantContext + AireqDbContext are both request-scoped, so the
//     tenant id is fixed for the lifetime of any single DbContext).
//   - Assert each tenant only sees its own rows; find-by-id across tenants
//     returns null; IgnoreQueryFilters() remains the escape hatch.
//
// Why per-tenant DbContexts instead of mutating ITenantContext mid-context:
//   EF Core's InMemory provider doesn't reliably re-bind query-filter params
//   after the first execution captures them. Real Postgres re-parameterises
//   every execution, so this is purely an InMemory quirk — but production
//   never mutates tenant inside a single DbContext anyway (request-scoped),
//   so the per-DbContext pattern is the supported contract.
//
// Refs: AIRMVP1-103

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Tenancy;

public sealed class QueryFilterTests
{
    // EF Core's InMemory provider doesn't reliably re-bind query-filter
    // parameters mid-DbContext-lifetime — once a tenant id is captured at the
    // first execution it tends to stick. Real Postgres re-parameterises every
    // execution, so this is purely an InMemory quirk.
    //
    // In production, ITenantContext is request-scoped and AireqDbContext is
    // request-scoped, so the tenant id never changes inside one DbContext.
    // These tests mirror that contract by creating a fresh DbContext per
    // tenant-view, all pointed at the same in-memory database name so the
    // seeded rows are visible across contexts.
    private static AireqDbContext NewDb(StubTenantContext tenant, string dbName)
    {
        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            // InMemory provider doesn't support transactions; warnings are fine.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AireqDbContext(options, tenant);
    }

    private static string NewDbName() => $"aireq-test-{Guid.NewGuid()}";

    private static async Task<(Tenant a, Tenant b, Consultant ca, Consultant cb, Match ma, Match mb)>
        SeedAsync(AireqDbContext db)
    {
        var a = new Tenant { Name = "Tenant A" };
        var b = new Tenant { Name = "Tenant B" };

        var ca = new Consultant { TenantId = a.Id, FullName = "Alice from A" };
        var cb = new Consultant { TenantId = b.Id, FullName = "Bob from B" };

        var jobA = new Job
        {
            Source = "test",
            SourceExternalId = "A1",
            Title = "Job A",
            Company = "Co A",
            PostedAt = DateTimeOffset.UtcNow,
        };
        var jobB = new Job
        {
            Source = "test",
            SourceExternalId = "B1",
            Title = "Job B",
            Company = "Co B",
            PostedAt = DateTimeOffset.UtcNow,
        };

        var ma = new Match { TenantId = a.Id, ConsultantId = ca.Id, JobId = jobA.Id, Score = 90 };
        var mb = new Match { TenantId = b.Id, ConsultantId = cb.Id, JobId = jobB.Id, Score = 80 };

        db.Tenants.AddRange(a, b);
        db.Consultants.AddRange(ca, cb);
        db.Jobs.AddRange(jobA, jobB);
        db.Matches.AddRange(ma, mb);
        await db.SaveChangesAsync();

        return (a, b, ca, cb, ma, mb);
    }

    [Fact]
    public async Task Consultants_are_scoped_to_the_current_tenant()
    {
        var dbName = NewDbName();

        // Seed with admin-equivalent context (null tenant).
        Guid aliceTenantId, bobTenantId, bobId;
        await using (var seedDb = NewDb(new StubTenantContext(), dbName))
        {
            var seeded = await SeedAsync(seedDb);
            aliceTenantId = seeded.a.Id;
            bobTenantId = seeded.b.Id;
            bobId = seeded.cb.Id;
        }

        // Tenant A view — fresh DbContext, fresh request scope.
        await using (var dbA = NewDb(
            new StubTenantContext { TenantId = aliceTenantId, UserId = Guid.NewGuid() },
            dbName))
        {
            var asA = await dbA.Consultants.ToListAsync();
            asA.Should().HaveCount(1, "Tenant A must only see its own consultants");
            asA[0].FullName.Should().Be("Alice from A");

            // Find-by-id of another tenant's row must look like it doesn't exist.
            var leakedBob = await dbA.Consultants.FindAsync(bobId);
            leakedBob.Should().BeNull(
                "looking up another tenant's row by id must not leak it");
        }

        // Tenant B view — fresh DbContext, fresh request scope.
        await using (var dbB = NewDb(
            new StubTenantContext { TenantId = bobTenantId, UserId = Guid.NewGuid() },
            dbName))
        {
            var asB = await dbB.Consultants.ToListAsync();
            asB.Should().HaveCount(1);
            asB[0].FullName.Should().Be("Bob from B");
        }
    }

    [Fact]
    public async Task Matches_are_scoped_to_the_current_tenant()
    {
        var dbName = NewDbName();

        Guid tenantAId;
        await using (var seedDb = NewDb(new StubTenantContext(), dbName))
        {
            var seeded = await SeedAsync(seedDb);
            tenantAId = seeded.a.Id;
        }

        await using var db = NewDb(
            new StubTenantContext { TenantId = tenantAId, UserId = Guid.NewGuid() },
            dbName);

        var matches = await db.Matches.ToListAsync();
        matches.Should().HaveCount(1);
        matches[0].TenantId.Should().Be(tenantAId);
    }

    [Fact]
    public async Task Users_are_scoped_to_the_current_tenant()
    {
        var dbName = NewDbName();

        Guid tenantAId, aliceUserId;
        await using (var seedDb = NewDb(new StubTenantContext(), dbName))
        {
            var (a, b, _, _, _, _) = await SeedAsync(seedDb);

            var ua = new User { TenantId = a.Id, Email = "alice@a.test", PasswordHash = "x" };
            var ub = new User { TenantId = b.Id, Email = "bob@b.test",   PasswordHash = "y" };
            seedDb.Users.AddRange(ua, ub);
            await seedDb.SaveChangesAsync();

            tenantAId = a.Id;
            aliceUserId = ua.Id;
        }

        await using var db = NewDb(
            new StubTenantContext { TenantId = tenantAId, UserId = aliceUserId },
            dbName);

        var users = await db.Users.ToListAsync();
        users.Should().ContainSingle().Which.Email.Should().Be("alice@a.test");
    }

    [Fact]
    public async Task Admin_path_sees_all_rows_via_IgnoreQueryFilters()
    {
        var dbName = NewDbName();

        Guid tenantAId;
        await using (var seedDb = NewDb(new StubTenantContext(), dbName))
        {
            var seeded = await SeedAsync(seedDb);
            tenantAId = seeded.a.Id;
        }

        await using var db = NewDb(
            new StubTenantContext { TenantId = tenantAId, UserId = Guid.NewGuid() },
            dbName);

        var scoped = await db.Consultants.CountAsync();
        scoped.Should().Be(1);

        // Admin-equivalent read.
        var all = await db.Consultants.IgnoreQueryFilters().CountAsync();
        all.Should().Be(2, "IgnoreQueryFilters() must remain the explicit escape hatch");
    }

    [Fact]
    public async Task Null_tenant_context_acts_as_admin_for_design_time()
    {
        // ITenantContext null path covers `dotnet ef` design-time scenarios
        // where no request scope exists.
        await using var db = NewDb(new StubTenantContext(), NewDbName()); // all properties null
        await SeedAsync(db);

        var all = await db.Consultants.CountAsync();
        all.Should().Be(2, "with a null tenant context the filter is pass-through");
    }
}
