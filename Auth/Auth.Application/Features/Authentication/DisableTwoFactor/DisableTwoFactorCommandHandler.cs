using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.DisableTwoFactor;

/// <summary>
/// Handler for the disable two-factor authentication command.
/// </summary>
public class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorAuthRepository _twoFactorRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<DisableTwoFactorCommandHandler> _logger;

    public DisableTwoFactorCommandHandler(
        IUserRepository userRepository,
        ITwoFactorAuthRepository twoFactorRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        TotpReplayPolicy replayPolicy,
        IDomainEventDispatcher eventDispatcher,
        ILogger<DisableTwoFactorCommandHandler> logger)
    {
        _userRepository = userRepository;
        _twoFactorRepository = twoFactorRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _totpService = totpService;
        _replayPolicy = replayPolicy;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        DisableTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // Get the 2FA configuration
        var twoFactor = await _twoFactorRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        if (twoFactor == null || !twoFactor.IsEnabled)
        {
            return UserErrors.TwoFactorNotEnabled;
        }

        // Check if locked out
        if (twoFactor.IsLocked)
        {
            return TwoFactorErrors.LockedOut;
        }

        // Validate the TOTP code
        if (_totpService.ValidateCode(twoFactor.SecretKey, request.Code) is not { } step)
        {
            await RecordFailureAsync(twoFactor, cancellationToken);

            _logger.LogWarning(
                "Invalid TOTP code during 2FA disable for user {UserId}",
                request.UserId);

            return UserErrors.InvalidTwoFactorCode;
        }

        // The code's time step is claimed BEFORE the factor is removed: a code
        // already accepted — by the sign-in it was typed for, say — cannot be
        // presented again to switch the factor off, and of two requests carrying
        // one code only one gets through.
        var claim = await _twoFactorStateStore.TryClaimTotpStepAsync(
            request.UserId, step, _replayPolicy.RejectReusedCodes, cancellationToken);

        if (claim == LoginCommitOutcome.StepReused)
        {
            // Refused like a wrong code, and counted like one.
            await RecordFailureAsync(twoFactor, cancellationToken);

            _logger.LogWarning(
                "Reused two-factor code rejected for user {UserId} on {Surface}",
                request.UserId, "disable");

            return TwoFactorErrors.CodeAlreadyUsed;
        }

        if (claim == LoginCommitOutcome.ReuseAccepted)
        {
            _logger.LogWarning(
                "Reused two-factor code accepted (RejectReusedCodes=false) for user {UserId} on {Surface}",
                request.UserId, "disable");
        }
        else if (claim != LoginCommitOutcome.Committed)
        {
            // The factor was switched off or removed between the read above and
            // the claim; nothing else lets the code through.
            return UserErrors.TwoFactorNotEnabled;
        }

        // Disable 2FA
        await _twoFactorRepository.DeleteAsync(request.UserId, cancellationToken);

        // Update user entity
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user != null)
        {
            user.DisableTwoFactor(request.UserId);
            await _userRepository.UpdateAsync(user, cancellationToken);
        }

        _logger.LogInformation(
            "Two-factor authentication disabled for user {UserId}",
            request.UserId);

        if (user != null)
        {
            await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);
        }

        return Result.Success;
    }

    /// <summary>
    /// Counts a refused code against the factor, as this path always has.
    /// </summary>
    private async Task RecordFailureAsync(TwoFactorAuth twoFactor, CancellationToken cancellationToken)
    {
        twoFactor.RecordFailure();
        await _twoFactorRepository.UpdateAsync(twoFactor, cancellationToken);
    }
}
