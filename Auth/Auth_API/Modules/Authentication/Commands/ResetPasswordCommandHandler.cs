using Auth_Lib.Application.Abstractions;
using Auth_Lib.Configuration;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Handler for the reset password command.
/// </summary>
public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly IUserSessionRepository _userSessionRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordSettings _passwordSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        IUserSessionRepository userSessionRepository,
        IPasswordHasher passwordHasher,
        IOptions<PasswordSettings> passwordSettings,
        IOptions<SessionSettings> sessionSettings,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _userSessionRepository = userSessionRepository;
        _passwordHasher = passwordHasher;
        _passwordSettings = passwordSettings.Value;
        _sessionSettings = sessionSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        // Hash the provided token using Argon2id to look up in database
        var tokenHash = _passwordHasher.HashPassword(request.Token);

        // Find the reset token
        var resetToken = await _passwordResetTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (resetToken == null)
        {
            _logger.LogWarning("Invalid or expired password reset token used");
            return PasswordResetErrors.InvalidOrExpiredToken;
        }

        // Additional validation (should already be filtered by repository query)
        if (!resetToken.IsValid)
        {
            _logger.LogWarning(
                "Attempted to use invalid reset token for user {UserId}",
                resetToken.UserId);
            return PasswordResetErrors.InvalidOrExpiredToken;
        }

        // Get the user
        var user = await _userRepository.GetByIdAsync(resetToken.UserId, cancellationToken);

        if (user == null)
        {
            _logger.LogError(
                "Password reset token references non-existent user {UserId}",
                resetToken.UserId);
            return PasswordResetErrors.InvalidOrExpiredToken;
        }

        // Validate password strength
        var passwordValidation = ValidatePasswordStrength(request.NewPassword);
        if (passwordValidation.IsError)
        {
            return passwordValidation.Errors;
        }

        // Check password history to prevent reuse
        var recentHashes = await _passwordHistoryRepository.GetRecentHashesAsync(
            user.Id,
            _passwordSettings.HistoryCount,
            cancellationToken);

        foreach (var historicalHash in recentHashes)
        {
            if (_passwordHasher.VerifyPassword(request.NewPassword, historicalHash))
            {
                _logger.LogWarning(
                    "Password reset rejected for user {UserId}: password recently used",
                    user.Id);
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

        // Add current password to history
        var passwordHistory = new PasswordHistory(
            Guid.NewGuid(),
            user.Id,
            user.PasswordHash,
            DateTime.UtcNow);

        await _passwordHistoryRepository.AddAsync(passwordHistory, cancellationToken);

        // Update the password
        await _userRepository.UpdatePasswordAsync(
            user.Id,
            newPasswordHash,
            user.Id,
            cancellationToken);

        // Mark the reset token as used
        await _passwordResetTokenRepository.MarkAsUsedAsync(resetToken.Id, cancellationToken);

        // Cleanup old password history
        await _passwordHistoryRepository.CleanupOldHistoryAsync(
            user.Id,
            _passwordSettings.HistoryCount,
            cancellationToken);

        // Determine whether to terminate sessions
        var shouldTerminateSessions = request.TerminateSessions
            ?? _sessionSettings.TerminateSessionsOnPasswordReset;

        if (shouldTerminateSessions)
        {
            // Terminate all existing sessions for security
            await _userSessionRepository.TerminateAllForUserAsync(
                user.Id,
                "Password reset",
                cancellationToken);

            _logger.LogInformation(
                "Password reset for user {UserId}, terminated all sessions",
                user.Id);
        }
        else
        {
            _logger.LogInformation(
                "Password reset for user {UserId}, sessions preserved per request/configuration",
                user.Id);
        }

        return Result.Success;
    }

    private ErrorOr<Success> ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return UserErrors.PasswordTooWeak;
        }

        if (password.Length < _passwordSettings.MinimumLength)
        {
            return UserErrors.PasswordTooWeak;
        }

        if (_passwordSettings.RequireUppercase && !password.Any(char.IsUpper))
        {
            return UserErrors.PasswordTooWeak;
        }

        if (_passwordSettings.RequireLowercase && !password.Any(char.IsLower))
        {
            return UserErrors.PasswordTooWeak;
        }

        if (_passwordSettings.RequireDigit && !password.Any(char.IsDigit))
        {
            return UserErrors.PasswordTooWeak;
        }

        if (_passwordSettings.RequireSpecialCharacter && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            return UserErrors.PasswordTooWeak;
        }

        return Result.Success;
    }
}
