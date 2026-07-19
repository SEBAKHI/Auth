using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Durable persistence for token/session revocations that back the in-memory
/// blacklist, so revocations survive app restarts.
/// </summary>
public interface IRevokedTokenStore
{
    /// <summary>
    /// Persists a revocation.
    /// </summary>
    Task AddAsync(TokenRevocation revocation, CancellationToken cancellationToken);

    /// <summary>
    /// Loads all revocations that have not yet expired (for rehydrating the
    /// in-memory blacklist on startup).
    /// </summary>
    Task<IReadOnlyList<TokenRevocation>> GetActiveAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes revocations that expired before the given cutoff.
    /// </summary>
    Task PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
