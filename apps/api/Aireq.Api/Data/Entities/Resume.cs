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

    /// <summary>Dense embedding of the parsed resume (dim = EmbeddingConfig.Dimensions).
    /// This is the consultant's matching vector.</summary>
    public Vector? Embedding { get; set; }

    /// <summary>When the embedding was last computed. Null = needs embedding.
    /// Mapped on every provider (unlike Embedding). (AIRMVP1-204)</summary>
    public DateTimeOffset? EmbeddedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Consultant Consultant { get; set; } = null!;
}
