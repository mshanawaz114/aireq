// SubmissionListServiceTests — newest-first ordering + cross-tenant isolation
// (submissions are scoped via the tenant-filtered Matches join).
//
// Refs: AIRMVP1-306

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Submissions;
using Aireq.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aireq.Api.Tests.Submissions;

public sealed class SubmissionListServiceTests
{
    private static AireqDbContext NewDb(ITenantContext tenant, string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, tenant);

    private static Job NewJob(string ext, string title) => new()
    {
        Source = "greenhouse", SourceExternalId = ext, Title = title, Company = "Acme",
        IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Lists_tenant_submissions_newest_first()
    {
        var dbName = $"sublist-{Guid.NewGuid()}";
        Guid tenantId;
        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var t = new Tenant { Name = "A" };
            var c = new Consultant { TenantId = t.Id, FullName = "Alice" };
            var j1 = NewJob("J1", "Older role"); var j2 = NewJob("J2", "Newer role");
            var m1 = new Match { TenantId = t.Id, ConsultantId = c.Id, JobId = j1.Id, Score = 70 };
            var m2 = new Match { TenantId = t.Id, ConsultantId = c.Id, JobId = j2.Id, Score = 80 };
            seed.AddRange(t, c, j1, j2, m1, m2);
            seed.Submissions.Add(new Submission { MatchId = m1.Id, Channel = SubmissionChannel.Api, ResponseStatus = "dry_run", SubmittedAt = DateTimeOffset.UtcNow.AddHours(-2) });
            seed.Submissions.Add(new Submission { MatchId = m2.Id, Channel = SubmissionChannel.Email, ResponseStatus = "received", SubmittedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
            tenantId = t.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantId, UserId = Guid.NewGuid() }, dbName);
        var list = await new SubmissionListService(db).ListAsync(CancellationToken.None);

        list.Should().HaveCount(2);
        list[0].JobTitle.Should().Be("Newer role", "ordered by SubmittedAt desc");
        list[0].Channel.Should().Be("Email");
        list[0].ResponseStatus.Should().Be("received");
        list[1].JobTitle.Should().Be("Older role");
    }

    [Fact]
    public async Task Does_not_leak_other_tenants_submissions()
    {
        var dbName = $"sublist-{Guid.NewGuid()}";
        Guid tenantAId;
        await using (var seed = NewDb(new StubTenantContext(), dbName))
        {
            var a = new Tenant { Name = "A" }; var b = new Tenant { Name = "B" };
            var ca = new Consultant { TenantId = a.Id, FullName = "Alice" };
            var cb = new Consultant { TenantId = b.Id, FullName = "Bob" };
            var ja = NewJob("JA", "A job"); var jb = NewJob("JB", "B job");
            var ma = new Match { TenantId = a.Id, ConsultantId = ca.Id, JobId = ja.Id, Score = 70 };
            var mb = new Match { TenantId = b.Id, ConsultantId = cb.Id, JobId = jb.Id, Score = 90 };
            seed.AddRange(a, b, ca, cb, ja, jb, ma, mb);
            seed.Submissions.Add(new Submission { MatchId = ma.Id, Channel = SubmissionChannel.Api, ResponseStatus = "dry_run", SubmittedAt = DateTimeOffset.UtcNow });
            seed.Submissions.Add(new Submission { MatchId = mb.Id, Channel = SubmissionChannel.Api, ResponseStatus = "received", SubmittedAt = DateTimeOffset.UtcNow });
            await seed.SaveChangesAsync();
            tenantAId = a.Id;
        }

        await using var db = NewDb(new StubTenantContext { TenantId = tenantAId, UserId = Guid.NewGuid() }, dbName);
        var list = await new SubmissionListService(db).ListAsync(CancellationToken.None);

        list.Should().ContainSingle("only tenant A's submission is visible");
        list[0].JobTitle.Should().Be("A job");
    }
}
