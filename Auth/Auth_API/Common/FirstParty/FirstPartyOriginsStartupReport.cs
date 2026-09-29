using Auth.Application.Configuration;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// The boot line that says which browser origins are first-party. Written at
/// Warning so it shows whatever the production log level: an empty list means the
/// Origin check on <c>POST /auth/login</c> refuses nobody and the refresh cookie is
/// never issued, which is only right until the operator lists the two apps.
/// </summary>
public static class FirstPartyOriginsStartupReport
{
    /// <summary>The event code the line starts with, for whoever counts it.</summary>
    public const string EventCode = "boot.first-party-origins";

    /// <summary>Writes the line for the configuration as it stands at boot.</summary>
    public static void Write(Serilog.ILogger logger, IConfiguration configuration)
    {
        var origins = SettingsArrayNormalizer.Resolve(
            configuration.GetSection("IdentityProvider:FirstPartySpaOrigins").Get<string[]>(), []);

        if (origins.Length == 0)
        {
            logger.Warning(
                EventCode + ": IdentityProvider:FirstPartySpaOrigins is empty. Password sign-in accepts any " +
                "browser Origin and the refresh cookie is never issued. List the console and accounts origins.");
            return;
        }

        logger.Warning(
            EventCode + ": first-party app origins are {Origins}; refresh cookie delivery enabled: {Enabled}",
            string.Join(", ", origins),
            configuration.GetValue<bool>("IdentityProvider:SpaRefreshCookieEnabled"));

        // The refresh cookie is SameSite=Strict on the API's host: an app on
        // another site never sends it, and every cookie session ends at its first
        // refresh. A heuristic (the last two host labels, no public suffix list),
        // so it warns and never refuses.
        if (Uri.TryCreate(configuration["IdentityProvider:PublicBaseUrl"], UriKind.Absolute, out var api))
        {
            foreach (var origin in origins)
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var app) && SiteOf(app.Host) != SiteOf(api.Host))
                {
                    logger.Warning(
                        EventCode + ": {Origin} does not look same-site with the API's public URL {PublicBaseUrl}; " +
                        "the browser would never send it the SameSite=Strict refresh cookie",
                        origin,
                        api.GetLeftPart(UriPartial.Authority));
                }
            }
        }
    }

    private static string SiteOf(string host)
    {
        var labels = host.TrimEnd('.').ToLowerInvariant().Split('.');
        return labels.Length <= 2 ? string.Join('.', labels) : string.Join('.', labels[^2..]);
    }
}
