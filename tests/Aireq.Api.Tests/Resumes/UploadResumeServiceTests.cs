// UploadResumeServiceTests — happy path + failure modes for the resume upload
// pipeline. Stays at the service layer so it doesn't need the HTTP stack.
//
// Coverage:
//   - Happy path: blob uploaded, Resume row written with v1, IResumeParser
//     job enqueued.
//   - Cross-tenant lookup returns NotFound (the global query filter hides
//     the consultant entirely).
//   - Versions are monotonic per consultant — a second upload produces v2.
//   - Validation: empty file, oversize, unsupported content type.
//   - Returns the URL the blob storage handed back, not a synthesised one.
//
// Refs: AIRMVP1-104

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Resumes;
using Aireq.Api.Tests.Infrastructure;
using Aireq.Shared.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aireq.Api.Tests.Resumes;

public sealed class UploadResumeServiceTests
{
    private static AireqDbContext NewDb(ITenantContext tenant, string dbName)
    {
        var options = new DbContextOptionsBuilder<AireqDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore
                .Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AireqDbContext(options, tenant);
    }

    private static async Task<(Tenant a, Tenant b, Consultant ca, Consultant cb)> SeedAsync(string dbName)
    {
        await using var seed = NewDb(new StubTenantContext(), dbName);
        var a = new Tenant { Name = "Tenant A" };
        var b = new Tenant { Name = "Tenant B" };
        var ca = new Consultant { TenantId = a.Id, FullName = "Alice from A" };
        var cb = new Consultant { TenantId = b.Id, FullName = "Bob from B" };
        seed.AddRange(a, b, ca, cb);
        await seed.SaveChangesAsync();
        return (a, b, ca, cb);
    }

    private static Stream MakePdf(int bytes = 256)
    {
        // Minimal "PDF-shaped" bytes — we don't parse them in this test.
        var buf = new byte[bytes];
        new Random(42).NextBytes(buf);
        return new MemoryStream(buf);
    }

