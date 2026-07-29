using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for the zero-PII destruction registry.
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
    /// Checks whether an email identifier is permanently reserved.
    /// </summary>
    /// <param name="emailHash">HMAC-SHA256 of the normalized email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsByEmailHashAsync(string emailHash, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether a username identifier is permanently reserved.
    /// </summary>
    /// <param name="usernameHash">HMAC-SHA256 of the upper-cased username.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ExistsByUsernameHashAsync(string usernameHash, CancellationToken cancellationToken);
}
