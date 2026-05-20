// MatchScorer — replaces the cosine vector score with an LLM judgment + a
// human-readable rationale and ATS-gap analysis.
//
// For each New, not-yet-reasoned match (best vector score first):
//   1. Load the consultant's latest parsed resume + the job description.
//   2. Ask the LLM (Groq fast model via ILlmGateway) for structured JSON:
//      { score, summary, rationale[], missingKeywords[] }.
//   3. Validate it parses as MatchReasoning; overwrite Match.Score and store
//      the JSON in Match.ReasoningJson. Malformed output leaves the match
//      untouched (re-tried next pass) — never persist junk.
//
// Cost: one LLM call per match, capped by BatchSize, ordered so the strongest
// candidates get reasoned first. Budget is enforced inside ILlmGateway.
//
// Refs: AIRMVP1-205

using System.Text.Json;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Worker.Matching;

public sealed class MatchScorer(
    AireqDbContext db,
    ILlmGateway llm,
    IOptions<MatchScoringOptions> options,
    ILogger<MatchScorer> log)
{
    private const string SystemPrompt =
        """
        You are a senior technical recruiter scoring how well a consultant fits a
        specific job. You are given the consultant's parsed resume (JSON) and a job
        posting. Return ONLY a single JSON object (no prose, no Markdown fences):

        {
          "score": <integer 0-100, overall fit>,
          "summary": "<one sentence verdict>",
          "rationale": ["<bullet>", "<bullet>", "<bullet>"],
          "missingKeywords": ["<ATS keyword in the JD absent from the resume>"]
        }

        Scoring guidance:
        - 80-100: strong fit, apply now.
        - 60-79: plausible with resume tailoring.
        - <60: weak fit.
        Base the score on real skill/experience overlap, not wishful thinking.
        Keep rationale to 3 concise bullets. missingKeywords drives ATS tailoring,
        so list concrete terms (skills, tools, certs), not sentences.
        """;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var opts = options.Value;

        var batch = await db.Matches
            .IgnoreQueryFilters()
            .Where(m => m.Status == MatchStatus.New && m.ReasoningJson == null)
            .OrderByDescending(m => m.Score)
            .Take(opts.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        // Preload the latest parsed resume per consultant (avoids N+1).
        var consultantIds = batch.Select(m => m.ConsultantId).Distinct().ToList();
        var resumeByConsultant = await db.Resumes
            .IgnoreQueryFilters()
            .Where(r => consultantIds.Contains(r.ConsultantId) && r.ParsedJson != null)
            .GroupBy(r => r.ConsultantId)
            .Select(g => g.OrderByDescending(r => r.Version).First())
            .ToDictionaryAsync(r => r.ConsultantId, r => r.ParsedJson!, ct);

        var jobIds = batch.Select(m => m.JobId).Distinct().ToList();
        var jobById = await db.Jobs
            .Where(j => jobIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, ct);

        var scored = 0;
        foreach (var match in batch)
        {
            if (ct.IsCancellationRequested) break;
            if (!resumeByConsultant.TryGetValue(match.ConsultantId, out var resumeJson)) continue;
            if (!jobById.TryGetValue(match.JobId, out var job)) continue;

            var userPrompt =
                $"## RESUME\n{Clamp(resumeJson, opts.MaxResumeChars)}\n\n" +
                $"## JOB\n{job.Title} at {job.Company}\n{Clamp(job.Description ?? "", opts.MaxJobChars)}";

            try
            {
                var resp = await llm.CompleteAsync(new LlmRequest(
                    TenantId: match.TenantId,
                    Model: LlmModel.Haiku,
                    Purpose: "match.score",
                    SystemPrompt: SystemPrompt,
                    UserPrompt: userPrompt), ct);

                if (!TryParse(resp.Text, out var reasoning, out var rawJson))
                {
                    log.LogWarning("Match {MatchId}: scorer returned non-conforming JSON; leaving unscored.", match.Id);
                    continue;
                }

                match.Score = Math.Clamp(reasoning!.Score, 0, 100);
                match.ReasoningJson = rawJson;
                scored++;
            }
            catch (LlmBudgetExceededException)
            {
                log.LogWarning("LLM budget exhausted mid-scoring; stopping this pass at {Scored} scored.", scored);
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Match {MatchId}: scoring failed; will retry next pass.", match.Id);
            }
        }

        if (scored > 0) await db.SaveChangesAsync(ct);
        log.LogInformation("Match scoring: {Scored}/{Batch} matches reasoned.", scored, batch.Count);
        return scored;
    }

    /// <summary>Parse the model output into MatchReasoning. Returns the normalized
    /// JSON to persist (so the stored shape is exactly MatchReasoning, even if the
    /// model added stray fields).</summary>
    private static bool TryParse(string text, out MatchReasoning? reasoning, out string normalizedJson)
    {
        reasoning = null;
        normalizedJson = "";
        var json = text.Trim();
        // Tolerate accidental ```json fences.
        if (json.StartsWith("```"))
        {
            var firstNl = json.IndexOf('\n');
            if (firstNl > 0) json = json[(firstNl + 1)..];
            if (json.EndsWith("```")) json = json[..^3];
            json = json.Trim();
        }

        try
        {
            reasoning = JsonSerializer.Deserialize<MatchReasoning>(json, JsonOpts);
            if (reasoning is null || reasoning.Summary is null) return false;
            normalizedJson = JsonSerializer.Serialize(reasoning, JsonOpts);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Clamp(string s, int max) => s.Length <= max ? s : s[..max];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
