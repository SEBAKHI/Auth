namespace Auth.Domain.Constants;

/// <summary>
/// The OAuth 2.0 scope vocabulary this server understands (RFC 6749 §3.3,
/// OpenID Connect Core §5.4). A scope is a word an application sends on the
/// authorize request to name a part of the user's data it wants; the server
/// grants the ones the application is allowed and records the result with the
/// code, the refresh token and the access token.
/// </summary>
/// <remarks>
/// The single place that spells a scope name. Parsing, the canonical order and
/// the grant rule live in <see cref="ValueObjects.ScopeSet"/>.
/// </remarks>
public static class OAuthScopes
{
    /// <summary>
    /// Identifies the user and nothing more. In every grant, requested or not,
    /// and not removable from an application.
    /// </summary>
    public const string OpenId = "openid";

    /// <summary>
    /// The user's name and picture.
    /// </summary>
    public const string Profile = "profile";

    /// <summary>
    /// The user's email address.
    /// </summary>
    public const string Email = "email";

    /// <summary>
    /// The user's phone number.
    /// </summary>
    public const string Phone = "phone";

    /// <summary>
    /// The standard request for a refresh token. Recognised so that libraries
    /// that add it on their own are not refused, and otherwise ignored: refresh
    /// tokens are issued whether it is asked for or not. Never granted, never
    /// advertised and never allowed on an application.
    /// </summary>
    public const string OfflineAccess = "offline_access";

    /// <summary>
    /// The scopes this server grants, in canonical order. Advertised as
    /// <c>scopes_supported</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Supported = [OpenId, Profile, Email, Phone];

    /// <summary>
    /// The scopes an administrator may allow on an application: every supported
    /// scope except <see cref="OpenId"/>, which every application has.
    /// </summary>
    public static readonly IReadOnlyList<string> Optional = [Profile, Email, Phone];

    /// <summary>
    /// Whether <paramref name="name"/> is one of <see cref="Supported"/>.
    /// Case-sensitive, as RFC 6749 §3.3 requires: <c>OpenID</c> is not
    /// <c>openid</c>.
    /// </summary>
    public static bool IsSupported(string? name) =>
        name is not null && Supported.Contains(name, StringComparer.Ordinal);
}
