using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
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
    private readonly ITwoFactorAuthRepository _twoFactorAuthRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly IPublisher _publisher;
    private readonly ILogger<AccountDeletionRecoverer> _logger;

    public AccountDeletionRecoverer(
        IAccountDeletionRequestRepository requestRepository,
        IUserRepository userRepository,
        ITwoFactorAuthRepository twoFactorAuthRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        TotpReplayPolicy replayPolicy,
        ILoginResponseBuilder loginResponseBuilder,
        IPublisher publisher,
        ILogger<AccountDeletionRecoverer> logger)
    {
        _requestRepository = requestRepository;
        _userRepository = userRepository;
        _twoFactorAuthRepository = twoFactorAuthRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _totpService = totpService;
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
    public async Task<ErrorOr<LoginResponse>> RecoverAsync(
        User user,
        AccountDeletionRequest request,
        string? twoFactorCode,
        string? ipAddress,
        string? userAgent,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        // The user opted into 2FA; recovery must not be a bypass. Verified in
        // the same request — no challenge dance for a deactivated account.
        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrEmpty(twoFactorCode))
            {
                return UserErrors.TwoFactorRequired;
            }

            var twoFactor = await _twoFactorAuthRepository.GetByUserIdAsync(user.Id, cancellationToken);
            if (twoFactor is null || _totpService.ValidateCode(twoFactor.SecretKey, twoFactorCode) is not { } step)
            {
                return UserErrors.InvalidTwoFactorCode;
            }

            // Claimed BEFORE the request is cancelled, so a code already accepted
            // cannot be presented again to restore the account.
            var claimed = await ClaimTwoFactorStepAsync(user.Id, twoFactor.IsEnabled, step, cancellationToken);
            if (claimed.IsError)
            {
                return claimed.Errors;
            }
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
            restored, ipAddress, userAgent, deviceId, cancellationToken);
    }

    /// <summary>
    /// Claims the time step of the code that was just checked, so it is accepted
    /// once.
    /// </summary>
    /// <param name="factorEnabled">Whether the user's two-factor row is enabled.</param>
    private async Task<ErrorOr<Success>> ClaimTwoFactorStepAsync(
        Guid userId,
        bool factorEnabled,
        long step,
        CancellationToken cancellationToken)
    {
        if (!factorEnabled)
        {
            // The account says two-factor is on, but its factor row is not enabled:
            // the two flags disagree. The claim settles only an enabled factor, so
            // it would refuse every code and leave the account unrecoverable. The
            // code was checked, so it is accepted without a claim, as before, and
            // the disagreement is logged.
            _logger.LogWarning(
                "Two-factor code for the recovery of user {UserId} accepted without a step claim: the factor row is not enabled",
                userId);
            return Result.Success;
        }

        var claim = await _twoFactorStateStore.TryClaimTotpStepAsync(
            userId, step, _replayPolicy.RejectReusedCodes, cancellationToken);

        if (claim == LoginCommitOutcome.StepReused)
        {
            _logger.LogWarning(
                "Reused two-factor code rejected for user {UserId} on {Surface}",
                userId, "account-recovery");
            return TwoFactorErrors.CodeAlreadyUsed;
        }

        if (claim == LoginCommitOutcome.ReuseAccepted)
        {
            _logger.LogWarning(
                "Reused two-factor code accepted (RejectReusedCodes=false) for user {UserId} on {Surface}",
                userId, "account-recovery");
        }
        else if (claim != LoginCommitOutcome.Committed)
        {
            // The factor was switched off or removed between the read and the
            // claim: the answer a missing factor has always had here.
            return UserErrors.InvalidTwoFactorCode;
        }

        return Result.Success;
    }
}
