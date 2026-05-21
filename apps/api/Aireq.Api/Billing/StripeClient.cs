// StripeClient — raw-HTTPS calls to the Stripe API.
//
// Consistent with the rest of the codebase (Resend, Gmail), this talks to the
// Stripe REST API directly with form-encoded bodies rather than pulling in the
// Stripe.net SDK. The surface we need is small: create a customer, open a
// Checkout Session, open a Billing Portal session. Webhook signature
// verification is a static helper (StripeSignature).
//
// Self-disables (Configured = false) until STRIPE_SECRET_KEY is a real key.
//
// Refs: AIRMVP1-406

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Aireq.Api.Billing;

public sealed class StripeClient(
    HttpClient http,
    IConfiguration config,
    IOptions<BillingOptions> options,
    ILogger<StripeClient> log)
{
    private const string Api = "https://api.stripe.com/v1";

    private BillingOptions Opts => options.Value;
    public string? SecretKey => config["STRIPE_SECRET_KEY"] ?? Opts.SecretKey;
    public string? WebhookSecret => config["STRIPE_WEBHOOK_SECRET"] ?? Opts.WebhookSecret;

    public bool Configured =>
        !string.IsNullOrWhiteSpace(SecretKey) && SecretKey!.StartsWith("sk_", StringComparison.Ordinal);

    /// <summary>Create a Stripe customer for the tenant. Returns the customer id.</summary>
    public async Task<string> CreateCustomerAsync(Guid tenantId, string email, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["email"] = email,
            ["metadata[tenant_id]"] = tenantId.ToString(),
        };
        var customer = await PostAsync<StripeId>("/customers", form, ct);
        return customer.Id ?? throw new InvalidOperationException("Stripe customer create returned no id.");
    }

    /// <summary>Create a subscription Checkout Session. Returns the hosted URL.</summary>
    public async Task<string> CreateCheckoutSessionAsync(
        Guid tenantId, string customerId, string priceId, int trialDays, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["customer"] = customerId,
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            ["success_url"] = Opts.SuccessUrl,
            ["cancel_url"] = Opts.CancelUrl,
            // Carry the tenant on the session AND the resulting subscription so
            // webhooks can map back without a customer lookup.
            ["metadata[tenant_id]"] = tenantId.ToString(),
            ["subscription_data[metadata][tenant_id]"] = tenantId.ToString(),
        };
        if (trialDays > 0)
            form["subscription_data[trial_period_days]"] = trialDays.ToString();

        var session = await PostAsync<StripeUrl>("/checkout/sessions", form, ct);
        return session.Url ?? throw new InvalidOperationException("Checkout session returned no url.");
    }

    /// <summary>Open a Billing Portal session for an existing customer.</summary>
    public async Task<string> CreatePortalSessionAsync(string customerId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["customer"] = customerId,
            ["return_url"] = Opts.PortalReturnUrl,
        };
        var session = await PostAsync<StripeUrl>("/billing_portal/sessions", form, ct);
        return session.Url ?? throw new InvalidOperationException("Portal session returned no url.");
    }

    private async Task<T> PostAsync<T>(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}{path}")
        {
            Content = new FormUrlEncodedContent(form),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);

        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            log.LogWarning("Stripe POST {Path} failed ({Status}): {Body}", path, (int)resp.StatusCode, body);
            throw new InvalidOperationException($"Stripe {path} failed ({(int)resp.StatusCode}).");
        }
        return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
               ?? throw new InvalidOperationException($"Stripe {path} returned an empty body.");
    }

    private sealed class StripeId
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    private sealed class StripeUrl
    {
        [JsonPropertyName("url")] public string? Url { get; set; }
    }
}
