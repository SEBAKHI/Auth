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
