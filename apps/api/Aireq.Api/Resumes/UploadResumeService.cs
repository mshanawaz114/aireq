// UploadResumeService — business logic for uploading a resume.
//
// Why a service (not endpoint inline like AuthEndpoints): keeps the upload
// path testable without spinning up the WebApplicationFactory and lets us
// inject IBlobStorage and IBackgroundJobClient fakes.
//
// Pipeline:
//   1. Resolve the consultant via the tenant-scoped DbContext (global query
//      filter does the access check for us — if the consultant isn't visible
//      we return NotFound, which is also what we want for cross-tenant lookups
//      so we don't leak "exists but forbidden").
//   2. Validate the incoming stream (size, content-type / extension).
//   3. Allocate a Resume row + monotonic version per consultant.
//   4. Upload to blob storage at a deterministic path.
//   5. Save the Resume row with the blob URL.
//   6. Enqueue IResumeParser.ParseAsync (Hangfire — runs on worker).
//
// Refs: AIRMVP1-104

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Storage;
using Aireq.Shared.Contracts;
using Aireq.Shared.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Resumes;

public sealed class UploadResumeService(
    AireqDbContext db,
    IBlobStorage blobs,
    IBackgroundJobClient jobs,
    ILogger<UploadResumeService> log)
{
    /// <summary>Hard cap on resume size. ~10 MB covers anything sane.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>Accepted MIME types — pdf / doc / docx / plain text.</summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/plain",
    };

    private static readonly Dictionary<string, string> ExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["text/plain"] = ".txt",
        };

    public async Task<UploadResult> UploadAsync(
        Guid consultantId,
        Stream content,
        long contentLength,
        string contentType,
        string? originalFilename,
        CancellationToken ct)
    {
        // ---- 1. validate input -----------------------------------------
        if (contentLength <= 0)
            return UploadResult.Fail("File is empty.");
        if (contentLength > MaxBytes)
            return UploadResult.Fail($"File is too large. Maximum is {MaxBytes / (1024 * 1024)} MB.");
        if (!AllowedContentTypes.Contains(contentType))
            return UploadResult.Fail($"Content type '{contentType}' is not supported. Allowed: PDF, DOC, DOCX, TXT.");

        // ---- 2. consultant must exist + belong to current tenant -------
        // The DbContext's global query filter scopes this to the caller's
        // tenant automatically — a consultant in another tenant looks like
        // it doesn't exist, which is exactly what we want for cross-tenant
        // probing to fail silently.
        var consultant = await db.Consultants
            .Where(c => c.Id == consultantId)
            .Select(c => new { c.Id, c.TenantId })
            .SingleOrDefaultAsync(ct);

        if (consultant is null) return UploadResult.NotFound();

        // ---- 3. version + storage path ---------------------------------
        // Versions are monotonic per consultant. The (ConsultantId, Version)
        // unique index in AireqDbContext means we can compute it without a
        // separate lock — a race just produces a duplicate-key exception that
        // the caller can retry.
        var nextVersion = 1 + (await db.Resumes
            .Where(r => r.ConsultantId == consultantId)
            .Select(r => (int?)r.Version)
            .MaxAsync(ct) ?? 0);

        var resumeId = Guid.NewGuid();
        var ext = ExtensionsByContentType.GetValueOrDefault(contentType, "");
        var path = $"tenants/{consultant.TenantId}/consultants/{consultantId}/resumes/{resumeId}-v{nextVersion}{ext}";

        // ---- 4. upload to blob -----------------------------------------
        var blobUri = await blobs.UploadAsync(path, content, contentType, ct);

        // ---- 5. persist Resume row -------------------------------------
        var resume = new Resume
        {
            Id = resumeId,
            ConsultantId = consultantId,
            Version = nextVersion,
            SourceBlobUrl = blobUri.ToString(),
            OriginalFilename = TrimFilename(originalFilename),
            // ParsedJson + Embedding stay null until AIRMVP1-105 fills them in.
        };
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(ct);

        // ---- 6. enqueue parse job --------------------------------------
        // The actual parser runs on the worker process (real impl in
        // AIRMVP1-105). Until then the worker logs and no-ops.
        var jobId = jobs.Enqueue<IResumeParser>(p => p.ParseAsync(resumeId, CancellationToken.None));
        log.LogInformation(
            "Resume {ResumeId} (v{Version}) uploaded for consultant {ConsultantId}; parse job {JobId} enqueued",
            resumeId, nextVersion, consultantId, jobId);

        return UploadResult.Ok(new ResumeResponse(
            resume.Id,
            resume.ConsultantId,
            resume.Version,
            resume.SourceBlobUrl,
            resume.OriginalFilename,
            resume.ParsedJson,
            resume.CreatedAt));
    }

    private static string? TrimFilename(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
        : name.Length > 255 ? name[..255]
        : name;
}

/// <summary>Discriminated result returned from <see cref="UploadResumeService.UploadAsync"/>.</summary>
public readonly record struct UploadResult(
    UploadResultKind Kind,
    ResumeResponse? Resume,
    string? Error)
{
    public static UploadResult Ok(ResumeResponse r) => new(UploadResultKind.Ok, r, null);
    public static UploadResult NotFound() => new(UploadResultKind.NotFound, null, null);
    public static UploadResult Fail(string err) => new(UploadResultKind.Invalid, null, err);
}

public enum UploadResultKind { Ok, NotFound, Invalid }
