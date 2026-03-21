namespace Auth.Application.Interfaces;

/// <summary>
/// Service for computing deterministic HMAC-SHA256 hashes for webhook keys.
/// Unlike Argon2id (used for API keys/passwords), HMAC-SHA256 is deterministic,
/// allowing direct database lookup by hash.
/// </summary>
public interface IWebhookKeyHasher
{
    /// <summary>
    /// Computes HMAC-SHA256 hash of a webhook key for secure database storage and lookup.
    /// </summary>
    /// <param name="rawKey">The plaintext webhook key.</param>
    /// <returns>The HMAC-SHA256 hash as a base64 string.</returns>
    string ComputeHash(string rawKey);
}
