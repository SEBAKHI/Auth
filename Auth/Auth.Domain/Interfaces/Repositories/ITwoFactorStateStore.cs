using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The second-factor state: the user's two-factor row, the challenge a sign-in
/// consumes, and the account flag the sign-in gate reads. Every change is one
/// conditional statement, or one transaction of them, whose affected-row count
/// decides the outcome — so concurrent requests can never both pass a check that
/// only one of them should.
/// </summary>
/// <remarks>
/// The only writer of <c>Users.IsTwoFactorEnabled</c> after an account is created:
/// <see cref="TryEnableAsync"/> and <see cref="TryDisableAsync"/> write it in the
/// same transaction as the two-factor row, so the flag and the row cannot be left
/// disagreeing by a request that stopped half way.
/// </remarks>
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
    /// settles the factor — clearing the failure count and the lock; for a TOTP
    /// code claiming the time step it matched, and for a recovery code replacing
    /// the stored set only while it is still the one the proof was made against.
    /// Either both rows change or neither does.
    /// </summary>
    /// <param name="rejectReusedSteps">
    /// True to refuse a TOTP step that is not newer than the last one accepted.
    /// False lets it settle, reported as <see cref="LoginCommitOutcome.ReuseAccepted"/>.
    /// The caller decides; the store holds no policy.
    /// </param>
    /// <returns>
    /// <see cref="LoginCommitOutcome.Committed"/> (or <see cref="LoginCommitOutcome.ReuseAccepted"/>)
    /// when both changed; otherwise the row that refused, and nothing was written:
    /// <see cref="LoginCommitOutcome.StepReused"/> when only the step held it back.
    /// </returns>
    Task<LoginCommitOutcome> TryCommitLoginAsync(
        Guid challengeId,
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims the time step a correct TOTP code matched, outside sign-in — with the
    /// same single statement the sign-in commit uses: it settles an enabled factor
    /// only while the step is newer than the last one accepted, so a code that
    /// switched a factor off, or recovered an account, cannot be presented again.
    /// </summary>
    /// <param name="userId">The user whose factor the code was checked against.</param>
    /// <param name="step">The absolute time step the code matched.</param>
    /// <param name="rejectReusedSteps">As for <see cref="TryCommitLoginAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="LoginCommitOutcome.Committed"/> or <see cref="LoginCommitOutcome.ReuseAccepted"/>
    /// when the step was claimed; <see cref="LoginCommitOutcome.StepReused"/> when it
    /// was not newer; <see cref="LoginCommitOutcome.FactorLost"/> when the factor is
    /// gone or switched off.
    /// </returns>
    Task<LoginCommitOutcome> TryClaimTotpStepAsync(
        Guid userId,
        long step,
        bool rejectReusedSteps,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores a freshly generated TOTP secret on the user's pending (not enabled)
    /// two-factor row: rotated in place when there is one, so its failure count and
    /// lock stay as they are; inserted when there is none.
    /// </summary>
    /// <param name="userId">The user setting up two-factor authentication.</param>
    /// <param name="protectedSecretKey">The new secret, already encrypted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// True when the pending secret is stored; false when the factor is enabled,
    /// and nothing was written.
    /// </returns>
    Task<bool> TryStorePendingSecretAsync(
        Guid userId,
        string protectedSecretKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Switches the factor on in one transaction: enables the pending row — only
    /// while it still holds the secret the code was checked against — storing the
    /// recovery codes and claiming the code's time step, then sets the account flag.
    /// Either both change or neither does.
    /// </summary>
    /// <param name="userId">The user switching the factor on.</param>
    /// <param name="protectedSecretSeen">
    /// The secret exactly as read before the code was checked: the stored ciphertext,
    /// never a re-encryption, which would not compare equal.
    /// </param>
    /// <param name="recoveryCodesJson">The hashed recovery codes to store.</param>
    /// <param name="step">The absolute time step the code matched.</param>
    /// <param name="rejectReusedSteps">As for <see cref="TryCommitLoginAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="LoginCommitOutcome.Committed"/> (or <see cref="LoginCommitOutcome.ReuseAccepted"/>)
    /// when both changed; <see cref="LoginCommitOutcome.AlreadyEnabled"/> when the factor
    /// is on already; <see cref="LoginCommitOutcome.FactorLost"/> when the pending row is
    /// gone or holds another secret. Nothing was written unless the factor was switched on.
    /// </returns>
    Task<LoginCommitOutcome> TryEnableAsync(
        Guid userId,
        string protectedSecretSeen,
        string recoveryCodesJson,
        long step,
        bool rejectReusedSteps,
        CancellationToken cancellationToken);

    /// <summary>
    /// Switches the factor off in one transaction: deletes the enabled row — for a
    /// TOTP proof only while the code's step is newer than the last one accepted,
    /// for a recovery code only while the stored set is the one it was checked
    /// against — then clears the account flag, whatever it said, which also repairs
    /// a flag that disagreed with the row. Either both change or neither does.
    /// </summary>
    /// <param name="userId">The user switching the factor off.</param>
    /// <param name="proof">What the presented code proved.</param>
    /// <param name="rejectReusedSteps">As for <see cref="TryCommitLoginAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="LoginCommitOutcome.Committed"/> (or <see cref="LoginCommitOutcome.ReuseAccepted"/>)
    /// when both changed; otherwise nothing was written, and the outcome names why:
    /// <see cref="LoginCommitOutcome.FactorLost"/> when no enabled factor is left,
    /// <see cref="LoginCommitOutcome.StepReused"/> when a TOTP step was not newer,
    /// <see cref="LoginCommitOutcome.RecoveryCodesChanged"/> when the recovery-code set changed.
    /// </returns>
    Task<LoginCommitOutcome> TryDisableAsync(
        Guid userId,
        SecondFactorProof proof,
        bool rejectReusedSteps,
        CancellationToken cancellationToken);
}
