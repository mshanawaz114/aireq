namespace Aireq.Api.Data.Entities;

/// <summary>
/// Global skill dictionary — NOT tenant-scoped. Skills extracted from any
/// resume go through a canonicalization step so "Apex", "Salesforce Apex",
/// and "apex (sfdc)" all resolve to one row.
/// </summary>
public sealed class Skill
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Canonical display name.</summary>
    public required string Name { get; set; }

    /// <summary>Lowercase slug used for de-dupe lookups.</summary>
    public required string Slug { get; set; }

    /// <summary>Free-text taxonomy category for dashboards: language | framework | platform | soft | cert.</summary>
    public string? Category { get; set; }

    public ICollection<ConsultantSkill> Consultants { get; set; } = new List<ConsultantSkill>();
}
