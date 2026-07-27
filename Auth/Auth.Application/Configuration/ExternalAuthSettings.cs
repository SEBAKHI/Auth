namespace Auth.Application.Configuration;

/// <summary>
/// Configuration settings for external authentication providers.
/// </summary>
public class ExternalAuthSettings
{
    public const string SectionName = "ExternalAuth";

    /// <summary>
    /// Gets or sets the Google authentication settings.
    /// </summary>
    public GoogleAuthSettings? Google { get; set; }

    /// <summary>
    /// Gets or sets the Apple authentication settings.
    /// </summary>
    public AppleAuthSettings? Apple { get; set; }
}

/// <summary>
/// Google-specific authentication settings.
/// </summary>
public class GoogleAuthSettings
{
    /// <summary>
    /// Gets or sets whether Google authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Google OAuth Client ID.
    /// This is a public identifier (not a secret) — safe to store in appsettings.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// Apple-specific ("Sign in with Apple") authentication settings.
/// </summary>
public class AppleAuthSettings
{
    /// <summary>
    /// Gets or sets whether Apple authentication is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Services ID (the web client_id / token audience).
    /// A public identifier — safe to store in appsettings.
    /// </summary>
    public string ServicesId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Apple Developer Team ID (client-secret issuer).
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key ID of the .p8 signing key.
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the .p8 private key (PKCS#8 PEM) used to sign client
    /// secrets. A SECRET: provisioned via SecretManagement
    /// (AppleSigningKeyPem in the encrypted secrets file) — never committed,
    /// and refused in plaintext Production configuration.
    /// </summary>
    public string? PrivateKeyPem { get; set; }
}
