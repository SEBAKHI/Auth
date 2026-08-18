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
