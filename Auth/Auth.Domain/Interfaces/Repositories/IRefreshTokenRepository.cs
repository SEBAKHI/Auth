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
    /// Revokes every live token scoped to one application, leaving tokens for
    /// other applications and for the platform alone. Used when the application
    /// is switched off or stops being open to everyone.
    /// </summary>
    Task RevokeAllForApplicationAsync(Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes one user's live tokens for one application. Used when an
    /// invitation is withdrawn: the user loses that application and keeps
    /// everything else.
    /// </summary>
    Task RevokeForUserAndApplicationAsync(Guid userId, Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active tokens for a user.
    /// </summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> rows that fell out of use
    /// before <paramref name="olderThanUtc"/>, and reports how many went.
    /// </summary>
    /// <remarks>
    /// One bounded batch per call, deliberately. The caller loops, which keeps
    /// the row count of a single statement below the ~5000 locks at which SQL
    /// Server escalates to a table lock, and keeps each batch in its own implicit
    /// transaction so the log can be truncated between them. An unbounded DELETE
    /// over a table that accumulated for months would hold both.
    /// <para>
    /// The cutoff is passed in rather than computed here so every table is swept
    /// against one clock — the application's — instead of each statement reading
    /// the database server's.
    /// </para>
    /// </remarks>
    /// <returns>Rows deleted; a value below <paramref name="batchSize"/> means the table is drained.</returns>
    Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken);
}
