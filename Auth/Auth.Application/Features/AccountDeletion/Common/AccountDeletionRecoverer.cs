using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.Common;

/// <summary>
/// The shared grace-period recovery pipeline behind the three recovery entry
/// points (password, external identity, and the emailed code of OI-215): 2FA
/// gate, deterministic cancel-vs-claim race, account restore, cancelled event and
/// auto-login. Callers authenticate the user BEFORE invoking this.
/// </summary>
/// <remarks>
/// <para>
/// The second factor is proved in the same request, routed by the code's shape: what
/// has a recovery code's shape (<c>XXXX-XXXX</c>, normalized as everywhere else) is a
/// recovery code, and anything else the account's own factor — the authenticator app's
/// code, or on an account whose factor is the emailed code (S24) a code emailed for it.
/// </para>
/// <para>
/// The emailed code is the password entry's alone: the provider entry has no way to
/// send one, so there it is refused unchecked, and the way through is a recovery code
/// (OI-243, deviation 261: the sign-in's rule, D-243, does not reach this recovery). Neither a recovery code nor an emailed code makes
/// the recovered session a two-factor one unless the code counts as a second factor.
/// </para>
/// </remarks>
public class AccountDeletionRecoverer
{
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITwoFactorChallengeRepository _challengeRepository;
    private readonly IEmailCodeSender _emailCodeSender;
    private readonly ICredentialHasher _credentialHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ISecondFactorLockout _lockout;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IPublisher _publisher;
    private readonly ILogger<AccountDeletionRecoverer> _logger;

