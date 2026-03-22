namespace Auth.Sdk;

/// <summary>
/// Configuration options for the AuthSystem SDK.
/// </summary>
public class AuthSystemOptions
{
    /// <summary>
    /// The base URL of the AuthSystem (e.g., "https://auth.example.com").
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The expected JWT issuer.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The expected JWT audience.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The gateway token for service-to-service authentication with the AuthSystem.
    /// </summary>
    public string GatewayToken { get; set; } = string.Empty;

    /// <summary>
    /// How long to cache validated API key results. Default: 60 seconds.
    /// </summary>
    public TimeSpan ApiKeyCacheDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long to cache validated webhook key results. Default: 5 minutes.
    /// </summary>
    public TimeSpan WebhookKeyCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enable automatic token refresh when access tokens are near expiry.
    /// Default: true. Follows the MSAL/Auth0 SDK pattern.
    /// </summary>
    public bool EnableAutoRefresh { get; set; } = true;

    /// <summary>
    /// Seconds before token expiry to trigger a proactive refresh.
    /// Default: 120 (2 minutes). Set to 0 to only refresh on 401 responses.
    /// </summary>
    public int RefreshBufferSeconds { get; set; } = 120;
}
