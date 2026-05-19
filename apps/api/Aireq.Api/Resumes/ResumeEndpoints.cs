// ResumeEndpoints — HTTP surface for resume upload.
//
//   POST /api/consultants/{consultantId}/resumes
//     Auth:    Bearer JWT (tenant-scoped).
//     Body:    multipart/form-data with a single "file" part.
//     Returns: 201 Created + ResumeResponse on success.
//              404 if the consultant is not visible to the caller's tenant.
//              400 with { error } if validation fails (size, type, empty).
//
// Refs: AIRMVP1-104

using Aireq.Api.Resumes;
using Microsoft.AspNetCore.Mvc;

namespace Aireq.Api.Endpoints;

public static class ResumeEndpoints
{
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/consultants/{consultantId:guid}/resumes")
            .WithTags("resumes")
            .RequireAuthorization()
            // 10 MB matches UploadResumeService.MaxBytes — set on the form
            // options too so Kestrel rejects oversize uploads before we
            // allocate any memory.
            .DisableAntiforgery();

        group.MapPost("/", UploadAsync)
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithSummary("Upload a new resume version for the given consultant.");

        return app;
    }

    private static async Task<IResult> UploadAsync(
        [FromRoute] Guid consultantId,
        [FromForm] IFormFile file,
        UploadResumeService service,
        HttpContext http,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "File is required." });

        await using var stream = file.OpenReadStream();
        var result = await service.UploadAsync(
            consultantId,
            stream,
            file.Length,
            file.ContentType,
            file.FileName,
            ct);

        return result.Kind switch
        {
            UploadResultKind.Ok => Results.Created(
                $"/api/consultants/{consultantId}/resumes/{result.Resume!.Id}",
                result.Resume),
            UploadResultKind.NotFound => Results.NotFound(new { error = "Consultant not found." }),
            UploadResultKind.Invalid => Results.BadRequest(new { error = result.Error }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
