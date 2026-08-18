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

    /// <summary>
    /// Gets or sets the provider profile-picture import settings. Every value is read
    /// per sign-in, so all three take effect without a restart.
    /// </summary>
    public ExternalAvatarImportSettings AvatarImport { get; set; } = new();

    /// <summary>
    /// Gets or sets whether a provider sign-in must present a nonce this server
    /// issued to this browser. When false the older, weaker check still runs
    /// (the value is compared to the token's claim only when one is sent).
    /// </summary>
    /// <remarks>
    /// Defaults to FALSE so enabling the server half cannot lock every provider
    /// user out before the browser half is deployed: an older app sends a
    /// self-generated value that no cookie backs, and turning this on rejects it.
    /// Turn it on once the deployed app is fetching nonces from
    /// <c>/auth/external-nonce</c>. Read per sign-in, so it takes effect without
    /// a restart and can be turned straight back off.
    /// <para>
    /// What it buys: a browser-generated nonce proves nothing, because the same
    /// request supplies both the token and the value it is checked against — a
    /// replayer simply reads the value out of the stolen token and sends it. A
    /// server-issued one is bound by cookie to the browser it was issued to, so a
    /// token minted for someone else's browser no longer matches.
    /// </para>
    /// </remarks>
    public bool RequireNonce { get; set; }
}

/// <summary>
/// Settings for copying a provider's profile picture into this system's own image
/// storage the first time an account signs in with one.
/// </summary>
public class ExternalAvatarImportSettings
{
    /// <summary>
    /// Gets or sets whether the picture is imported at all. A kill switch for an
    /// environment with no outbound HTTP: turning it off leaves accounts on the
    /// initials fallback and changes nothing else about sign-in.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the budget for the download, in milliseconds. Bounds the delay the
    /// import can add to the one sign-in that performs it.
    /// </summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>
    /// Gets or sets the maximum number of bytes read from the provider. Enforced while
    /// reading, not from the declared length, so a wrong or absent Content-Length
    /// cannot get past it. Profile pictures are far smaller than this.
    /// </summary>
    public int MaxBytes { get; set; } = 2 * 1024 * 1024;
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
