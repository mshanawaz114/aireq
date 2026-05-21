// BillingEndpoints — billing status, checkout, portal, and the Stripe webhook.
//
//   GET  /api/billing/status     (auth) → entitlement + trial/period info.
//   POST /api/billing/checkout   (auth) → { url } to a Stripe Checkout Session.
//   POST /api/billing/portal     (auth) → { url } to the Stripe Billing Portal.
//   POST /api/billing/webhook          → Stripe events (signature-verified, no JWT).
//
// The webhook reads the RAW request body (signature is computed over the exact
// bytes Stripe sent) before parsing JSON.
//
// Refs: AIRMVP1-406

using Aireq.Shared.Contracts;
using System.Text.Json;

namespace Aireq.Api.Billing;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing").WithTags("billing");

        group.MapGet("/status", async (BillingService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetStatusAsync(ct)))
            .RequireAuthorization()
            .WithSummary("Current tenant's billing status + entitlement.");

        group.MapPost("/checkout", async (BillingService svc, CancellationToken ct) =>
            {
                if (!svc.Configured)
                    return Results.Problem("Billing is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
                return Results.Ok(new BillingRedirectResponse(await svc.StartCheckoutAsync(ct)));
            })
            .RequireAuthorization()
            .WithSummary("Create a Stripe Checkout Session and return its URL.");

        group.MapPost("/portal", async (BillingService svc, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(new BillingRedirectResponse(await svc.OpenPortalAsync(ct)));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
                }
            })
            .RequireAuthorization()
            .WithSummary("Open the Stripe customer portal and return its URL.");

        group.MapPost("/webhook", WebhookAsync)
            .AllowAnonymous()
            .WithSummary("Stripe webhook receiver (signature-verified).");

        return app;
    }

    private static async Task<IResult> WebhookAsync(
        HttpRequest request, StripeClient stripe, BillingService billing,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var log = loggers.CreateLogger("StripeWebhook");

        // Raw body — signature is over the exact bytes.
        request.EnableBuffering();
        string payload;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
            payload = await reader.ReadToEndAsync(ct);

        var sig = request.Headers["Stripe-Signature"].ToString();
        if (!StripeSignature.Verify(payload, sig, stripe.WebhookSecret))
        {
            log.LogWarning("Stripe webhook rejected: bad signature.");
            return Results.BadRequest(new { error = "invalid signature" });
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            await billing.ApplyWebhookAsync(doc, ct);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Stripe webhook body was not valid JSON.");
            return Results.BadRequest(new { error = "invalid payload" });
        }

        // 200 quickly so Stripe doesn't retry; processing is best-effort + idempotent.
        return Results.Ok(new { received = true });
    }
}
