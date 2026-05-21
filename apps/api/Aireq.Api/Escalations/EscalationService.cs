// EscalationService — read model + resolve action for the "needs you" queue.
//
// Escalation has no own tenant filter, so we tenant-scope by joining through
// Matches (which IS globally filtered). The latest thread for the match supplies
// recruiter + sentiment context for the card.
//
// Refs: AIRMVP1-402

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Escalations;

public sealed class EscalationService(AireqDbContext db, ITenantContext tenant)
{
    private const int MaxRows = 200;

    /// <summary>List escalations for the current tenant. <paramref name="openOnly"/>
    /// true (default) returns only unresolved ones, newest first.</summary>
    public async Task<IReadOnlyList<EscalationResponse>> ListAsync(bool openOnly, CancellationToken ct)
    {
        // Join to tenant-filtered Matches to scope; left-join the latest thread
        // per match for recruiter + sentiment context.
        var query =
            from e in db.Escalations
            join m in db.Matches on e.MatchId equals m.Id
            where !openOnly || e.ResolvedAt == null
            orderby e.CreatedAt descending
            let thread = m.Threads
                .OrderByDescending(t => t.LastInboundAt ?? t.CreatedAt)
                .FirstOrDefault()
            select new EscalationResponse(
                e.Id,
                e.MatchId,
                m.Job.Title,
                m.Job.Company,
                thread != null ? thread.RecruiterEmail : null,
                thread != null ? thread.RecruiterName : null,
                thread != null ? thread.Sentiment : null,
                e.Reason,
                e.Summary,
                e.CreatedAt,
                e.ResolvedAt);

        return await query.Take(MaxRows).ToListAsync(ct);
    }

    /// <summary>Mark an escalation resolved. Returns false when it doesn't exist
    /// for the current tenant (so the endpoint can 404).</summary>
    public async Task<bool> ResolveAsync(Guid id, CancellationToken ct)
    {
        // Scope through the tenant-filtered Matches set.
        var escalation = await (
            from e in db.Escalations
            join m in db.Matches on e.MatchId equals m.Id
            where e.Id == id
            select e).FirstOrDefaultAsync(ct);

        if (escalation is null) return false;
        if (escalation.ResolvedAt is not null) return true; // idempotent

        escalation.ResolvedAt = DateTimeOffset.UtcNow;
        escalation.ResolvedByUserId = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
