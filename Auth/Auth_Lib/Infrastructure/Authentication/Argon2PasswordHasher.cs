using System.Security.Cryptography;
using System.Text;
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Infrastructure.Configuration;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Auth_Lib.Infrastructure.Authentication;

/// <summary>
/// Argon2id password hasher implementation following OWASP recommendations.
/// Hash format: $argon2id$v=19$m={memory},t={iterations},p={parallelism}${salt}${hash}
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
    }

    /// <summary>
    /// Constructor with explicit parameters (for testing or custom configuration).
    /// Not used by DI - all parameters are required.
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
        var hash = ComputeHash(password, salt, _memorySize, _iterations, _parallelism);

        return EncodeHash(hash, salt, _memorySize, _iterations, _parallelism);
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string storedHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(storedHash);

        if (!TryDecodeHash(storedHash, out var hash, out var salt, out var memory, out var iterations, out var parallelism))
        {
            return false;
        }

        var computedHash = ComputeHash(password, salt, memory, iterations, parallelism);

        return CryptographicOperations.FixedTimeEquals(hash, computedHash);
    }

    /// <inheritdoc />
    public bool NeedsRehash(string storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return true;

        if (!TryDecodeHash(storedHash, out _, out _, out var memory, out var iterations, out var parallelism))
        {
            return true;
        }

        return memory != _memorySize ||
               iterations != _iterations ||
               parallelism != _parallelism;
    }

    private byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(_saltSize);
    }

    private byte[] ComputeHash(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        return argon2.GetBytes(_hashSize);
    }

    private static string EncodeHash(byte[] hash, byte[] salt, int memory, int iterations, int parallelism)
    {
        var saltBase64 = Convert.ToBase64String(salt).TrimEnd('=').Replace('+', '.').Replace('/', '_');
        var hashBase64 = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '.').Replace('/', '_');

        return $"{Argon2IdPrefix}v={Argon2Version}$m={memory},t={iterations},p={parallelism}${saltBase64}${hashBase64}";
    }

    private static bool TryDecodeHash(
        string encodedHash,
        out byte[] hash,
        out byte[] salt,
        out int memory,
        out int iterations,
        out int parallelism)
    {
        hash = Array.Empty<byte>();
        salt = Array.Empty<byte>();
        memory = 0;
        iterations = 0;
        parallelism = 0;

        if (string.IsNullOrEmpty(encodedHash) || !encodedHash.StartsWith(Argon2IdPrefix))
        {
            return false;
        }

        try
        {
            // Format: $argon2id$v=19$m={memory},t={iterations},p={parallelism}${salt}${hash}
            var parts = encodedHash.Split('$');
            if (parts.Length != 6)
            {
                return false;
            }

            // parts[0] = "" (before first $)
            // parts[1] = "argon2id"
            // parts[2] = "v=19"
            // parts[3] = "m={memory},t={iterations},p={parallelism}"
            // parts[4] = salt (base64)
            // parts[5] = hash (base64)

            // Parse version
            if (!parts[2].StartsWith("v="))
            {
                return false;
            }

            // Parse parameters
            var paramParts = parts[3].Split(',');
            if (paramParts.Length != 3)
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
