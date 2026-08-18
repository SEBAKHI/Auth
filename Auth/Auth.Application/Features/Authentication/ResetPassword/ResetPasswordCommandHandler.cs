using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Validators;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.ResetPassword;

/// <summary>
/// Handler for the reset password command.
/// </summary>
public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, ErrorOr<Success>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenKeyService _tokenKeyService;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly PasswordSettings _passwordSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ILogger<ResetPasswordCommandHandler> _logger;

    public ResetPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        ICredentialRevocationService credentialRevocation,
        IPasswordHasher passwordHasher,
        IRefreshTokenKeyService tokenKeyService,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        IOptionsSnapshot<SessionSettings> sessionSettings,
        IDomainEventDispatcher eventDispatcher,
        ILogger<ResetPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _credentialRevocation = credentialRevocation;
        _passwordHasher = passwordHasher;
        _tokenKeyService = tokenKeyService;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _passwordSettings = passwordSettings.Value;
        _sessionSettings = sessionSettings.Value;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        // The token identifies the user on its own: it is hashed deterministically,
        // so the hash of what was submitted is looked up directly. The query also
        // filters out used and expired tokens.
        var tokenHash = _tokenKeyService.ComputeTokenHash(request.Token);
        var resetToken = await _passwordResetTokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);

        if (resetToken == null)
        {
            _logger.LogWarning("Invalid, used or expired password reset token submitted");
            return PasswordResetErrors.InvalidOrExpiredToken;
        }

        var user = await _userRepository.GetByIdAsync(resetToken.UserId, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning(
                "Password reset token {TokenId} resolved to a missing user {UserId}",
                resetToken.Id, resetToken.UserId);
            return PasswordResetErrors.InvalidOrExpiredToken;
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

        // Also check against the current password — absent when an
        // external-only account uses the reset link to set its first one.
        if (user.PasswordHash is not null
            && _passwordHasher.VerifyPassword(request.NewPassword, user.PasswordHash))
        {
            return UserErrors.PasswordRecentlyUsed;
        }

        // Hash the new password
        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);

        // Add current password to history
        if (user.PasswordHash is not null)
        {
            var passwordHistory = new PasswordHistory(
                Guid.NewGuid(),
                user.Id,
                user.PasswordHash,
                DateTime.UtcNow);

            await _passwordHistoryRepository.AddAsync(passwordHistory, cancellationToken);
        }

        // Let the aggregate decide which event this is, BEFORE the write. An external-only
        // account (Google, Apple) has no hash, so this reset is the first credential it has
        // ever had - a different security event from a rotation. Until now this handler
        // mutated nothing and dispatched nothing, so a reset left no audit trail at all.
        if (user.PasswordHash is null)
        {
            var initialPassword = user.SetInitialPassword(newPasswordHash, user.Id);
            if (initialPassword.IsError)
            {
                return initialPassword.Errors;
            }
        }
        else
        {
            user.ChangePassword(newPasswordHash, user.Id);
        }

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
            // The full wipe, not a session-row termination. Ending the rows alone
            // evicts nobody: nothing on the refresh path consults the session, so
            // an unrevoked refresh token mints a fresh access token and the reset
            // locks nothing out. This also revokes the SSO sessions.
            await _credentialRevocation.RevokeAllCredentialsAsync(
                user.Id,
                revokedBy: user.Id,
                "Password reset",
                cancellationToken);

            _logger.LogInformation(
                "Password reset for user {UserId}, revoked all credentials",
                user.Id);
        }
        else
        {
            // Preserving application sessions is a usability choice the operator
            // may make. Preserving single sign-on is not: this is the action
            // someone takes when they believe they have been compromised, and an
            // SSO cookie that predates the reset still mints authorization codes
            // for every application the account can enter. So it goes regardless
            // of the flag, and no caller can opt out.
            var idpRevoked = await _credentialRevocation.RevokeIdpSessionsAsync(
                user.Id,
                exceptIdpSessionToken: null,
                cancellationToken);

            _logger.LogInformation(
                "Password reset for user {UserId}, application sessions preserved per request/configuration; revoked {IdpSessionCount} SSO sessions",
                user.Id, idpRevoked);
        }

        // Dispatched last, mirroring ChangePasswordCommandHandler: the password is persisted
        // and the sessions are settled before anything reacts to the event.
        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return Result.Success;
    }
}
