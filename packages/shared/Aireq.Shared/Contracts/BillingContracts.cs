// Billing DTOs for the settings/billing UI (AIRMVP1-406).
// Refs: AIRMVP1-406

namespace Aireq.Shared.Contracts;

/// <param name="Status">trialing | active | past_due | canceled | trial_expired | incomplete.</param>
/// <param name="Entitled">Whether the tenant currently has product access.</param>
/// <param name="TrialEndsAt">When the trial ends (local or Stripe), if applicable.</param>
/// <param name="CurrentPeriodEnd">End of the current paid period, if subscribed.</param>
/// <param name="HasStripeCustomer">True once a Stripe customer exists (portal is usable).</param>
public sealed record BillingStatusResponse(
    string Status,
    bool Entitled,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset? CurrentPeriodEnd,
    bool HasStripeCustomer);

/// <param name="Url">Stripe-hosted URL to redirect the browser to (checkout or portal).</param>
public sealed record BillingRedirectResponse(string Url);
