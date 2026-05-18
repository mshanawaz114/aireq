// DB status endpoint — returns row counts for every table.
//
// Purpose: gives the dashboard a tile that visibly proves the schema is
// alive. Until AIRMVP1-104 seeds data, every row will be 0 — but the table
// names rendering at all is the visual confirmation that AIRMVP1-102
// migrations actually applied.
//
// AllowAnonymous for AIRMVP1-102; AIRMVP1-103 will require admin role.
//
// Refs: AIRMVP1-102

using Aireq.Api.Data;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Endpoints;

public static class DbStatusEndpoints
{
    public static IEndpointRouteBuilder MapDbStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/db/status", async (AireqDbContext db) =>
        {
            var counts = new Dictionary<string, long>
            {
                ["tenants"]            = await db.Tenants.LongCountAsync(),
                ["users"]              = await db.Users.LongCountAsync(),
                ["consultants"]        = await db.Consultants.IgnoreQueryFilters().LongCountAsync(),
                ["resumes"]            = await db.Resumes.LongCountAsync(),
                ["skills"]             = await db.Skills.LongCountAsync(),
                ["consultant_skills"]  = await db.ConsultantSkills.LongCountAsync(),
                ["jobs"]               = await db.Jobs.LongCountAsync(),
                ["matches"]            = await db.Matches.LongCountAsync(),
                ["tailored_resumes"]   = await db.TailoredResumes.LongCountAsync(),
                ["submissions"]        = await db.Submissions.LongCountAsync(),
                ["recruiter_threads"]  = await db.RecruiterThreads.LongCountAsync(),
                ["messages"]           = await db.Messages.LongCountAsync(),
                ["escalations"]        = await db.Escalations.LongCountAsync(),
            };

            return Results.Ok(new DbStatusResponse(
                AppliedMigrations: (await db.Database.GetAppliedMigrationsAsync()).ToArray(),
                PendingMigrations: (await db.Database.GetPendingMigrationsAsync()).ToArray(),
                RowCounts: counts,
                Timestamp: DateTimeOffset.UtcNow));
        })
        .WithName("DbStatus")
        .WithSummary("Row counts per table + migration state. Used by the dashboard's schema tile.")
        .AllowAnonymous();

        return app;
    }
}
