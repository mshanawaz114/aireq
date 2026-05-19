// Resume DTOs shared between API + (eventually) generated TS types.
// Refs: AIRMVP1-104

namespace Aireq.Shared.Contracts;

/// <summary>
/// Returned by <c>POST /api/consultants/{id}/resumes</c> after a successful
/// upload. Parsing is queued and happens asynchronously — the <see cref="ParsedJson"/>
/// field will be <c>null</c> until <c>IResumeParser</c> finishes (AIRMVP1-105).
/// </summary>
public sealed record ResumeResponse(
    Guid Id,
    Guid ConsultantId,
    int Version,
    string SourceBlobUrl,
    string? OriginalFilename,
    string? ParsedJson,
    DateTimeOffset CreatedAt);
