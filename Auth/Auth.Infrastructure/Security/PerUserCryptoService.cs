using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Security;

/// <summary>
/// AES-256-GCM field encryption under a lazily-created per-user DEK. The DEK
/// is wrapped at rest by the application's Data Protection key ring (the same
/// ring that protects the JWT signing key and TOTP secrets), and the field
/// purpose plus user id are bound as AAD so ciphertexts fail closed when moved
/// between columns or rows. Payload layout: <c>v2:</c> + Base64(nonce ||
/// tag || ciphertext).
/// </summary>
public class PerUserCryptoService : IPerUserCryptoService
{
    public const string CiphertextPrefix = "v2:";

    // Versioned purpose string: rotating it would orphan every wrapped DEK,
    // so keep it stable.
    private const string DekWrapPurpose = "UserEncryptionKeys.UserDek.v1";

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    // Unwrapped DEKs are cached in process memory so list reads do not pay a
    // key lookup per row. Bounded exposure: same trust boundary as the Data
    // Protection key ring held by this process, sliding expiry keeps entries
    // short-lived, and a shredded DEK ages out with nothing left to decrypt.
    private static readonly TimeSpan DekCacheTtl = TimeSpan.FromMinutes(15);

    private readonly IUserEncryptionKeyRepository _keyRepository;
    private readonly IDataProtector _dekProtector;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PerUserCryptoService> _logger;

    public PerUserCryptoService(
        IUserEncryptionKeyRepository keyRepository,
        IDataProtectionProvider dataProtectionProvider,
        IMemoryCache cache,
        ILogger<PerUserCryptoService> logger)
    {
        _keyRepository = keyRepository;
        _dekProtector = dataProtectionProvider.CreateProtector(DekWrapPurpose);
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> EncryptAsync(Guid userId, string plaintext, string purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            throw new ArgumentException("Plaintext cannot be null or empty.", nameof(plaintext));
        }

        var dek = await GetOrCreateDekAsync(userId, cancellationToken);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(dek, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, Aad(userId, purpose));

        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        ciphertext.CopyTo(payload, NonceSize + TagSize);

        return CiphertextPrefix + Convert.ToBase64String(payload);
    }

    /// <inheritdoc />
    public async Task<string> DecryptAsync(Guid userId, string ciphertext, string purpose, CancellationToken cancellationToken)
    {
        if (!IsEncrypted(ciphertext))
        {
            throw new InvalidOperationException(
                "Value is not a per-user ciphertext. Dual-read callers must check IsEncrypted and pass legacy values through untouched.");
        }

        var dek = await GetDekAsync(userId, cancellationToken)
            ?? throw new CryptographicException(
                $"No encryption key exists for user {userId}; the ciphertext is unrecoverable (crypto-shredded or foreign row).");

        var payload = Convert.FromBase64String(ciphertext[CiphertextPrefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Ciphertext payload is truncated.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipherBytes = payload.AsSpan(NonceSize + TagSize);
        var plaintextBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(dek, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plaintextBytes, Aad(userId, purpose));

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <inheritdoc />
    public bool IsEncrypted(string? value) =>
        value?.StartsWith(CiphertextPrefix, StringComparison.Ordinal) == true;

    private static byte[] Aad(Guid userId, string purpose) =>
        Encoding.UTF8.GetBytes(purpose + ":" + userId.ToString("D"));

    private static string CacheKey(Guid userId) => $"userdek:{userId:D}";

    private async Task<byte[]?> GetDekAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<byte[]>(CacheKey(userId), out var cached))
        {
            return cached;
        }

        var key = await _keyRepository.GetByUserIdAsync(userId, cancellationToken);
        if (key is null)
        {
            return null;
        }

        return CacheUnwrapped(userId, key);
    }

    private async Task<byte[]> GetOrCreateDekAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await GetDekAsync(userId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var candidate = UserEncryptionKey.Create(
            userId,
            _dekProtector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize))));

        try
        {
            await _keyRepository.CreateAsync(candidate, cancellationToken);
            _logger.LogInformation("Created encryption key for user {UserId}", userId);
            return CacheUnwrapped(userId, candidate);
        }
        catch (Exception)
        {
            // Benign create race: if a concurrent writer won, use its key —
            // exactly one DEK may ever exist per user. Anything else rethrows.
            var winner = await _keyRepository.GetByUserIdAsync(userId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return CacheUnwrapped(userId, winner);
        }
    }

    private byte[] CacheUnwrapped(Guid userId, UserEncryptionKey key)
    {
        var dek = Convert.FromBase64String(_dekProtector.Unprotect(key.WrappedDek));
        if (dek.Length != KeySize)
        {
            throw new CryptographicException(
                $"Unwrapped DEK for user {userId} is {dek.Length} bytes; expected {KeySize}.");
        }

        _cache.Set(CacheKey(userId), dek, new MemoryCacheEntryOptions { SlidingExpiration = DekCacheTtl });
        return dek;
    }
}
