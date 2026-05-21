// BillingService — entitlement logic + Stripe orchestration.
//
//   GetStatusAsync   — derive the tenant's status/entitlement from the local
//                      BillingSubscription cache, falling back to the 14-day
//                      trial computed off Tenant.CreatedAt when there's no row.
//   StartCheckoutAsync — ensure a Stripe customer, open a Checkout Session
//                      (carrying the remaining trial), return the hosted URL.
//   OpenPortalAsync  — open a Billing Portal session for the existing customer.
//   ApplyWebhookAsync — upsert the local cache from a verified Stripe event.
//
// Refs: AIRMVP1-406

using System.Text.Json;
using Aireq.Api.Auth;
using Aireq.Api.Data;
using Aireq.Api.Data.Entities;
using Aireq.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aireq.Api.Billing;

public sealed class BillingService(
    AireqDbContext db,
    ITenantContext tenant,
    StripeClient stripe,
    IOptions<BillingOptions> options,
    ILogger<BillingService> log)
{
    // Stripe statuses that grant access while current.
    private static readonly HashSet<string> ActiveStatuses = ["active", "trialing", "past_due"];

    public bool Configured => stripe.Configured && !string.IsNullOrWhiteSpace(options.Value.PriceId);

    public async Task<BillingStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant.");
        var sub = await db.BillingSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var trialEndsAt = await TrialEndsAtAsync(tenantId, sub, ct);
        var now = DateTimeOffset.UtcNow;

        // Active Stripe subscription wins.
        if (sub?.StripeSubscriptionId is not null && ActiveStatuses.Contains(sub.Status))
        {
            var entitled = sub.Status == "trialing"
                || sub.CurrentPeriodEnd is null
                || sub.CurrentPeriodEnd > now;
            return new BillingStatusResponse(
                sub.Status, entitled, trialEndsAt, sub.CurrentPeriodEnd, sub.StripeCustomerId is not null);
        }

        // No active subscription -> fall back to the local trial.
        if (now < trialEndsAt)
            return new BillingStatusResponse("trialing", true, trialEndsAt, null, sub?.StripeCustomerId is not null);

        var status = sub?.Status == "canceled" ? "canceled" : "trial_expired";
        return new BillingStatusResponse(status, false, trialEndsAt, sub?.CurrentPeriodEnd, sub?.StripeCustomerId is not null);
    }

    public async Task<string> StartCheckoutAsync(CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("Billing is not configured.");
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant.");

        var sub = await EnsureSubscriptionRowAsync(tenantId, ct);
        if (sub.StripeCustomerId is null)
        {
            var email = await OwnerEmailAsync(tenantId, ct);
            sub.StripeCustomerId = await stripe.CreateCustomerAsync(tenantId, email, ct);
            sub.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        // Pass the *remaining* trial so checkout doesn't extend it.
        var trialEndsAt = await TrialEndsAtAsync(tenantId, sub, ct);
        var remainingDays = (int)Math.Ceiling(Math.Max(0, (trialEndsAt - DateTimeOffset.UtcNow).TotalDays));

        return await stripe.CreateCheckoutSessionAsync(
            tenantId, sub.StripeCustomerId!, options.Value.PriceId!, remainingDays, ct);
    }

    public async Task<string> OpenPortalAsync(CancellationToken ct)
    {
        if (!stripe.Configured) throw new InvalidOperationException("Billing is not configured.");
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("No tenant.");
        var sub = await db.BillingSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub?.StripeCustomerId is null)
            throw new InvalidOperationException("No Stripe customer for this tenant yet.");
        return await stripe.CreatePortalSessionAsync(sub.StripeCustomerId, ct);
    }

    /// <summary>Apply a verified Stripe webhook event to the local cache. Returns
    /// true if it changed anything.</summary>
    public async Task<bool> ApplyWebhookAsync(JsonDocument evt, CancellationToken ct)
    {
        var root = evt.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type is null || !root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("object", out var obj))
            return false;

        switch (type)
        {
            case "checkout.session.completed":
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                return await UpsertFromObjectAsync(type, obj, ct);
            default:
                log.LogDebug("Stripe webhook {Type} ignored.", type);
                return false;
        }
    }

    // ---- internals ---------------------------------------------------------

    private async Task<bool> UpsertFromObjectAsync(string type, JsonElement obj, CancellationToken ct)
    {
        var tenantId = MetadataTenantId(obj);
        var customerId = Str(obj, "customer");

        // Resolve the row by tenant metadata first, else by customer id.
        BillingSubscription? sub = null;
        if (tenantId is { } tid)
            sub = await db.BillingSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tid, ct);
        if (sub is null && customerId is not null)
            sub = await db.BillingSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.StripeCustomerId == customerId, ct);

        if (sub is null)
        {
            if (tenantId is null) { log.LogWarning("Stripe {Type}: no tenant_id and unknown customer; skipping.", type); return false; }
            sub = new BillingSubscription { TenantId = tenantId.Value, CreatedAt = DateTimeOffset.UtcNow };
            db.BillingSubscriptions.Add(sub);
        }

        if (customerId is not null) sub.StripeCustomerId = customerId;

        if (type == "checkout.session.completed")
        {
            // The session carries the subscription id; status flips in via the
            // subsequent subscription.* events, but mark active optimistically.
            var subscriptionId = Str(obj, "subscription");
            if (subscriptionId is not null) sub.StripeSubscriptionId = subscriptionId;
            if (sub.Status == "trial_expired" || sub.Status == "canceled") sub.Status = "active";
        }
        else
        {
            // subscription.* — full state.
            sub.StripeSubscriptionId = Str(obj, "id") ?? sub.StripeSubscriptionId;
            sub.Status = type == "customer.subscription.deleted" ? "canceled" : Str(obj, "status") ?? sub.Status;
            sub.CurrentPeriodEnd = Unix(obj, "current_period_end") ?? sub.CurrentPeriodEnd;
            sub.TrialEndsAt = Unix(obj, "trial_end") ?? sub.TrialEndsAt;
            sub.PriceId = FirstItemPriceId(obj) ?? sub.PriceId;
            if (type == "customer.subscription.deleted") sub.CanceledAt = DateTimeOffset.UtcNow;
        }

        sub.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Stripe {Type} applied for tenant {Tenant} (status={Status}).", type, sub.TenantId, sub.Status);
        return true;
    }

    private async Task<BillingSubscription> EnsureSubscriptionRowAsync(Guid tenantId, CancellationToken ct)
    {
        var sub = await db.BillingSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub is not null) return sub;

        var now = DateTimeOffset.UtcNow;
        sub = new BillingSubscription
        {
            TenantId = tenantId,
            Status = "trialing",
            TrialEndsAt = await TrialEndsAtAsync(tenantId, null, ct),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.BillingSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return sub;
    }

    private async Task<DateTimeOffset> TrialEndsAtAsync(Guid tenantId, BillingSubscription? sub, CancellationToken ct)
    {
        if (sub?.TrialEndsAt is { } te) return te;
        var createdAt = await db.Tenants.IgnoreQueryFilters()
            .Where(x => x.Id == tenantId).Select(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (createdAt == default) createdAt = DateTimeOffset.UtcNow;
        return createdAt.AddDays(options.Value.TrialDays);
    }

    private async Task<string> OwnerEmailAsync(Guid tenantId, CancellationToken ct) =>
        await db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => u.Role == "owner")
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct) ?? "billing@aireq.local";

    private static Guid? MetadataTenantId(JsonElement obj)
    {
        if (obj.TryGetProperty("metadata", out var md)
            && md.ValueKind == JsonValueKind.Object
            && md.TryGetProperty("tenant_id", out var tid)
            && Guid.TryParse(tid.GetString(), out var g))
            return g;
        return null;
    }

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Unix(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(v.GetInt64())
            : null;

    private static string? FirstItemPriceId(JsonElement obj)
    {
        if (obj.TryGetProperty("items", out var items)
            && items.TryGetProperty("data", out var arr)
            && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0)
        {
            var first = arr[0];
            if (first.TryGetProperty("price", out var price) && price.TryGetProperty("id", out var id))
                return id.GetString();
        }
        return null;
    }
}
