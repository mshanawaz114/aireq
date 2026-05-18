using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// AI-rewritten resume targeted at a specific Match. One Match can have
/// multiple variants — the latest one is the one used for submission.
/// </summary>
public sealed class TailoredResume : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }

    /// <summary>Rendered PDF blob URL.</summary>
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>0–100 ATS keyword-coverage score against the JD.</summary>
    public int? AtsScore { get; set; }

    /// <summary>Diff of changes vs. master resume (jsonb).</summary>
    public string? DiffJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Match Match { get; set; } = null!;
}
