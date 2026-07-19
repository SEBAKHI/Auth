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
    /// (e.g. https://accounts.astoom.com). The authorize endpoint redirects
    /// unauthenticated users to its /login page.
    /// </summary>
    public string AccountsBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the lifetime of one-time authorization codes in seconds.
    /// Kept short by design (OAuth 2.0 Security BCP recommends at most 60s).
    /// </summary>
    public int AuthorizationCodeLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the name of the HttpOnly IdP session cookie.
    /// </summary>
    public string IdpSessionCookieName { get; set; } = "astoom_idp";

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
