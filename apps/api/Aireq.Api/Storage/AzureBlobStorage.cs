// AzureBlobStorage — production / Azurite implementation of IBlobStorage.
//
// Configuration (from .env.local):
//   AZURE_BLOB_CONNECTION_STRING  → Azurite default ("UseDevelopmentStorage=true")
//                                   or a real Azure Storage connection string.
//   AZURE_BLOB_CONTAINER_RESUMES  → container name (default "resumes").
//
// The container is created on first use if missing; subsequent calls are no-ops.
//
// Refs: AIRMVP1-104

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Aireq.Api.Storage;

public sealed class AzureBlobStorage : IBlobStorage
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobStorage> _log;
    private bool _ensured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public AzureBlobStorage(IConfiguration config, ILogger<AzureBlobStorage> log)
    {
        _log = log;

        var conn = config["AZURE_BLOB_CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "AZURE_BLOB_CONNECTION_STRING not configured. Set it in .env.local " +
                "(Azurite default: 'UseDevelopmentStorage=true').");

        var container = config["AZURE_BLOB_CONTAINER_RESUMES"] ?? "resumes";

        var serviceClient = new BlobServiceClient(conn);
        _container = serviceClient.GetBlobContainerClient(container);
    }

    public async Task<Uri> UploadAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);

        var blob = _container.GetBlobClient(path);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            },
            ct);

        _log.LogInformation("Uploaded blob {Path} ({Bytes} bytes, {ContentType})",
            path, content.CanSeek ? content.Length : -1, contentType);
        return blob.Uri;
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var blob = _container.GetBlobClient(path);
        if (!await blob.ExistsAsync(ct)) return null;
        return await blob.OpenReadAsync(cancellationToken: ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        await _container.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: ct);
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_ensured) return;
        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_ensured) return;
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            _ensured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}
