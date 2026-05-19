// IResumeParser — Hangfire job contract for parsing an uploaded resume.
//
// Lives in Aireq.Shared so the API can enqueue
// (BackgroundJobClient.Enqueue<IResumeParser>(x => x.ParseAsync(...))) without
// taking a project reference on Aireq.Worker.
//
// Implementations:
//   - Aireq.Worker.Resumes.ResumeParser (placeholder in AIRMVP1-104; real
//     Claude Haiku parsing lands in AIRMVP1-105).
//
// Refs: AIRMVP1-104

namespace Aireq.Shared.Jobs;

public interface IResumeParser
{
    /// <summary>
    /// Parse the uploaded resume into structured fields (skills, experiences,
    /// educations) and persist the result on the Resume row.
    /// </summary>
    /// <remarks>
    /// Invoked by Hangfire on the worker process. <paramref name="resumeId"/>
    /// must already exist; the job re-loads it from the DB rather than
    /// trusting any state passed in.
    /// </remarks>
    Task ParseAsync(Guid resumeId, CancellationToken ct = default);
}
