namespace Auth.Application.Interfaces;

/// <summary>
/// Generates API keys with environment-specific prefixes and Argon2id hashing.
/// </summary>
public interface IApiKeyGenerator
{
    /// <summary>
    /// Generates a new API key for the specified environment.
    /// </summary>
    /// <param name="environment">The target environment (production, staging, development).</param>
    /// <returns>The plain key (shown once), the key prefix (for identification), and the Argon2id hash (for storage).</returns>
    (string PlainKey, string KeyPrefix, string KeyHash) Generate(string environment);
}
