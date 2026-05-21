// BillingOptions — Stripe config + trial length.
//
// Secret key + webhook secret are read flat (STRIPE_SECRET_KEY /
// STRIPE_WEBHOOK_SECRET) to match the rest of the env layout; the price id and
// URLs bind from the STRIPE section. Billing self-disables until a real secret
// key is set (test keys start "sk_test_", live "sk_live_").
//
// Refs: AIRMVP1-406

namespace Aireq.Api.Billing;

public sealed class BillingOptions
{
    public const string ConfigKey = "STRIPE";

    public string? SecretKey { get; set; }
    public string? WebhookSecret { get; set; }

    /// <summary>The recurring price the checkout subscribes to (price_…).</summary>
    public string? PriceId { get; set; }

    /// <summary>Trial length for new tenants (days), also passed to Stripe checkout.</summary>
    public int TrialDays { get; set; } = 14;

    /// <summary>Where Stripe sends the browser after a successful checkout.</summary>
    public string SuccessUrl { get; set; } = "http://localhost:3000/settings/billing?checkout=success";

    /// <summary>Where Stripe sends the browser if checkout is abandoned.</summary>
    public string CancelUrl { get; set; } = "http://localhost:3000/settings/billing?checkout=cancelled";

    /// <summary>Where the customer portal returns to when the user is done.</summary>
    public string PortalReturnUrl { get; set; } = "http://localhost:3000/settings/billing";
}
