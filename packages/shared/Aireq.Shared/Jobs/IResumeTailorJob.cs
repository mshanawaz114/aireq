// IResumeTailorJob — Hangfire job contract for tailoring a resume to a match.
// Lives in shared so the API can enqueue without referencing the worker.
//
// Implementation: Aireq.Worker.Tailoring.ResumeTailorJob.
//
// Refs: AIRMVP1-302

namespace Aireq.Shared.Jobs;

public interface IResumeTailorJob
{
    /// <summary>
    /// Rewrite the consultant's resume targeted at the given match's JD, render
    /// a PDF, store it, and record a TailoredResume row. Idempotent-ish: each
    /// run produces a new TailoredResume variant for the match.
    /// </summary>
    Task TailorAsync(Guid matchId, CancellationToken ct = default);
}
