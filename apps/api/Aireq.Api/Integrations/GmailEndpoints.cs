// GmailEndpoints — the "connect your inbox" OAuth surface + status.
//
//   GET /api/integrations/gmail/connect   (auth) → 302 to Google consent.
//   GET /api/integrations/gmail/callback         → Google redirects here with
//                                                   code+state; we exchange,
//                                                   store, then 302 to the web
//                                                   app. (No JWT — trust is the
//                                                   signed state.)
//   GET /api/integrations/gmail/status    (auth) → { connected, emailAddress,
//                                                   lastPolledAt }.
//   DELETE /api/integrations/gmail        (auth) → disconnect (drop tokens).
//
// Refs: AIRMVP1-401

using Aireq.Api.Auth;
using Aireq.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aireq.Api.Integrations;

public static class GmailEndpoints
{
    public static IEndpointRouteBuilder MapGmailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations/gmail").WithTags("integrations");

        group.MapGet("/connect", ConnectAsync).RequireAuthorization()
            .WithSummary("Begin Gmail OAuth — redirects to Google consent.");

        group.MapGet("/callback", CallbackAsync).AllowAnonymous()
            .WithSummary("Gmail OAuth callback — exchanges the code and stores the mailbox.");

        group.MapGet("/status", StatusAsync).RequireAuthorization()
            .WithSummary("Whether the tenant has a connected Gmail mailbox.");

        group.MapDelete("", DisconnectAsync).RequireAuthorization()
            .WithSummary("Disconnect the tenant's Gmail mailbox.");

        return app;
    }

    private static IResult ConnectAsync(GmailOAuthService oauth, ITenantContext tenant)
    {
        if (tenant.TenantId is null) return Results.Unauthorized();
        if (!oauth.IsEnabled)
            return Results.Problem(
                "Gmail integration is not configured on this server.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        return Results.Redirect(oauth.BuildAuthorizationUrl(tenant.TenantId.Value));
    }

    private static async Task<IResult> CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        GmailOAuthService oauth,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var log = loggers.CreateLogger("GmailCallback");

        if (!string.IsNullOrEmpty(error))
        {
            log.LogWarning("Gmail consent denied/aborted: {Error}", error);
            return Results.Redirect($"{oauth.PostConnectRedirect.Split('?')[0]}?gmail=denied");
        }
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Results.BadRequest(new { error = "Missing code or state." });

        try
        {
            await oauth.HandleCallbackAsync(code, state, ct);
            return Results.Redirect(oauth.PostConnectRedirect);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Gmail OAuth callback failed.");
            return Results.Redirect($"{oauth.PostConnectRedirect.Split('?')[0]}?gmail=error");
        }
    }

    private static async Task<IResult> StatusAsync(
        AireqDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        if (tenant.TenantId is null) return Results.Unauthorized();

        var account = await db.GmailAccounts.IgnoreQueryFilters()
            .Where(g => g.TenantId == tenant.TenantId)
            .Select(g => new { g.EmailAddress, g.LastPolledAt })
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new GmailStatusResponse(
            Connected: account is not null,
            EmailAddress: account?.EmailAddress,
            LastPolledAt: account?.LastPolledAt));
    }

    private static async Task<IResult> DisconnectAsync(
        AireqDbContext db, ITenantContext tenant, CancellationToken ct)
    {
        if (tenant.TenantId is null) return Results.Unauthorized();

        var account = await db.GmailAccounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(g => g.TenantId == tenant.TenantId, ct);
        if (account is null) return Results.NoContent();

        db.GmailAccounts.Remove(account);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

/// <param name="Connected">True when a mailbox is connected for this tenant.</param>
/// <param name="EmailAddress">The connected mailbox address, if any.</param>
/// <param name="LastPolledAt">When the inbound poller last ran for it.</param>
public sealed record GmailStatusResponse(bool Connected, string? EmailAddress, DateTimeOffset? LastPolledAt);
