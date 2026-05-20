// ResumeContent — structured resume shape produced by the tailoring LLM and
// consumed by the PDF renderer. Mirrors the parse schema from AIRMVP1-105 so a
// tailored resume is the same shape as the parsed master, just rewritten.
//
// Refs: AIRMVP1-302

namespace Aireq.Shared.Contracts;

public sealed record ResumeContent(
    string? FullName,
    string? Headline,
    string? Location,
    string? Email,
    string? Phone,
    string? Summary,
    IReadOnlyList<ResumeSkill> Skills,
    IReadOnlyList<ResumeExperience> Experiences,
    IReadOnlyList<ResumeEducation> Educations,
    IReadOnlyList<string> Certifications);

public sealed record ResumeSkill(string Name, double? YearsOfExperience);

public sealed record ResumeExperience(
    string Company,
    string Title,
    string? StartDate,
    string? EndDate,
    IReadOnlyList<string> Bullets);

public sealed record ResumeEducation(
    string School,
    string? Degree,
    string? Field,
    string? EndDate);
