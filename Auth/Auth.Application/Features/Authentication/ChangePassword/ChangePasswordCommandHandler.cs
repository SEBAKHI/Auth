using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Validators;
using Auth.Domain.Constants;
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
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPasswordHasher _passwordHasher;
    private readonly PasswordValidator _passwordValidator;
    private readonly IPasswordBreachEvaluator _breachEvaluator;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly IAccountLockout _accountLockout;
    private readonly PasswordSettings _passwordSettings;
    private readonly SessionSettings _sessionSettings;
    private readonly RecentPasskeyRevocation _recentPasskeys;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        ICredentialRevocationService credentialRevocation,
        IPasswordHasher passwordHasher,
        PasswordValidator passwordValidator,
        IPasswordBreachEvaluator breachEvaluator,
        IDomainEventDispatcher eventDispatcher,
        IAccountLockout accountLockout,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        IOptionsSnapshot<SessionSettings> sessionSettings,
        RecentPasskeyRevocation recentPasskeys,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _recentPasskeys = recentPasskeys;
        _userRepository = userRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _credentialRevocation = credentialRevocation;
        _passwordHasher = passwordHasher;
        _passwordValidator = passwordValidator;
        _breachEvaluator = breachEvaluator;
        _eventDispatcher = eventDispatcher;
        _accountLockout = accountLockout;
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
        var passwordValidation = _passwordValidator.Validate(request.NewPassword, nameof(request.NewPassword));
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

        // Update the password. S20 (D-T19 D): the passkeys registered in the 72 hours before
        // the change go once it has committed — a phishing proxy holding a two-factor
        // session could have planted one — and the older ones stay, listed in the notice.
        // Cut once, now.
        var passkeyCutoff = _recentPasskeys.CutoffNow();

        // From the write on, no step takes the request's token (REVIEW 150 F1, F2; REVIEW-FIXES
        // 150 F14): a client that disconnects must not leave the password changed and the rest
        // undone — the sessions, the passkeys, the notice, the lock.
        await _userRepository.UpdatePasswordAsync(
            request.UserId,
            newPasswordHash,
            request.UserId,
            CancellationToken.None);

        // The other sessions first, so a passkey registration in flight from one of them either
        // committed already, and the revocation below catches it, or finds its session ended
        // and stores nothing; then the passkeys, even when ending the sessions throws, whose
        // exception surfaces after it (F14).
        var shouldTerminateSessions = request.TerminateSessions
            ?? _sessionSettings.TerminateSessionsOnPasswordChange;

        try
        {
            await EndOtherSessionsAsync(request, shouldTerminateSessions);
        }
        finally
        {
            await _recentPasskeys.RevokeRecentAsync(user, passkeyCutoff, PasskeyRemovalReasons.PasswordChange, request.UserId);
        }

        // The entity raises PasswordChangedEvent with the passkeys still live now, read after
        // the revocation: a failed one leaves the recent passkeys listed, never hidden.
        var passkeysThatStay = await _recentPasskeys.StillLiveAsync(request.UserId, CancellationToken.None);
        user.ChangePassword(newPasswordHash, request.UserId, passkeysThatStay);

        // The current password was just proved, and replaced: a lock raised by
        // strangers' wrong passwords is lifted and the count starts again (owner
        // decision G8). An administrator's lock is left as it is.
        await _accountLockout.ClearAutomaticLockAsync(request.UserId, CancellationToken.None);

        // Cleanup old password history entries beyond the limit
        await _passwordHistoryRepository.CleanupOldHistoryAsync(
            request.UserId,
            _passwordSettings.HistoryCount,
            CancellationToken.None);

        await _eventDispatcher.DispatchEventsAsync(user, CancellationToken.None);

        return Result.Success;
    }

    /// <summary>
    /// Ends the account's other sessions when the change asks for it, with no token a client
    /// can cancel.
    /// </summary>
    private async Task EndOtherSessionsAsync(ChangePasswordCommand request, bool terminateSessions)
    {
        if (!terminateSessions)
        {
            _logger.LogInformation(
                "Password changed for user {UserId}, sessions preserved per request/configuration",
                request.UserId);
            return;
        }

        // Through the revocation service, never the session repository alone.
        // Ending a UserSessions row evicts nobody: nothing on the refresh path
        // consults it, so an unrevoked refresh token mints a new access token
        // and walks the ended session straight back in. The service is what
        // also revokes the refresh tokens, blacklists the session ids so
        // already-issued access tokens die now, and revokes the SSO sessions.
        await _credentialRevocation.RevokeCredentialsAsync(
            request.UserId,
            request.CurrentSessionId,
            request.IdpSessionToken,
            revokedBy: request.UserId,
            "Password changed",
            CancellationToken.None);

        _logger.LogInformation(
            request.CurrentSessionId.HasValue
                ? "Password changed for user {UserId}, revoked other credentials (current session preserved)"
                : "Password changed for user {UserId}, revoked all credentials",
            request.UserId);
    }
}
