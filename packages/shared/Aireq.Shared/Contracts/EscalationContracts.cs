// Escalation DTOs for the dashboard's "needs you" queue (AIRMVP1-402).
//
// EscalationResponse is the read model: the escalation plus enough match/thread
// context to render a card and link through to the conversation.
//
// Refs: AIRMVP1-402

namespace Aireq.Shared.Contracts;

/// <param name="Reason">interview_request | rejection | info_request |
/// salary_question | scheduling | captcha | other.</param>
/// <param name="Sentiment">Latest thread sentiment, when classified.</param>
public sealed record EscalationResponse(
    Guid Id,
    Guid MatchId,
    string JobTitle,
    string Company,
    string? RecruiterEmail,
    string? RecruiterName,
    string? Sentiment,
    string Reason,
    string? Summary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);
