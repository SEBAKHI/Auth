using Auth.Application.Configuration;
using Auth.Domain.Enums;
using Microsoft.Extensions.Options;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Common;

/// <summary>
/// The one answer to "where may this application be entered from IAM": after accepting an
/// invitation that gave the invitee one of its roles (OI-192), and, through
/// <see cref="HomeUrlOf"/>, the way home the anonymous branding endpoint offers.
/// </summary>
/// <remarks>
/// <para>
/// Every destination comes from the application's own registration, written by an
/// administrator, and nothing in a request can influence it. That is what keeps the launch
/// from being an open redirect: an invitation link, a query string or a crafted body can
/// change whether the invitee is sent, never where.
/// </para>
/// <para>
/// The accept page re-checks the scheme before it navigates; this is the rule, that is a
/// second lock on the same door.
/// </para>
/// </remarks>
public class ApplicationLaunchTarget
{
    private readonly JwtSettings _jwtSettings;

    public ApplicationLaunchTarget(IOptionsSnapshot<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    /// <summary>
    /// Where the invitee's browser goes after accepting, or null to stay in IAM.
    /// </summary>
    /// <param name="application">The application the invitation named, or null when it named none.</param>
    /// <param name="outcome">What the acceptance did with its role.</param>
    /// <returns>
    /// Null unless the invitee now holds the role (<see cref="InvitationApplicationRoleOutcome.Applied"/>
    /// or <see cref="InvitationApplicationRoleOutcome.AlreadyHeld"/>) and the application is
    /// switched on. Holding the role also means the organization has the application enabled:
    /// the acceptance's own re-check refuses the role otherwise, so the invitee is entitled at
    /// authorize. Then the initiate-login URI with <c>iss</c> appended (OpenID Connect Core
    /// 1.0 §4, "Initiating Login from a Third Party"), else the home URL, else null.
    /// </returns>
    public string? For(ApplicationEntity? application, InvitationApplicationRoleOutcome? outcome)
    {
        if (application is null || !application.IsActive)
        {
            return null;
        }

        if (outcome is not (InvitationApplicationRoleOutcome.Applied or InvitationApplicationRoleOutcome.AlreadyHeld))
        {
            return null;
        }

        // Re-checked on the way out, though a save already refused anything else: the
        // stored value is the browser's destination, and a row written by any other means
        // must not become one.
        var initiateLoginUri = AsSafeAbsoluteUrl(application.InitiateLoginUri);
        return initiateLoginUri is not null
            ? WithIssuer(initiateLoginUri, _jwtSettings.Issuer)
            : HomeUrlOf(application);
    }

    /// <summary>
    /// The application's home address when it may be offered as a way into it: only for an
    /// application a browser can sign in to, one with at least one registered redirect URI
    /// (owner decision D-68-1 (c)), public or server-side (OI-171), and only when the base
    /// URL is safe to render as a link. A machine client never exposes its address.
    /// </summary>
    public static string? HomeUrlOf(ApplicationEntity application) =>
        application.RedirectUris.Count > 0 || application.ConfidentialRedirectUris.Count > 0
            ? AsSafeAbsoluteUrl(application.BaseUrl)
            : null;

    /// <summary>
    /// The value when it is safe to render as a link or to navigate to: absolute, and
    /// <c>https</c>, or <c>http</c> on a loopback host for local development. Anything else
    /// (a relative path, another scheme such as <c>javascript:</c>) is withheld.
    /// </summary>
    private static string? AsSafeAbsoluteUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var allowed = uri.Scheme == Uri.UriSchemeHttps ||
                      (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
        return allowed ? uri.AbsoluteUri : null;
    }

    /// <summary>
    /// Appends <c>iss</c>, URL-encoded, as a new query parameter: after <c>?</c>, or after
    /// <c>&amp;</c> when the address already has a query. The application can compare it with
    /// the issuer it trusts before it starts a sign-in there (OpenID Connect Core 1.0 §4).
    /// </summary>
    private static string WithIssuer(string url, string issuer)
    {
        var fragmentAt = url.IndexOf('#');
        var (address, fragment) = fragmentAt < 0 ? (url, string.Empty) : (url[..fragmentAt], url[fragmentAt..]);

        var separator = address.Contains('?')
            ? address.EndsWith('?') || address.EndsWith('&') ? string.Empty : "&"
            : "?";

        return $"{address}{separator}iss={Uri.EscapeDataString(issuer)}{fragment}";
    }
}
