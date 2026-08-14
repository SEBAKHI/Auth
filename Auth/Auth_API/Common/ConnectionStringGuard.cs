using Auth.Shared.Configuration;

namespace Auth_API.Common;

/// <summary>
/// Refuses to start when the resolved AuthDb connection string is still the
/// placeholder that ships in appsettings.json.
///
/// <para>
/// <c>appsettings.json</c> declares <c>"AuthDb": "ConnectionStrings__AuthDb"</c> as
/// a reminder of the environment variable that is meant to override it. Nothing
/// resolves that literal — and because it is a non-empty string, the
/// <c>?? throw</c> on <c>GetConnectionString("AuthDb")</c> never fires. The
/// placeholder is handed to the driver instead, and the operator gets a keyword
/// parse error naming an argument they never wrote.
/// </para>
///
/// <para>
/// That was survivable while the connection string could only come from an
/// environment variable. Now that it can also live in the encrypted secrets file,
/// this symptom has a second cause — the file failing to decrypt — so the message
/// has to name both, or a certificate problem on a newly migrated server reads as
/// a malformed connection string.
/// </para>
/// </summary>
public static class ConnectionStringGuard
{
    /// <summary>
    /// The literal in appsettings.json. Matching on it exactly is deliberate: a
    /// heuristic ("looks nothing like a connection string") would eventually
    /// reject a real one.
    /// </summary>
    private const string Placeholder = "ConnectionStrings__AuthDb";

    public static void EnsureResolved(string? connectionString)
    {
        if (!string.Equals(connectionString?.Trim(), Placeholder, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            "Refusing to start: the AuthDb connection string is still the placeholder " +
            $"'{Placeholder}' from appsettings.json, so nothing supplied a real value. Supply it in one " +
            "of these ways:" + Environment.NewLine +
            "  - store it in the encrypted secrets file (Console > System settings > Secrets), which is " +
            "where it belongs in Certificate/Dpapi mode; or" + Environment.NewLine +
            "  - set the ConnectionStrings__AuthDb environment variable (web.config <environmentVariables> " +
            "on IIS)." + Environment.NewLine +
            "If it IS stored in the secrets file, that file could not be read — check the error logged above " +
            "for a decryption failure, which on a newly migrated server usually means the Data Protection " +
            $"certificate or key ring did not come with it. To bypass a stored value that has gone stale, set " +
            $"{DpapiSecretConfigurationProvider.IgnoreConnectionStringVariable}=true and supply the correct " +
            "string through the environment variable above.");
    }
}
