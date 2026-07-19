using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for OAuth authorization code operations.
/// </summary>
public interface IAuthorizationCodeRepository
{
    /// <summary>
    /// Creates a new authorization code.
    /// </summary>
    Task<AuthorizationCode> CreateAsync(AuthorizationCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically marks the code with the given hash as consumed and returns it.
    /// Returns null when no unconsumed code with that hash exists — the caller
    /// must then treat the code as invalid (and may check
    /// <see cref="GetByCodeHashAsync"/> to detect a reuse attempt).
    /// </summary>
    Task<AuthorizationCode?> ConsumeByCodeHashAsync(string codeHash, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a code by its HMAC-SHA256 hash regardless of state (used for
    /// reuse-attempt detection after a failed consume).
    /// </summary>
    Task<AuthorizationCode?> GetByCodeHashAsync(string codeHash, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired codes older than the specified date.
    /// </summary>
    Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
