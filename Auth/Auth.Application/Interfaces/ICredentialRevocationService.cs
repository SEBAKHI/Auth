using Auth.Domain.Entities;

namespace Auth.Application.Interfaces;

/// <summary>
/// The single implementation of "kill this user's credentials": session
/// termination with session-id blacklisting and refresh-token revocation.
/// Used by the self-service session screens and by every account deletion
/// flow (deactivation must log the user out everywhere, immediately).
/// </summary>
public interface ICredentialRevocationService
{
    /// <summary>
    /// Terminates the user's active sessions (optionally sparing one), revokes
    /// each terminated session's refresh tokens and blacklists the session ids
    /// so outstanding access tokens are rejected immediately.
    /// </summary>
    /// <param name="userId">The user whose sessions to terminate.</param>
    /// <param name="exceptSessionId">Session to spare (usually the caller's current one), or null for all.</param>
    /// <param name="revokedBy">Actor recorded on the revocations.</param>
    /// <param name="reason">Human-readable reason recorded on the terminations/revocations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of sessions terminated.</returns>
    Task<int> TerminateSessionsAsync(Guid userId, Guid? exceptSessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Terminates every active session started from one browser, with the same
    /// refresh-token revocation and session-id blacklisting as the other paths.
    ///
    /// This is the sanctioned direction of the cascade. Forgetting a browser may
    /// end its sessions; ending a session must never forget a browser, or an
    /// ordinary sign-out would make the next sign-in look like an intrusion and
    /// send the user a security email about themselves.
    /// </summary>
    /// <param name="userId">The user whose sessions to terminate.</param>
    /// <param name="deviceHash">Signature of the browser whose sessions to end.</param>
    /// <param name="reason">Human-readable reason recorded on the terminations/revocations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of sessions terminated.</returns>
    Task<int> TerminateSessionsByDeviceAsync(Guid userId, string deviceHash, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the user back within <paramref name="maxSessions"/> by killing their
    /// least recently used sessions — ending the rows, revoking each one's refresh
    /// tokens and blacklisting its session id, exactly as the user-initiated paths
    /// do. Ending the row alone would evict nobody: the access token stays valid
    /// until it expires and the refresh token puts the device straight back.
    ///
    /// Called after the new session exists, so the sign-in that triggered the
    /// limit is itself the most recent and always survives.
    /// </summary>
    /// <param name="userId">The user whose sessions to trim.</param>
    /// <param name="maxSessions">How many sessions the user may keep. 0 or less does nothing.</param>
    /// <param name="reason">Reason recorded on the terminations/revocations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sessions that were ended, for the caller to report on.</returns>
    Task<IReadOnlyList<UserSession>> EnforceConcurrentSessionLimitAsync(
        Guid userId,
        int maxSessions,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Full credential wipe for account deletion/deactivation: terminates every
    /// session, revokes ALL refresh tokens (including session-less ones) and
    /// removes the user's IdP SSO sessions.
    /// </summary>
    /// <param name="userId">The user whose credentials to revoke.</param>
    /// <param name="revokedBy">Actor recorded on the revocations.</param>
    /// <param name="reason">Human-readable reason recorded on the terminations/revocations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of sessions terminated.</returns>
    Task<int> RevokeAllCredentialsAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken);
}
