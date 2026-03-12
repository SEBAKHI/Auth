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
