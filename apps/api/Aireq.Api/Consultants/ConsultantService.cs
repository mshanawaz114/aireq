// ConsultantService — tenant-scoped CRUD for consultants.
//
// All reads/writes go through the tenant-filtered DbContext, so a consultant
// belonging to another tenant is invisible (looks like NotFound), and creates
// are stamped with the current tenant id from ITenantContext.
//
// Kept as a service (not inline in the endpoints) so the tenant-isolation
// behaviour is unit-testable without the HTTP stack.
//
// Refs: AIRMVP1-106

using System.Linq.Expressions;
using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Consultants;

public sealed class ConsultantService(AireqDbContext db, ITenantContext tenant)
{
    // Projection used directly inside EF queries so it translates to SQL
    // (and resolves c.Resumes.Count as a correlated subquery).
    private static readonly Expression<Func<Consultant, ConsultantResponse>> ToResponse =
        c => new ConsultantResponse(
            c.Id,
            c.FullName,
            c.Headline,
            c.Location,
            c.WorkAuth,
            c.RateTargetUsdHourly,
            c.Resumes.Count,
            c.CreatedAt,
            c.UpdatedAt);

    public async Task<IReadOnlyList<ConsultantResponse>> ListAsync(CancellationToken ct)
    {
        return await db.Consultants
            .OrderByDescending(c => c.CreatedAt)
            .Select(ToResponse)
            .ToListAsync(ct);
    }

    public async Task<ConsultantResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        return await db.Consultants
            .Where(c => c.Id == id)
            .Select(ToResponse)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CreateConsultantResult> CreateAsync(UpsertConsultantRequest req, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId)
            return CreateConsultantResult.Unauthenticated();

        var error = Validate(req);
        if (error is not null) return CreateConsultantResult.Invalid(error);

        var consultant = new Consultant
        {
            TenantId = tenantId,
            FullName = req.FullName.Trim(),
            Headline = Clean(req.Headline),
            Location = Clean(req.Location),
            WorkAuth = Clean(req.WorkAuth),
            RateTargetUsdHourly = req.RateTargetUsdHourly,
        };
        db.Consultants.Add(consultant);
        await db.SaveChangesAsync(ct);

        var created = await GetAsync(consultant.Id, ct)
            ?? throw new InvalidOperationException("Consultant not found immediately after create.");
        return CreateConsultantResult.Ok(created);
    }

    public async Task<UpdateConsultantResult> UpdateAsync(
        Guid id, UpsertConsultantRequest req, CancellationToken ct)
    {
        var error = Validate(req);
        if (error is not null) return UpdateConsultantResult.Invalid(error);

        // Tenant-scoped fetch — another tenant's row is invisible → NotFound.
        var consultant = await db.Consultants.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (consultant is null) return UpdateConsultantResult.NotFound();

        consultant.FullName = req.FullName.Trim();
        consultant.Headline = Clean(req.Headline);
        consultant.Location = Clean(req.Location);
        consultant.WorkAuth = Clean(req.WorkAuth);
        consultant.RateTargetUsdHourly = req.RateTargetUsdHourly;
        await db.SaveChangesAsync(ct);

        var updated = await GetAsync(id, ct)
            ?? throw new InvalidOperationException("Consultant vanished after update.");
        return UpdateConsultantResult.Ok(updated);
    }

    private static string? Validate(UpsertConsultantRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName))
            return "Full name is required.";
        if (req.FullName.Trim().Length > 200)
            return "Full name must be 200 characters or fewer.";
        if (req.RateTargetUsdHourly is < 0)
            return "Rate target cannot be negative.";
        return null;
    }

    private static string? Clean(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public readonly record struct CreateConsultantResult(
    CreateConsultantKind Kind, ConsultantResponse? Consultant, string? Error)
{
    public static CreateConsultantResult Ok(ConsultantResponse c) => new(CreateConsultantKind.Ok, c, null);
    public static CreateConsultantResult Invalid(string e) => new(CreateConsultantKind.Invalid, null, e);
    public static CreateConsultantResult Unauthenticated() => new(CreateConsultantKind.Unauthenticated, null, null);
}

public enum CreateConsultantKind { Ok, Invalid, Unauthenticated }

public readonly record struct UpdateConsultantResult(
    UpdateConsultantKind Kind, ConsultantResponse? Consultant, string? Error)
{
    public static UpdateConsultantResult Ok(ConsultantResponse c) => new(UpdateConsultantKind.Ok, c, null);
    public static UpdateConsultantResult Invalid(string e) => new(UpdateConsultantKind.Invalid, null, e);
    public static UpdateConsultantResult NotFound() => new(UpdateConsultantKind.NotFound, null, null);
}

public enum UpdateConsultantKind { Ok, Invalid, NotFound }
