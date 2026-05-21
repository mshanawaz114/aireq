// PipelineE2ETests — the W3 chaos/integration test. Stitches the real worker
// services (matching -> scoring -> tailoring -> submission) together with fakes
// for the external edges (pgvector finder, LLM, blob, submission channel) and
// asserts the full discover->apply loop end-to-end on one shared InMemory db.
//
// This is the automated half of AIRMVP1-307's "10 applies end-to-end" — it
// proves the chain wires together and the state transitions are correct. The
// live, browser/employer half is the manual bug-bash (docs/RUNBOOK-bugbash.md).
//
// Refs: AIRMVP1-307

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Api.Tests.Matching; // FakeLlmGateway
using Aireq.Api.Tests.Resumes;  // FakeBlobStorage
using Aireq.Shared.Email;
using Aireq.Worker.Matching;
using Aireq.Worker.Submission;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.E2E;

public sealed class PipelineE2ETests
{
    static PipelineE2ETests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private const string ScoreJson =
        "{\"score\":88,\"summary\":\"Strong fit.\",\"rationale\":[\"a\",\"b\"],\"missingKeywords\":[\"azure\"]}";
    private const string TailorJson = """
        { "fullName":"Alice","headline":"Engineer","location":"Remote","email":"a@x.com",
          "phone":null,"summary":"Salesforce + Azure.","skills":[{"name":"Salesforce","yearsOfExperience":8}],
          "experiences":[{"company":"Acme","title":"Lead","startDate":"2019-01","endDate":null,"bullets":["Did X."]}],
          "educations":[],"certifications":[] }
        """;

    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private sealed class FixedFinder(IReadOnlyList<JobCandidate> c) : IJobCandidateFinder
    {
        public Task<IReadOnlyList<JobCandidate>> FindForConsultantAsync(Guid consultantId, int limit, CancellationToken ct) =>
            Task.FromResult(c);
    }

    private sealed class StubChannel : ISubmissionChannel
    {
        public SubmissionChannel Kind => SubmissionChannel.Api;
        public int Tier => 0;
        public bool CanHandle(string jobSource) => true;
        public Task<SubmissionOutcome> SubmitAsync(SubmissionRequest r, bool live, CancellationToken ct) =>
            Task.FromResult(SubmissionOutcome.DryRun(Kind, "{}"));
    }

