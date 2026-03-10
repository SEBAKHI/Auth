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
    Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a refresh token by its HMAC-SHA256 hash.
    /// </summary>
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a refresh token (for revocation or rotation).
    /// </summary>
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all tokens for a user.
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes all tokens for a user on a specific device.
    /// </summary>
    Task RevokeByDeviceAsync(Guid userId, string deviceInfo, Guid? revokedBy, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active tokens for a user.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired tokens older than the specified date.
    /// </summary>
    Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
