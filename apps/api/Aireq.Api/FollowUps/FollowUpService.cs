// FollowUpService — the owner-facing approval queue for planned nudges.
//
// FollowUp IS tenant-filtered, so reads/writes are auto-scoped. Approve flips a
// Pending draft to Approved (the worker's sender ships it next pass); cancel
// drops it. Both are no-ops on an already-terminal status.
//
// Refs: AIRMVP1-404

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.FollowUps;

public sealed class FollowUpService(AireqDbContext db, ITenantContext tenant)
{
    private const int MaxRows = 200;

    /// <summary>List the tenant's follow-ups. <paramref name="pendingOnly"/> true
    /// (default) returns only those awaiting approval.</summary>
    public async Task<IReadOnlyList<FollowUpResponse>> ListAsync(bool pendingOnly, CancellationToken ct)
    {
        var query =
            from f in db.FollowUps
            join m in db.Matches on f.MatchId equals m.Id
            where !pendingOnly || f.Status == FollowUpStatus.Pending
            orderby f.CreatedAt descending
            select new FollowUpResponse(
                f.Id, f.MatchId, m.Job.Title, m.Job.Company, f.Recipient,
                f.DraftSubject, f.DraftBody, f.Sequence, f.Status.ToString(),
                f.CreatedAt, f.SentAt);

        return await query.Take(MaxRows).ToListAsync(ct);
    }

    /// <summary>Approve a pending follow-up. Returns false when not found for the
    /// tenant. Idempotent on an already-approved one.</summary>
    public async Task<bool> ApproveAsync(Guid id, CancellationToken ct)
    {
        var f = await db.FollowUps.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return false;
        if (f.Status == FollowUpStatus.Pending)
        {
            f.Status = FollowUpStatus.Approved;
            f.ApprovedAt = DateTimeOffset.UtcNow;
            f.ApprovedByUserId = tenant.UserId;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    /// <summary>Cancel a pending/approved follow-up. Returns false when not found.</summary>
    public async Task<bool> CancelAsync(Guid id, CancellationToken ct)
    {
        var f = await db.FollowUps.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return false;
        if (f.Status is FollowUpStatus.Pending or FollowUpStatus.Approved)
        {
            f.Status = FollowUpStatus.Cancelled;
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}