    [Fact]
    public async Task Full_loop_match_score_tailor_submit()
    {
        var dbName = $"e2e-{Guid.NewGuid()}";

        // ---- Seed: tenant + owner + consultant + parsed resume + a job ------
        Guid tenantId, consultantId, jobId;
        await using (var seed = NewDb(dbName))
        {
            var tenant = new Tenant { Name = "Acme Staffing" };
            var owner = new User { TenantId = tenant.Id, Email = "owner@acme.test", PasswordHash = "x", Role = "owner" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice Architect", Location = "Remote" };
            var resume = new Resume
            {
                ConsultantId = consultant.Id, Version = 1,
                ParsedJson = "{\"skills\":[\"salesforce\"],\"summary\":\"Salesforce dev.\"}",
                EmbeddedAt = DateTimeOffset.UtcNow,
            };
            var job = new Job
            {
                Source = "greenhouse", SourceExternalId = "G1", Title = "Salesforce Architect",
                Company = "stripe", Location = "Remote", Description = "Need Salesforce, Apex, Azure.",
                IsActive = true, LastSeenAt = DateTimeOffset.UtcNow, EmbeddedAt = DateTimeOffset.UtcNow,
            };
            seed.AddRange(tenant, owner, consultant, resume, job);
            await seed.SaveChangesAsync();
            tenantId = tenant.Id; consultantId = consultant.Id; jobId = job.Id;
        }

        // ---- 1. Matching (fake finder returns the job near-distance) --------
        await using (var db = NewDb(dbName))
        {
            var consultant = await db.Consultants.IgnoreQueryFilters().SingleAsync(c => c.Id == consultantId);
            var finder = new FixedFinder(new[] { new JobCandidate(jobId, "Remote", 0.1) }); // ~90 score
            var matching = new MatchingService(db, finder,
                Options.Create(new MatchingOptions { TopN = 50, MinScore = 50 }),
                NullLogger<MatchingService>.Instance);
            var created = await matching.MatchConsultantAsync(consultant, CancellationToken.None);
            created.Should().Be(1);
        }

        // ---- 2. LLM scoring -> overwrites score + stores reasoning ----------
        await using (var db = NewDb(dbName))
        {
            var scorer = new MatchScorer(db, new FakeLlmGateway(ScoreJson),
                Options.Create(new MatchScoringOptions { BatchSize = 25 }), NullLogger<MatchScorer>.Instance);
            (await scorer.RunAsync(CancellationToken.None)).Should().Be(1);
            var m = await db.Matches.IgnoreQueryFilters().SingleAsync();
            m.Score.Should().Be(88);
            m.ReasoningJson.Should().NotBeNull();
        }

        // ---- 3. Tailoring -> tailored resume + match Tailored ---------------
        Guid matchId;
        await using (var db = NewDb(dbName))
        {
            matchId = (await db.Matches.IgnoreQueryFilters().SingleAsync()).Id;
            var tailor = new ResumeTailor(db, new FakeLlmGateway(TailorJson), new FakeBlobStorage(),
                NullLogger<ResumeTailor>.Instance);
            await tailor.TailorAsync(matchId, CancellationToken.None);
            (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status.Should().Be(MatchStatus.Tailored);
            (await db.TailoredResumes.CountAsync()).Should().Be(1);
        }

        // ---- 4. Submission (dry-run) -> Submission row, match stays Tailored -
        await using (var db = NewDb(dbName))
        {
            // The tailored PDF must be where SubmissionService looks for it.
            var tailored = await db.TailoredResumes.SingleAsync();
            var blobs = new FakeBlobStorage();
            var path = $"tenants/{tenantId}/consultants/{consultantId}/tailored/{matchId}/{tailored.Id}.pdf";
            using (var pdf = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }))
                await blobs.UploadAsync(path, pdf, "application/pdf");

            var svc = new SubmissionService(db, new ISubmissionChannel[] { new StubChannel() }, blobs,
                Options.Create(new SubmissionOptions { EnableLiveSubmit = false }),
                NullLogger<SubmissionService>.Instance);
            await svc.SubmitAsync(matchId, CancellationToken.None);

            var sub = await db.Submissions.SingleAsync();
            sub.ResponseStatus.Should().Be("dry_run");
            (await db.Matches.IgnoreQueryFilters().SingleAsync()).Status
                .Should().Be(MatchStatus.Tailored, "dry-run does not advance the match");
        }
    }

    [Fact]
    public async Task Below_threshold_match_never_enters_the_loop()
    {
        var dbName = $"e2e-{Guid.NewGuid()}";
        Guid consultantId, jobId;
        await using (var seed = NewDb(dbName))
        {
            var tenant = new Tenant { Name = "T" };
            var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
            var job = new Job { Source = "greenhouse", SourceExternalId = "G1", Title = "X", Company = "c", IsActive = true, LastSeenAt = DateTimeOffset.UtcNow };
            seed.AddRange(tenant, consultant, job);
            await seed.SaveChangesAsync();
            consultantId = consultant.Id; jobId = job.Id;
        }

        await using var db = NewDb(dbName);
        var consultant2 = await db.Consultants.IgnoreQueryFilters().SingleAsync();
        // distance 0.7 -> score 30, below MinScore 50.
        var matching = new MatchingService(db, new FixedFinder(new[] { new JobCandidate(jobId, null, 0.7) }),
            Options.Create(new MatchingOptions { MinScore = 50 }), NullLogger<MatchingService>.Instance);

        (await matching.MatchConsultantAsync(consultant2, CancellationToken.None)).Should().Be(0);
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }
}
