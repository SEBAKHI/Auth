namespace Auth.Application.Configuration;

/// <summary>
/// Configuration for the identity-provider (universal login) features:
/// the authorization-code + PKCE flow and the IdP SSO session cookie.
/// </summary>
public class IdentityProviderSettings
{
    public const string SectionName = "IdentityProvider";

    /// <summary>
    /// Gets or sets the public base URL of the end-user accounts SPA
    /// (e.g. https://accounts.example.com). The authorize endpoint redirects
    /// unauthenticated users to its /login page.
    /// </summary>
    public string AccountsBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets this identity provider's own PUBLIC base URL as seen by
    /// browsers and consuming apps (e.g. https://auth.example.com). Behind a
    /// reverse proxy the request's Host header is the INTERNAL destination
    /// (e.g. identity.example.com), so any public URL the server emits — the
    /// authorize returnTo and the discovery-document endpoints — must be built
    /// from this configured value, never from Request.Host. Leave empty only
    /// where there is no proxy (local dev), where the request host is public.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Returns the configured <see cref="PublicBaseUrl"/> (trailing slash
    /// trimmed), or the supplied request-derived value when none is configured
    /// (proxy-less dev). Deterministic: it never depends on the reverse proxy's
    /// forwarded headers or address.
    /// </summary>
    /// <param name="requestFallback">e.g. $"{Request.Scheme}://{Request.Host}".</param>
    public string ResolvePublicBaseUrl(string requestFallback)
        => string.IsNullOrWhiteSpace(PublicBaseUrl) ? requestFallback : PublicBaseUrl.TrimEnd('/');

    /// <summary>
    /// Gets or sets the lifetime of one-time authorization codes in seconds.
    /// Kept short by design (OAuth 2.0 Security BCP recommends at most 60s).
    /// </summary>
    public int AuthorizationCodeLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the name of the HttpOnly IdP session cookie.
    /// </summary>
    public string IdpSessionCookieName { get; set; } = "auth_idp";

    /// <summary>
    /// Gets or sets the absolute lifetime of an IdP session in days.
    /// </summary>
    public int IdpSessionLifetimeDays { get; set; } = 7;

    /// <summary>
    /// Gets the authorization code lifetime as a TimeSpan.
    /// </summary>
    public TimeSpan AuthorizationCodeLifetime => TimeSpan.FromSeconds(AuthorizationCodeLifetimeSeconds);

    /// <summary>
    /// Gets the IdP session lifetime as a TimeSpan.
    /// </summary>
    public TimeSpan IdpSessionLifetime => TimeSpan.FromDays(IdpSessionLifetimeDays);
}
