// ResumeParser — Hangfire job implementation. Downloads the uploaded blob,
// extracts text (PDF via PdfPig, txt verbatim), sends it through ILlmGateway
// to Claude Haiku with a structured-extraction prompt, and persists the JSON
// the model returns on Resume.ParsedJson.
//
// What happens to embeddings: NOT generated here. AIRMVP1-204 (vector matching)
// owns the embedding pass — keeping the parser focused on structure extraction.
//
// Idempotency: re-running the job for the same resumeId is safe — it just
// overwrites ParsedJson with the latest extraction.
//
// Refs: AIRMVP1-105

using System.Globalization;
using System.Text.Json;
using Aireq.Api.Data;
using Aireq.Api.Storage;
using Aireq.Shared.Jobs;
using Aireq.Shared.Llm;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Worker.Resumes;

public sealed class ResumeParser(
    AireqDbContext db,
    IBlobStorage blobs,
    ILlmGateway llm,
    ILogger<ResumeParser> log) : IResumeParser
{
    /// <summary>Claude Haiku output tokens for resume parsing. 4k is plenty for the JSON.</summary>
    public const int MaxOutputTokens = 4096;

    private const string SystemPrompt =
        """
        You are a resume-parsing service for a recruiting-automation platform.
        Extract structured fields from the resume text the user sends and return
        ONLY a single JSON object (no prose, no Markdown fences) with this shape:

        {
          "fullName": string | null,
          "headline": string | null,
          "location": string | null,
          "email": string | null,
          "phone": string | null,
          "summary": string | null,
          "skills": [{ "name": string, "yearsOfExperience": number | null }],
          "experiences": [{
            "company": string,
            "title": string,
            "startDate": string | null,  // ISO yyyy-MM (best-effort)
            "endDate":   string | null,  // ISO yyyy-MM, or "present"
            "bullets":   [string]
          }],
          "educations": [{
            "school": string,
            "degree": string | null,
            "field":  string | null,
            "endDate": string | null     // ISO yyyy
          }],
          "certifications": [string]
        }

        Rules:
        - If a field is genuinely absent, use null (or [] for arrays).
        - Do NOT invent values. Prefer null over guessing.
        - Preserve the resume's own bullet phrasing in `bullets` — do not rewrite.
        """;

    public async Task ParseAsync(Guid resumeId, CancellationToken ct = default)
    {
        // Workers don't have an HTTP request scope, so CurrentTenantId is null →
        // global query filter passes through. Loading by id directly is fine.
        var resume = await db.Resumes
            .Include(r => r.Consultant)
            .SingleOrDefaultAsync(r => r.Id == resumeId, ct);

        if (resume is null)
        {
            log.LogWarning("ResumeParser: resume {ResumeId} not found, skipping.", resumeId);
            return;
        }

        // Reconstruct the blob path from the row data — same convention as
        // UploadResumeService. Deriving from the URL would break when the
        // storage account / container name changes (e.g. Azurite → real Azure).
        var ext = (Path.GetExtension(resume.OriginalFilename) ?? "").ToLowerInvariant();
        var blobPath = $"tenants/{resume.Consultant.TenantId}/consultants/{resume.ConsultantId}/resumes/{resume.Id}-v{resume.Version}{ext}";
        await using var stream = await blobs.OpenReadAsync(blobPath, ct);
        if (stream is null)
        {
            log.LogWarning(
                "ResumeParser: blob {Path} missing for resume {ResumeId}, skipping.",
                blobPath, resumeId);
            return;
        }

        // Extract text. PDF/txt only in v1 — DOC/DOCX upload is accepted by the
        // endpoint but parsing is deferred to AIRMVP1-105.1 (Open XML reader).
        var text = ExtractText(stream, resume.OriginalFilename);
        if (string.IsNullOrWhiteSpace(text))
        {
            log.LogWarning(
                "ResumeParser: extracted text is empty for resume {ResumeId} ({Filename}).",
                resumeId, resume.OriginalFilename);
            return;
        }

        // Trim to keep input cost predictable. ~30k chars ≈ 7-8k tokens, well
        // under the budget for a single Haiku call.
        const int MaxCharsForLlm = 30_000;
        if (text.Length > MaxCharsForLlm) text = text[..MaxCharsForLlm];

        var tenantId = resume.Consultant.TenantId;
        var response = await llm.CompleteAsync(new LlmRequest(
            TenantId: tenantId,
            Model: LlmModel.Haiku,
            Purpose: "resume.parse",
            SystemPrompt: SystemPrompt,
            UserPrompt: text,
            MaxOutputTokens: MaxOutputTokens), ct);

        // Validate the response is JSON before storing it; if Claude returned
        // anything else we'd rather log + keep ParsedJson null than persist junk
        // that downstream code can't deserialize.
        var json = response.Text.Trim();
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException jex)
        {
            log.LogError(jex,
                "ResumeParser: Claude returned non-JSON for resume {ResumeId}. " +
                "Tokens in={InputTokens} out={OutputTokens}. Body kept in llm_calls audit row.",
                resumeId, response.InputTokens, response.OutputTokens);
            return;
        }

        resume.ParsedJson = json;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Parsed resume {ResumeId} for tenant {TenantId} — {InputTokens} in / {OutputTokens} out, ~${Cost}.",
            resumeId, tenantId, response.InputTokens, response.OutputTokens,
            response.CostUsdEstimate.ToString("F4", CultureInfo.InvariantCulture));
    }

    private string ExtractText(Stream content, string? filename)
    {
        var ext = (Path.GetExtension(filename) ?? "").ToLowerInvariant();
        if (ext == ".pdf") return PdfTextExtractor.Extract(content);
        if (ext == ".txt")
        {
            using var reader = new StreamReader(content);
            return reader.ReadToEnd();
        }

        log.LogWarning(
            "ResumeParser: file extension {Ext} not yet supported. Skipping text extraction.",
            ext);
        return "";
    }

}
