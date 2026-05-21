// ReplyClassification — the structured judgment the reply classifier (Haiku)
// produces for an inbound recruiter message. Lives in shared so the worker
// (producer) and the API/web escalation views (consumers) agree on the shape.
//
// Sentiment drives the thread badge; Intent maps onto the Escalation.Reason
// vocabulary; RequiresHuman gates whether an Escalation is raised.
//
// Refs: AIRMVP1-402

namespace Aireq.Shared.Contracts;

/// <param name="Sentiment">positive | neutral | negative.</param>
/// <param name="Intent">interview_request | rejection | info_request |
/// salary_question | scheduling | other — aligns with Escalation.Reason.</param>
/// <param name="RequiresHuman">True when a human should act (interview, info
/// request, salary/scheduling negotiation). Plain rejections do not.</param>
/// <param name="Summary">One-line summary for the escalation card.</param>
public sealed record ReplyClassification(
    string Sentiment,
    string Intent,
    bool RequiresHuman,
    string Summary);
