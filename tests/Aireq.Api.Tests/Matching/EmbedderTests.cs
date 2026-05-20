// EmbedderTests — JobEmbedder + ResumeEmbedder behaviour.
//
// Coverage:
//   - Skip cleanly when the provider isn't configured (no embeds, no throw).
//   - Embed only rows with EmbeddedAt == null; stamp EmbeddedAt after.
//   - Respect the batch size.
//   - ResumeEmbedder only touches parsed resumes (ParsedJson != null).
//
// (The vector column itself is Npgsql-only / Ignored on InMemory, so we assert
// on EmbeddedAt + gateway call records rather than the stored vector.)
//
// Refs: AIRMVP1-204

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Worker.Matching;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Matching;

public sealed class EmbedderTests
{
    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static IOptions<EmbeddingOptions> Opts(int batch = 100) =>
        Options.Create(new EmbeddingOptions { BatchSize = batch });

    private static Job NewJob(string extId) => new()
    {
        Source = "adzuna", SourceExternalId = extId, Title = "Engineer",
        Company = "Acme", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task JobEmbedder_skips_when_not_configured()
    {
        var dbName = $"emb-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        db.Jobs.Add(NewJob("J1"));
        await db.SaveChangesAsync();

        var gw = new FakeEmbeddingGateway(configured: false);
        var done = await new JobEmbedder(db, gw, Opts(), NullLogger<JobEmbedder>.Instance)
            .RunAsync(CancellationToken.None);

        done.Should().Be(0);
        gw.Embedded.Should().BeEmpty();
    }

    [Fact]
    public async Task JobEmbedder_embeds_unembedded_active_jobs_and_stamps_EmbeddedAt()
    {
        var dbName = $"emb-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        db.Jobs.AddRange(NewJob("J1"), NewJob("J2"));
        await db.SaveChangesAsync();

        var gw = new FakeEmbeddingGateway();
        var done = await new JobEmbedder(db, gw, Opts(), NullLogger<JobEmbedder>.Instance)
            .RunAsync(CancellationToken.None);

        done.Should().Be(2);
        gw.Embedded.Should().HaveCount(2);
        (await db.Jobs.CountAsync(j => j.EmbeddedAt != null)).Should().Be(2);

        // Re-running embeds nothing — they're all stamped now.
        var second = await new JobEmbedder(db, new FakeEmbeddingGateway(), Opts(), NullLogger<JobEmbedder>.Instance)
            .RunAsync(CancellationToken.None);
        second.Should().Be(0);
    }

    [Fact]
    public async Task JobEmbedder_respects_batch_size()
    {
        var dbName = $"emb-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        db.Jobs.AddRange(NewJob("J1"), NewJob("J2"), NewJob("J3"));
        await db.SaveChangesAsync();

        var gw = new FakeEmbeddingGateway();
        var done = await new JobEmbedder(db, gw, Opts(batch: 2), NullLogger<JobEmbedder>.Instance)
            .RunAsync(CancellationToken.None);

        done.Should().Be(2, "batch size caps the pass");
    }

    [Fact]
    public async Task ResumeEmbedder_only_embeds_parsed_resumes()
    {
        var dbName = $"emb-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var tenant = new Tenant { Name = "T" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "A" };
        db.AddRange(tenant, consultant);
        // One parsed (embeddable), one not parsed (skipped).
        db.Resumes.Add(new Resume { ConsultantId = consultant.Id, Version = 1, ParsedJson = "{\"skills\":[]}" });
        db.Resumes.Add(new Resume { ConsultantId = consultant.Id, Version = 2, ParsedJson = null });
        await db.SaveChangesAsync();

        var gw = new FakeEmbeddingGateway();
        var done = await new ResumeEmbedder(db, gw, Opts(), NullLogger<ResumeEmbedder>.Instance)
            .RunAsync(CancellationToken.None);

        done.Should().Be(1, "only the parsed resume is embedded");
        gw.Embedded.Should().ContainSingle();
    }
}
