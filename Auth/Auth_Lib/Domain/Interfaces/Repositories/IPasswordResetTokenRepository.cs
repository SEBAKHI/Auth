using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for password reset token operations.
/// </summary>
public interface IPasswordResetTokenRepository
{
    /// <summary>
    /// Gets a password reset token by its hash.
    /// </summary>
    /// <param name="tokenHash">The Argon2id hash of the token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token if found and valid, null otherwise.</returns>
    Task<PasswordResetToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new password reset token.
    /// </summary>
    /// <param name="token">The token to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(PasswordResetToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all unused tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the latest valid (unused and not expired) password reset token for a user.
    /// Returns the most recently created token for efficient Argon2id verification.
    /// </summary>
    /// <param name="userId">The user ID to get the token for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest valid token if exists, null otherwise.</returns>
    Task<PasswordResetToken?> GetLatestValidTokenForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
