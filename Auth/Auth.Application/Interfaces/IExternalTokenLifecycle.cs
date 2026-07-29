namespace Auth.Application.Interfaces;

/// <summary>
/// Optional token-lifecycle capability of an external identity provider:
/// exchanging the sign-in authorization code for a revocable refresh token,
/// and revoking it when the account is destroyed. A separate strategy from
/// <see cref="IExternalAuthProvider"/> (interface segregation): only providers
/// that support server-side revocation (Apple) implement it, and callers
/// resolve it by provider name from the registered strategies.
/// </summary>
public interface IExternalTokenLifecycle
{
    /// <summary>
    /// Gets the provider's unique code (e.g., "apple").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Exchanges a sign-in authorization code for the provider's refresh
    /// token. Best-effort: returns null (never throws) when the exchange
    /// fails or the provider issues no refresh token — sign-in must never
    /// break over it.
    /// </summary>
    /// <param name="authorizationCode">The single-use authorization code from the sign-in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> ExchangeCodeAsync(string authorizationCode, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a refresh token at the provider (deletion-time obligation).
    /// </summary>
    /// <param name="refreshToken">The plaintext refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the provider confirmed the revocation.</returns>
    Task<bool> RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}
