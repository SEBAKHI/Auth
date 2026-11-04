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
    /// Loads all revocations still in force at <paramref name="now"/> (for rehydrating the
    /// in-memory blacklist at startup and on every periodic re-read). A user-level row is in
    /// force until the user revocation horizon after it took effect, whatever expiry it carries.
    /// </summary>
    Task<IReadOnlyList<TokenRevocation>> GetActiveAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes revocations no longer in force at <paramref name="olderThan"/>, by the same rule.
    /// </summary>
    Task PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
