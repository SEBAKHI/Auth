using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for per-user data-encryption keys.
/// </summary>
public interface IUserEncryptionKeyRepository
{
    /// <summary>
    /// Gets the user's encryption key, if one has been created.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The key if found, null otherwise.</returns>
    Task<UserEncryptionKey?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the user's key. Callers race benignly: on a duplicate-key
    /// violation the caller re-reads the winner's row, so exactly one DEK
    /// ever exists per user.
    /// </summary>
    /// <param name="key">The key to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(UserEncryptionKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Crypto-shred: deletes the user's key, rendering every ciphertext under
    /// it unrecoverable. The account destruction transaction also covers this
    /// deletion inline; this method exists for out-of-band shredding.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
