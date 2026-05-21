// SubmissionOptions — the live-submit safety switch.
//
// LiveSubmit defaults to FALSE. Until the operator explicitly flips
// FEATURES__ENABLE_LIVE_SUBMIT=true, every submission is a dry-run: the channel
// builds + logs the payload and records a Submission row, but no application is
// actually sent to any employer. This is the §12/§14 safety guardrail.
//
// Refs: AIRMVP1-303

namespace Aireq.Worker.Submission;

public sealed class SubmissionOptions
{
    public const string ConfigKey = "FEATURES";

    /// <summary>Bound from FEATURES__ENABLE_LIVE_SUBMIT. False = dry-run only.</summary>
    public bool EnableLiveSubmit { get; set; }
}
