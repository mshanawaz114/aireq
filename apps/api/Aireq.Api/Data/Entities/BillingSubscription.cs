// BillingSubscription — a tenant's Stripe billing state.
//
// One row per tenant, created lazily the first time the tenant touches Stripe
// (checkout) or a webhook arrives. Before that, entitlement is derived from the
// 14-day trial computed off Tenant.CreatedAt, so a brand-new tenant needs no row.
//
// Stripe is the source of truth for subscription status; this row is a local
// cache kept in sync by the webhook so the app can gate features without a
// round-trip to Stripe on every request.
//
// Tenant-scoped via the global query filter, like Match.
//
// Refs: AIRMVP1-406

namespace Aireq.Api.Data.Entities;

public sealed class BillingSubscription : Common.ITimestamped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Stripe customer id (cus_…). Set once a customer is created.</summary>
    public string? StripeCustomerId { get; set; }

    /// <summary>Stripe subscription id (sub_…). Null until they subscribe.</summary>
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Stripe price id the subscription is on.</summary>
    public string? PriceId { get; set; }

    /// <summary>trialing | active | past_due | canceled | incomplete | unpaid.
    /// Mirrors Stripe's subscription.status; "trialing" also covers the local
    /// pre-Stripe trial.</summary>
    public string Status { get; set; } = "trialing";

    /// <summary>End of the trial (Stripe's trial_end, or the local 14-day trial).</summary>
    public DateTimeOffset? TrialEndsAt { get; set; }

    /// <summary>End of the current paid period (Stripe's current_period_end).</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>When the subscription was canceled, if it was.</summary>
    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
