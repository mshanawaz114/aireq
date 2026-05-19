// IBlobStorage — minimal abstraction over object storage. Keeps test doubles
// trivial (see FakeBlobStorage in tests) and gives us a single place to swap
// providers later (Azure Blob → S3 → R2) without rippling through callers.
//
// Refs: AIRMVP1-104

namespace Aireq.Api.Storage;

public interface IBlobStorage
{
    /// <summary>
    /// Upload the contents of <paramref name="content"/> to <paramref name="path"/>
    /// inside the configured container. Overwrites if the path already exists.
    /// Returns a URL that the caller persists in <c>Resume.SourceBlobUrl</c>.
    /// </summary>
    /// <param name="path">
    /// Slash-separated key under the container. Convention used by
    /// <c>UploadResumeService</c>:
    /// <c>tenants/{tenantId}/consultants/{consultantId}/resumes/{resumeId}-v{version}{ext}</c>.
    /// </param>
    /// <param name="content">Source stream. Read from current position to end.</param>
    /// <param name="contentType">MIME type to set on the blob (e.g. <c>application/pdf</c>).</param>
    Task<Uri> UploadAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    /// <summary>
    /// Open a read-only stream over an existing blob. Caller disposes.
    /// Returns <c>null</c> if the blob does not exist.
    /// </summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default);

    /// <summary>Delete a blob. No-op if it does not exist.</summary>
    Task DeleteAsync(string path, CancellationToken ct = default);
}
