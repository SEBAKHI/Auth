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
        CancellationToken cancellationToken);

    /// <summary>
    /// Spends the approval. Returns false if the row was already spent, is
    /// unverified, or the approval window has closed — the single-use guarantee
    /// lives in this conditional update, not in the caller.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Increments the attempt count after a rejected code.
    /// </summary>
    Task IncrementAttemptCountAsync(Guid id, CancellationToken cancellationToken);

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
