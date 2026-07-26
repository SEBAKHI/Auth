using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for deletion re-authentication OTPs.
/// </summary>
public interface IAccountDeletionVerificationRepository
{
    /// <summary>
    /// Creates a new verification code.
    /// </summary>
    /// <param name="verification">The verification to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(AccountDeletionVerification verification, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the most recent unused, unexpired code for an email address.
    /// </summary>
    /// <param name="email">The email address (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most recent valid code if found, null otherwise.</returns>
    Task<AccountDeletionVerification?> GetValidForEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a code as used.
    /// </summary>
    /// <param name="verificationId">The verification ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsUsedAsync(Guid verificationId, CancellationToken cancellationToken);

    /// <summary>
    /// Increments the attempt count for a code.
    /// </summary>
    /// <param name="verificationId">The verification ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IncrementAttemptCountAsync(Guid verificationId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the count of codes created for an email in a time window (for
    /// issuance rate limiting).
    /// </summary>
    /// <param name="email">The email address.</param>
    /// <param name="window">The time window to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> GetRecentCountAsync(string email, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired and used codes (retention sweep).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteExpiredAsync(CancellationToken cancellationToken);
}
