// MatchListServiceTests — tenant-scoped read model: ordering, minScore filter,
// reasoning deserialization, and cross-tenant isolation.
//
// Refs: AIRMVP1-206

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Matches;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Matches;

public sealed class MatchListServiceTests
{
    private static AireqDbContext NewDb(ITenantContext tenant, string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    private static Job NewJob(string ext, string title) => new()
    {
        Source = "greenhouse", SourceExternalId = ext, Title = title,
        Company = "Acme", Location = "Remote", IsActive = true,
        PostedAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Lists_tenant_matches_ordered_by_score_with_reasoning()
    {
        var dbName = $"matchlist-{Guid.NewGuid()}";
        Guid tenantId;

        // Seed (admin/null tenant) two scored matches + one reasoned.
        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var tenant = new Tenant { Name = "A" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
            var job1 = NewJob("J1", "Senior Engineer");
            var job2 = NewJob("J2", "Junior Engineer");
            seed.AddRange(tenant, consultant, job1, job2);
            seed.Matches.Add(new Match
            {
                TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job1.Id,
                Score = 90, Status = MatchStatus.New,
                ReasoningJson = "{\"score\":90,\"summary\":\"Great fit\",\"rationale\":[\"a\",\"b\"],\"missingKeywords\":[\"k8s\"]}",
            });
            seed.Matches.Add(new Match
            {
                TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job2.Id,
                Score = 55, Status = MatchStatus.New, // not yet reasoned
            });
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() }, dbName);
        var list = await new MatchListService(db).ListAsync(minScore: null, status: null, CancellationToken.None);

        list.Should().HaveCount(2);
        list[0].Score.Should().Be(90, "ordered best-score-first");
        list[0].Reasoned.Should().BeTrue();
        list[0].Summary.Should().Be("Great fit");
        list[0].MissingKeywords.Should().Contain("k8s");
        list[1].Score.Should().Be(55);
        list[1].Reasoned.Should().BeFalse("no reasoning json yet");
        list[1].Rationale.Should().BeEmpty();
    }

    [Fact]
    public async Task MinScore_filter_excludes_low_matches()
    {
        var dbName = $"matchlist-{Guid.NewGuid()}";
        Guid tenantId;
        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var tenant = new Tenant { Name = "A" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
            var job1 = NewJob("J1", "Hi"); var job2 = NewJob("J2", "Lo");
            seed.AddRange(tenant, consultant, job1, job2);
            seed.Matches.Add(new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job1.Id, Score = 85 });
            seed.Matches.Add(new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job2.Id, Score = 40 });
            await seed.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() }, dbName);
        var list = await new MatchListService(db).ListAsync(minScore: 70, status: null, CancellationToken.None);

        list.Should().ContainSingle();
        list[0].Score.Should().Be(85);
    }

    [Fact]
    public async Task Does_not_leak_other_tenants_matches()
    {
        var dbName = $"matchlist-{Guid.NewGuid()}";
        Guid tenantAId;
        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var a = new Tenant { Name = "A" }; var b = new Tenant { Name = "B" };
            var ca = new Consultant { TenantId = a.Id, FullName = "Alice" };
            var cb = new Consultant { TenantId = b.Id, FullName = "Bob" };
            var ja = NewJob("JA", "A job"); var jb = NewJob("JB", "B job");
            seed.AddRange(a, b, ca, cb, ja, jb);
            seed.Matches.Add(new Match { TenantId = a.Id, ConsultantId = ca.Id, JobId = ja.Id, Score = 80 });
            seed.Matches.Add(new Match { TenantId = b.Id, ConsultantId = cb.Id, JobId = jb.Id, Score = 95 });
            await seed.SaveChangesAsync();
            tenantAId = a.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantAId, UserId = Guid.NewGuid() }, dbName);
        var list = await new MatchListService(db).ListAsync(minScore: null, status: null, CancellationToken.None);

        list.Should().ContainSingle("only tenant A's match is visible");
        list[0].JobTitle.Should().Be("A job");
    }
}
