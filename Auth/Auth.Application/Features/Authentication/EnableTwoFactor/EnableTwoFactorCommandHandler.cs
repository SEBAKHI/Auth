using System.Text.Json;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.EnableTwoFactor;

/// <summary>
/// Handler for the enable two-factor authentication command.
/// </summary>
public class EnableTwoFactorCommandHandler : IRequestHandler<EnableTwoFactorCommand, ErrorOr<EnableTwoFactorResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ITwoFactorAuthRepository _twoFactorRepository;
    private readonly ITwoFactorStateStore _twoFactorStateStore;
    private readonly ITotpService _totpService;
    private readonly TotpReplayPolicy _replayPolicy;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<EnableTwoFactorCommandHandler> _logger;

    public EnableTwoFactorCommandHandler(
        IUserRepository userRepository,
        ITwoFactorAuthRepository twoFactorRepository,
        ITwoFactorStateStore twoFactorStateStore,
        ITotpService totpService,
        TotpReplayPolicy replayPolicy,
        IDomainEventDispatcher eventDispatcher,
        ILogger<EnableTwoFactorCommandHandler> logger)
    {
        _userRepository = userRepository;
        _twoFactorRepository = twoFactorRepository;
        _twoFactorStateStore = twoFactorStateStore;
        _totpService = totpService;
        _replayPolicy = replayPolicy;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<EnableTwoFactorResponse>> Handle(
        EnableTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        // Get the pending 2FA setup
        var twoFactor = await _twoFactorRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        if (twoFactor == null)
        {
            return TwoFactorErrors.SetupRequired;
        }

        if (twoFactor.IsEnabled)
        {
            return UserErrors.TwoFactorAlreadyEnabled;
        }

        // Validate the TOTP code
        if (_totpService.ValidateCode(twoFactor.SecretKey, request.Code) is not { } step)
        {
            _logger.LogWarning(
                "Invalid TOTP code during 2FA enable for user {UserId}",
                request.UserId);
            return UserErrors.InvalidTwoFactorCode;
        }

        // Generate recovery codes
        var recoveryCodes = _totpService.GenerateRecoveryCodes(10);
        var hashedCodes = recoveryCodes.Select(c => _totpService.HashRecoveryCode(c)).ToArray();
        var recoveryCodesJson = JsonSerializer.Serialize(hashedCodes);

        // Enable 2FA
        twoFactor.Enable(recoveryCodesJson);
        await _twoFactorRepository.UpdateAsync(twoFactor, cancellationToken);

        // Only now is the factor enabled, which the claim requires. The code was
        // just proved, so the outcome changes nothing here; the claim exists so
        // that this same code cannot go on to sign in, or switch the factor off
        // again, inside its window. A fault in it is not swallowed.
        await _twoFactorStateStore.TryClaimTotpStepAsync(
            request.UserId, step, _replayPolicy.RejectReusedCodes, cancellationToken);

        // Update user entity to reflect 2FA is enabled
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user != null)
        {
            user.EnableTwoFactor(twoFactor.SecretKey, request.UserId);
            await _userRepository.UpdateAsync(user, cancellationToken);
        }

        _logger.LogInformation(
            "Two-factor authentication enabled for user {UserId}",
            request.UserId);

        if (user != null)
        {
            await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);
        }

        return new EnableTwoFactorResponse(recoveryCodes);
    }
}
