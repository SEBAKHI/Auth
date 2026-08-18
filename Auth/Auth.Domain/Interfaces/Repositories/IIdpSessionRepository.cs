using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for identity-provider (SSO) session operations.
/// </summary>
public interface IIdpSessionRepository
{
    /// <summary>
    /// Creates a new IdP session.
    /// </summary>
    Task<IdpSession> CreateAsync(IdpSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a session by the HMAC-SHA256 hash of its cookie token.
    /// </summary>
    Task<IdpSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a revocation (RevokedAt) for the given session.
    /// </summary>
    Task UpdateAsync(IdpSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes all active sessions for a user (security action).
    /// </summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the user's active SSO sessions, optionally sparing the one whose
    /// cookie token hashes to <paramref name="exceptTokenHash"/>.
    /// </summary>
    /// <remarks>
    /// The exception exists for the authenticated paths that must not sign the
    /// caller out of the browser they are acting from — changing a password
    /// should end every OTHER browser's SSO session, not the current one. There
    /// is no link between a <c>UserSessions</c> row and an <c>IdpSessions</c>
    /// row, so the caller identifies the survivor by its token hash (read from
    /// the request cookie) rather than by session id.
    /// </remarks>
    /// <param name="userId">The user whose SSO sessions to revoke.</param>
    /// <param name="exceptTokenHash">Token hash to spare, or null to revoke all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of sessions revoked.</returns>
    Task<int> RevokeAllForUserExceptAsync(Guid userId, string? exceptTokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes expired or revoked sessions older than the specified date.
    /// </summary>
    Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
