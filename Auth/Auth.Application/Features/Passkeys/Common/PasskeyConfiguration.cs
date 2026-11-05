using System.Globalization;
using Auth.Application.Interfaces;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// Whether passkeys can run with the configuration in force (PK2, PK4, PK19), and the
/// relying party they run as. A pure decision over four inputs, so the endpoints, the
/// handlers and the posture check read one rule.
/// </summary>
/// <remarks>
/// <para>
/// Passkeys run only when all hold: <c>Passkeys:Enabled</c> is on; an RP ID is set and is a
/// host name (<c>localhost</c>, or a dotted domain), never an address, a URL or a port; at
/// least one first-party origin is listed (<c>IdentityProvider:FirstPartySpaOrigins</c>), and
/// every one is an https origin whose host is the RP ID or ends with "." and the RP ID (a
/// browser refuses anything else); and the schema is deployed. Otherwise they are off — the
/// endpoints answer 404 — and the posture names the first problem.
/// </para>
/// <para>
/// The RP ID is never derived from a URL or a request's host: it is a setting, read once at
/// start-up (PK2). The origins are the exact list the ceremony's <c>clientDataJSON.origin</c>
/// must match; the CORS list is never a source (PK4).
/// </para>
/// </remarks>
public static class PasskeyConfiguration
{
    /// <summary>The problem an empty RP ID is reported as.</summary>
    public const string NoRpId = "Passkeys:RpId is empty";

    /// <summary>The problem an empty origin list is reported as.</summary>
    public const string NoOrigins = "IdentityProvider:FirstPartySpaOrigins lists no origin";

    /// <summary>The problem a missing schema is reported as.</summary>
    public const string NoSchema = "the passkey tables are not deployed (DACPAC)";

    /// <summary>
    /// The problem a new RP ID is refused with while live passkeys carry another (PK3), decided
    /// by <see cref="PasskeyFeature"/> over the stored rows.
    /// </summary>
    public const string OtherRpIdLive =
        "live passkeys are bound to another RP ID: restore the previous Passkeys:RpId, or retire them first (Auth_Setup --retire-passkey-rp-id)";

    /// <summary>Decides whether passkeys can run, and as which relying party.</summary>
    /// <param name="enabled"><c>Passkeys:Enabled</c>.</param>
    /// <param name="rpId">The RP ID the process started with, normalized.</param>
    /// <param name="origins"><c>IdentityProvider:FirstPartySpaOrigins</c> now.</param>
    /// <param name="schemaPresent">Whether the passkey schema is deployed.</param>
    public static PasskeyConfigurationAssessment Assess(
        bool enabled,
        string rpId,
        IReadOnlyList<string> origins,
        bool schemaPresent)
    {
        var problems = new List<string>();

        var rpIdValid = false;
        if (string.IsNullOrEmpty(rpId))
        {
            problems.Add(NoRpId);
        }
        else if (!IsHostName(rpId))
        {
            problems.Add($"Passkeys:RpId '{rpId}' is not a host name (localhost, or a domain such as example.com)");
        }
        else
        {
            rpIdValid = true;
        }

        var listed = origins.Where(origin => !string.IsNullOrWhiteSpace(origin)).ToList();
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        if (listed.Count == 0)
        {
            problems.Add(NoOrigins);
        }

        foreach (var origin in listed)
        {
            if (!TryParseOrigin(origin, out var exact, out var host))
            {
                problems.Add($"first-party origin '{origin}' is not a bare https origin");
                continue;
            }

            if (rpIdValid && !Covers(rpId, host))
            {
                problems.Add($"Passkeys:RpId '{rpId}' does not cover the first-party origin '{origin}'");
            }

            normalized.Add(exact);
        }

        if (!schemaPresent)
        {
            problems.Add(NoSchema);
        }

        var configured = problems.Count == 0;
        return new PasskeyConfigurationAssessment(
            IsAvailable: enabled && configured,
            IsConfigured: configured,
            RelyingParty: configured ? new PasskeyRelyingParty(rpId, rpId, normalized) : null,
            Problems: problems);
    }

    /// <summary>
    /// Whether <paramref name="rpId"/> may be the RP ID of a page on <paramref name="host"/>:
    /// the same host, or a parent domain of it (WebAuthn L3 §5.1.3).
    /// </summary>
    public static bool Covers(string rpId, string host) =>
        string.Equals(host, rpId, StringComparison.Ordinal)
        || host.EndsWith("." + rpId, StringComparison.Ordinal);

    /// <summary>
    /// A host name an RP ID may be: <c>localhost</c>, or two or more DNS labels of letters,
    /// digits and hyphens. Never an IP literal (a browser refuses one), and never a single
    /// label other than localhost (a public suffix such as <c>com</c> is refused by browsers).
    /// </summary>
    public static bool IsHostName(string value)
    {
        if (value == "localhost")
        {
            return true;
        }

        if (value.Length is 0 or > 253 || Uri.CheckHostName(value) != UriHostNameType.Dns)
        {
            return false;
        }

        var labels = value.Split('.');
        return labels.Length >= 2
            && labels.All(label => label.Length is > 0 and <= 63
                && label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
                && label[0] != '-' && label[^1] != '-')
            // The last label of a real domain is never all digits; this keeps "1.2.3.4" out.
            && !labels[^1].All(char.IsAsciiDigit);
    }

    private static bool TryParseOrigin(string value, out string exact, out string host)
    {
        exact = string.Empty;
        host = string.Empty;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.PathAndQuery is not ("/" or "")
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        host = uri.IdnHost.ToLowerInvariant();
        exact = uri.IsDefaultPort
            ? $"{uri.Scheme}://{host}"
            : $"{uri.Scheme}://{host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }
}

/// <summary>What <see cref="PasskeyConfiguration.Assess"/> decided.</summary>
/// <param name="IsAvailable">Passkeys run: switched on, and configured.</param>
/// <param name="IsConfigured">The RP ID, the origins and the schema allow passkeys to run, switched on or not.</param>
/// <param name="RelyingParty">The relying party, when configured.</param>
/// <param name="Problems">Why it is not configured, for the posture: empty when it is.</param>
public sealed record PasskeyConfigurationAssessment(
    bool IsAvailable,
    bool IsConfigured,
    PasskeyRelyingParty? RelyingParty,
    IReadOnlyList<string> Problems);
