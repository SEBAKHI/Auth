using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ChangePassword;

/// <summary>
/// Handler for the change password command.
/// </summary>
public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly PasswordSettings _passwordSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        IUserSessionRepository userSessionRepository,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IDomainEventDispatcher eventDispatcher,
        IOptions<PasswordSettings> passwordSettings,
        IOptions<SessionSettings> sessionSettings,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _userSessionRepository = userSessionRepository;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _eventDispatcher = eventDispatcher;
        _passwordSettings = passwordSettings.Value;
        _sessionSettings = sessionSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        // Get the user
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user == null)
        {
            return UserErrors.NotFound(request.UserId);
        }

        // External-only accounts have no password to change.
        if (user.PasswordHash is null)
        {
            return UserErrors.InvalidCurrentPassword;
        }

        // Verify current password
        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            _logger.LogWarning(
                "Failed password change attempt for user {UserId}: invalid current password",
                request.UserId);

            return UserErrors.InvalidCurrentPassword;
        }

        // Validate password strength
        var passwordValidation = _passwordValidator.Validate(request.NewPassword);
        if (passwordValidation.IsError)
        {
            return passwordValidation.Errors;
        }

        // Breached-password policy (no-op when disabled; may warn-and-allow or reject)
        var breachResult = await _breachEvaluator.EvaluateAsync(request.NewPassword, cancellationToken);
        if (breachResult.IsError)
        {
            return breachResult.Errors;
        }

        // Check password history to prevent reuse
        var recentHashes = await _passwordHistoryRepository.GetRecentHashesAsync(
            request.UserId,
            _passwordSettings.HistoryCount,
            cancellationToken);

        foreach (var historicalHash in recentHashes)
        {
            if (_passwordHasher.VerifyPassword(request.NewPassword, historicalHash))
            {
                _logger.LogWarning(
                    "Password change rejected for user {UserId}: password recently used",
                    request.UserId);

                return UserErrors.PasswordRecentlyUsed;
            }
        }

        // Also check against current password
        if (_passwordHasher.VerifyPassword(request.NewPassword, user.PasswordHash))
        {
            return UserErrors.PasswordRecentlyUsed;
        }

        // Hash the new password
        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);

        // Add current password to history before updating
        var passwordHistory = new PasswordHistory(
            Guid.NewGuid(),
            request.UserId,
            user.PasswordHash,
            DateTime.UtcNow);

        await _passwordHistoryRepository.AddAsync(passwordHistory, cancellationToken);

        // Update the password (entity raises PasswordChangedEvent)
        user.ChangePassword(newPasswordHash, request.UserId);
        await _userRepository.UpdatePasswordAsync(
            request.UserId,
            newPasswordHash,
            request.UserId,
            cancellationToken);

        // Cleanup old password history entries beyond the limit
        await _passwordHistoryRepository.CleanupOldHistoryAsync(
            request.UserId,
            _passwordSettings.HistoryCount,
            cancellationToken);

        // Determine whether to terminate sessions
        var shouldTerminateSessions = request.TerminateSessions
            ?? _sessionSettings.TerminateSessionsOnPasswordChange;

        if (shouldTerminateSessions)
        {
            if (request.CurrentSessionId.HasValue)
            {
                // Terminate all sessions except the current one
                await _userSessionRepository.TerminateOtherSessionsAsync(
                    request.UserId,
                    request.CurrentSessionId.Value,
                    "Password changed",
                    cancellationToken);

                _logger.LogInformation(
                    "Password changed for user {UserId}, terminated other sessions (current session preserved)",
                    request.UserId);
            }
            else
            {
                // Terminate all sessions
                await _userSessionRepository.TerminateAllForUserAsync(
                    request.UserId,
                    "Password changed",
                    cancellationToken);

                _logger.LogInformation(
                    "Password changed for user {UserId}, terminated all sessions",
                    request.UserId);
            }
        }
        else
        {
            _logger.LogInformation(
                "Password changed for user {UserId}, sessions preserved per request/configuration",
                request.UserId);
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return Result.Success;
    }
}
