// AtsAnalysisServiceTests — loads the right job + resume, tenant-scoped.
//
// Refs: AIRMVP1-301

using Aireq.Api.Auth;
using Aireq.Api.Ats;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Ats;

public sealed class AtsAnalysisServiceTests
{
    private static AireqDbContext NewDb(ITenantContext tenant, string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    [Fact]
    public async Task Analyzes_match_with_consultant_resume()
    {
        var dbName = $"ats-{Guid.NewGuid()}";
        Guid tenantId, matchId;

        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var tenant = new Tenant { Name = "A" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
            var resume = new Resume { ConsultantId = consultant.Id, Version = 1, ParsedJson = "skills: c#, postgresql" };
            var job = new Job
            {
                Source = "greenhouse", SourceExternalId = "G1", Title = "C# Engineer",
                Company = "Acme", Description = "Need C#, Azure, Kubernetes.",
                IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
            };
            var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Score = 80 };
            seed.AddRange(tenant, consultant, resume, job, match);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id; matchId = match.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() }, dbName);
        var a = await new AtsAnalysisService(db).AnalyzeAsync(matchId, CancellationToken.None);

        a.Should().NotBeNull();
        a!.MatchId.Should().Be(matchId);
        a.PresentKeywords.Should().Contain("c#");
        a.MissingKeywords.Should().Contain("azure").And.Contain("kubernetes");
    }

    [Fact]
    public async Task Returns_null_for_other_tenants_match()
    {
        var dbName = $"ats-{Guid.NewGuid()}";
        Guid tenantAId, otherMatchId;

        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var a = new Tenant { Name = "A" };
            var b = new Tenant { Name = "B" };
            var cb = new Consultant { TenantId = b.Id, FullName = "Bob" };
            var job = new Job { Source = "x", SourceExternalId = "1", Title = "T", Company = "C", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
            var match = new Match { TenantId = b.Id, ConsultantId = cb.Id, JobId = job.Id, Score = 90 };
            seed.AddRange(a, b, cb, job, match);
            await seed.SaveChangesAsync();
            tenantAId = a.Id; otherMatchId = match.Id;
        }

        // Acting as tenant A, request tenant B's match.
        await using var db = NewDb(new StubTenantContext { TenantId = tenantAId, UserId = Guid.NewGuid() }, dbName);
        var result = await new AtsAnalysisService(db).AnalyzeAsync(otherMatchId, CancellationToken.None);

        result.Should().BeNull("cross-tenant match must be invisible");
    }
}
