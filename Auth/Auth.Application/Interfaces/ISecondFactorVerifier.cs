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
/// refused without being counted.</item>
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
    /// missing or already on; <c>TwoFactor.LockedOut</c> when the factor is locked.
    /// </returns>
    Task<ErrorOr<SecondFactorReservation>> ReserveAsync(
        Guid userId,
        bool expectEnabled,
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
}
