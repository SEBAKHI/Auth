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
    /// Gets the user's active sessions that were started from one browser.
    /// Used to end them all when that browser is forgotten.
    /// </summary>
    Task<IReadOnlyList<UserSession>> GetActiveByDeviceHashAsync(
        Guid userId,
        string deviceHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts the user's live sessions, using the same "active" definition as
    /// <see cref="GetActiveSessionsForUserAsync"/>: not ended and not expired.
    /// </summary>
    Task<int> CountActiveForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every active session the user has beyond <paramref name="keepNewest"/>,
    /// keeping the most recently used ones, and returns the sessions it ended.
    ///
    /// Ordered by last activity rather than start time on purpose: the session
    /// started first may be the phone its owner picks up hourly, while the one
    /// nobody has touched in a week is the forgotten machine. Activity is what
    /// identifies the session a user will not miss.
    ///
    /// Convergent by design — it ends everything past the limit, not one row —
    /// so a lowered limit or a transient failure is corrected by the next
    /// sign-in rather than leaving the account permanently over its cap.
    /// </summary>
    /// <param name="userId">The user whose sessions to trim.</param>
    /// <param name="keepNewest">How many of the most recently used sessions survive.</param>
    /// <param name="reason">Reason recorded on each ended session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sessions this call ended; empty when the user was within the limit.</returns>
    Task<IReadOnlyList<UserSession>> TerminateBeyondLimitAsync(
        Guid userId,
        int keepNewest,
        string reason,
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
    /// Terminates every active session belonging to one application. Only the
    /// OAuth token endpoint stamps <c>UserSession.ApplicationId</c>, so platform
    /// sessions (null) are untouched.
    /// </summary>
    Task TerminateForApplicationAsync(Guid applicationId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Terminates one user's active sessions for one application.
    /// </summary>
    Task TerminateForUserAndApplicationAsync(Guid userId, Guid applicationId, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Cleans up expired sessions.
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken cancellationToken);
}
