using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for password reset token operations.
/// </summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>
    /// Gets an unused, unexpired password reset token by its hash.
    /// </summary>
    /// <param name="tokenHash">The HMAC-SHA256 hash of the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token if found and valid, null otherwise.</returns>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new password reset token.
    /// </summary>
    /// <param name="token">The token to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(PasswordResetToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates all unused tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up expired tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
