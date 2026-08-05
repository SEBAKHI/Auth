using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for refresh token operations.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Gets a refresh token by its ID.
    /// </summary>
    Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a refresh token by its HMAC-SHA256 hash.
    /// </summary>
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a refresh token (for revocation or rotation).
    /// </summary>
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes all tokens for a user and ends their active sessions.
    /// </summary>
    /// <returns>
    /// The number of tokens that were still LIVE when this ran - unrevoked and
    /// unexpired. Expired-but-unrevoked rows are swept up too, but are excluded
    /// from the count so that callers can tell whether the account owner
    /// actually lost anything. Zero means everything was already gone, which is
    /// what a repeated revocation of the same incident looks like.
    /// </returns>
    Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes all tokens for a user on a specific device.
    /// </summary>
    Task RevokeByDeviceAsync(Guid userId, string deviceInfo, Guid? revokedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes all active tokens belonging to a login session.
    /// </summary>
    Task RevokeBySessionIdAsync(Guid sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active tokens for a user.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up expired tokens older than the specified date.
    /// </summary>
    Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
