using System.Security.Cryptography;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Generates webhook keys with environment-specific prefixes and HMAC-SHA256 hashing.
/// </summary>
public class WebhookKeyGenerator : IWebhookKeyGenerator
{
    private readonly IWebhookKeyHasher _webhookKeyHasher;

    public WebhookKeyGenerator(IWebhookKeyHasher webhookKeyHasher)
    {
        _webhookKeyHasher = webhookKeyHasher;
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
            "production" => "wk_prod_",
            "staging" => "wk_stag_",
            "development" => "wk_dev_",
            _ => "wk_"
        };

        var plainKey = $"{prefix}{randomPart}";
        var keyHash = _webhookKeyHasher.ComputeHash(plainKey);

        return (plainKey, prefix, keyHash);
    }
}
