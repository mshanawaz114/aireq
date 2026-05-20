// ResumeTailorTests — the tailoring pipeline produces a TailoredResume, uploads
// a PDF, recomputes ATS coverage, and flips the match to Tailored. Uses fake
// LLM + fake blob (the FakeLlmGateway / FakeBlobStorage from sibling test files).
//
// Refs: AIRMVP1-302

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Matching; // FakeLlmGateway
using Aireq.Api.Tests.Resumes;  // FakeBlobStorage
using Aireq.Worker.Tailoring;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Tailoring;

public sealed class ResumeTailorTests
{
    static ResumeTailorTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private const string TailoredJson = """
        {
          "fullName": "Alice", "headline": "Salesforce Architect", "location": "Austin, TX",
          "email": "a@x.com", "phone": null, "summary": "Salesforce + Apex + Azure expert.",
          "skills": [{"name":"Salesforce","yearsOfExperience":10},{"name":"Azure","yearsOfExperience":3}],
          "experiences": [{"company":"Acme","title":"Lead","startDate":"2019-01","endDate":null,
            "bullets":["Built Salesforce + Azure integrations."]}],
          "educations": [], "certifications": []
        }
        """;

    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static async Task<Match> SeedAsync(AireqDbContext db)
    {
        var tenant = new Tenant { Name = "T" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var resume = new Resume
        {
            ConsultantId = consultant.Id, Version = 1,
            ParsedJson = "{\"skills\":[\"salesforce\"],\"summary\":\"Salesforce dev.\"}",
        };
        var job = new Job
        {
            Source = "greenhouse", SourceExternalId = "G1", Title = "Salesforce Architect",
            Company = "Acme", Description = "Need Salesforce, Apex, Azure.",
            IsActive = true, LastSeenAt = DateTimeOffset.UtcNow,
        };
        var match = new Match { TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id, Score = 80, Status = MatchStatus.New };
        db.AddRange(tenant, consultant, resume, job, match);
        await db.SaveChangesAsync();
        return match;
    }

    [Fact]
    public async Task Produces_tailored_resume_uploads_pdf_and_marks_tailored()
    {
        var dbName = $"tailor-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var match = await SeedAsync(db);

        var llm = new FakeLlmGateway(TailoredJson);
        var blobs = new FakeBlobStorage();
        await new ResumeTailor(db, llm, blobs, NullLogger<ResumeTailor>.Instance)
            .TailorAsync(match.Id, CancellationToken.None);

        // TailoredResume row created with a blob url + ATS score.
        var tailored = await db.TailoredResumes.SingleAsync();
        tailored.MatchId.Should().Be(match.Id);
        tailored.BlobUrl.Should().StartWith("https://fake-blob.local/");
        tailored.AtsScore.Should().NotBeNull();
        tailored.DiffJson.Should().Contain("atsCoverageBefore").And.Contain("addedKeywords");

        // A PDF was uploaded.
        blobs.Uploaded.Should().ContainSingle();
        blobs.Uploaded.First().ContentType.Should().Be("application/pdf");
        blobs.Uploaded.First().Path.Should().Contain("/tailored/");

        // Match flipped to Tailored, billed as resume.tailor.
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(MatchStatus.Tailored);
        llm.Requests.Should().ContainSingle();
        llm.Requests[0].Purpose.Should().Be("resume.tailor");
    }

    [Fact]
    public async Task Malformed_llm_output_aborts_without_side_effects()
    {
        var dbName = $"tailor-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        var match = await SeedAsync(db);

        var blobs = new FakeBlobStorage();
        await new ResumeTailor(db, new FakeLlmGateway("not json"), blobs, NullLogger<ResumeTailor>.Instance)
            .TailorAsync(match.Id, CancellationToken.None);

        (await db.TailoredResumes.CountAsync()).Should().Be(0, "no row on bad output");
        blobs.Uploaded.Should().BeEmpty("nothing uploaded");
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(MatchStatus.New, "status unchanged");
    }

    [Fact]
    public async Task Missing_match_is_a_noop()
    {
        var dbName = $"tailor-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);

        var act = async () => await new ResumeTailor(db, new FakeLlmGateway(TailoredJson),
            new FakeBlobStorage(), NullLogger<ResumeTailor>.Instance)
            .TailorAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await db.TailoredResumes.CountAsync()).Should().Be(0);
    }
}
