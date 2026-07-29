using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for account deletion request operations.
/// </summary>
public interface IAccountDeletionRequestRepository
{
    /// <summary>
    /// Creates a new deletion request. The filtered unique index allows at
    /// most one active (PendingGrace/Processing) request per user; losing that
    /// race returns false so the caller can map it without touching SQL
    /// exception types.
    /// </summary>
    /// <param name="request">The request to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when created; false when an active request already exists.</returns>
    Task<bool> TryCreateAsync(AccountDeletionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a deletion request by id.
    /// </summary>
    /// <param name="id">The request ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AccountDeletionRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the user's active (PendingGrace or Processing) request, if any.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active request if found, null otherwise.</returns>
    Task<AccountDeletionRequest?> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets pending-grace requests whose grace window has elapsed, oldest
    /// grace-end first (worker scan).
    /// </summary>
    /// <param name="utcNow">The current UTC time.</param>
    /// <param name="batchSize">Maximum number of requests to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AccountDeletionRequest>> GetDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the entity's current state, but only if the database row is
    /// still in <paramref name="expectedStatus"/> — the optimistic guard that
    /// gives the recovery-vs-claim race exactly one winner.
    /// </summary>
    /// <param name="request">The request whose state to persist.</param>
    /// <param name="expectedStatus">The status the row must still hold.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the row was updated; false when the race was lost.</returns>
    Task<bool> UpdateAsync(AccountDeletionRequest request, AccountDeletionStatus expectedStatus, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Processing row to the grace queue. Called only at worker
    /// startup: with a single worker process, a Processing row at startup can
    /// only be an orphan of a crashed/recycled execution.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of reclaimed requests.</returns>
    Task<int> ReclaimProcessingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets Completed requests whose user row exists again — the signature of
    /// a backup restore that resurrected destroyed data. The retention sweep
    /// re-applies destruction for each.
    /// </summary>
    /// <param name="batchSize">Maximum number of requests to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AccountDeletionRequest>> GetCompletedWithLiveUserAsync(int batchSize, CancellationToken cancellationToken);
}