    [Fact]
    public async Task Happy_path_uploads_blob_creates_resume_and_enqueues_parse_job()
    {
        var dbName = $"upload-test-{Guid.NewGuid()}";
        var (a, _, ca, _) = await SeedAsync(dbName);

        var tenant = new StubTenantContext { TenantId = a.Id, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var blobs = new FakeBlobStorage();
        var jobs = new FakeBackgroundJobClient();
        var sut = new UploadResumeService(db, blobs, jobs, NullLogger<UploadResumeService>.Instance);

        using var content = MakePdf(512);
        var result = await sut.UploadAsync(
            ca.Id,
            content,
            content.Length,
            "application/pdf",
            "alice-resume.pdf",
            CancellationToken.None);

        result.Kind.Should().Be(UploadResultKind.Ok);
        result.Resume.Should().NotBeNull();
        result.Resume!.Version.Should().Be(1);
        result.Resume.OriginalFilename.Should().Be("alice-resume.pdf");
        result.Resume.ConsultantId.Should().Be(ca.Id);
        result.Resume.SourceBlobUrl.Should().StartWith("https://fake-blob.local/");

        // Blob storage saw the upload.
        blobs.Uploaded.Should().ContainSingle();
        var blob = blobs.Uploaded.First();
        blob.Path.Should().Contain($"tenants/{a.Id}/consultants/{ca.Id}/resumes/");
        blob.Path.Should().EndWith(".pdf");
        blob.ContentType.Should().Be("application/pdf");
        blob.Bytes.Length.Should().Be(512);

        // Resume row persisted (re-read to bypass change tracker).
        var saved = await db.Resumes.IgnoreQueryFilters()
            .SingleAsync(r => r.Id == result.Resume.Id);
        saved.Version.Should().Be(1);
        saved.SourceBlobUrl.Should().Be(result.Resume.SourceBlobUrl);

        // Parse job enqueued against IResumeParser.ParseAsync.
        jobs.Enqueued.Should().ContainSingle();
        var job = jobs.Enqueued.Single();
        job.Type.Should().Be(typeof(IResumeParser));
        job.Method.Should().Be(nameof(IResumeParser.ParseAsync));
        job.Args[0].Should().Be(result.Resume.Id);
        job.State.Should().Be("Enqueued");
    }

    [Fact]
    public async Task Second_upload_for_same_consultant_gets_version_2()
    {
        var dbName = $"upload-test-{Guid.NewGuid()}";
        var (a, _, ca, _) = await SeedAsync(dbName);
        var tenant = new StubTenantContext { TenantId = a.Id, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var sut = new UploadResumeService(db, new FakeBlobStorage(), new FakeBackgroundJobClient(),
            NullLogger<UploadResumeService>.Instance);

        using var first = MakePdf();
        var r1 = await sut.UploadAsync(ca.Id, first, first.Length,
            "application/pdf", "v1.pdf", CancellationToken.None);
        using var second = MakePdf();
        var r2 = await sut.UploadAsync(ca.Id, second, second.Length,
            "application/pdf", "v2.pdf", CancellationToken.None);

        r1.Resume!.Version.Should().Be(1);
        r2.Resume!.Version.Should().Be(2);
    }

    [Fact]
    public async Task Cross_tenant_consultant_lookup_returns_NotFound()
    {
        var dbName = $"upload-test-{Guid.NewGuid()}";
        var (a, _, _, cb) = await SeedAsync(dbName);

        // Acting as Tenant A, try to upload to Bob (Tenant B's consultant).
        var tenant = new StubTenantContext { TenantId = a.Id, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var sut = new UploadResumeService(db, new FakeBlobStorage(), new FakeBackgroundJobClient(),
            NullLogger<UploadResumeService>.Instance);

        using var content = MakePdf();
        var result = await sut.UploadAsync(
            cb.Id, content, content.Length, "application/pdf", "intruder.pdf", CancellationToken.None);

        result.Kind.Should().Be(UploadResultKind.NotFound,
            "cross-tenant consultants must look invisible, not 'forbidden'");
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        var dbName = $"upload-test-{Guid.NewGuid()}";
        var (a, _, ca, _) = await SeedAsync(dbName);
        var tenant = new StubTenantContext { TenantId = a.Id, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var sut = new UploadResumeService(db, new FakeBlobStorage(), new FakeBackgroundJobClient(),
            NullLogger<UploadResumeService>.Instance);

        using var empty = new MemoryStream();
        var result = await sut.UploadAsync(
            ca.Id, empty, 0, "application/pdf", "empty.pdf", CancellationToken.None);

        result.Kind.Should().Be(UploadResultKind.Invalid);
        result.Error.Should().Contain("empty", because: "user needs to know what to fix");
    }

    [Fact]
    public async Task Oversize_file_is_rejected()
    {
        var dbName = $"upload-test-{Guid.NewGuid()}";
        var (a, _, ca, _) = await SeedAsync(dbName);
        var tenant = new StubTenantContext { TenantId = a.Id, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var sut = new UploadResumeService(db, new FakeBlobStorage(), new FakeBackgroundJobClient(),
            NullLogger<UploadResumeService>.Instance);

        // We don't actually allocate 11 MB — just claim the length is over the cap.
        using var content = MakePdf(64);
        var result = await sut.UploadAsync(
            ca.Id, content, UploadResumeService.MaxBytes + 1, "application/pdf",
            "huge.pdf", CancellationToken.None);

        result.Kind.Should().Be(UploadResultKind.Invalid);
        result.Error.Should().Contain("too large");
    }

    [Fact]
    public async Task Unsupported_content_type_is_rejected()
    {
        var dbName = $"upload-test-{Guid.NewGuid()}";
        var (a, _, ca, _) = await SeedAsync(dbName);
        var tenant = new StubTenantContext { TenantId = a.Id, UserId = Guid.NewGuid() };
        await using var db = NewDb(tenant, dbName);
        var sut = new UploadResumeService(db, new FakeBlobStorage(), new FakeBackgroundJobClient(),
            NullLogger<UploadResumeService>.Instance);

        using var content = MakePdf();
        var result = await sut.UploadAsync(
            ca.Id, content, content.Length, "image/png", "resume.png", CancellationToken.None);

        result.Kind.Should().Be(UploadResultKind.Invalid);
        result.Error.Should().Contain("not supported");
    }
}
