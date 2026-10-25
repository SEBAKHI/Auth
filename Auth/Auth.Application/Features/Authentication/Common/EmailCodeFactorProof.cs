using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The proof that the emailed second factor may go (S24, EO8, D-T34 B): switched off,
/// or replaced by an authenticator app. Either a code emailed for that purpose — the
/// switch-off challenge (<see cref="TwoFactorChallengePurpose.EmailDisable"/>) — or one
/// of the account's recovery codes, for an owner whose mailbox is out of reach.
/// </summary>
/// <remarks>
/// <para>
/// Each is checked the way every second-factor code is: an attempt counted first on
/// the factor's own counter, under the consecutive-failure limit — the emailed code's
/// counter for an emailed code, then the challenge's five attempts; the shared counter
/// for a recovery code — and only then the code. Nothing is spent here: the caller
/// spends the proof in the same transaction that switches the factor off or over.
/// </para>
/// <para>
/// One service for both callers, so that switching the emailed code off and
/// replacing it ask for exactly the same proof.
/// </para>
/// </remarks>
public sealed class EmailCodeFactorProof
{
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ISecondFactorLockout _lockout;
    private readonly IEmailCodeSender _emailCodeSender;
    private readonly AccountEmailCodeChallenge _accountChallenge;

    public EmailCodeFactorProof(
        ITwoFactorChallengeRepository challengeRepository,
        ISecondFactorVerifier secondFactorVerifier,
        ISecondFactorLockout lockout,
        IEmailCodeSender emailCodeSender,
        AccountEmailCodeChallenge accountChallenge)
    {
        _challengeRepository = challengeRepository;
        _secondFactorVerifier = secondFactorVerifier;
        _lockout = lockout;
        _emailCodeSender = emailCodeSender;
        _accountChallenge = accountChallenge;
    }

    /// <summary>
    /// Emails a code that lets the emailed code go, on the account's live switch-off
    /// challenge, which is opened when none is live. Every limit of the sender holds:
    /// email on, three codes per challenge, the per-window and daily caps, and no code
    /// for an emailed code that is locked.
    /// </summary>
    public async Task<ErrorOr<EmailCodeSent>> SendAsync(
        User user,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Recorded as opened after the password, the only first factor the emailed code
        // may follow (an account binds it only with a password).
        var challenge = await _accountChallenge.GetOrOpenAsync(
            user.Id, TwoFactorChallengePurpose.EmailDisable, AuthenticationMethods.Password, ipAddress, cancellationToken);

        return await _emailCodeSender.SendAsync(user, challenge, ipAddress, _lockout.HardLimit, cancellationToken);
    }

    /// <summary>
    /// Checks a proof that the emailed code may go, counting it first.
    /// </summary>
    /// <param name="userId">The account whose emailed code goes.</param>
    /// <param name="code">The emailed code, or a recovery code.</param>
    /// <param name="isRecoveryCode">Whether <paramref name="code"/> is a recovery code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What the code proved, to be spent by the caller's transaction; or the refusal:
    /// <c>TwoFactor.RecoveryEmailCodeRequired</c> when no switch-off code is live (none
    /// was sent, or it expired or ran out of attempts), and every refusal a code gets
    /// at sign-in (wrong, superseded, locked).
    /// </returns>
    public Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        Guid userId,
        string code,
        bool isRecoveryCode,
        CancellationToken cancellationToken) =>
        isRecoveryCode
            ? VerifyRecoveryCodeAsync(userId, code, cancellationToken)
            : VerifyEmailCodeAsync(userId, code, cancellationToken);

    private async Task<ErrorOr<SecondFactorProof>> VerifyRecoveryCodeAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        var presentation = new SecondFactorPresentation(userId, SecondFactorMethod.RecoveryCode, code, Challenge: null);

        var reservation = await _secondFactorVerifier.ReserveAsync(presentation, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        return await _secondFactorVerifier.VerifyAsync(
            reservation.Value, code, SecondFactorMethod.RecoveryCode, cancellationToken);
    }

    private async Task<ErrorOr<SecondFactorProof>> VerifyEmailCodeAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken)
    {
        var challenge = await _challengeRepository.GetLiveForUserAsync(
            userId, TwoFactorChallengePurpose.EmailDisable, cancellationToken);
        if (challenge is null)
        {
            // No code was sent, or it expired: nothing could match, so nothing is
            // counted. The answer asks for a code, or a recovery code.
            return TwoFactorErrors.RecoveryEmailCodeRequired;
        }

        // The emailed code's own counter first — a code a newer email replaced is
        // refused here uncounted — then the challenge's five attempts.
        var presentation = new SecondFactorPresentation(userId, SecondFactorMethod.EmailCode, code, challenge);
        var reservation = await _secondFactorVerifier.ReserveAsync(presentation, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.Errors;
        }

        if (await _challengeRepository.TryReserveAttemptAsync(
                challenge.Id, TwoFactorChallengePurpose.EmailDisable, TwoFactorChallenge.MaxAttempts, cancellationToken) is null)
        {
            return TwoFactorErrors.RecoveryEmailCodeRequired;
        }

        return await _secondFactorVerifier.VerifyAsync(
            reservation.Value, code, SecondFactorMethod.EmailCode, cancellationToken);
    }
}