    public AccountDeletionRecoverer(
        IAccountDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        ITwoFactorChallengeRepository challengeRepository,
        IEmailCodeSender emailCodeSender,
        ICredentialHasher credentialHasher,
        IJwtTokenService jwtTokenService,
        ISecondFactorLockout lockout,
        ILoginResponseBuilder loginResponseBuilder,
        IPublisher publisher,
        ILogger<AccountDeletionRecoverer> logger)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _challengeRepository = challengeRepository;
        _emailCodeSender = emailCodeSender;
        _credentialHasher = credentialHasher;
        _jwtTokenService = jwtTokenService;
        _lockout = lockout;
        _loginResponseBuilder = loginResponseBuilder;
        _publisher = publisher;
        _logger = logger;
    }

    /// <summary>
    /// Cancels the pending deletion and restores the account, returning an
    /// auto-login response. Refused deterministically once the worker has
    /// claimed the request (the race has exactly one winner).
    /// </summary>
    /// <param name="primaryMethod">
    /// How the entry point authenticated the user: the password, or an external
    /// identity. Recorded on the session the recovery signs in, with the second
    /// factor the code proved when one was checked and claimed here.
    /// </param>
    /// <param name="spendFirstProof">
    /// For an entry whose first proof is single-use (the emailed code, OI-215): spends
    /// it once the second factor was accepted and before anything is cancelled, so a
    /// wrong authenticator code costs the person no new email, and a proof spent by a
    /// concurrent request recovers nothing. False refuses with the code's own error.
    /// </param>
    public async Task<ErrorOr<LoginResponse>> RecoverAsync(
        User user,
        AccountDeletionRequest request,
        AuthenticationMethods primaryMethod,
        string? twoFactorCode,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? spendFirstProof = null)
    {
        var methods = primaryMethod;

        // The user opted into 2FA; recovery must not be a bypass. Verified in
        // the same request — no challenge dance for a deactivated account.
        if (user.TwoFactorEnabled)
        {
            var snapshot = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);

            // The emailed-code entry (OI-215) of an account whose second factor is the
            // emailed code: the code that opened it just proved the mailbox a second
            // code would go to, so none is asked, as at sign-in. The authenticator app
            // is still asked: it proves something no mailbox does.
            if (EmailCodeProofStrategy.OpenedByEmailCode(primaryMethod) && snapshot is { HasEmailCode: true, IsEnabled: false })
            {
                _logger.LogInformation(
                    "{EventCode}: no second step for the recovery by code of account {UserId}: the code proved the mailbox its second factor would",
                    TwoFactorSignalCodes.SignInCodeMailboxWaived, user.Id);
            }
            else if (string.IsNullOrEmpty(twoFactorCode))
            {
                // The emailed code's account on the password entry asks for a code
                // it can send; every other case asks for the code it always asked for.
                return snapshot?.HasEmailCode == true && EmailCodeProofStrategy.MayFollow(primaryMethod)
                    ? TwoFactorErrors.RecoveryEmailCodeRequired
                    : UserErrors.TwoFactorRequired;
            }
            else
            {
                // Verified and claimed BEFORE the request is cancelled, so a code
                // already accepted cannot be presented again to restore the account.
                var verified = await VerifyTwoFactorCodeAsync(
                    user.Id, snapshot, request, primaryMethod, twoFactorCode, ipAddress, cancellationToken);
                if (verified.IsError)
                {
                    return verified.Errors;
                }

                methods = methods.With(verified.Value);
            }
        }

        if (spendFirstProof is not null && !await spendFirstProof(cancellationToken))
        {
            _logger.LogWarning(
                "Recovery of account {UserId} refused: its emailed code was spent or changed in between", user.Id);
            return EmailVerificationErrors.InvalidOrExpiredOtp;
        }

        var cancelResult = request.Cancel();
        if (cancelResult.IsError)
        {
            return cancelResult.Errors;
        }

        if (!await _requestRepository.UpdateAsync(request, AccountDeletionStatus.PendingGrace, cancellationToken))
        {
            // The worker claimed the request between our read and this write.
            return UserErrors.RecoveryWindowExpired;
        }

        await _userRepository.RestoreAsync(user.Id, cancellationToken);

        _logger.LogInformation("Account {UserId} recovered from pending deletion", user.Id);

        await _publisher.Publish(
            new AccountDeletionCancelledEvent(
                user.Id, user.Email, AccountDeletionRequestor.DisplayNameOf(user), request.CancelledAtUtc!.Value),
            cancellationToken);

        // Re-read the restored account so the login response is built from
        // live (non-deleted) state.
        var restored = await _userRepository.GetByIdAsync(user.Id, cancellationToken);
        if (restored is null)
        {
            return UserErrors.NotFound(user.Id);
        }

        // The password entry point proved the password in this request, so its
        // success lifts an automatic lock carried from before the deletion; the
        // provider entry point proved who the person is, never the password, and
        // lifts nothing (owner decision G8).
        return await _loginResponseBuilder.BuildAsync(
            restored, ipAddress, userAgent, deviceId, methods,
            passwordProven: primaryMethod.ProvesPassword, cancellationToken);
    }

    /// <summary>
    /// Emails a code for the recovery of an account whose second factor is the emailed
    /// code, on the password entry (S24). The code is checked by the next
    /// <see cref="RecoverAsync"/> of the same account, against this recovery's own
    /// challenge, which it opens when none is live.
    /// </summary>
    /// <returns>
    /// Where the code went, or a refusal of the sender; or <c>User.InvalidCredentials</c>
    /// when the account's factor is not the emailed code. Only an account whose factor is
    /// the emailed code is ever asked for this request, so any other answer would turn it
    /// into a silent check of the password: the recovery itself answers a correct one by
    /// recovering the account, which its owner hears about.
    /// </returns>
    public async Task<ErrorOr<EmailCodeSent>> SendEmailCodeAsync(
        User user,
        AccountDeletionRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(user.Id, cancellationToken);
        if (!user.TwoFactorEnabled || snapshot?.HasEmailCode != true)
        {
            return UserErrors.InvalidCredentials;
        }

        var challenge = await _challengeRepository.GetLiveForUserAsync(
            user.Id, TwoFactorChallengePurpose.DeletionRecovery, cancellationToken);

        if (challenge is null)
        {
            // A recovery is answered by the account, never by a token: the token
            // exists only because the column needs a unique fingerprint, and nobody
            // ever receives it.
            challenge = TwoFactorChallenge.Create(
                user.Id,
                _credentialHasher.Hash(CredentialPurpose.TwoFactorChallenge, _jwtTokenService.GenerateRefreshToken()),
                ipAddress,
                primaryMethod: AuthenticationMethods.Password,
                purpose: TwoFactorChallengePurpose.DeletionRecovery);

            await _challengeRepository.CreateAsync(challenge, cancellationToken);
        }

        return await _emailCodeSender.SendAsync(
            user, challenge, ipAddress, EmailCodeHardLimit(snapshot, request), cancellationToken);
    }

    /// <summary>
    /// Checks the code the way every second-factor code is checked: one attempt
    /// counted against the factor first — so a locked factor checks nothing and the
    /// fifth wrong code locks it, as at sign-in — then the code, then its single use
    /// claimed, so it counts once.
    /// </summary>
    /// <remarks>
    /// Under the consecutive-failure limit like every door, with one exception: a
    /// deletion the account's own signed-in session asked for recovers under the
    /// timed lock alone (owner decisions 2026-10-12, X12 PR B row «pending deletion»
    /// (b), narrowed by its re-opening, D-66-1 (a)). No way out of the hard lock
    /// reaches an account waiting for deletion (the password reset, an
    /// administrator's reset and the emergency script read live accounts only), so
    /// the limit would cost that owner the account when the grace period ends; the
    /// timed rhythm still bounds guessing, at about 96 a day. A deletion asked
    /// through the public no-login page proved the mailbox alone, which together
    /// with the password would otherwise buy those guesses on demand, so it
    /// recovers under the limit. The emailed code narrows the exception further
    /// (S24, ARR-0693): while recovery codes remain, which are a way out the limit
    /// does not close, it is under the limit too.
    /// </remarks>
    /// <param name="ipAddress">The caller's address, for the reuse lines only.</param>
    /// <returns>
    /// What the code proved, for the session: the authenticator app when its step was
    /// claimed on an enabled factor; the emailed code, or a recovery code that counts,
    /// when spent; nothing when the code was accepted against a pending factor or was a
    /// recovery code that does not count.
    /// </returns>
    private async Task<ErrorOr<AuthenticationMethods>> VerifyTwoFactorCodeAsync(
        Guid userId,
        TwoFactorSnapshot? snapshot,
        AccountDeletionRequest request,
        AuthenticationMethods primaryMethod,
        string code,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // A recovery code: input with a recovery code's shape. Anything else — six
        // digits, or a mistyped code — is checked as the factor's own code, so a
        // wrong one is answered and counted as a wrong code always was here.
        if (RecoveryCodeFormat.HasShape(code))
        {
            return await SpendRecoveryCodeAsync(userId, snapshot, request, code, cancellationToken);
        }

        // The emailed code's account. Checked before the pending-row rule below,
        // which therefore only ever reads a row that is not the emailed code's.
        if (snapshot?.HasEmailCode == true)
        {
            // The provider entry has no way to send a code: six digits there are
            // refused unchecked, and nothing is counted or sent.
            return EmailCodeProofStrategy.MayFollow(primaryMethod)
                ? await VerifyEmailCodeAsync(userId, snapshot, request, code, cancellationToken)
                : UserErrors.InvalidTwoFactorCode;
        }

        // Which factor the code is checked against. The account says two-factor
        // is on; normally its factor row is enabled. When the row is only pending —
        // the two flags disagree — the code is checked against the pending secret
        // (the rule this path has always had), and no step is claimed below.
        var factorEnabled = snapshot?.IsEnabled != false;

        var reservation = request.WasAskedFromASignedInSession
            ? await _secondFactorVerifier.ReserveUnderTheTimedLockOnlyAsync(userId, factorEnabled, cancellationToken)
            : await _secondFactorVerifier.ReserveAsync(userId, factorEnabled, cancellationToken);
        if (reservation.IsError)
        {
            // A missing factor — or one that changed state since the read above —
            // gets the answer a missing factor has always had here. Anything else,
            // a locked factor first of all, is refused as it is.
            return FactorStateErrors.Contains(reservation.FirstError.Code)
                ? UserErrors.InvalidTwoFactorCode
                : reservation.Errors;
        }

        var proof = await _secondFactorVerifier.VerifyAsync(
            reservation.Value, code, SecondFactorMethod.Totp, cancellationToken);
        if (proof.IsError)
        {
            // The reservation stays counted: a wrong code is a failure.
            return proof.Errors;
        }

        var step = proof.Value.Step
            ?? throw new InvalidOperationException("A TOTP proof must carry the time step it matched.");

        return await ClaimTwoFactorStepAsync(userId, factorEnabled, step, ipAddress, cancellationToken);
    }

    /// <summary>
    /// The emailed code of the password entry: checked against this recovery's own
    /// challenge, on the emailed code's own counter, then spent with the challenge.
    /// </summary>
    private async Task<ErrorOr<AuthenticationMethods>> VerifyEmailCodeAsync(
        Guid userId,
        TwoFactorSnapshot snapshot,
        AccountDeletionRequest request,
        string code,
        CancellationToken cancellationToken)
    {
        var challenge = await _challengeRepository.GetLiveForUserAsync(
            userId, TwoFactorChallengePurpose.DeletionRecovery, cancellationToken);
        if (challenge is null)
        {
            // No code was sent, or it expired: nothing could match, so nothing is
            // counted. The answer asks for a code.
            return TwoFactorErrors.RecoveryEmailCodeRequired;
        }

        var presentation = new SecondFactorPresentation(userId, SecondFactorMethod.EmailCode, code, challenge);
        var reservation = EmailCodeHardLimit(snapshot, request) is null
            ? await _secondFactorVerifier.ReserveUnderTheTimedLockOnlyAsync(presentation, cancellationToken)
            : await _secondFactorVerifier.ReserveAsync(presentation, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.FirstError.Code == UserErrors.TwoFactorNotEnabled.Code
                ? UserErrors.InvalidTwoFactorCode
                : reservation.Errors;
        }

        // Then this recovery's challenge: its five attempts, its expiry, its single use.
        if (await _challengeRepository.TryReserveAttemptAsync(
                challenge.Id, TwoFactorChallengePurpose.DeletionRecovery, TwoFactorChallenge.MaxAttempts, cancellationToken) is null)
        {
            return TwoFactorErrors.RecoveryEmailCodeRequired;
        }

        var proof = await _secondFactorVerifier.VerifyAsync(
            reservation.Value, code, SecondFactorMethod.EmailCode, cancellationToken);
        if (proof.IsError)
        {
            return proof.Errors;
        }

        var commit = await _twoFactorStateStore.TryCommitChallengeAsync(
            challenge.Id, TwoFactorChallengePurpose.DeletionRecovery, userId, proof.Value,
            cancellationToken);

        return commit == LoginCommitOutcome.Committed
            ? proof.Value.Methods
            : TwoFactorErrors.RecoveryEmailCodeRequired;
    }

    /// <summary>
    /// A recovery code, on either entry and for either factor: one attempt counted on
    /// the shared counter first — under the limit as the authenticator app's code is
    /// here — then the code, then the set replaced so it counts once.
    /// </summary>
    private async Task<ErrorOr<AuthenticationMethods>> SpendRecoveryCodeAsync(
        Guid userId,
        TwoFactorSnapshot? snapshot,
        AccountDeletionRequest request,
        string code,
        CancellationToken cancellationToken)
    {
        if (snapshot?.HasSecondFactor != true)
        {
            // A pending factor, or none: recovery codes exist only beside a factor.
            return UserErrors.InvalidTwoFactorCode;
        }

        var presentation = new SecondFactorPresentation(userId, SecondFactorMethod.RecoveryCode, code, Challenge: null);
        var reservation = request.WasAskedFromASignedInSession
            ? await _secondFactorVerifier.ReserveUnderTheTimedLockOnlyAsync(presentation, cancellationToken)
            : await _secondFactorVerifier.ReserveAsync(presentation, cancellationToken);
        if (reservation.IsError)
        {
            return reservation.FirstError.Code == UserErrors.TwoFactorNotEnabled.Code
                ? UserErrors.InvalidTwoFactorCode
                : reservation.Errors;
        }

        var proof = await _secondFactorVerifier.VerifyAsync(
            reservation.Value, code, SecondFactorMethod.RecoveryCode, cancellationToken);
        if (proof.IsError)
        {
            return proof.Errors;
        }

        var spent = await _twoFactorStateStore.TrySpendRecoveryCodeAsync(userId, proof.Value, cancellationToken);
        if (spent != LoginCommitOutcome.Committed)
        {
            // Spent by a concurrent request first, or the factor went: the code
            // restores nothing.
            return TwoFactorErrors.InvalidRecoveryCode;
        }

        return proof.Value.Methods;
    }

    /// <summary>
    /// The limit the emailed code is under here (ARR-0693): Hard′, unless the deletion
    /// was asked from the account's own signed-in session AND no recovery code is
    /// left — then nothing would remain to recover the account by, and the timed lock
    /// alone bounds the guesses.
    /// </summary>
    private int? EmailCodeHardLimit(TwoFactorSnapshot snapshot, AccountDeletionRequest request) =>
        request.WasAskedFromASignedInSession && !snapshot.HasRecoveryCodes ? null : _lockout.HardLimit;


    /// <summary>
    /// The reservation's answers for a factor in the wrong state, as opposed to a
    /// locked one.
    /// </summary>
    private static readonly HashSet<string> FactorStateErrors =
    [
        UserErrors.TwoFactorNotEnabled.Code,
        UserErrors.TwoFactorAlreadyEnabled.Code,
        TwoFactorErrors.SetupRequired.Code,
    ];

    /// <summary>
    /// Claims the time step of the code that was just checked, so it is accepted
    /// once.
    /// </summary>
    /// <param name="factorEnabled">Whether the user's two-factor row is enabled.</param>
    /// <param name="ipAddress">The caller's address, for the reuse lines only.</param>
    private async Task<ErrorOr<AuthenticationMethods>> ClaimTwoFactorStepAsync(
        Guid userId,
        bool factorEnabled,
        long step,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (!factorEnabled)
        {
            // The account says two-factor is on, but its factor row is not enabled:
            // the two flags disagree. The claim settles only an enabled factor, so
            // it would refuse every code and leave the account unrecoverable. The
            // code was checked — after an attempt was counted on the pending row,
            // which therefore stays counted — so it is accepted without a claim, as
            // before, and the disagreement is logged.
            _logger.LogWarning(
                "Two-factor code for the recovery of user {UserId} accepted without a step claim: the factor row is not enabled",
                userId);

            // A pending factor is no factor: nothing is recorded for the session.
            return AuthenticationMethods.Unknown;
        }

        var claim = await _twoFactorStateStore.TryClaimTotpStepAsync(
            userId, step, cancellationToken);

        if (claim == LoginCommitOutcome.StepReused)
        {
            _logger.ReusedCodeRejected(userId, TotpReplayLog.AccountRecovery, ipAddress);
            return TwoFactorErrors.CodeAlreadyUsed;
        }

        if (claim != LoginCommitOutcome.Committed)
        {
            // The factor was switched off or removed between the read and the
            // claim: the answer a missing factor has always had here.
            return UserErrors.InvalidTwoFactorCode;
        }

        return AuthenticationMethods.Totp;
    }
}
