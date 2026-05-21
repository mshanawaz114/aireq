// Waitlist DTOs for the marketing landing page (AIRMVP1-405).
// Refs: AIRMVP1-405

namespace Aireq.Shared.Contracts;

/// <param name="Email">Required. Validated server-side.</param>
/// <param name="Persona">Optional self-description (agency | solo | …).</param>
/// <param name="Source">Optional referral / UTM tag.</param>
public sealed record WaitlistRequest(string Email, string? Persona = null, string? Source = null);

/// <param name="AlreadyJoined">True when the email was already on the list (idempotent).</param>
public sealed record WaitlistResponse(bool Joined, bool AlreadyJoined);
