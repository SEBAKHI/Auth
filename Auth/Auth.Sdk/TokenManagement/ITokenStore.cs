namespace Auth.Sdk.TokenManagement;

/// <summary>
/// Stores authentication tokens for the SDK's auto-refresh mechanism.
/// Default implementation is <see cref="InMemoryTokenStore"/>.
/// Implement this interface to use distributed cache or external storage.
/// </summary>
public interface ITokenStore
{
    /// <summary>
    /// Gets the current token set.
    /// </summary>
    Task<TokenSet?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a new token set.
    /// </summary>
    Task SetAsync(string accessToken, string refreshToken, int expiresInSeconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all stored tokens (e.g., on logout).
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a set of authentication tokens with expiration metadata.
/// </summary>
public record TokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
