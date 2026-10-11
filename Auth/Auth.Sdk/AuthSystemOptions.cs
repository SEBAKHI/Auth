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

    /// <summary>
    /// This application's code, its <c>client_id</c> as a machine client. One of the three machine
    /// options; the machine client is active only when all three are set.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The <c>kid</c> of this application's machine key, as the console showed it when the key was
    /// created.
    /// </summary>
    public string? MachineKeyId { get; set; }

    /// <summary>
    /// The machine key's private key, PKCS#8 PEM (<c>-----BEGIN PRIVATE KEY-----</c>), P-256.
    /// A secret: load it from a secret store, never from a file under version control.
    /// </summary>
    public string? MachinePrivateKeyPem { get; set; }

    /// <summary>True when the three machine options are all set.</summary>
    public bool IsMachineClientConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(MachineKeyId)
        && !string.IsNullOrWhiteSpace(MachinePrivateKeyPem);
}
