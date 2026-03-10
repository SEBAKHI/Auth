using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for email verification token operations.
/// </summary>
public interface IEmailVerificationTokenRepository
{
    /// <summary>
    /// Gets the most recent valid token for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent valid token if found, null otherwise.</returns>
    Task<EmailVerificationToken?> GetValidTokenForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new email verification token.
    /// </summary>
    /// <param name="token">The token to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(EmailVerificationToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments the attempt count for a token.
    /// </summary>
    /// <param name="tokenId">The token ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementAttemptCountAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all unused tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of tokens created for an email in a time window (for rate limiting).
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="window">The time window to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of tokens created in the window.</returns>
    Task<int> GetRecentTokenCountAsync(string email, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired tokens older than 7 days.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
