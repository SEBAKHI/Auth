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
    /// Deletes expired or revoked sessions older than the specified date.
    /// </summary>
    Task CleanupExpiredAsync(DateTime olderThan, CancellationToken cancellationToken);
}
