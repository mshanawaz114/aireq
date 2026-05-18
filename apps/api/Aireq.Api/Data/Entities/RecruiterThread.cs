using Aireq.Api.Data.Common;

namespace Aireq.Api.Data.Entities;

/// <summary>
/// An email thread with a recruiter for a given Match. Created the first
/// time an inbound email is classified as belonging to this match.
/// </summary>
public sealed class RecruiterThread : ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }

    public required string RecruiterEmail { get; set; }
    public string? RecruiterName { get; set; }

    public DateTimeOffset? LastInboundAt { get; set; }
    public DateTimeOffset? LastOutboundAt { get; set; }

    /// <summary>positive | neutral | negative — Haiku-classified.</summary>
    public string? Sentiment { get; set; }

    /// <summary>True when the AI marks the thread as needing human action.</summary>
    public bool RequiresHuman { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Match Match { get; set; } = null!;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
