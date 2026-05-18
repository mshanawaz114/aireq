using System.Web;
using Npgsql;

namespace Aireq.Shared.Db;

/// <summary>
/// Normalizes a Postgres connection string from URI form
/// (<c>postgresql://user:pass@host:port/db?option=value</c>, the format Neon
/// gives you) into the canonical key=value form that every Npgsql code path
/// understands without surprise.
///
/// Why this exists:
///   - EF Core + recent Npgsql accept URIs.
///   - Older code paths (notably <c>AspNetCore.HealthChecks.NpgSql</c>) call
///     the legacy key=value parser, which throws on URIs and *leaks the
///     connection string in the exception message* — passwords included.
///   - Normalizing once at startup means everything downstream gets a string
///     they can all parse, and Npgsql doesn't have a reason to scream.
/// </summary>
public static class NeonConnectionString
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        // Already key=value? Leave it alone.
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,

            // Neon defaults. Channel binding is required on the pooled endpoint;
            // we set it here even if the URI omitted it (it does on some plans).
            // TrustServerCertificate is intentionally NOT set — it's obsolete in
            // Npgsql 10 ("no longer needed and does nothing") and triggers
            // CS0618 with TreatWarningsAsErrors.
            SslMode = SslMode.Require,
            ChannelBinding = ChannelBinding.Require,

            // Connection pooler health.
            Pooling = true,
            MaxPoolSize = 20,
            ConnectionIdleLifetime = 60,
            Timeout = 15,
            CommandTimeout = 30,
        };

        // Apply any query-string overrides the user explicitly set (e.g.
        // ?application_name=foo). Keys Neon's URL ships with — sslmode and
        // channel_binding — are no-ops because we already configured them.
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var qs = HttpUtility.ParseQueryString(uri.Query);
            foreach (string key in qs.AllKeys.Where(k => k is not null)!)
            {
                var v = qs[key];
                if (v is null) continue;

                switch (key.ToLowerInvariant())
                {
                    case "sslmode":
                    case "channel_binding":
                        break; // already handled above
                    case "application_name":
                        builder.ApplicationName = v;
                        break;
                    default:
                        // Unknown / non-portable params are dropped on the
                        // floor by design. Add new switch arms if needed.
                        break;
                }
            }
        }

        return builder.ConnectionString;
    }
}
