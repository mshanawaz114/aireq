// Cross-tenant isolation — the contract AIRMVP1-103 establishes. THIS TEST
// MUST STAY GREEN FOR THE LIFE OF THE PROJECT. If a future refactor breaks
// the global query filter, this test fails BEFORE the regression escapes to
// production.
//
// Strategy:
//   - Boot AireqDbContext against EF InMemory.
//   - Seed Tenant A + Tenant B, each with one Consultant and one Match.
//   - Pin StubTenantContext to Tenant A → verify only A's rows are visible.
//   - Pin to Tenant B → verify only B's rows are visible.
//   - Pin to null (admin/design-time) → verify both are visible.
//   - Verify IgnoreQueryFilters() still works as the explicit escape hatch.
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
    private static AireqDbContext NewDb(StubTenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase($"aireq-test-{Guid.NewGuid()}")
            // InMemory provider doesn't support transactions; warnings are fine.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AireqDbContext(options, tenant);
    }

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
        var tenant = new StubTenantContext();

        // Seed with no tenant context (admin-equivalent).
        await using var seedDb = NewDb(tenant);
        var seeded = await SeedAsync(seedDb);

        // Switch to Tenant A — should ONLY see Alice.
        tenant.TenantId = seeded.a.Id;
        tenant.UserId = Guid.NewGuid();

        var asA = await seedDb.Consultants.ToListAsync();
        asA.Should().HaveCount(1, "Tenant A must only see its own consultants");
        asA[0].FullName.Should().Be("Alice from A");

        // Try to fetch Bob (Tenant B's) by id — must come back null because
        // the global filter excludes it entirely (looks like it doesn't exist).
        var leakedBob = await seedDb.Consultants.FindAsync(seeded.cb.Id);
        leakedBob.Should().BeNull("looking up another tenant's row by id must not leak it");

        // Switch to Tenant B — should ONLY see Bob.
        tenant.TenantId = seeded.b.Id;
        var asB = await seedDb.Consultants.ToListAsync();
        asB.Should().HaveCount(1);
        asB[0].FullName.Should().Be("Bob from B");
    }

    [Fact]
    public async Task Matches_are_scoped_to_the_current_tenant()
    {
        var tenant = new StubTenantContext();

        await using var db = NewDb(tenant);
        var seeded = await SeedAsync(db);

        tenant.TenantId = seeded.a.Id;
        tenant.UserId = Guid.NewGuid();

        var matches = await db.Matches.ToListAsync();
        matches.Should().HaveCount(1);
        matches[0].TenantId.Should().Be(seeded.a.Id);
    }

    [Fact]
    public async Task Users_are_scoped_to_the_current_tenant()
    {
        var tenant = new StubTenantContext();

        await using var db = NewDb(tenant);
        var (a, b, _, _, _, _) = await SeedAsync(db);

        var ua = new User { TenantId = a.Id, Email = "alice@a.test", PasswordHash = "x" };
        var ub = new User { TenantId = b.Id, Email = "bob@b.test",   PasswordHash = "y" };
        db.Users.AddRange(ua, ub);
        await db.SaveChangesAsync();

        tenant.TenantId = a.Id;
        tenant.UserId = ua.Id;

        var users = await db.Users.ToListAsync();
        users.Should().ContainSingle().Which.Email.Should().Be("alice@a.test");
    }

    [Fact]
    public async Task Admin_path_sees_all_rows_via_IgnoreQueryFilters()
    {
        var tenant = new StubTenantContext();

        await using var db = NewDb(tenant);
        await SeedAsync(db);

        // Pin to Tenant A — the normal filter is active.
        tenant.TenantId = (await db.Tenants.FirstAsync(t => t.Name == "Tenant A")).Id;
        tenant.UserId = Guid.NewGuid();

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
        var tenant = new StubTenantContext(); // all properties null

        await using var db = NewDb(tenant);
        await SeedAsync(db);

        var all = await db.Consultants.CountAsync();
        all.Should().Be(2, "with a null tenant context the filter is pass-through");
    }
}
