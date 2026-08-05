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
    /// Gets the unused, unexpired codes outstanding for an email address,
    /// newest first.
    /// <para>
    /// Deliberately not "the newest one": anyone who knows an address can mint
    /// a fresh code for it through the anonymous public deletion endpoint, and
    /// a newest-row-only lookup would let that orphan the code a legitimate
    /// user is holding — a remote denial of the self-service deletion right.
    /// Every outstanding code stays redeemable until it expires or is used.
    /// </para>
    /// </summary>
    /// <param name="email">The email address (case-insensitive).</param>
    /// <param name="maxCandidates">
    /// Upper bound on rows returned. Caps the verification work one request can
    /// provoke, since each candidate costs an Argon2id verification.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outstanding codes, newest first; empty when there are none.</returns>
    Task<IReadOnlyList<AccountDeletionVerification>> GetValidForEmailAsync(
        string email, int maxCandidates, CancellationToken cancellationToken);

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
