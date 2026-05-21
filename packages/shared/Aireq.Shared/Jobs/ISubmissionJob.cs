// ISubmissionJob — Hangfire job contract for submitting an application for a
// match. Lives in shared so the API enqueues without referencing the worker.
//
// Implementation: Aireq.Worker.Submission.SubmissionJob.
//
// Refs: AIRMVP1-303

namespace Aireq.Shared.Jobs;

public interface ISubmissionJob
{
    /// <summary>
    /// Submit the latest tailored resume for the match through the best
    /// available channel. Dry-run unless live submit is enabled. The caller
    /// (API) has already verified the match is approved (Tailored).
    /// </summary>
    Task SubmitAsync(Guid matchId, CancellationToken ct = default);
}
