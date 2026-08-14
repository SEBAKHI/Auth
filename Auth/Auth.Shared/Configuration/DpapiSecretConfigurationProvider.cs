using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace Auth.Shared.Configuration;

/// <summary>
/// Configuration source that loads secrets from DPAPI-encrypted file.
/// </summary>
public class DpapiSecretConfigurationSource : IConfigurationSource
{
    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public DpapiSecretConfigurationSource(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider)
    {
        _secretFilePath = secretFilePath;
        _dataProtectionProvider = dataProtectionProvider;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DpapiSecretConfigurationProvider(_secretFilePath, _dataProtectionProvider);
    }
}

/// <summary>
/// Configuration provider that loads secrets from DPAPI file and maps them to configuration keys.
/// This enables DPAPI secrets to override values from appsettings.json.
/// </summary>
public class DpapiSecretConfigurationProvider : ConfigurationProvider
{
    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private const string ProtectorPurpose = "AuthSystem.SecretManagement.v1";

    /// <summary>
    /// Escape hatch: when set to "true", the stored connection string is not
    /// layered onto configuration, so the value from web.config / appsettings
    /// wins again.
    /// </summary>
    /// <remarks>
    /// A stored connection string that stops working — the database host renamed,
    /// the password rotated at the server, the site moved — leaves the API unable
    /// to start, and therefore unable to serve the admin console that would fix
    /// it. Without a way to bypass the stored value the only remedy is editing an
    /// encrypted file by hand. Mirrors AUTH_DISABLE_DB_SETTINGS, which exists so a
    /// bad database-backed override can always be bypassed.
    /// </remarks>
    public const string IgnoreConnectionStringVariable = "AUTH_IGNORE_SECRET_CONNECTIONSTRING";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public DpapiSecretConfigurationProvider(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider)
    {
        _secretFilePath = secretFilePath;
        _dataProtectionProvider = dataProtectionProvider;
    }

    /// <summary>
    /// The failure from the last <see cref="Load"/>, or null when the file loaded
    /// (or is simply absent). Lets startup report why the secrets are missing
    /// instead of leaving the operator to infer it from a downstream symptom.
    /// </summary>
    public Exception? LoadError { get; private set; }

    public override void Load()
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        LoadError = null;

        if (!File.Exists(_secretFilePath))
        {
            return;
        }

        try
        {
            var encryptedData = File.ReadAllBytes(_secretFilePath);
            var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);
            var decryptedBytes = protector.Unprotect(encryptedData);
            var json = Encoding.UTF8.GetString(decryptedBytes);

            var secrets = JsonSerializer.Deserialize<SecretConfiguration>(json, JsonOptions);

            if (secrets != null)
            {
                MapSecretsToConfiguration(secrets);
            }
        }
        catch (CryptographicException ex)
        {
            // Deliberately non-fatal here: RequiredSecretsGuard fails the boot a
            // few steps later with the full list of what is missing, which is a
            // better message than an exception from inside a configuration
            // provider. What this must NOT do is stay quiet — the file also
            // carries the connection string and the SMTP password, and a silent
            // fall-through leaves those reverting to the inert "{Key}__{Name}"
            // placeholders in appsettings, which surfaces as an unrelated-looking
            // SQL parse error rather than "the secrets file could not be read".
            LoadError = ex;
            Console.Error.WriteLine(
                $"ERROR: Could not DECRYPT the secrets file at '{_secretFilePath}'. Every secret it holds — " +
                "signing keys, the database connection string, the SMTP password — is unavailable, and " +
                "configuration values are being used instead. Usual cause: the file was encrypted on another " +
                "machine, or the Data Protection certificate / key ring is missing on this one. " +
                $"Details: {ex.Message}");
        }
        catch (JsonException ex)
        {
            LoadError = ex;
            Console.Error.WriteLine(
                $"ERROR: Could not PARSE the secrets file at '{_secretFilePath}'. It decrypted but its contents " +
                $"are not valid JSON, which usually means a truncated write. Details: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps secrets from the DPAPI file to configuration keys that override appsettings.json.
    /// </summary>
    private void MapSecretsToConfiguration(SecretConfiguration secrets)
    {
        // JWT Settings - Store decrypted PEM directly for JwtTokenService
        if (!string.IsNullOrEmpty(secrets.JwtPrivateKeyPem))
        {
            Data["Jwt:PrivateKeyPem"] = secrets.JwtPrivateKeyPem;
        }

        // Store the raw HMAC key (not DPAPI encrypted) for RefreshTokenKeyService
        // This uses a NEW key that takes priority over the legacy encrypted key
        if (!string.IsNullOrEmpty(secrets.RefreshTokenHmacKey))
        {
            Data["Jwt:RefreshTokenHmacKeyPlain"] = secrets.RefreshTokenHmacKey;
        }

        // Account-deletion identifier HMAC key (permanent; never rotated) for IdentifierHasher
        if (!string.IsNullOrEmpty(secrets.AccountDeletionIdentifierHmacKey))
        {
            Data["AccountDeletion:IdentifierHmacKeyPlain"] = secrets.AccountDeletionIdentifierHmacKey;
        }

        // Apple .p8 signing key for the Sign in with Apple client secrets
        if (!string.IsNullOrEmpty(secrets.AppleSigningKeyPem))
        {
            Data["ExternalAuth:Apple:PrivateKeyPem"] = secrets.AppleSigningKeyPem;
        }

        // Email settings - SMTP password
        if (!string.IsNullOrEmpty(secrets.SmtpPassword))
        {
            Data["Email:Password"] = secrets.SmtpPassword;
        }

        // Gateway settings - Token for inter-service authentication
        // Maps to both Auth API (ExpectedToken) and API Gateway (Token) keys
        if (!string.IsNullOrEmpty(secrets.GatewayToken))
        {
            Data["Gateway:ExpectedToken"] = secrets.GatewayToken;  // Auth API expects this
            Data["Gateway:Token"] = secrets.GatewayToken;          // API Gateway sends this
        }

        // Connection strings (when SQL authentication is used)
        if (!string.IsNullOrEmpty(secrets.ConnectionStrings?.AuthDb))
        {
            if (ShouldIgnoreStoredConnectionString())
            {
                Console.WriteLine(
                    $"Warning: {IgnoreConnectionStringVariable}=true - the connection string in the secrets " +
                    "file is being IGNORED. The value from configuration/environment is in effect. Remove " +
                    "this variable once the stored value has been corrected.");
            }
            else
            {
                Data["ConnectionStrings:AuthDb"] = secrets.ConnectionStrings.AuthDb;
            }
        }

        // Argon2id password pepper(s) -> Password:Pepper:Keys:{id} (+ current key id).
        // The Password:Pepper:Enabled toggle stays in appsettings; only the key material is secret-managed.
        if (secrets.PasswordPeppers is { Count: > 0 })
        {
            foreach (var (id, value) in secrets.PasswordPeppers)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    Data[$"Password:Pepper:Keys:{id}"] = value;
                }
            }

            if (secrets.PasswordPepperCurrentKeyId > 0)
            {
                Data["Password:Pepper:CurrentKeyId"] = secrets.PasswordPepperCurrentKeyId.ToString();
            }
        }

        // Custom secrets under Secrets:Custom:* namespace
        foreach (var (key, value) in secrets.Custom)
        {
            Data[$"Secrets:Custom:{key}"] = value;
        }
    }

    private static bool ShouldIgnoreStoredConnectionString() =>
        string.Equals(
            Environment.GetEnvironmentVariable(IgnoreConnectionStringVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
