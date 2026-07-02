using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository interface for user session operations.
/// </summary>
public interface IUserSessionRepository
{
    /// <summary>
    /// Gets a session by its ID.
    /// </summary>
    Task<UserSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a session by its token hash.
    /// </summary>
    Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new session.
    /// </summary>
    Task<UserSession> CreateAsync(UserSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a session.
    /// </summary>
    Task UpdateAsync(UserSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active sessions for a user. <paramref name="sortBy"/> accepts the
    /// allow-listed field names in <see cref="Constants.SortFields.Sessions"/>;
    /// null keeps the default order.
    /// </summary>
    Task<IReadOnlyList<UserSession>> GetActiveSessionsForUserAsync(
        Guid userId,
        string? sortBy,
        Enums.SortDirection sortDirection,
        CancellationToken cancellationToken);

    /// <summary>
    /// Terminates all sessions for a user.
    /// </summary>
    Task TerminateAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Terminates all sessions for a user except the specified one.
    /// </summary>
    Task TerminateOtherSessionsAsync(Guid userId, Guid exceptSessionId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Terminates a specific session.
    /// </summary>
    Task TerminateAsync(Guid sessionId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up expired sessions.
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
