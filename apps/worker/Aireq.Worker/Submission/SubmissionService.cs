// SubmissionService — orchestrates a single application submission.
//
//   1. Load match (must be Tailored — i.e. user-approved with a tailored resume).
//   2. Load the latest TailoredResume + fetch its PDF from blob.
//   3. Build the applicant request (name from Consultant, email from tenant owner).
//   4. Pick the channel that handles the job's source; none -> record a Manual
//      (Tier D) submission so the dashboard surfaces it for human follow-up.
//   5. Submit (DRY-RUN unless EnableLiveSubmit). Record a Submission row.
//   6. Advance match to Submitted ONLY on a live "received" outcome — a dry-run
//      leaves the match Tailored so a real submit can still happen later.
//
// Refs: AIRMVP1-303

using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// 'Submission' is both this namespace and the entity type — alias the entity
// so `new SubmissionRow { ... }` is unambiguous.
using SubmissionRow = Aireq.Api.Data.Entities.Submission;

namespace Aireq.Worker.Submission;

public sealed class SubmissionService(
    AireqDbContext db,
    IEnumerable<ISubmissionChannel> channels,
    IBlobStorage blobs,
    IOptions<SubmissionOptions> options,
    ILogger<SubmissionService> log)
{
    public async Task SubmitAsync(Guid matchId, CancellationToken ct)
    {
        var match = await db.Matches
            .IgnoreQueryFilters()
            .Include(m => m.Job)
            .Include(m => m.Consultant)
            .SingleOrDefaultAsync(m => m.Id == matchId, ct);
        if (match is null) { log.LogWarning("Submit: match {MatchId} not found.", matchId); return; }

        if (match.Status != MatchStatus.Tailored)
        {
            log.LogWarning("Submit: match {MatchId} is {Status}, expected Tailored; skipping.",
                matchId, match.Status);
            return;
        }

        var tailored = await db.TailoredResumes
            .Where(t => t.MatchId == matchId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (tailored is null) { log.LogWarning("Submit: no tailored resume for match {MatchId}.", matchId); return; }

        // Applicant identity: name from the consultant, email from the tenant owner.
        var ownerEmail = await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == match.TenantId)
            .OrderBy(u => u.Role == "owner" ? 0 : 1)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct) ?? "noreply@aireq.local";
        var (first, last) = SplitName(match.Consultant.FullName);

        // Tailored PDF from blob (path reconstructed the same way the tailor wrote it).
        var pdfPath = $"tenants/{match.TenantId}/consultants/{match.ConsultantId}/tailored/{matchId}/{tailored.Id}.pdf";
        await using var pdfStream = await blobs.OpenReadAsync(pdfPath, ct);
        if (pdfStream is null) { log.LogWarning("Submit: tailored PDF missing at {Path}.", pdfPath); return; }
        using var ms = new MemoryStream();
        await pdfStream.CopyToAsync(ms, ct);

        var request = new SubmissionRequest(
            matchId, match.TenantId, match.Job.Source, match.Job.SourceExternalId, match.Job.Company,
            first, last, ownerEmail, null, ms.ToArray(), $"{first}-{last}-resume.pdf");

        // Try handling channels lowest-tier first (API -> Playwright -> email),
        // falling through on a "failed" outcome. The first non-failed result wins.
        var handlers = channels.Where(c => c.CanHandle(match.Job.Source)).OrderBy(c => c.Tier).ToList();
        SubmissionOutcome outcome;
        if (handlers.Count == 0)
        {
            // Tier D fallback — surfaced in the dashboard for manual handling.
            log.LogInformation("Submit: no channel for source '{Source}'; recording Manual.", match.Job.Source);
            outcome = new SubmissionOutcome(SubmissionChannel.Manual, "pending_manual",
                $"{{\"reason\":\"no channel for source {match.Job.Source}\"}}");
        }
        else
        {
            outcome = SubmissionOutcome.Failed(handlers[0].Kind, "{\"reason\":\"not attempted\"}");
            foreach (var ch in handlers)
            {
                outcome = await ch.SubmitAsync(request, options.Value.EnableLiveSubmit, ct);
                if (outcome.Status != "failed") break; // success or dry-run -> stop
                log.LogWarning("Submit: tier {Tier} channel failed for match {MatchId}; trying next.",
                    ch.Tier, matchId);
            }

            // Every automated tier failed (e.g. API down + no JD recipient) ->
            // surface for human handling (Tier D) rather than a dead "failed".
            if (outcome.Status == "failed")
            {
                log.LogInformation("Submit: all channels failed for match {MatchId}; recording Manual.", matchId);
                outcome = new SubmissionOutcome(SubmissionChannel.Manual, "pending_manual",
                    outcome.PayloadJson);
            }
        }

        db.Submissions.Add(new SubmissionRow
        {
            MatchId = matchId,
            Channel = outcome.Channel,
            SubmittedAt = DateTimeOffset.UtcNow,
            ResponseStatus = outcome.Status,
            ResponsePayloadJson = outcome.PayloadJson,
        });

        // Only a real, accepted submission advances the match.
        if (outcome.Status == "received") match.Status = MatchStatus.Submitted;

        await db.SaveChangesAsync(ct);
        log.LogInformation("Submission recorded for match {MatchId}: channel={Channel} status={Status} (live={Live}).",
            matchId, outcome.Channel, outcome.Status, options.Value.EnableLiveSubmit);
    }

    private static (string first, string last) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => ("Applicant", ""),
            1 => (parts[0], ""),
            _ => (parts[0], parts[1]),
        };
    }
}
