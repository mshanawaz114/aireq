// GmailOAuthOptions — config for the Gmail "connect your inbox" OAuth flow.
//
// Bound from the GOOGLE configuration section, with the client id/secret read
// from flat env keys (GOOGLE_CLIENT_ID / GOOGLE_CLIENT_SECRET) so the API and
// the worker's GmailClient share exactly one credential pair.
//
// Refs: AIRMVP1-401

namespace Aireq.Api.Integrations;

public sealed class GmailOAuthOptions
{
    public const string ConfigKey = "GOOGLE";

    /// <summary>OAuth client id (also read from GOOGLE_CLIENT_ID).</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret (also read from GOOGLE_CLIENT_SECRET).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Where Google sends the user back after consent. MUST exactly match an
    /// Authorized redirect URI in the Google Cloud console. Default targets the
    /// API's own callback endpoint in local dev.
    /// </summary>
    public string RedirectUri { get; set; } = "http://localhost:5080/api/integrations/gmail/callback";

    /// <summary>Where to bounce the browser after a successful connect (the web app).</summary>
    public string PostConnectRedirect { get; set; } = "http://localhost:3000/settings/integrations?gmail=connected";

    /// <summary>Read-only Gmail scope — enough to poll + read replies.</summary>
    public string Scope { get; set; } = "https://www.googleapis.com/auth/gmail.readonly";
}
