using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The second-factor state that sign-in reads and changes: the user's two-factor
/// row and the challenge it consumes. Every change is one conditional statement,
/// or one transaction of them, whose affected-row count decides the outcome — so
/// concurrent requests can never both pass a check that only one of them should.
/// </summary>
public interface ITwoFactorStateStore
{
    /// <summary>
    /// Reads the user's two-factor row with the TOTP secret still encrypted.
    /// </summary>
    /// <returns>The row as read, or null when the user has none.</returns>
    Task<TwoFactorSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one failed verification against the account before any code is
    /// checked, and locks the factor in the same statement when the count reaches
    /// <see cref="Entities.TwoFactorAuth.MaxFailedAttempts"/>. A correct code later
    /// settles the count back to zero; anything else leaves the failure counted.
    /// </summary>
    /// <returns>
    /// The failure count including this reservation, or null when the factor is
    /// locked (or the user has no two-factor row) and nothing may be verified.
    /// </returns>
    Task<int?> TryReserveAttemptAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Commits a verified sign-in in one transaction: consumes the challenge, then
    /// settles the factor — clearing the failure count and the lock, and for a
    /// recovery code replacing the stored set only while it is still the one the
    /// proof was made against. Either both rows change or neither does.
    /// </summary>
    /// <returns>
    /// <see cref="LoginCommitOutcome.Committed"/> when both changed; otherwise the
    /// row that refused, and nothing was written.
    /// </returns>
    Task<LoginCommitOutcome> TryCommitLoginAsync(
        Guid challengeId,
        Guid userId,
        SecondFactorProof proof,
        CancellationToken cancellationToken);
}
