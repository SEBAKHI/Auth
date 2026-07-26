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
    /// most one active (PendingGrace/Processing) request per user; a duplicate
    /// insert surfaces as a unique-key violation for the caller to map.
    /// </summary>
    /// <param name="request">The request to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(AccountDeletionRequest request, CancellationToken cancellationToken);

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
}
