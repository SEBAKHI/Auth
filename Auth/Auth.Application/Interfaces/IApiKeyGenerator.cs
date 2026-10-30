namespace Auth.Application.Interfaces;

/// <summary>
/// Generates API keys in the format of <see cref="Auth.Domain.Services.ApiKeyFormat"/>, hashed with
/// Argon2id.
/// </summary>
public interface IApiKeyGenerator
{
    /// <summary>
    /// Generates a new API key for the specified environment.
    /// </summary>
    /// <param name="environment">The key's environment; production, staging and development get their own segment, any other value <c>othr</c>.</param>
    /// <returns>The plain key (shown once), its stored prefix and selector, and the Argon2id hash of the whole key.</returns>
    GeneratedApiKey Generate(string environment);
}

/// <summary>A newly generated API key.</summary>
/// <param name="PlainKey">The key itself: returned to its creator once, never stored.</param>
/// <param name="KeyPrefix">The text up to the second underscore, stored for display and for the rollback rule.</param>
/// <param name="KeySelector">The random selector the key carries in clear, by which validation finds its row.</param>
/// <param name="KeyHash">The Argon2id hash of the whole key.</param>
public sealed record GeneratedApiKey(string PlainKey, string KeyPrefix, string KeySelector, string KeyHash)
{
    /// <summary>Never the plain key: a logged or interpolated record must not carry it.</summary>
    public override string ToString() => $"GeneratedApiKey {{ KeyPrefix = {KeyPrefix}, KeySelector = {KeySelector} }}";
}
