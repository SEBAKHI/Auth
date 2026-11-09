using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Asks for a recent sign-in before a change to the second factor.
/// </summary>
/// <remarks>
/// A bearer token proves that someone signed in at some point, not that the person
/// holding it now is the account's owner: a session left open on a shared computer,
/// or a stolen token, would otherwise be enough to switch two-factor off or bind an
/// authenticator of the holder's choosing. The session the token belongs to records
/// when its sign-in happened, and a refreshed token keeps that session, so the age
/// of the sign-in is the age of the session row — not of the token. A person who proved
/// themself again inside the session (<see cref="Domain.ValueObjects.SessionProof"/>) counts
/// from that proof, with what that one proof proved.
/// </remarks>
public interface IReauthenticationGuard
{
    /// <summary>
    /// Checks that the request's session belongs to the user and is still active, whatever its
    /// age: the session a person may prove themself again in. Changes nothing.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="sessionId">The session the access token belongs to, or null when it names none.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The session, or <c>Auth.ReauthenticationRequired</c> when it ended, is someone else's, or
    /// there is no session row: such a token can only sign in again.
    /// </returns>
    Task<ErrorOr<UserSession>> EnsureLiveSessionAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks that the request's session belongs to the user, is still active, and
    /// began, or last proved itself again, no longer ago than
    /// <c>TwoFactor:ReauthenticationMaxAgeMinutes</c>. Changes nothing.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="sessionId">
    /// The session the access token belongs to (its <c>sid</c> claim), or null when
    /// the token carries none.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The session, with what its sign-in proved or, past that, what its last proof proved; or
    /// <c>Auth.ReauthenticationRequired</c> when it is too old, ended, someone else's, or there is
    /// no session row to measure.
    /// </returns>
    Task<ErrorOr<RecentSession>> EnsureRecentSignInAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="EnsureRecentSignInAsync"/>, and the session proved two factors:
    /// the condition for changing a factor in use — new recovery codes, a new
    /// authenticator — which a stolen password alone, or a session that only
    /// emailed a code, must never meet. Changes nothing.
    /// </summary>
    /// <returns>
    /// The session, or <c>Auth.ReauthenticationRequired</c> when it is not recent,
    /// not the user's, ended, or did not prove two factors (a session from before
    /// methods were recorded included).
    /// </returns>
    Task<ErrorOr<RecentSession>> EnsureRecentTwoFactorSignInAsync(
        Guid userId,
        Guid? sessionId,
        CancellationToken cancellationToken);
}
