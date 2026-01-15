namespace Auth_Lib.Configuration;

/// <summary>
/// Settings for the centralized secret management system.
/// These settings control how secrets are stored, loaded, and auto-generated.
/// </summary>
public class SecretManagementSettings
{
    public const string SectionName = "SecretManagement";

    /// <summary>
    /// Path to the DPAPI-encrypted secrets file.
    /// Default: %LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi
    /// </summary>
    public string SecretFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuthSystem",
        "Secrets",
        "secrets.dpapi");

    /// <summary>
    /// Whether to auto-generate missing cryptographic keys on first startup.
    /// When true, if the secret file doesn't exist, keys will be generated automatically.
    /// Default: true
    /// </summary>
    public bool AutoGenerateKeys { get; set; } = true;

    /// <summary>
    /// Whether to enable the admin API endpoints for secret management.
    /// Should be false in production unless explicitly needed.
    /// Default: false
    /// </summary>
    public bool EnableAdminApi { get; set; } = false;

    /// <summary>
    /// Required permission to access secret management API.
    /// Default: "secrets.manage"
    /// </summary>
    public string RequiredPermission { get; set; } = "secrets.manage";
}
