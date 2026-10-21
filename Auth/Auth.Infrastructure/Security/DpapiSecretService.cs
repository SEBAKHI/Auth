using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth.Application.Security.Keyring;

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
            return await ReadUnlockedAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ErrorOr<T>> UpdateAsync<T>(
        Func<SecretConfiguration, ErrorOr<T>> change,
        bool overwriteBackup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        return await UpdateCoreAsync<T>(
            secrets => change(secrets).Then(value => (Value: value, Save: true)),
            overwriteBackup,
            cancellationToken);
    }

    // The one read-modify-write of the file. The lock is held from the read to the end
    // of the write: SemaphoreSlim is not re-entrant, so the read and the write called
    // here are the unlocked halves, never the public methods that take the lock again.
    private async Task<ErrorOr<T>> UpdateCoreAsync<T>(
        Func<SecretConfiguration, ErrorOr<(T Value, bool Save)>> change,
        bool overwriteBackup,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var isNewFile = !SecretFileExists();
            var secrets = await ReadUnlockedAsync(cancellationToken);

            var changed = change(secrets);
            if (changed.IsError)
            {
                return changed.Errors;
            }

            if (changed.Value.Save)
            {
                if (isNewFile)
                {
                    secrets.CreatedAt = DateTime.UtcNow;
                }

                // Past this point the change is written whatever the caller's token
                // says: a save cancelled between its two writes would leave the backup
                // holding what the change removed.
                await WriteUnlockedAsync(secrets, CancellationToken.None);
                if (overwriteBackup)
                {
                    await WriteUnlockedAsync(secrets, CancellationToken.None);
                }
            }

            return changed.Value.Value;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<SecretConfiguration> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Replaces the whole file with <paramref name="secrets"/>. Not part of
    /// <see cref="IDpapiSecretService"/>: a load followed by this save is two lock
    /// acquisitions, and a change made between them is lost. Every read-modify-write
    /// goes through <see cref="UpdateAsync{T}"/>.
    /// </summary>
    public async Task SaveSecretsAsync(SecretConfiguration secrets, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await WriteUnlockedAsync(secrets, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task WriteUnlockedAsync(SecretConfiguration secrets, CancellationToken cancellationToken)
    {
        secrets.ModifiedAt = DateTime.UtcNow;
        secrets.MachineName = Environment.MachineName;
        MirrorHmacScalar(secrets);
        MarkKeyringBeyondKey1(secrets);

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
        await UpdateAsync<Success>(
            secrets => SetSecretValue(secrets, key, value)
                ? Result.Success
                : throw new ArgumentException($"Unknown secret key: {key}", nameof(key)),
            overwriteBackup: false,
            cancellationToken);
        _logger.LogInformation("Secret {Key} updated", key);
    }

    public async Task<bool> RemoveSecretAsync(string key, CancellationToken cancellationToken)
    {
        // Only allow removing custom secrets
        if (!key.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var customKey = key.Substring(7);
        var removed = await UpdateCoreAsync<bool>(
            secrets =>
            {
                var found = secrets.Custom.Remove(customKey);
                return (found, found);
            },
            overwriteBackup: false,
            cancellationToken);

        if (removed.Value)
        {
            _logger.LogInformation("Custom secret {Key} removed", key);
        }

        return removed.Value;
    }

    // ═══════════════════════════════════════════════════════════════
    // Key Generation Operations
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> GenerateRsaKeyPairAsync(CancellationToken cancellationToken)
    {
        var (privateKeyPem, publicKeyPem) = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateRsaKeyPair();

        await UpdateAsync<Success>(
            secrets =>
            {
                secrets.JwtPrivateKeyPem = privateKeyPem;
                secrets.JwtPublicKeyPem = publicKeyPem;
                return Result.Success;
            },
            overwriteBackup: false,
            cancellationToken);

        _logger.LogWarning("Generated new RSA-{KeySize} key pair - all existing access tokens are now invalid", RsaKeySizeBits);
        return publicKeyPem;
    }

    public async Task<string> GenerateGatewayTokenAsync(CancellationToken cancellationToken)
    {
        var token = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateGatewayToken();

        await UpdateAsync<Success>(
            secrets =>
            {
                secrets.GatewayToken = token;
                return Result.Success;
            },
            overwriteBackup: false,
            cancellationToken);

        _logger.LogWarning("Generated new gateway token - API Gateway configuration must be updated");
        return token;
    }

    public async Task<KeyGenerationResult> GenerateMissingKeysAsync(CancellationToken cancellationToken)
    {
        var generated = await UpdateCoreAsync<KeyGenerationResult>(
            secrets =>
            {
                var result = new KeyGenerationResult();
                return (result, GenerateMissingKeys(secrets, result));
            },
            overwriteBackup: false,
            cancellationToken);

        return generated.Value;
    }

    // Fills in every missing key of a loaded file and answers whether anything changed.
    private bool GenerateMissingKeys(SecretConfiguration secrets, KeyGenerationResult result)
    {
        var modified = false;

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
            var hmacKey = Auth.Shared.Configuration.KeyMaterialGenerator.GenerateHmacKeyBase64();
            secrets.RefreshTokenHmacKey = hmacKey;

            // A new file starts with its keyring written out: key 1, current. An
            // existing file is never given one here: an empty ring already means the
            // single key is key 1, and writing it on boot would be a write nobody asked for.
            if (secrets.HmacKeys is not { Count: > 0 })
            {
                secrets.HmacKeys = new Dictionary<int, HmacKeyEntry>();
                secrets.HmacKeys[HmacKeyring.LegacyKeyId] = new HmacKeyEntry
                {
                    Material = hmacKey,
                    CreatedAtUtc = DateTime.UtcNow
                };
                secrets.HmacCurrentKeyId = HmacKeyring.LegacyKeyId;
            }

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
        // Its absence here broke registration wherever this generator ran — the
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

        return modified;
    }

    // ═══════════════════════════════════════════════════════════════
    // Key Import Operations (bring-your-own-keys)
    // ═══════════════════════════════════════════════════════════════

    public async Task ImportRsaKeyPairAsync(string privateKeyPem, string publicKeyPem, CancellationToken cancellationToken)
    {
        await UpdateAsync<Success>(
            secrets =>
            {
                secrets.JwtPrivateKeyPem = privateKeyPem;
                secrets.JwtPublicKeyPem = publicKeyPem;
                return Result.Success;
            },
            overwriteBackup: false,
            cancellationToken);

        _logger.LogWarning("Imported caller-supplied RSA signing key - all existing access tokens are now invalid");
    }

    public async Task ImportGatewayTokenAsync(string token, CancellationToken cancellationToken)
    {
        await UpdateAsync<Success>(
            secrets =>
            {
                secrets.GatewayToken = token;
                return Result.Success;
            },
            overwriteBackup: false,
            cancellationToken);

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

    /// <summary>
    /// The single key mirrors the keyring's current key on every save (KR1), so a
    /// build without the keyring, which reads only the single key, keeps issuing and
    /// verifying what this one does.
    /// </summary>
    private static void MirrorHmacScalar(SecretConfiguration secrets)
    {
        // A hand-edited file may hold "hmacKeys": null or a null entry: neither may stop
        // an unrelated save (S11 SEC-3), and neither names a key to mirror.
        if (secrets.HmacKeys?.GetValueOrDefault(secrets.HmacCurrentKeyId) is { } current &&
            !string.IsNullOrEmpty(current.Material))
        {
            secrets.RefreshTokenHmacKey = current.Material;
        }
    }

    /// <summary>
    /// Raises the file's version once its keyring holds anything beyond key 1. A
    /// build without the keyring keeps the version and erases the ring, so the next
    /// start can tell an erased ring from one that never existed (KR1, P4-R1).
    /// </summary>
    private static void MarkKeyringBeyondKey1(SecretConfiguration secrets)
    {
        var beyondKey1 = (secrets.HmacKeys?.Keys.Any(id => id != HmacKeyring.LegacyKeyId) ?? false)
            || secrets.HmacCurrentKeyId > HmacKeyring.LegacyKeyId
            || secrets.HmacNextKeyId > 0
            || secrets.HmacLegacyCutoverUtc is not null;

        if (beyondKey1 && secrets.Version < Auth.Shared.Configuration.SecretFileVersions.HmacKeyringBeyondKey1)
        {
            secrets.Version = Auth.Shared.Configuration.SecretFileVersions.HmacKeyringBeyondKey1;
        }
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
