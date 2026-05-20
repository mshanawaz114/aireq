// ResumeTailor — rewrites the master resume targeted at a match's JD, renders a
// PDF, stores it, records a TailoredResume row, and flips the match to Tailored.
//
// Pipeline:
//   1. Load match -> consultant + job; load the consultant's latest parsed resume.
//   2. Compute the JD's ATS missing keywords (reuses the 301 extractor).
//   3. LLM rewrite (Groq strong model) -> ResumeContent JSON. Prompt forbids
//      fabrication: missing keywords are woven in only where truthful.
//   4. Render the tailored ResumeContent to a PDF, upload to blob.
//   5. Recompute ATS coverage on the tailored text; persist TailoredResume with
//      AtsScore + DiffJson (added keywords, before/after coverage).
//   6. Set match.Status = Tailored.
//
// Refs: AIRMVP1-302

using System.Text;
using System.Text.Json;
using Aireq.Api.Ats;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Storage;
using Aireq.Shared.Contracts;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Worker.Tailoring;

public sealed class ResumeTailor(
    AireqDbContext db,
    ILlmGateway llm,
    IBlobStorage blobs,
    ILogger<ResumeTailor> log)
{
    private const int MaxResumeChars = 8_000;
    private const int MaxJobChars = 8_000;

    private const string SystemPrompt =
        """
        You are an expert resume writer for technical staffing. Rewrite the
        consultant's master resume so it is tailored to the target job and passes
        ATS keyword screening. Return ONLY a JSON object with this exact shape:

        {
          "fullName": string|null, "headline": string|null, "location": string|null,
          "email": string|null, "phone": string|null, "summary": string|null,
          "skills": [{ "name": string, "yearsOfExperience": number|null }],
          "experiences": [{ "company": string, "title": string,
            "startDate": string|null, "endDate": string|null, "bullets": [string] }],
          "educations": [{ "school": string, "degree": string|null,
            "field": string|null, "endDate": string|null }],
          "certifications": [string]
        }

        Rules:
        - NEVER fabricate experience, employers, dates, or credentials.
        - Weave the listed missing ATS keywords in ONLY where the consultant
          plausibly already has that experience; otherwise leave them out.
        - Rewrite the summary + bullets to foreground JD-relevant work using the
          JD's own terminology. Keep everything truthful and concise.
        """;

    public async Task TailorAsync(Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .IgnoreQueryFilters()
            .Include(m => m.Job)
            .SingleOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) { log.LogWarning("Tailor: match {MatchId} not found.", matchId); return; }

        var resumeJson = await db.Resumes
            .IgnoreQueryFilters()
            .Where(r => r.ConsultantId == match.ConsultantId && r.ParsedJson != null)
            .OrderByDescending(r => r.Version)
            .Select(r => r.ParsedJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(resumeJson))
        {
            log.LogWarning("Tailor: consultant {ConsultantId} has no parsed resume; skipping.", match.ConsultantId);
            return;
        }

        var jobText = $"{match.Job.Title}\n{match.Job.Description}";
        var before = AtsKeywordExtractor.Analyze(matchId, jobText, resumeJson);

        var userPrompt =
            $"## MASTER RESUME\n{Clamp(resumeJson, MaxResumeChars)}\n\n" +
            $"## TARGET JOB\n{match.Job.Title} at {match.Job.Company}\n{Clamp(match.Job.Description ?? "", MaxJobChars)}\n\n" +
            $"## MISSING ATS KEYWORDS (weave in truthfully)\n{string.Join(", ", before.MissingKeywords)}";

        LlmResponse resp;
        try
        {
            resp = await llm.CompleteAsync(new LlmRequest(
                TenantId: match.TenantId,
                Model: LlmModel.Sonnet,       // quality matters for rewriting
                Purpose: "resume.tailor",
                SystemPrompt: SystemPrompt,
                UserPrompt: userPrompt,
                MaxOutputTokens: 4096), ct);
        }
        catch (LlmBudgetExceededException)
        {
            log.LogWarning("Tailor: LLM budget exhausted for tenant {TenantId}; skipping match {MatchId}.",
                match.TenantId, matchId);
            return;
        }

        if (!TryParse(resp.Text, out var content) || content is null)
        {
            log.LogError("Tailor: LLM returned non-conforming JSON for match {MatchId}; aborting.", matchId);
            return;
        }

        // Render + upload.
        var pdf = TailoredResumeRenderer.Render(content);
        var tailoredId = Guid.NewGuid();
        var path = $"tenants/{match.TenantId}/consultants/{match.ConsultantId}/tailored/{matchId}/{tailoredId}.pdf";
        using var stream = new MemoryStream(pdf);
        var blobUri = await blobs.UploadAsync(path, stream, "application/pdf", ct);

        // Recompute coverage on the tailored text.
        var after = AtsKeywordExtractor.Analyze(matchId, jobText, FlattenForAts(content));
        var addedKeywords = before.MissingKeywords.Intersect(after.PresentKeywords).ToList();

        db.TailoredResumes.Add(new TailoredResume
        {
            Id = tailoredId,
            MatchId = matchId,
            BlobUrl = blobUri.ToString(),
            AtsScore = after.CoveragePercent,
            DiffJson = JsonSerializer.Serialize(new
            {
                atsCoverageBefore = before.CoveragePercent,
                atsCoverageAfter = after.CoveragePercent,
                addedKeywords,
            }, JsonOpts),
        });

        if (match.Status == MatchStatus.New) match.Status = MatchStatus.Tailored;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Tailored resume for match {MatchId}: ATS {Before}% -> {After}%, +{Added} keyword(s).",
            matchId, before.CoveragePercent, after.CoveragePercent, addedKeywords.Count);
    }

    private static bool TryParse(string text, out ResumeContent? content)
    {
        content = null;
        var json = text.Trim();
        if (json.StartsWith("```"))
        {
            var nl = json.IndexOf('\n');
            if (nl > 0) json = json[(nl + 1)..];
            if (json.EndsWith("```")) json = json[..^3];
            json = json.Trim();
        }
        try
        {
            content = JsonSerializer.Deserialize<ResumeContent>(json, JsonOpts);
            return content is not null;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Flatten the tailored content into text for ATS re-scoring.</summary>
    private static string FlattenForAts(ResumeContent c)
    {
        var sb = new StringBuilder();
        sb.AppendLine(c.Headline).AppendLine(c.Summary);
        sb.AppendLine(string.Join(", ", c.Skills.Select(s => s.Name)));
        foreach (var e in c.Experiences)
        {
            sb.AppendLine($"{e.Title} {e.Company}");
            foreach (var b in e.Bullets) sb.AppendLine(b);
        }
        sb.AppendLine(string.Join(", ", c.Certifications));
        return sb.ToString();
    }

    private static string Clamp(string s, int max) => s.Length <= max ? s : s[..max];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}
