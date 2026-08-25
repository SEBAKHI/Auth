using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// DPAPI-based secret management service implementation.
/// Provides centralized storage for all application secrets encrypted with Windows DPAPI.
/// </summary>
public class DpapiSecretService : IDpapiSecretService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly SecretManagementSettings _settings;
    private readonly ILogger<DpapiSecretService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private const string ProtectorPurpose = "AuthSystem.SecretManagement.v1";
    private const int RsaKeySizeBits = 2048;
    private const int HmacKeySizeBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DpapiSecretService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<SecretManagementSettings> settings,
        ILogger<DpapiSecretService> logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // File Operations
    // ═══════════════════════════════════════════════════════════════

    public bool SecretFileExists() => File.Exists(_settings.SecretFilePath);

    public string GetSecretFilePath() => _settings.SecretFilePath;

    public async Task<SecretConfiguration> LoadSecretsAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!SecretFileExists())
            {
                _logger.LogDebug("Secret file does not exist at {Path}, returning empty configuration", _settings.SecretFilePath);
                return new SecretConfiguration();
            }

            var encryptedData = await File.ReadAllBytesAsync(_settings.SecretFilePath, cancellationToken);

            var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);
            var decryptedBytes = protector.Unprotect(encryptedData);
            var json = Encoding.UTF8.GetString(decryptedBytes);

            var secrets = JsonSerializer.Deserialize<SecretConfiguration>(json, JsonOptions)
                ?? new SecretConfiguration();

            _logger.LogDebug(
                "Loaded secrets from {Path}, version {Version}, last modified {Modified}",
                _settings.SecretFilePath,
                secrets.Version,
                secrets.ModifiedAt);

            return secrets;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to decrypt secret file at {Path} - may have been encrypted on a different machine", _settings.SecretFilePath);
            throw new SecretDecryptionException(
                $"Failed to decrypt secrets at '{_settings.SecretFilePath}'. The file may have been encrypted on a different machine or the DPAPI keys may have changed.",
                ex);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Replaces a file's contents in one step, so a reader never sees a partial
    /// write and a crash never leaves one behind.
    /// </summary>
    /// <remarks>
    /// This file holds the JWT signing key, the refresh-token HMAC key, the
    /// gateway token, the connection string and the Argon2id pepper, and there is
    /// no second copy of any of them. Writing over it in place put all of that
    /// inside the window between truncation and the last byte: a recycle, a full
    /// disk or a power loss in that window left a file that decrypts to nothing,
    /// and the process cannot start without it.
    ///
    /// The rename is the atomic part — on NTFS a replace within one volume either
    /// happened or did not. The flush before it is what makes that guarantee mean
    /// something: without it the rename can be durable while the bytes it points
    /// at are still in the cache.
    ///
    /// The old file is kept as .bak rather than discarded. It is one version
    /// behind and useless for the current key, but on a host where the only copy
    /// of a signing key just failed to decrypt, one version behind is the
    /// difference between re-issuing every token and re-provisioning the system.
    /// </remarks>
    private static async Task WriteAtomicallyAsync(
        string path, byte[] contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        var temporaryPath = Path.Combine(
            string.IsNullOrEmpty(directory) ? "." : directory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(contents, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                // Flush(true) pushes past the OS cache to the device. The rename
                // below is only as safe as the bytes it publishes.
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, $"{path}.bak", ignoreMetadataErrors: true);
            }
            else
            {
                // First write on this host: Replace requires an existing target.
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            // A temp file left by a failed write is not a secret anyone should
            // find lying around, and it would collide with nothing anyway.
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Losing the cleanup must not lose the error that caused it.
                }
            }
        }
    }

    public async Task SaveSecretsAsync(SecretConfiguration secrets, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            secrets.ModifiedAt = DateTime.UtcNow;
            secrets.MachineName = Environment.MachineName;

            var json = JsonSerializer.Serialize(secrets, JsonOptions);

            var protector = _dataProtectionProvider.CreateProtector(ProtectorPurpose);
            var encryptedData = protector.Protect(Encoding.UTF8.GetBytes(json));

            // Ensure directory exists
            var directory = Path.GetDirectoryName(_settings.SecretFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created secrets directory: {Directory}", directory);
            }

            await WriteAtomicallyAsync(_settings.SecretFilePath, encryptedData, cancellationToken);

            _logger.LogInformation(
                "Saved secrets to {Path}, version {Version}",
                _settings.SecretFilePath,
                secrets.Version);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Individual Secret Operations
    // ═══════════════════════════════════════════════════════════════

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken)
    {
        var secrets = await LoadSecretsAsync(cancellationToken);
        return GetSecretValue(secrets, key);
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken)
    {
        var secrets = await LoadSecretsAsync(cancellationToken);

        if (!SetSecretValue(secrets, key, value))
        {
            throw new ArgumentException($"Unknown secret key: {key}", nameof(key));
        }

        await SaveSecretsAsync(secrets, cancellationToken);
        _logger.LogInformation("Secret {Key} updated", key);
    }

    public async Task<bool> RemoveSecretAsync(string key, CancellationToken cancellationToken)
    {
        var secrets = await LoadSecretsAsync(cancellationToken);

        // Only allow removing custom secrets
        if (key.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase))
        {
            var customKey = key.Substring(7);
            if (secrets.Custom.Remove(customKey))
            {
                await SaveSecretsAsync(secrets, cancellationToken);
                _logger.LogInformation("Custom secret {Key} removed", key);
                return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // Key Generation Operations
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> GenerateRsaKeyPairAsync(CancellationToken cancellationToken)
    {
        var (privateKeyPem, publicKeyPem) = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateRsaKeyPair();

        var secrets = await LoadSecretsAsync(cancellationToken);
        secrets.JwtPrivateKeyPem = privateKeyPem;
        secrets.JwtPublicKeyPem = publicKeyPem;

        if (!SecretFileExists())
        {
            secrets.CreatedAt = DateTime.UtcNow;
        }

        await SaveSecretsAsync(secrets, cancellationToken);

        _logger.LogWarning("Generated new RSA-{KeySize} key pair - all existing access tokens are now invalid", RsaKeySizeBits);
        return publicKeyPem;
    }

    public async Task GenerateHmacKeyAsync(CancellationToken cancellationToken)
    {
        var keyBase64 = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();

        var secrets = await LoadSecretsAsync(cancellationToken);
        secrets.RefreshTokenHmacKey = keyBase64;

        if (!SecretFileExists())
        {
            secrets.CreatedAt = DateTime.UtcNow;
        }

        await SaveSecretsAsync(secrets, cancellationToken);

        _logger.LogWarning("Generated new HMAC-SHA256 key ({Bytes} bytes) - all existing refresh tokens are now invalid", HmacKeySizeBytes);
    }

    public async Task<string> GenerateGatewayTokenAsync(CancellationToken cancellationToken)
    {
        var token = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateGatewayToken();

        var secrets = await LoadSecretsAsync(cancellationToken);
        secrets.GatewayToken = token;

        if (!SecretFileExists())
        {
            secrets.CreatedAt = DateTime.UtcNow;
        }

        await SaveSecretsAsync(secrets, cancellationToken);

        _logger.LogWarning("Generated new gateway token - API Gateway configuration must be updated");
        return token;
    }

    public async Task<KeyGenerationResult> GenerateMissingKeysAsync(CancellationToken cancellationToken)
    {
        var result = new KeyGenerationResult();
        var secrets = await LoadSecretsAsync(cancellationToken);
        var modified = false;
        var isNewFile = !SecretFileExists();

        // Generate RSA key pair if missing
        if (string.IsNullOrEmpty(secrets.JwtPrivateKeyPem))
        {
            var (privateKeyPem, publicKeyPem) = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateRsaKeyPair();
            secrets.JwtPrivateKeyPem = privateKeyPem;
            secrets.JwtPublicKeyPem = publicKeyPem;
            result.RsaKeyGenerated = true;
            result.PublicKeyPem = secrets.JwtPublicKeyPem;
            result.GeneratedKeys.Add("JwtPrivateKeyPem");
            result.GeneratedKeys.Add("JwtPublicKeyPem");
            modified = true;
            _logger.LogInformation("Auto-generated RSA-{KeySize} key pair", RsaKeySizeBits);
        }
        else
        {
            result.SkippedKeys.Add("JwtPrivateKeyPem (already exists)");
        }

        // Generate HMAC key if missing
        if (string.IsNullOrEmpty(secrets.RefreshTokenHmacKey))
        {
            secrets.RefreshTokenHmacKey = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();
            result.HmacKeyGenerated = true;
            result.GeneratedKeys.Add("RefreshTokenHmacKey");
            modified = true;
            _logger.LogInformation("Auto-generated HMAC-SHA256 key ({Bytes} bytes)", HmacKeySizeBytes);
        }
        else
        {
            result.SkippedKeys.Add("RefreshTokenHmacKey (already exists)");
        }

        // Generate gateway token if missing
        if (string.IsNullOrEmpty(secrets.GatewayToken))
        {
            secrets.GatewayToken = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateGatewayToken();
            result.GatewayTokenGenerated = true;
            result.GeneratedKeys.Add("GatewayToken");
            modified = true;
            _logger.LogInformation("Auto-generated gateway token");
        }
        else
        {
            result.SkippedKeys.Add("GatewayToken (already exists)");
        }

        // Generate the account-deletion identifier HMAC key if missing.
        // PERMANENT once generated: the deletion registry's stored digests are
        // derived from it, so a second key silently orphans every reservation.
        // Its absence here is what broke registration in production — the
        // PlainText generator had it, this one never did, so Development was
        // green while Certificate/Dpapi deployments had no key at all.
        // RequiredSecretsRegistry is the shared declaration both generators are
        // tested against; add new secrets there first.
        if (string.IsNullOrEmpty(secrets.AccountDeletionIdentifierHmacKey))
        {
            secrets.AccountDeletionIdentifierHmacKey =
                Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();
            result.GeneratedKeys.Add(nameof(secrets.AccountDeletionIdentifierHmacKey));
            modified = true;
            _logger.LogWarning(
                "Auto-generated the account-deletion identifier HMAC key. This key is PERMANENT: " +
                "back it up with the secrets file and never rotate it — identifier reservations and " +
                "restore re-application are derived from it.");
        }
        else
        {
            result.SkippedKeys.Add("AccountDeletionIdentifierHmacKey (already exists)");
        }

        if (modified)
        {
            if (isNewFile)
            {
                secrets.CreatedAt = DateTime.UtcNow;
            }
            await SaveSecretsAsync(secrets, cancellationToken);
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // Key Import Operations (bring-your-own-keys)
    // ═══════════════════════════════════════════════════════════════

    public async Task ImportRsaKeyPairAsync(string privateKeyPem, string publicKeyPem, CancellationToken cancellationToken)
    {
        var secrets = await LoadSecretsAsync(cancellationToken);
        secrets.JwtPrivateKeyPem = privateKeyPem;
        secrets.JwtPublicKeyPem = publicKeyPem;

        if (!SecretFileExists())
        {
            secrets.CreatedAt = DateTime.UtcNow;
        }

        await SaveSecretsAsync(secrets, cancellationToken);

        _logger.LogWarning("Imported caller-supplied RSA signing key - all existing access tokens are now invalid");
    }

    public async Task ImportHmacKeyAsync(string hmacKeyBase64, CancellationToken cancellationToken)
    {
        var secrets = await LoadSecretsAsync(cancellationToken);
        secrets.RefreshTokenHmacKey = hmacKeyBase64;

        if (!SecretFileExists())
        {
            secrets.CreatedAt = DateTime.UtcNow;
        }

        await SaveSecretsAsync(secrets, cancellationToken);

        _logger.LogWarning("Imported caller-supplied HMAC key - all existing refresh tokens are now invalid");
    }

    public async Task ImportGatewayTokenAsync(string token, CancellationToken cancellationToken)
    {
        var secrets = await LoadSecretsAsync(cancellationToken);
        secrets.GatewayToken = token;

        if (!SecretFileExists())
        {
            secrets.CreatedAt = DateTime.UtcNow;
        }

        await SaveSecretsAsync(secrets, cancellationToken);

        _logger.LogWarning("Imported caller-supplied gateway token - API Gateway configuration must be updated to match");
    }

    // ═══════════════════════════════════════════════════════════════
    // Status Operations
    // ═══════════════════════════════════════════════════════════════

    public async Task<SecretStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        var result = new SecretStatusResult
        {
            SecretFileExists = SecretFileExists(),
            SecretFilePath = _settings.SecretFilePath
        };

        if (!result.SecretFileExists)
        {
            result.Secrets = new Dictionary<string, SecretStatus>
            {
                ["JwtPrivateKeyPem"] = SecretStatus.NotConfigured,
                ["JwtPublicKeyPem"] = SecretStatus.NotConfigured,
                ["RefreshTokenHmacKey"] = SecretStatus.NotConfigured,
                ["SmtpPassword"] = SecretStatus.NotConfigured,
                ["GatewayToken"] = SecretStatus.NotConfigured,
                ["AccountDeletionIdentifierHmacKey"] = SecretStatus.NotConfigured,
                ["ConnectionStrings.AuthDb"] = SecretStatus.NotConfigured
            };
            return result;
        }

        var secrets = await LoadSecretsAsync(cancellationToken);
        result.LastModified = secrets.ModifiedAt;
        result.MachineName = secrets.MachineName;
        result.SchemaVersion = secrets.Version;

        result.Secrets = new Dictionary<string, SecretStatus>
        {
            ["JwtPrivateKeyPem"] = GetStatus(secrets.JwtPrivateKeyPem),
            ["JwtPublicKeyPem"] = GetStatus(secrets.JwtPublicKeyPem),
            ["RefreshTokenHmacKey"] = GetStatus(secrets.RefreshTokenHmacKey),
            ["SmtpPassword"] = GetStatus(secrets.SmtpPassword),
            ["GatewayToken"] = GetStatus(secrets.GatewayToken),
            ["AccountDeletionIdentifierHmacKey"] = GetStatus(secrets.AccountDeletionIdentifierHmacKey),
            ["PasswordPepper"] = secrets.PasswordPeppers.Count > 0 ? SecretStatus.Configured : SecretStatus.NotConfigured,
            ["ConnectionStrings.AuthDb"] = GetStatus(secrets.ConnectionStrings?.AuthDb)
        };

        // Add custom secrets
        foreach (var key in secrets.Custom.Keys)
        {
            result.Secrets[$"Custom:{key}"] = GetStatus(secrets.Custom[key]);
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // Private Helper Methods
    // ═══════════════════════════════════════════════════════════════

    private static SecretStatus GetStatus(string? value) =>
        string.IsNullOrEmpty(value) ? SecretStatus.NotConfigured :
        string.IsNullOrWhiteSpace(value) ? SecretStatus.Empty :
        SecretStatus.Configured;

    private static string? GetSecretValue(SecretConfiguration secrets, string key)
    {
        return key.ToLowerInvariant() switch
        {
            "jwtprivatekeypem" => secrets.JwtPrivateKeyPem,
            "jwtpublickeypem" => secrets.JwtPublicKeyPem,
            "refreshtokenhmackey" => secrets.RefreshTokenHmacKey,
            "smtppassword" => secrets.SmtpPassword,
            "gatewaytoken" => secrets.GatewayToken,
            // Readable so the one PERMANENT key in the system can actually be
            // backed up. It cannot be regenerated: losing it orphans every
            // identifier reservation, and nothing fails loudly when that
            // happens. A key an operator cannot export is a key they will lose.
            "accountdeletionidentifierhmackey" => secrets.AccountDeletionIdentifierHmacKey,
            "connectionstrings.authdb" => secrets.ConnectionStrings?.AuthDb,
            _ when key.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) =>
                secrets.Custom.TryGetValue(key.Substring(7), out var value) ? value : null,
            _ => null
        };
    }

    private static bool SetSecretValue(SecretConfiguration secrets, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "jwtprivatekeypem":
                secrets.JwtPrivateKeyPem = value;
                return true;
            case "jwtpublickeypem":
                secrets.JwtPublicKeyPem = value;
                return true;
            case "refreshtokenhmackey":
                secrets.RefreshTokenHmacKey = value;
                return true;
            case "smtppassword":
                secrets.SmtpPassword = value;
                return true;
            case "gatewaytoken":
                secrets.GatewayToken = value;
                return true;
            // Settable for ONE legitimate reason: restoring the key from a
            // backup after a secrets-file loss. Setting a DIFFERENT value while
            // the deletion registry holds rows silently orphans every
            // reservation — deleted addresses become registrable again with
            // nothing failing anywhere. IdentifierKeyRegenerationGuard catches
            // the accidental case at startup; this path is a deliberate
            // operator action and is audited by the controller above it.
            case "accountdeletionidentifierhmackey":
                secrets.AccountDeletionIdentifierHmacKey = value;
                return true;
            case "connectionstrings.authdb":
                secrets.ConnectionStrings.AuthDb = value;
                return true;
            default:
                if (key.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
                {
                    secrets.Custom[key.Substring(7)] = value;
                    return true;
                }
                return false;
        }
    }

}
