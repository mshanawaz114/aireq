namespace Aireq.Api.Data.Entities;

/// <summary>
/// Single email in a RecruiterThread. Audit-only — every AI-generated outbound
/// is logged here with model + prompt provenance.
/// </summary>
public sealed class Message
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }

    public MessageDirection Direction { get; set; }

    public string? Subject { get; set; }
    public required string Body { get; set; }

    public DateTimeOffset SentAt { get; set; }

    /// <summary>True for outbound messages composed by the AI.</summary>
    public bool GeneratedByAi { get; set; }

    /// <summary>Model id when GeneratedByAi=true (e.g. "claude-sonnet-4-6").</summary>
    public string? AiModel { get; set; }

    /// <summary>SHA-256 of the prompt that produced this message (audit).</summary>
    public string? PromptHash { get; set; }

    // Navigation
    public RecruiterThread Thread { get; set; } = null!;
}

public enum MessageDirection
{
    Inbound = 0,
    Outbound = 1,
}
