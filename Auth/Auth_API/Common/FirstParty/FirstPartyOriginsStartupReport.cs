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
    }
}
