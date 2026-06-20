namespace Auth.Application.Configuration;

/// <summary>
/// Settings for the centralized secret management system.
/// These settings control how secrets are stored, loaded, and auto-generated.
/// </summary>
public class SecretManagementSettings
{
    public const string SectionName = "SecretManagement";

    private static readonly string DefaultSecretFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AuthSystem",
        "Secrets",
        "secrets.dpapi");

    private string _secretFilePath = DefaultSecretFilePath;

    /// <summary>
    /// Where the cryptographic secrets are stored and how they are protected at rest.
    /// Allowed values (case-insensitive): <c>PlainText</c>, <c>Certificate</c>, <c>Dpapi</c>.
    /// Defaults to <c>PlainText</c> (keys stored as plain text in the appsettings target file).
    /// Parsed via <c>AuthDataProtectionExtensions.ParseStorageMode</c>.
    /// </summary>
    public string StorageMode { get; set; } = "PlainText";

    /// <summary>
    /// Appsettings file that generated secrets are written to when <see cref="StorageMode"/> is
    /// <c>PlainText</c>. May be absolute or relative to the application content root.
    /// Default: <c>appsettings.Production.json</c>.
    /// </summary>
    public string PlainTextTargetFile { get; set; } = "appsettings.Production.json";

    /// <summary>
    /// Path to the encrypted secrets file (used by the <c>Certificate</c> and <c>Dpapi</c> modes).
    /// Default: %LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi
    /// </summary>
    public string SecretFilePath
    {
        get => _secretFilePath;
        set => _secretFilePath = string.IsNullOrWhiteSpace(value) ? DefaultSecretFilePath : value;
    }

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

    /// <summary>
    /// True when <see cref="StorageMode"/> selects PlainText storage (the default), where secrets
    /// live in the appsettings target file rather than the encrypted secrets file. Importing key
    /// material via the admin API is not applicable in this mode (edit appsettings directly instead).
    /// </summary>
    public bool IsPlainTextMode =>
        string.IsNullOrWhiteSpace(StorageMode)
        || StorageMode.Equals("PlainText", StringComparison.OrdinalIgnoreCase);
}
