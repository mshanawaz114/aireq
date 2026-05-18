using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// A person being marketed by this Tenant. Solo plans have one Consultant
/// (the user themselves). Agencies have many.
/// </summary>
public sealed class Consultant : ITimestamped, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public required string FullName { get; set; }

    /// <summary>"Sr. Salesforce Architect · 20 yrs" — short pitch line.</summary>
    public string? Headline { get; set; }

    public string? Location { get; set; }

    /// <summary>e.g. US Citizen, H1B, EAD, OPT, GC.</summary>
    public string? WorkAuth { get; set; }

    public decimal? RateTargetUsdHourly { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Resume> Resumes { get; set; } = new List<Resume>();
    public ICollection<ConsultantSkill> Skills { get; set; } = new List<ConsultantSkill>();
    public ICollection<Match> Matches { get; set; } = new List<Match>();
}
