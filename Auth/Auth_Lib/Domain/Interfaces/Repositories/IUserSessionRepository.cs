using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for user session operations.
/// </summary>
public interface IUserSessionRepository
{
    /// <summary>
    /// Gets a session by its ID.
    /// </summary>
    Task<UserSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a session by its token hash.
    /// </summary>
    Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new session.
    /// </summary>
    Task<UserSession> CreateAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a session.
    /// </summary>
    Task UpdateAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active sessions for a user.
    /// </summary>
    Task<IReadOnlyList<UserSession>> GetActiveSessionsForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates all sessions for a user.
    /// </summary>
    Task TerminateAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates all sessions for a user except the specified one.
    /// </summary>
    Task TerminateOtherSessionsAsync(Guid userId, Guid exceptSessionId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a specific session.
    /// </summary>
    Task TerminateAsync(Guid sessionId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired sessions.
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
