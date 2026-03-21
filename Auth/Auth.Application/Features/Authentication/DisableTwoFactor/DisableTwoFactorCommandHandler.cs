using Auth.Application.Interfaces;
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
    private readonly ITotpService _totpService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<DisableTwoFactorCommandHandler> _logger;

    public DisableTwoFactorCommandHandler(
        IUserRepository userRepository,
        ITwoFactorAuthRepository twoFactorRepository,
        ITotpService totpService,
        IDomainEventDispatcher eventDispatcher,
        ILogger<DisableTwoFactorCommandHandler> logger)
    {
        _userRepository = userRepository;
        _twoFactorRepository = twoFactorRepository;
        _totpService = totpService;
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
        if (!_totpService.ValidateCode(twoFactor.SecretKey, request.Code))
        {
            twoFactor.RecordFailure();
            await _twoFactorRepository.UpdateAsync(twoFactor, cancellationToken);

            _logger.LogWarning(
                "Invalid TOTP code during 2FA disable for user {UserId}",
                request.UserId);

            return UserErrors.InvalidTwoFactorCode;
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
}
