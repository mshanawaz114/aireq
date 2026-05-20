// MetricsServiceTests — aggregation correctness + tenant scoping.
//
// Coverage:
//   - Job metrics count the global pool (total/active/embedded/by-source).
//   - Match metrics + avg score are tenant-scoped.
//   - Resume metrics scope via the tenant's consultants.
//   - LLM metrics filter to the current tenant only.
//
// Refs: AIRMVP1-207

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Metrics;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Metrics;

public sealed class MetricsServiceTests
{
    private static AireqDbContext NewDb(ITenantContext tenant, string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    private static Job NewJob(string source, string ext, bool active = true, bool embedded = false) => new()
    {
        Source = source, SourceExternalId = ext, Title = "Eng", Company = "Acme",
        IsActive = active, LastSeenAt = DateTimeOffset.UtcNow,
        EmbeddedAt = embedded ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public async Task Aggregates_pipeline_and_tenant_metrics()
    {
        var dbName = $"metrics-{Guid.NewGuid()}";
        Guid tenantId, otherTenantId;

        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var a = new Tenant { Name = "A" };
            var b = new Tenant { Name = "B" };
            var ca = new Consultant { TenantId = a.Id, FullName = "Alice" };
            var cb = new Consultant { TenantId = b.Id, FullName = "Bob" };

            // Global job pool: 3 jobs (2 active, 1 embedded), two sources.
            var j1 = NewJob("adzuna", "1", active: true, embedded: true);
            var j2 = NewJob("greenhouse", "2", active: true);
            var j3 = NewJob("adzuna", "3", active: false);

            seed.AddRange(a, b, ca, cb, j1, j2, j3);

            // Tenant A: 2 matches (one reasoned), avg score (80+60)/2 = 70.
            seed.Matches.Add(new Match { TenantId = a.Id, ConsultantId = ca.Id, JobId = j1.Id, Score = 80, Status = MatchStatus.New, ReasoningJson = "{}" });
            seed.Matches.Add(new Match { TenantId = a.Id, ConsultantId = ca.Id, JobId = j2.Id, Score = 60, Status = MatchStatus.New });
            // Tenant B: 1 match (must NOT leak into A's metrics).
            seed.Matches.Add(new Match { TenantId = b.Id, ConsultantId = cb.Id, JobId = j3.Id, Score = 99, Status = MatchStatus.New });

            // Tenant A resumes: 2, one parsed.
            seed.Resumes.Add(new Resume { ConsultantId = ca.Id, Version = 1, ParsedJson = "{}" });
            seed.Resumes.Add(new Resume { ConsultantId = ca.Id, Version = 2 });
            // Tenant B resume (must not count for A).
            seed.Resumes.Add(new Resume { ConsultantId = cb.Id, Version = 1, ParsedJson = "{}" });

            // LLM calls: 2 for A, 1 for B.
            seed.LlmCalls.Add(new LlmCall { TenantId = a.Id, Model = "m", Purpose = "resume.parse", InputTokens = 10, OutputTokens = 5, CostUsdEstimate = 0.01m, PromptText = "p", ResponseText = "r", CreatedAt = DateTimeOffset.UtcNow });
            seed.LlmCalls.Add(new LlmCall { TenantId = a.Id, Model = "m", Purpose = "match.score", InputTokens = 20, OutputTokens = 8, CostUsdEstimate = 0.02m, PromptText = "p", ResponseText = "r", CreatedAt = DateTimeOffset.UtcNow });
            seed.LlmCalls.Add(new LlmCall { TenantId = b.Id, Model = "m", Purpose = "match.score", InputTokens = 99, OutputTokens = 99, CostUsdEstimate = 9.99m, PromptText = "p", ResponseText = "r", CreatedAt = DateTimeOffset.UtcNow });

            await seed.SaveChangesAsync();
            tenantId = a.Id; otherTenantId = b.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() }, dbName);
        var m = await new MetricsService(db, new StubTenantContext { TenantId = tenantId }).GetAsync(CancellationToken.None);

        // Jobs (global)
        m.Jobs.Total.Should().Be(3);
        m.Jobs.Active.Should().Be(2);
        m.Jobs.Embedded.Should().Be(1);
        m.Jobs.BySource["adzuna"].Should().Be(2);
        m.Jobs.BySource["greenhouse"].Should().Be(1);

        // Matches (tenant A only)
        m.Matches.Total.Should().Be(2, "tenant B's match must not leak");
        m.Matches.Reasoned.Should().Be(1);
        m.Matches.AvgScore.Should().Be(70.0);

        // Resumes (tenant A only)
        m.Resumes.Total.Should().Be(2);
        m.Resumes.Parsed.Should().Be(1);

        // LLM (tenant A only)
        m.Llm.Calls.Should().Be(2);
        m.Llm.CostUsd.Should().Be(0.03m, "9.99 from tenant B excluded");
        m.Llm.ByPurpose.Should().ContainKey("resume.parse").And.ContainKey("match.score");
    }

    [Fact]
    public async Task Empty_workspace_returns_zeros_without_throwing()
    {
        var dbName = $"metrics-{Guid.NewGuid()}";
        var tenantId = Guid.NewGuid();
        await using var db = NewDb(new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() }, dbName);

        var m = await new MetricsService(db, new StubTenantContext { TenantId = tenantId }).GetAsync(CancellationToken.None);

        m.Jobs.Total.Should().Be(0);
        m.Matches.AvgScore.Should().Be(0.0, "no divide-by-zero on empty matches");
        m.Llm.CostUsd.Should().Be(0m);
    }
}
