using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Checks a second-factor code in two phases, so that a caller can reserve its
/// own attempts between them and no code is ever checked before every attempt it
/// costs has been counted.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><see cref="ReserveAsync"/> reads the factor and counts one failed
/// attempt against the account before anything is checked. A locked factor is
/// refused without being counted, and one at the consecutive-failure limit is
/// refused for good, until it is reset. <see cref="ReserveUnderTheTimedLockOnlyAsync"/>
/// is the same reservation without that limit, for the one path that may not
/// have it.</item>
/// <item><see cref="VerifyAsync"/> checks the code against what the reservation
/// read. It writes nothing and opens no transaction, so the expensive work — TOTP
/// computation, up to ten recovery-code hashes — never holds a database lock.</item>
/// </list>
/// The resulting proof counts only once a commit of the caller's settles it; until
/// then the reserved failure stands, which is also the outcome of a request that
/// is cancelled or fails half-way.
/// </remarks>
public interface ISecondFactorVerifier
{
    /// <summary>
    /// Reads the user's factor and reserves one attempt on it.
    /// </summary>
    /// <param name="userId">The user whose factor is being presented.</param>
    /// <param name="expectEnabled">
    /// True on every path that verifies an enabled factor (sign-in). False only
    /// where a pending, not yet enabled factor is being confirmed.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The reservation; or <c>User.TwoFactorNotEnabled</c> when an enabled factor
    /// was expected and there is none; <c>TwoFactor.SetupRequired</c> or
    /// <c>User.TwoFactorAlreadyEnabled</c> when a pending one was expected and is
    /// missing or already on; <c>TwoFactor.LockedOut</c> when the factor is locked
    /// for a while; <c>TwoFactor.LockedAdminResetRequired</c> when it reached the
    /// consecutive-failure limit, which no waiting ends — never <c>LockedOut</c> then.
    /// The limit applies on every path that calls this, a pending factor included.
    /// </returns>
    Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        Guid userId,
        bool expectEnabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// The reservation of <see cref="ReserveAsync"/> under the timed lock alone:
    /// five wrong codes still lock the factor for a while, but the
    /// consecutive-failure limit does not apply, and reaching it is no transition
    /// (no line, no event, no email).
    /// </summary>
    /// <remarks>
    /// Only the recovery of a deletion that the account's own signed-in session
    /// asked for calls this (owner decisions 2026-10-12, X12 PR B row «pending
    /// deletion» (b), narrowed by D-66-1 (a)): no way out of the hard lock reaches
    /// an account waiting for deletion — the password reset, an administrator's
    /// reset and the emergency script all read live accounts — so the limit would
    /// cost that owner the account at the end of the grace period. What it leaves
    /// open is the timed rhythm's guesses there, about 96 a day (ARR-0633). A
    /// deletion asked through the public no-login page recovers through
    /// <see cref="ReserveAsync"/>. A guard test keeps every other use of this member
    /// out of the production code.
    /// </remarks>
    /// <returns>
    /// As <see cref="ReserveAsync"/>, except that <c>TwoFactor.LockedAdminResetRequired</c>
    /// is never the answer: a locked factor is <c>TwoFactor.LockedOut</c>.
    /// </returns>
    Task<ErrorOr<SecondFactorReservation>> ReserveUnderTheTimedLockOnlyAsync(
        Guid userId,
        bool expectEnabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reserves one attempt for a code presented as one named factor, on that
    /// factor's own counter: the authenticator app and the recovery codes share one,
    /// the emailed code has its own (S24). The paths that complete a sign-in or a
    /// recovery call this; the paths that switch a factor on or off keep the
    /// reservation above.
    /// </summary>
    /// <remarks>
    /// Before anything is counted, two refusals: an account whose row does not hold
    /// the named factor (<c>User.InvalidTwoFactorCode</c>; <c>User.TwoFactorNotEnabled</c>
    /// when it holds no second factor at all), and whatever the factor's own screen
    /// refuses as no guess — an emailed code that a newer send replaced
    /// (<c>TwoFactor.EmailCodeSuperseded</c>), or an emailed code after a first factor
    /// other than the password (<c>TwoFactor.EmailCodeUnavailable</c>).
    /// </remarks>
    /// <returns>
    /// The reservation, the refusals above, or the counter's locks:
    /// <c>TwoFactor.LockedOut</c> for a while; <c>TwoFactor.LockedAdminResetRequired</c>
    /// (the shared counter) or <c>TwoFactor.EmailCodeLocked</c> (the emailed code) at
    /// the consecutive-failure limit.
    /// </returns>
    Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        SecondFactorPresentation presentation,
        CancellationToken cancellationToken);

    /// <summary>
    /// The reservation of <see cref="ReserveAsync(SecondFactorPresentation, CancellationToken)"/>
    /// under the timed lock alone: the one recovery path exempted from the
    /// consecutive-failure limit (see <see cref="ReserveUnderTheTimedLockOnlyAsync(Guid, bool, CancellationToken)"/>).
    /// The same guard test keeps it out of every other production path.
    /// </summary>
    Task<ErrorOr<SecondFactorReservation>> ReserveUnderTheTimedLockOnlyAsync(
        SecondFactorPresentation presentation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a code against the factor the reservation read, without touching
    /// the database's two-factor state.
    /// </summary>
    /// <param name="reservation">A reservation from <see cref="ReserveAsync"/>.</param>
    /// <param name="code">The code as the user typed it.</param>
    /// <param name="method">The factor the code was presented as.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proof to commit, or the error that rejected the code.</returns>
    Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        SecondFactorReservation reservation,
        string code,
        SecondFactorMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a code from the NEW authenticator against the replacement secret
    /// waiting on the factor the reservation read — with the same check every
    /// authenticator code goes through, so the attempt it costs was counted on the
    /// factor first.
    /// </summary>
    /// <param name="reservation">A reservation from <see cref="ReserveAsync"/> on the enabled factor.</param>
    /// <param name="code">The code the new authenticator shows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The proof (its step is the new secret's), <c>TwoFactor.NoPendingReplacement</c>
    /// when the reservation read no replacement, or the error that rejected the code.
    /// </returns>
    Task<ErrorOr<SecondFactorProof>> VerifyReplacementAsync(
        SecondFactorReservation reservation,
        string code,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a code from the authenticator app set up on an emailed-code row to take
    /// the emailed code's place (S24), WITHOUT counting an attempt.
    /// </summary>
    /// <remarks>
    /// The code proves only that the caller holds the secret it set up a moment ago,
    /// which anyone with the session can read back by setting up again; counting it
    /// would only lock the recovery codes it shares a counter with, the owner's way in
    /// while the mailbox is out of reach. The counted proof on that path is the
    /// replacement proof. Never for a first factor, where the code is counted.
    /// </remarks>
    /// <param name="snapshot">The emailed-code row, holding the secret set up on it.</param>
    /// <param name="code">The code the app shows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proof, with the step the code matched, or the error that rejected it.</returns>
    Task<ErrorOr<SecondFactorProof>> VerifyUncountedAuthenticatorAsync(
        TwoFactorSnapshot snapshot,
        string code,
        CancellationToken cancellationToken);
}
