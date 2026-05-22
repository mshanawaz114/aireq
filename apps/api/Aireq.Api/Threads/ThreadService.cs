// ThreadService — read model for the recruiter Inbox.
//
// RecruiterThread has no own tenant filter, so we scope by joining through
// Matches (which IS globally filtered). Each thread carries its messages
// (oldest-first) plus the job + sentiment context the Inbox renders.
//
// Refs: AIRMVP1-401 (read side)

using Aireq.Api.Data;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Threads;

public sealed class ThreadService(AireqDbContext db)
{
    private const int MaxThreads = 100;

    public async Task<IReadOnlyList<ThreadResponse>> ListAsync(CancellationToken ct)
    {
        var query =
            from t in db.RecruiterThreads
            join m in db.Matches on t.MatchId equals m.Id
            orderby (t.LastInboundAt ?? t.CreatedAt) descending
            select new ThreadResponse(
                t.Id,
                t.MatchId,
                m.Job.Title,
                m.Job.Company,
                t.RecruiterEmail,
                t.RecruiterName,
                t.Sentiment,
                t.RequiresHuman,
                t.LastInboundAt,
                t.Messages
                    .OrderBy(msg => msg.SentAt)
                    .Select(msg => new ThreadMessageResponse(
                        msg.Id,
                        msg.Direction.ToString(),
                        msg.Subject,
                        msg.Body,
                        msg.SentAt,
                        msg.GeneratedByAi))
                    .ToList());

        return await query.Take(MaxThreads).ToListAsync(ct);
    }
}
