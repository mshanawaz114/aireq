// ConsultantEndpoints — tenant-scoped consultant CRUD.
//
//   GET    /api/consultants            → list (tenant-scoped)
//   GET    /api/consultants/{id}        → one, or 404
//   POST   /api/consultants             → create, 201 + Location
//   PUT    /api/consultants/{id}        → update, 200, or 404
//
// All require auth. Resume upload lives on ResumeEndpoints (AIRMVP1-104):
//   POST /api/consultants/{id}/resumes
//
// Refs: AIRMVP1-106

using Aireq.Api.Consultants;
using Aireq.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Aireq.Api.Endpoints;

public static class ConsultantEndpoints
{
    public static IEndpointRouteBuilder MapConsultantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/consultants")
            .WithTags("consultants")
            .RequireAuthorization();

        group.MapGet("/", async (ConsultantService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)))
            .WithSummary("List consultants for the current tenant.");

        group.MapGet("/{id:guid}", async (Guid id, ConsultantService svc, CancellationToken ct) =>
        {
            var c = await svc.GetAsync(id, ct);
            return c is null ? Results.NotFound(new { error = "Consultant not found." }) : Results.Ok(c);
        }).WithSummary("Get a single consultant.");

        group.MapPost("/", async (
            [FromBody] UpsertConsultantRequest req, ConsultantService svc, CancellationToken ct) =>
        {
            var result = await svc.CreateAsync(req, ct);
            return result.Kind switch
            {
                CreateConsultantKind.Ok => Results.Created(
                    $"/api/consultants/{result.Consultant!.Id}", result.Consultant),
                CreateConsultantKind.Invalid => Results.BadRequest(new { error = result.Error }),
                CreateConsultantKind.Unauthenticated => Results.Unauthorized(),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        }).WithSummary("Create a consultant under the current tenant.");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] UpsertConsultantRequest req, ConsultantService svc, CancellationToken ct) =>
        {
            var result = await svc.UpdateAsync(id, req, ct);
            return result.Kind switch
            {
                UpdateConsultantKind.Ok => Results.Ok(result.Consultant),
                UpdateConsultantKind.Invalid => Results.BadRequest(new { error = result.Error }),
                UpdateConsultantKind.NotFound => Results.NotFound(new { error = "Consultant not found." }),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
            };
        }).WithSummary("Update a consultant.");

        return app;
    }
}
