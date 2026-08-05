using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for the destruction registry.
/// </summary>
public interface IAccountDeletionTombstoneRepository
{
    /// <summary>
    /// Writes a tombstone idempotently (MERGE keyed by EmailHash): re-running
    /// a destruction never fails on the unique constraint.
    /// </summary>
    /// <param name="tombstone">The tombstone to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(AccountDeletionTombstone tombstone, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether an email identifier is currently reserved.
    /// </summary>
    /// <param name="emailHash">Keyed HMAC-SHA256 of the normalized email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsByEmailHashAsync(string emailHash, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes tombstones whose reservation window has elapsed, releasing the
    /// identifier and erasing the digest.
    /// <para>
    /// This is the registry's erasure mechanism. The digests are keyed hashes of
    /// e-mail addresses under a key the system retains, which makes them
    /// pseudonymised personal data rather than anonymous records. Deleting the
    /// row is the only disposal route available, because the key cannot be
    /// destroyed while any live reservation still depends on it.
    /// </para>
    /// </summary>
    /// <param name="cutoffUtc">Delete tombstones written before this instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of reservations released.</returns>
    Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
}
