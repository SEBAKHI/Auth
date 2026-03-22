namespace Auth.Application.Interfaces;

/// <summary>
/// Generates webhook keys with environment-specific prefixes and HMAC-SHA256 hashing.
/// </summary>
public interface IWebhookKeyGenerator
{
    /// <summary>
    /// Generates a new webhook key for the specified environment.
    /// </summary>
    /// <param name="environment">The target environment (production, staging, development).</param>
    /// <returns>The plain key (shown once), the key prefix (for identification), and the HMAC-SHA256 hash (for storage).</returns>
    (string PlainKey, string KeyPrefix, string KeyHash) Generate(string environment);
}
