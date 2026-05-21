// WaitlistEntry — a prospective customer who signed up on the marketing landing
// page before (or instead of) creating an account.
//
// Global (NOT tenant-scoped): these arrive from anonymous visitors, before any
// tenant exists. Email is unique so a double-submit is idempotent.
//
// Refs: AIRMVP1-405

namespace Aireq.Api.Data.Entities;

public sealed class WaitlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Normalized (trimmed + lower-cased) email. Unique.</summary>
    public required string Email { get; set; }

    /// <summary>Optional free-text: how they describe themselves (agency / solo / …).</summary>
    public string? Persona { get; set; }

    /// <summary>UTM / referral tag captured from the landing page, if any.</summary>
    public string? Source { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
