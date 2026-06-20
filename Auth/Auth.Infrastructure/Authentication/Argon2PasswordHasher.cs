using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Argon2id password hasher implementation following OWASP recommendations.
/// Hash format: $argon2id$v=19$m={memory},t={iterations},p={parallelism}[,keyid={id}]${salt}${hash}
/// <para>
/// An optional server-side <b>pepper</b> (Argon2id <c>KnownSecret</c>) can be mixed into the hash.
/// When peppering is enabled the encoded hash carries a <c>keyid</c> identifying which pepper was
/// used; legacy hashes without a <c>keyid</c> verify with no pepper and are transparently upgraded
/// on next login via <see cref="NeedsRehash"/>. The pepper itself is never stored in the hash, so a
/// database-only breach cannot brute-force peppered hashes without also obtaining the secret store.
/// </para>
/// </summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    private const int Argon2Version = 19;
    private const string Argon2IdPrefix = "$argon2id$";

    private readonly int _memorySize;
    private readonly int _iterations;
    private readonly int _parallelism;
    private readonly int _saltSize;
    private readonly int _hashSize;

    // Pepper state. _peppers is always populated from configuration (so old peppered hashes can be
    // verified even after peppering is turned off); _pepperEnabled only gates hashing of NEW passwords.
    private readonly bool _pepperEnabled;
    private readonly int _currentPepperId;
    private readonly IReadOnlyDictionary<int, byte[]> _peppers;

    /// <summary>
    /// Primary constructor for DI - uses configuration from appsettings.
    /// </summary>
    public Argon2PasswordHasher(IOptions<PasswordSettings> settings)
    {
        var config = settings.Value;
        _memorySize = config.Argon2MemorySize;
        _iterations = config.Argon2Iterations;
        _parallelism = config.Argon2Parallelism;
        _saltSize = config.SaltSize;
        _hashSize = config.HashSize;

        var pepper = config.Pepper ?? new PepperSettings();
        _peppers = DecodePeppers(pepper.Keys);
        _currentPepperId = pepper.CurrentKeyId;
        _pepperEnabled = pepper.Enabled;

        if (_pepperEnabled && (_currentPepperId <= 0 || !_peppers.ContainsKey(_currentPepperId)))
        {
            throw new InvalidOperationException(
                "Password peppering is enabled (Password:Pepper:Enabled=true) but no pepper material is " +
                "available for the current key id. Ensure the secret store has generated a pepper " +
                "(Password:Pepper:CurrentKeyId / Password:Pepper:Keys:{id}).");
        }
    }

    /// <summary>
    /// Constructor with explicit parameters (for testing or custom configuration).
    /// Not used by DI - all parameters are required. Peppering is off in this mode.
    /// </summary>
    internal Argon2PasswordHasher(
        int memorySize,
        int iterations,
        int parallelism,
        int saltSize,
        int hashSize)
    {
        _memorySize = memorySize;
        _iterations = iterations;
        _parallelism = parallelism;
        _saltSize = saltSize;
        _hashSize = hashSize;
        _peppers = new Dictionary<int, byte[]>();
        _currentPepperId = 0;
        _pepperEnabled = false;
    }

    /// <summary>
    /// Creates a default instance with OWASP recommended settings.
    /// Use for testing only.
    /// </summary>
    public static Argon2PasswordHasher CreateDefault() => new(
        memorySize: 19456,
        iterations: 2,
        parallelism: 1,
        saltSize: 16,
        hashSize: 32);

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = GenerateSalt();

        int? keyId = null;
        byte[]? pepper = null;
        if (_pepperEnabled)
        {
            keyId = _currentPepperId;
            pepper = _peppers[_currentPepperId]; // presence guaranteed by the constructor
        }

        var hash = ComputeHash(password, salt, _memorySize, _iterations, _parallelism, pepper);

        return EncodeHash(hash, salt, _memorySize, _iterations, _parallelism, keyId);
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string storedHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(storedHash);

        if (!TryDecodeHash(storedHash, out var hash, out var salt, out var memory, out var iterations, out var parallelism, out var keyId))
        {
            return false;
        }

        byte[]? pepper = null;
        if (keyId.HasValue)
        {
            // The hash was peppered. If we no longer hold that pepper, verification cannot succeed
            // (fail closed) - this is the catastrophic "lost pepper" case the operations guide warns about.
            if (!_peppers.TryGetValue(keyId.Value, out pepper))
            {
                return false;
            }
        }

        var computedHash = ComputeHash(password, salt, memory, iterations, parallelism, pepper);

        return CryptographicOperations.FixedTimeEquals(hash, computedHash);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return true;

        if (!TryDecodeHash(storedHash, out _, out _, out var memory, out var iterations, out var parallelism, out var keyId))
        {
            return true;
        }

        if (memory != _memorySize ||
            iterations != _iterations ||
            parallelism != _parallelism)
        {
            return true;
        }

        // Pepper migration: rehash when the stored pepper key id differs from what we would use now.
        // Covers legacy (no keyid) -> peppered, pepper rotation, and peppered -> disabled.
        var storedKeyId = keyId ?? 0;
        var desiredKeyId = _pepperEnabled ? _currentPepperId : 0;
        return storedKeyId != desiredKeyId;
    }

    private byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(_saltSize);
    }

    private byte[] ComputeHash(string password, byte[] salt, int memory, int iterations, int parallelism, byte[]? pepper)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        if (pepper is { Length: > 0 })
        {
            argon2.KnownSecret = pepper;
        }

        return argon2.GetBytes(_hashSize);
    }

    private static Dictionary<int, byte[]> DecodePeppers(Dictionary<int, string>? keys)
    {
        var result = new Dictionary<int, byte[]>();
        if (keys == null)
        {
            return result;
        }

        foreach (var (id, value) in keys)
        {
            if (!string.IsNullOrEmpty(value))
            {
                result[id] = Convert.FromBase64String(value);
            }
        }

        return result;
    }

    private static string EncodeHash(byte[] hash, byte[] salt, int memory, int iterations, int parallelism, int? keyId)
    {
        var saltBase64 = Convert.ToBase64String(salt).TrimEnd('=').Replace('+', '.').Replace('/', '_');
        var hashBase64 = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '.').Replace('/', '_');

        var keyIdSegment = keyId.HasValue ? $",keyid={keyId.Value}" : string.Empty;

        return $"{Argon2IdPrefix}v={Argon2Version}$m={memory},t={iterations},p={parallelism}{keyIdSegment}${saltBase64}${hashBase64}";
    }

    private static bool TryDecodeHash(
        string encodedHash,
        out byte[] hash,
        out byte[] salt,
        out int memory,
        out int iterations,
        out int parallelism,
        out int? keyId)
    {
        hash = Array.Empty<byte>();
        salt = Array.Empty<byte>();
        memory = 0;
        iterations = 0;
        parallelism = 0;
        keyId = null;

        if (string.IsNullOrEmpty(encodedHash) || !encodedHash.StartsWith(Argon2IdPrefix))
        {
            return false;
        }

        try
        {
            // Format: $argon2id$v=19$m={memory},t={iterations},p={parallelism}[,keyid={id}]${salt}${hash}
            var parts = encodedHash.Split('$');
            if (parts.Length != 6)
            {
                return false;
            }

            // parts[0] = "" (before first $)
            // parts[1] = "argon2id"
            // parts[2] = "v=19"
            // parts[3] = "m={memory},t={iterations},p={parallelism}[,keyid={id}]"
            // parts[4] = salt (base64)
            // parts[5] = hash (base64)

            // Parse version
            if (!parts[2].StartsWith("v="))
            {
                return false;
            }

            // Parse parameters (3 required: m, t, p; plus an optional keyid)
            var paramParts = parts[3].Split(',');
            if (paramParts.Length is < 3 or > 4)
            {
                return false;
            }

            foreach (var param in paramParts)
            {
                var kv = param.Split('=');
                if (kv.Length != 2)
                {
                    return false;
                }

                switch (kv[0])
                {
                    case "m":
                        memory = int.Parse(kv[1]);
                        break;
                    case "t":
                        iterations = int.Parse(kv[1]);
                        break;
                    case "p":
                        parallelism = int.Parse(kv[1]);
                        break;
                    case "keyid":
                        keyId = int.Parse(kv[1]);
                        break;
                    default:
                        return false;
                }
            }

            // Decode salt and hash (base64 with _ instead of / and . instead of +)
            var saltBase64 = parts[4].Replace('.', '+').Replace('_', '/');
            var hashBase64 = parts[5].Replace('.', '+').Replace('_', '/');

            // Add padding if needed
            saltBase64 = PadBase64(saltBase64);
            hashBase64 = PadBase64(hashBase64);

            salt = Convert.FromBase64String(saltBase64);
            hash = Convert.FromBase64String(hashBase64);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string PadBase64(string base64)
    {
        var padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 += new string('=', 4 - padding);
        }
        return base64;
    }
}
