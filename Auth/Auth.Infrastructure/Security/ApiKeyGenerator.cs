using System.Security.Cryptography;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Generates API keys with environment-specific prefixes and Argon2id hashing.
/// </summary>
public class ApiKeyGenerator : IApiKeyGenerator
{
    private readonly IPasswordHasher _passwordHasher;

    public ApiKeyGenerator(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    public (string PlainKey, string KeyPrefix, string KeyHash) Generate(string environment)
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var randomPart = Convert.ToBase64String(randomBytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "")[..32];

        var prefix = environment.ToLowerInvariant() switch
        {
            "production" => "ak_prod_",
            "staging" => "ak_stag_",
            "development" => "ak_dev_",
            _ => "ak_"
        };

        var plainKey = $"{prefix}{randomPart}";
        var keyHash = _passwordHasher.HashPassword(plainKey);

        return (plainKey, prefix, keyHash);
    }
}
