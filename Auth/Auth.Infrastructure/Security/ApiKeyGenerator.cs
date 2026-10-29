using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Domain.Services;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Generates API keys in the format of <see cref="ApiKeyFormat"/> and hashes the whole key with
/// Argon2id.
/// </summary>
public class ApiKeyGenerator : IApiKeyGenerator
{
    private readonly IPasswordHasher _passwordHasher;

    public ApiKeyGenerator(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    /// <inheritdoc />
    public GeneratedApiKey Generate(string environment)
    {
        // GetItems draws each character uniformly from the alphabet: 16 characters of selector
        // (about 95 bits) and 32 of secret (about 190 bits).
        var selector = RandomBase62(ApiKeyFormat.SelectorLength);
        var secret = RandomBase62(ApiKeyFormat.SecretLength);

        var plainKey = ApiKeyFormat.Compose(environment, selector, secret);
        var keyHash = _passwordHasher.HashPassword(plainKey);

        return new GeneratedApiKey(plainKey, ApiKeyFormat.PrefixFor(environment), selector, keyHash);
    }

    private static string RandomBase62(int length) =>
        new(RandomNumberGenerator.GetItems<char>(ApiKeyFormat.Base62Alphabet, length));
}
