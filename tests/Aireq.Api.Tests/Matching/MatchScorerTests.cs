// MatchScorerTests — the LLM scorer overwrites the vector score with a parsed
// judgment + reasoning, skips malformed output, and stops on budget exhaustion.
//
// Refs: AIRMVP1-205

using System.Text.Json;
using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Shared.Contracts;
using Aireq.Shared.Llm;
using Aireq.Worker.Matching;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aireq.Api.Tests.Matching;

public sealed class MatchScorerTests
{
    private const string GoodResponse = """
        {
          "score": 82,
          "summary": "Strong Salesforce fit.",
          "rationale": ["10y Salesforce", "Apex + LWC match", "Location compatible"],
          "missingKeywords": ["MuleSoft", "CPQ"]
        }
        """;

    private static AireqDbContext NewDb(string dbName) =>
        new(new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options, new StubTenantContext());

    private static MatchScorer Build(AireqDbContext db, ILlmGateway llm, int batch = 25) =>
        new(db, llm, Options.Create(new MatchScoringOptions { BatchSize = batch }),
            NullLogger<MatchScorer>.Instance);

    private static async Task<Match> SeedMatchAsync(AireqDbContext db, int vectorScore = 70, MatchStatus status = MatchStatus.New)
    {
        var tenant = new Tenant { Name = "T" };
        var consultant = new Consultant { TenantId = tenant.Id, FullName = "Alice" };
        var resume = new Resume { ConsultantId = consultant.Id, Version = 1, ParsedJson = "{\"skills\":[\"salesforce\"]}" };
        var job = new Job
        {
            Source = "greenhouse", SourceExternalId = "G1", Title = "Salesforce Architect",
            Company = "Acme", Description = "Need Apex, LWC, MuleSoft.", IsActive = true,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        var match = new Match
        {
            TenantId = tenant.Id, ConsultantId = consultant.Id, JobId = job.Id,
            Score = vectorScore, Status = status,
        };
        db.AddRange(tenant, consultant, resume, job, match);
        await db.SaveChangesAsync();
        return match;
    }

    [Fact]
    public async Task Scores_match_and_stores_reasoning()
    {
        var dbName = $"score-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedMatchAsync(db, vectorScore: 70);
        var llm = new FakeLlmGateway(GoodResponse);

        var scored = await Build(db, llm).RunAsync(CancellationToken.None);

        scored.Should().Be(1);
        var match = await db.Matches.IgnoreQueryFilters().SingleAsync();
        match.Score.Should().Be(82, "the LLM score overwrites the vector score");
        match.ReasoningJson.Should().NotBeNull();

        var reasoning = JsonSerializer.Deserialize<MatchReasoning>(match.ReasoningJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        reasoning!.Summary.Should().Be("Strong Salesforce fit.");
        reasoning.MissingKeywords.Should().Contain("MuleSoft");

        // The prompt was billed to the right tenant + purpose.
        llm.Requests.Should().ContainSingle();
        llm.Requests[0].Purpose.Should().Be("match.score");
        llm.Requests[0].TenantId.Should().Be(match.TenantId);
    }

    [Fact]
    public async Task Tolerates_markdown_fenced_json()
    {
        var dbName = $"score-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedMatchAsync(db);
        var fenced = "```json\n" + GoodResponse + "\n```";

        var scored = await Build(db, new FakeLlmGateway(fenced)).RunAsync(CancellationToken.None);

        scored.Should().Be(1);
        (await db.Matches.IgnoreQueryFilters().SingleAsync()).Score.Should().Be(82);
    }

    [Fact]
    public async Task Malformed_response_leaves_match_unscored()
    {
        var dbName = $"score-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedMatchAsync(db, vectorScore: 65);

        var scored = await Build(db, new FakeLlmGateway("not json at all")).RunAsync(CancellationToken.None);

        scored.Should().Be(0);
        var match = await db.Matches.IgnoreQueryFilters().SingleAsync();
        match.Score.Should().Be(65, "vector score is preserved when the LLM output is unusable");
        match.ReasoningJson.Should().BeNull("we never persist non-conforming output");
    }

    [Fact]
    public async Task Only_scores_new_unreasoned_matches()
    {
        var dbName = $"score-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        // Already-reasoned match should be skipped.
        var m = await SeedMatchAsync(db);
        m.ReasoningJson = "{\"score\":90,\"summary\":\"x\",\"rationale\":[],\"missingKeywords\":[]}";
        await db.SaveChangesAsync();

        var scored = await Build(db, new FakeLlmGateway(GoodResponse)).RunAsync(CancellationToken.None);

        scored.Should().Be(0, "matches with existing reasoning are not re-scored");
    }

    [Fact]
    public async Task Stops_cleanly_on_budget_exhaustion()
    {
        var dbName = $"score-{Guid.NewGuid()}";
        await using var db = NewDb(dbName);
        await SeedMatchAsync(db);
        var llm = new FakeLlmGateway(GoodResponse)
        {
            ThrowOnCall = new LlmBudgetExceededException(Guid.NewGuid(), LlmModel.Haiku, 1, 0, 0, 0),
            ThrowAtCallNumber = 1,
        };

        var scored = await Build(db, llm).RunAsync(CancellationToken.None);

        scored.Should().Be(0, "budget exhaustion stops the pass without throwing");
    }
}
