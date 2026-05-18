using Aireq.Api.Data.Common;
using Pgvector;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// One uploaded resume version for a Consultant. The latest version per
/// Consultant is the "master" used for matching; tailored variants per Match
/// live in <see cref="TailoredResume"/>.
/// </summary>
public sealed class Resume : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConsultantId { get; set; }

    /// <summary>Monotonic per Consultant. New upload = Version + 1.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Blob URL in Azure Blob storage (Azurite locally).</summary>
    public string SourceBlobUrl { get; set; } = string.Empty;

    /// <summary>Original filename as uploaded.</summary>
    public string? OriginalFilename { get; set; }

    /// <summary>
    /// Structured fields extracted by Claude Haiku (skills, experiences,
    /// educations, certifications). Stored as jsonb for indexable queries.
    /// </summary>
    public string? ParsedJson { get; set; }

    /// <summary>1536-dim embedding from text-embedding-3-small.</summary>
    public Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Consultant Consultant { get; set; } = null!;
}
