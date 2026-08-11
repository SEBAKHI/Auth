using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Persistence for step-up confirmation challenges guarding destructive secret
/// operations.
/// </summary>
public interface ISecretOperationChallengeRepository
{
    /// <summary>
    /// Gets a challenge by id, or null if no such row exists.
    /// </summary>
    Task<SecretOperationChallenge?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a newly issued challenge.
    /// </summary>
    Task CreateAsync(SecretOperationChallenge challenge, CancellationToken cancellationToken);

    /// <summary>
    /// Records a correct code: stamps the verification time and opens the
    /// approval window. Returns false if the row was already verified or spent,
    /// so two concurrent verifications cannot both win.
    /// </summary>
    Task<bool> MarkVerifiedAsync(
        Guid id,
        DateTime verifiedAt,
        DateTime approvalExpiresAt,
        int maxAttempts,
        CancellationToken cancellationToken);

    /// <summary>
    /// Spends the approval. Returns false if the row was already spent, is
    /// unverified, or the approval window has closed — the single-use guarantee
    /// lives in this conditional update, not in the caller.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Claims one of the challenge's capped attempts before the submitted code
    /// is evaluated. Returns false when the row is spent, verified, expired or
    /// already out of attempts — so the cap is decided by a single conditional
    /// update rather than by a count read earlier in the request.
    /// </summary>
    /// <remarks>
    /// Reserving before evaluating is what makes the cap hold: verification is
    /// deliberately slow (Argon2id), and any design that reads the count, spends
    /// that time, then writes lets every request inside the window pass the same
    /// stale count.
    /// </remarks>
    Task<bool> TryRegisterAttemptAsync(Guid id, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes challenges whose expiry has passed, returning the row count. Run
    /// by the retention sweep: a challenge is a short-lived credential row and
    /// has no purpose once it can no longer be answered or spent.
    /// </summary>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes every outstanding challenge held by an administrator. A newly
    /// issued code supersedes the old ones, so a guesser never accumulates
    /// live targets.
    /// </summary>
    Task InvalidateOutstandingForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts challenges issued to an administrator inside the window, for
    /// issuance rate limiting.
    /// </summary>
    Task<int> GetRecentCountForUserAsync(Guid userId, TimeSpan window, CancellationToken cancellationToken);
}
