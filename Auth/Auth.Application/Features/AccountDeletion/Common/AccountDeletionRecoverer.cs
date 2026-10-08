using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
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
/// The shared grace-period recovery pipeline behind both recovery entry
/// points (password and external identity): 2FA gate, deterministic
/// cancel-vs-claim race, account restore, cancelled event and auto-login.
/// Callers authenticate the user BEFORE invoking this.
/// </summary>
public class AccountDeletionRecoverer
{
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly IUserRepository _userRepository;
    private readonly ISecondFactorVerifier _secondFactorVerifier;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IPublisher _publisher;
    private readonly ILogger<AccountDeletionRecoverer> _logger;

    public AccountDeletionRecoverer(
        IAccountDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        ISecondFactorVerifier secondFactorVerifier,
        ITwoFactorStateStore twoFactorStateStore,
        TotpReplayPolicy replayPolicy,
        ILoginResponseBuilder loginResponseBuilder,
        IPublisher publisher,
        ILogger<AccountDeletionRecoverer> logger)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _secondFactorVerifier = secondFactorVerifier;
        _twoFactorStateStore = twoFactorStateStore;
        _replayPolicy = replayPolicy;
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
    /// identity. Recorded on the session the recovery signs in, with the
    /// authenticator-app code when one was checked and claimed here.
    /// </param>
    public async Task<ErrorOr<LoginResponse>> RecoverAsync(
        User user,
        AccountDeletionRequest request,
        AuthenticationMethods primaryMethod,
        string? twoFactorCode,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        var methods = primaryMethod;

        // The user opted into 2FA; recovery must not be a bypass. Verified in
        // the same request — no challenge dance for a deactivated account.
        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(twoFactorCode))
            {
                return UserErrors.TwoFactorRequired;
            }

            // Verified and claimed BEFORE the request is cancelled, so a code
            // already accepted cannot be presented again to restore the account.
            var verified = await VerifyTwoFactorCodeAsync(user.Id, twoFactorCode, ipAddress, cancellationToken);
            if (verified.IsError)
            {
                return verified.Errors;
            }

            methods = methods.With(verified.Value);
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

        return await _loginResponseBuilder.BuildAsync(
            restored, ipAddress, userAgent, deviceId, methods, cancellationToken);
    }

    /// <summary>
    /// Checks the code the way every second-factor code is checked: one attempt
    /// counted against the factor first — so a locked factor checks nothing and the
    /// fifth wrong code locks it, as at sign-in — then the code, then its time step
    /// claimed, so it counts once.
    /// </summary>
    /// <param name="ipAddress">The caller's address, for the reuse lines only.</param>
    /// <returns>
    /// The second factor the code proved, for the session: the authenticator app
    /// when its step was claimed on an enabled factor, nothing when the code was
    /// accepted against a pending one.
    /// </returns>
    private async Task<ErrorOr<AuthenticationMethods>> VerifyTwoFactorCodeAsync(
        Guid userId,
        string code,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        // Which factor the code is checked against. The account says two-factor
        // is on; normally its factor row is enabled. When the row is only pending —
        // the two flags disagree — the code is checked against the pending secret
        // (the rule this path has always had), and no step is claimed below.
        var snapshot = await _twoFactorStateStore.GetSnapshotAsync(userId, cancellationToken);
        var factorEnabled = snapshot?.IsEnabled != false;

        var reservation = await _secondFactorVerifier.ReserveAsync(userId, factorEnabled, cancellationToken);
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
            userId, step, _replayPolicy.RejectReusedCodes, cancellationToken);

        if (claim == LoginCommitOutcome.StepReused)
        {
            _logger.ReusedCodeRejected(userId, TotpReplayLog.AccountRecovery, ipAddress);
            return TwoFactorErrors.CodeAlreadyUsed;
        }

        if (claim == LoginCommitOutcome.ReuseAccepted)
        {
            _logger.ReusedCodeAccepted(userId, TotpReplayLog.AccountRecovery, ipAddress);
        }
        else if (claim != LoginCommitOutcome.Committed)
        {
            // The factor was switched off or removed between the read and the
            // claim: the answer a missing factor has always had here.
            return UserErrors.InvalidTwoFactorCode;
        }

        return AuthenticationMethods.Totp;
    }
}
