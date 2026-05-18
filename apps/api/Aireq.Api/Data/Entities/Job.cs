using Aireq.Api.Data.Common;
using Pgvector;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// A job posting ingested from one of our sources. Jobs are NOT tenant-scoped —
/// the same posting can match multiple tenants' consultants. (Per-tenant
/// preference filtering happens at match time.)
/// </summary>
public sealed class Job : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>adzuna | usajobs | greenhouse | lever | ashby | rss | playwright.</summary>
    public required string Source { get; set; }

    /// <summary>Provider's stable id for this posting. Unique per (source, source_external_id).</summary>
    public required string SourceExternalId { get; set; }

    public required string Title { get; set; }
    public required string Company { get; set; }
    public string? Location { get; set; }

    /// <summary>Original JD text (truncated to 50k chars).</summary>
    public string? Description { get; set; }

    public DateTimeOffset PostedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Full provider payload — kept for forensics + re-derivation.</summary>
    public string? RawJson { get; set; }

    /// <summary>1536-dim embedding of the JD for vector matching.</summary>
    public Vector? Embedding { get; set; }

    /// <summary>Becomes false after 2 consecutive ingestion passes don't see this posting.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public ICollection<Match> Matches { get; set; } = new List<Match>();
}
