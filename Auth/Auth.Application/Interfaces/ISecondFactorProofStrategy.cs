using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Checks a code presented as one particular second factor. One implementation per
/// <see cref="SecondFactorMethod"/>; the verifier picks it by <see cref="Method"/>.
/// </summary>
public interface ISecondFactorProofStrategy
{
    /// <summary>
    /// Gets the factor this strategy checks.
    /// </summary>
    SecondFactorMethod Method { get; }

    /// <summary>
    /// Gets the account counter a code presented as this factor is charged to, and
    /// whose locks refuse it: the authenticator app and the recovery codes share one,
    /// the emailed code has its own.
    /// </summary>
    SecondFactorCounter Counter { get; }

    /// <summary>
    /// Whether the row as read holds this factor, so a code presented as it can be
    /// right at all.
    /// </summary>
    bool IsBound(TwoFactorSnapshot snapshot);

    /// <summary>
    /// Refuses, before any attempt is counted, a presentation that is no guess: one
    /// this factor cannot serve after the challenge's first factor, or a code the
    /// strategy can tell apart from a guess without checking it. Writes nothing.
    /// </summary>
    /// <param name="presentation">The code and what it is presented for.</param>
    /// <returns>Success to go on and count the attempt, or the refusal.</returns>
    ErrorOr<Success> Screen(SecondFactorPresentation presentation);

    /// <summary>
    /// Checks the code against the factor as read. Writes nothing.
    /// </summary>
    /// <param name="reservation">The reservation that counted this attempt.</param>
    /// <param name="code">The code as the user typed it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The proof to commit, or the error that rejected the code.</returns>
    Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        SecondFactorReservation reservation,
        string code,
        CancellationToken cancellationToken);
}
