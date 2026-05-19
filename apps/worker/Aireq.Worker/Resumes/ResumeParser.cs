// ResumeParser — placeholder implementation of IResumeParser.
//
// AIRMVP1-104 only wires the Hangfire enqueue path end-to-end so we can prove
// the upload → parse-job hand-off. Actual blob fetch + Claude Haiku call land
// in AIRMVP1-105.
//
// Until then this job logs that it would have parsed and returns. The DB
// row's ParsedJson stays null, which the UI treats as "still parsing".
//
// Refs: AIRMVP1-104, AIRMVP1-105

using Aireq.Shared.Jobs;

namespace Aireq.Worker.Resumes;

public sealed class ResumeParser(ILogger<ResumeParser> log) : IResumeParser
{
    public Task ParseAsync(Guid resumeId, CancellationToken ct = default)
    {
        log.LogInformation(
            "ResumeParser placeholder hit for resume {ResumeId}. Real parsing lands in AIRMVP1-105.",
            resumeId);
        return Task.CompletedTask;
    }
}
