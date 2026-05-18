using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// A scored pairing of one Consultant with one Job. Tenant-scoped because
/// it carries the consultant's per-tenant context (preferences, history).
/// </summary>
public sealed class Match : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConsultantId { get; set; }
    public Guid JobId { get; set; }

    /// <summary>0–100 from the LLM scorer.</summary>
    public int Score { get; set; }

    /// <summary>3-bullet rationale + missing keywords + ATS gap analysis (jsonb).</summary>
    public string? ReasoningJson { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.New;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public Consultant Consultant { get; set; } = null!;
    public Job Job { get; set; } = null!;
    public ICollection<TailoredResume> TailoredResumes { get; set; } = new List<TailoredResume>();
    public ICollection<Submission> Submissions { get; set; } = new List<Submission>();
    public ICollection<RecruiterThread> Threads { get; set; } = new List<RecruiterThread>();
    public ICollection<Escalation> Escalations { get; set; } = new List<Escalation>();
}

public enum MatchStatus
{
    New = 0,
    Reviewing = 1,
    Tailored = 2,
    Submitted = 3,
    Reply = 4,
    Interview = 5,
    Rejected = 6,
    Closed = 7,
}
