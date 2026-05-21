// ISubmissionChannel — one way to deliver an application (Tier A API, Tier B
// Playwright, Tier C email). 303 implements the Tier A API channels.
//
// Safety: every channel honours the `live` flag. When false (the default —
// FEATURES__ENABLE_LIVE_SUBMIT is off), the channel builds + logs exactly what
// it WOULD send and returns a "dry_run" outcome WITHOUT contacting the employer.
//
// Refs: AIRMVP1-303

using Aireq.Api.Data.Entities;

namespace Aireq.Worker.Submission;

public interface ISubmissionChannel
{
    /// <summary>The Submission.Channel kind this implements (Api for Tier A).</summary>
    SubmissionChannel Kind { get; }

    /// <summary>
    /// Preference order — lower wins. 0 = Tier A (API), 1 = Tier B (Playwright),
    /// 2 = Tier C (email). The orchestrator tries handling channels lowest-tier
    /// first and falls through to the next tier on a "failed" outcome.
    /// </summary>
    int Tier { get; }

    /// <summary>True if this channel can submit to the given job source.</summary>
    bool CanHandle(string jobSource);

    /// <summary>
    /// Submit (or, when <paramref name="live"/> is false, simulate). Never
    /// throws for an expected provider error — returns a "failed" outcome so the
    /// orchestrator records it.
    /// </summary>
    Task<SubmissionOutcome> SubmitAsync(SubmissionRequest request, bool live, CancellationToken ct);
}

public sealed record SubmissionRequest(
    Guid MatchId,
    Guid TenantId,
    string JobSource,
    string JobExternalId,
    string BoardToken,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    byte[] ResumePdf,
    string ResumeFileName);

/// <summary>Status values: "dry_run" | "received" | "failed".</summary>
public sealed record SubmissionOutcome(SubmissionChannel Channel, string Status, string PayloadJson)
{
    public static SubmissionOutcome DryRun(SubmissionChannel ch, string payload) => new(ch, "dry_run", payload);
    public static SubmissionOutcome Received(SubmissionChannel ch, string payload) => new(ch, "received", payload);
    public static SubmissionOutcome Failed(SubmissionChannel ch, string payload) => new(ch, "failed", payload);
}
