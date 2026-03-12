using Auth.Application.DTOs;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Strategy interface for external authentication providers.
/// Each provider (Google, Apple, Facebook, etc.) implements this interface.
/// </summary>
public interface IExternalAuthProvider
{
    /// <summary>
    /// Gets the provider's unique code (e.g., "google", "apple").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Validates the provider's ID token and extracts user information.
    /// </summary>
    /// <param name="idToken">The ID token from the provider (e.g., Google ID token).</param>
    /// <param name="nonce">Optional nonce for token replay prevention.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extracted user information or an error.</returns>
    Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(string idToken, string? nonce, CancellationToken cancellationToken = default);
}
