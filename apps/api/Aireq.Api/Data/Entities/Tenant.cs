using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// Top-level isolation unit. Solo plans have one Tenant with one User. Agency
/// plans have one Tenant with multiple Users and multiple Consultants.
/// </summary>
public sealed class Tenant : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    /// <summary>solo | pro | agency | enterprise — matches pricing tiers in memory.md §13.</summary>
    public string Plan { get; set; } = "solo";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Consultant> Consultants { get; set; } = new List<Consultant>();
    public ICollection<Match> Matches { get; set; } = new List<Match>();
}
