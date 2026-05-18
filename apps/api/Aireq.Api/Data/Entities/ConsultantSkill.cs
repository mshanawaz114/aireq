namespace Aireq.Api.Data.Entities;

/// <summary>
/// Join table — a Consultant's claim on a Skill, plus evidence the AI
/// extracted from their resume.
/// </summary>
public sealed class ConsultantSkill
{
    public Guid ConsultantId { get; set; }
    public Guid SkillId { get; set; }

    /// <summary>Years of experience as parsed from the resume.</summary>
    public decimal? Years { get; set; }

    /// <summary>Short excerpt from the resume that justifies this skill claim.</summary>
    public string? Evidence { get; set; }

    // Navigation
    public Consultant Consultant { get; set; } = null!;
    public Skill Skill { get; set; } = null!;
}
