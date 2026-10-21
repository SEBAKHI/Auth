using System.Collections.Frozen;
using System.Globalization;
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
    private readonly IReadOnlySet<string>? _onlyKeys;

    public DpapiSecretConfigurationSource(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider,
        IReadOnlySet<string>? onlyKeys = null)
    {
        _secretFilePath = secretFilePath;
        _dataProtectionProvider = dataProtectionProvider;
        _onlyKeys = onlyKeys;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new DpapiSecretConfigurationProvider(_secretFilePath, _dataProtectionProvider, _onlyKeys);
    }
}

/// <summary>
/// Allow-lists of the configuration keys a process takes from the secrets file.
/// The file is decrypted whole, but a process that needs one secret keeps only
/// that one, so the rest never sit in its configuration.
/// </summary>
public static class DpapiSecretFilters
{
    /// <summary>
    /// The API Gateway's: the gateway token, and nothing else. In particular not the
    /// HMAC keyring (S11) or the signing key, which the gateway never uses.
    /// </summary>
    public static readonly IReadOnlySet<string> GatewayTokenOnly =
        new[] { "Gateway:Token" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Configuration provider that loads secrets from DPAPI file and maps them to configuration keys.
/// This enables DPAPI secrets to override values from appsettings.json.
/// </summary>
public class DpapiSecretConfigurationProvider : ConfigurationProvider
{
    private readonly string _secretFilePath;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IReadOnlySet<string>? _onlyKeys;
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

    /// <param name="secretFilePath">The encrypted secrets file.</param>
    /// <param name="dataProtectionProvider">Decrypts it.</param>
    /// <param name="onlyKeys">
    /// When set, the only configuration keys this provider exposes (for example
    /// <see cref="DpapiSecretFilters.GatewayTokenOnly"/>); null exposes every mapped secret.
    /// </param>
    public DpapiSecretConfigurationProvider(
        string secretFilePath,
        IDataProtectionProvider dataProtectionProvider,
        IReadOnlySet<string>? onlyKeys = null)
    {
        _secretFilePath = secretFilePath;
        _dataProtectionProvider = dataProtectionProvider;
        _onlyKeys = onlyKeys;
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
                KeepOnlyAllowedKeys();
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

        // The raw HMAC key (not DPAPI encrypted): the HMAC keyring's current key.
        if (!string.IsNullOrEmpty(secrets.RefreshTokenHmacKey))
        {
            Data["Jwt:RefreshTokenHmacKeyPlain"] = secrets.RefreshTokenHmacKey;
        }

        MapHmacKeyring(secrets);

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

    /// <summary>
    /// The HMAC keyring (S11) -> Keyring:Hmac:*. Every key's material is mapped, the
    /// current one included, so the startup step can refuse a file whose single key
    /// (Jwt:RefreshTokenHmacKeyPlain) no longer matches the ring's current key.
    /// Nothing is mapped for an empty ring: the single key is then key 1.
    /// </summary>
    private void MapHmacKeyring(SecretConfiguration secrets)
    {
        // Mapped even without a ring: it is how the startup step learns that a ring
        // existed and was erased by a save from a build without it.
        if (secrets.Version >= SecretFileVersions.HmacKeyringBeyondKey1)
        {
            Data["Keyring:Hmac:FileVersion"] = secrets.Version.ToString(CultureInfo.InvariantCulture);
        }

        if (secrets.HmacKeys is not { Count: > 0 })
        {
            return;
        }

        foreach (var (id, entry) in secrets.HmacKeys)
        {
            if (entry is null)
            {
                // A hand-edited null entry: left unmapped, so the startup step reports
                // the keyring as incomplete instead of the provider failing silently.
                continue;
            }

            var prefix = "Keyring:Hmac:Keys:" + id.ToString(CultureInfo.InvariantCulture) + ":";
            if (!string.IsNullOrEmpty(entry.Material))
            {
                Data[prefix + "Material"] = entry.Material;
            }

            SetDate(prefix + "CreatedAtUtc", entry.CreatedAtUtc);
            SetDate(prefix + "VerifyUntilUtc", entry.VerifyUntilUtc);
            SetDate(prefix + "DemotedAtUtc", entry.DemotedAtUtc);
            if (entry.Compromised)
            {
                Data[prefix + "Compromised"] = "true";
            }
        }

        if (secrets.HmacCurrentKeyId > 0)
        {
            Data["Keyring:Hmac:CurrentKeyId"] = secrets.HmacCurrentKeyId.ToString(CultureInfo.InvariantCulture);
        }

        if (secrets.HmacNextKeyId > 0)
        {
            Data["Keyring:Hmac:NextKeyId"] = secrets.HmacNextKeyId.ToString(CultureInfo.InvariantCulture);
        }

        SetDate("Keyring:Hmac:LegacyCutoverUtc", secrets.HmacLegacyCutoverUtc);
        SetDate("Keyring:Hmac:LegacyVerifyUntilUtc", secrets.HmacLegacyVerifyUntilUtc);
    }

    private void SetDate(string key, DateTime? value)
    {
        if (value is { } date)
        {
            // A value written with an offset deserializes as local time: convert it,
            // never relabel it, or the instant moves by the server's offset.
            var utc = date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);
            Data[key] = utc.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private void KeepOnlyAllowedKeys()
    {
        if (_onlyKeys is null)
        {
            return;
        }

        foreach (var key in Data.Keys.Where(key => !_onlyKeys.Contains(key)).ToList())
        {
            Data.Remove(key);
        }
    }

    private static bool ShouldIgnoreStoredConnectionString() =>
        string.Equals(
            Environment.GetEnvironmentVariable(IgnoreConnectionStringVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
}
