using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Computes deterministic HMAC-SHA256 hashes for webhook keys.
/// Reuses the same HMAC key infrastructure as refresh tokens (DPAPI-protected).
/// </summary>
public class WebhookKeyHasher : IWebhookKeyHasher
{
    private readonly byte[] _hmacKey;

    public WebhookKeyHasher(IOptions<JwtSettings> settings)
    {
        var plainKey = settings.Value.RefreshTokenHmacKeyPlain;

        if (!string.IsNullOrEmpty(plainKey))
        {
            _hmacKey = Convert.FromBase64String(plainKey);
        }
        else
        {
            throw new InvalidOperationException(
                "HMAC key is not configured. WebhookKeyHasher requires the RefreshTokenHmacKeyPlain setting.");
        }

        if (_hmacKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"HMAC key must be at least 32 bytes (256 bits). Current key is {_hmacKey.Length} bytes.");
        }
    }

    /// <inheritdoc />
    public string ComputeHash(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey))
        {
            throw new ArgumentException("Webhook key cannot be null or empty.", nameof(rawKey));
        }

        var keyBytes = Encoding.UTF8.GetBytes(rawKey);
        using var hmac = new HMACSHA256(_hmacKey);
        var hashBytes = hmac.ComputeHash(keyBytes);
        return Convert.ToBase64String(hashBytes);
    }
}
