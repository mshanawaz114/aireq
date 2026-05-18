using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// A single attempted application against a Match. Multiple submissions can
/// exist per Match (e.g. tried portal API, fell back to Playwright, then
/// cold email).
/// </summary>
public sealed class Submission : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }

    public SubmissionChannel Channel { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>received | accepted | rejected | failed | pending.</summary>
    public string? ResponseStatus { get; set; }

    /// <summary>Provider response (HTTP body, screenshot URL, etc.) — jsonb.</summary>
    public string? ResponsePayloadJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Match Match { get; set; } = null!;
}

public enum SubmissionChannel
{
    /// <summary>Tier A — direct API to Greenhouse / Lever / Ashby.</summary>
    Api = 0,

    /// <summary>Tier B — Playwright against ATS-hosted portal.</summary>
    Portal = 1,

    /// <summary>Tier C — cold email to recruiter / careers@ address.</summary>
    Email = 2,

    /// <summary>Tier D — manual fallback by human.</summary>
    Manual = 3,
}
