// SubmissionListService — read model for the submission tracker.
//
// Submission has no own tenant filter, so we tenant-scope by joining through
// Matches (which IS globally filtered) — only the current tenant's submissions
// come back. Ordered newest-first.
//
// Refs: AIRMVP1-306

using Aireq.Api.Data;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Submissions;

public sealed class SubmissionListService(AireqDbContext db)
{
    private const int MaxRows = 200;

    public async Task<IReadOnlyList<SubmissionResponse>> ListAsync(CancellationToken ct)
    {
        // Join to the tenant-filtered Matches set to scope results.
        var query =
            from s in db.Submissions
            join m in db.Matches on s.MatchId equals m.Id
            orderby s.SubmittedAt descending
            select new SubmissionResponse(
                s.Id,
                s.MatchId,
                m.Job.Title,
                m.Job.Company,
                s.Channel.ToString(),
                s.ResponseStatus,
                s.SubmittedAt,
                s.ResponsePayloadJson);

        return await query.Take(MaxRows).ToListAsync(ct);
    }
}
