// FakeBlobStorage — in-memory IBlobStorage for tests. Records every Upload
// so assertions can inspect what was sent (path, bytes, content type).
//
// Refs: AIRMVP1-104

using System.Collections.Concurrent;
using Aireq.Api.Storage;

namespace Aireq.Api.Tests.Resumes;

public sealed class FakeBlobStorage : IBlobStorage
{
    public sealed record StoredBlob(string Path, byte[] Bytes, string ContentType);

    private readonly ConcurrentDictionary<string, StoredBlob> _blobs = new();

    public IReadOnlyCollection<StoredBlob> Uploaded => _blobs.Values.ToList();

    public Task<Uri> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        _blobs[path] = new StoredBlob(path, ms.ToArray(), contentType);
        return Task.FromResult(new Uri($"https://fake-blob.local/{path}"));
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
    {
        if (!_blobs.TryGetValue(path, out var blob)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(new MemoryStream(blob.Bytes));
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        _blobs.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}
