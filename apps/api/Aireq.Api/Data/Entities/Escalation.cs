namespace Aireq.Api.Data.Entities;

/// <summary>
/// A human-attention event raised by the agent. The owner sees these in the
/// dashboard's Escalations page and resolves them by acting in the inbox.
/// </summary>
public sealed class Escalation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MatchId { get; set; }

    /// <summary>interview_request | rejection | info_request | salary_question | scheduling | captcha | other.</summary>
    public required string Reason { get; set; }

    /// <summary>Short summary for the dashboard card.</summary>
    public string? Summary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    // Navigation
    public Match Match { get; set; } = null!;
}
