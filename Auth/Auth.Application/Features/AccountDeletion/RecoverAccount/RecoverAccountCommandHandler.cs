using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Auth.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.AccountDeletion.RecoverAccount;

/// <summary>
/// Handler for password-based grace-period recovery. Anti-enumeration: a live
/// account, an unknown account, an admin-soft-deleted account and a wrong
/// password all return the identical invalid-credentials error — pending
/// deletion is only ever revealed to a caller holding valid credentials.
/// </summary>
/// <remarks>
/// An account pending deletion is never locked by wrong passwords: a lock its
/// owner could not lift by signing in would outlast the grace period. Guessing is
/// bounded per client address instead, by the sign-in's own ceiling, checked
/// before the password hash runs and shared with the sign-in page: every wrong
/// password here is recorded against the account, as it is there.
/// </remarks>
public class RecoverAccountCommandHandler
    : IRequestHandler<RecoverAccountCommand, ErrorOr<LoginResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IAccountDeletionRequestRepository _requestRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly AccountDeletionRecoverer _recoverer;
    private readonly PasswordSettings _passwordSettings;
    private readonly ILogger<RecoverAccountCommandHandler> _logger;

    public RecoverAccountCommandHandler(
        IUserRepository userRepository,
        IAccountDeletionRequestRepository requestRepository,
        IPasswordHasher passwordHasher,
        ILoginAttemptRepository loginAttemptRepository,
        AccountDeletionRecoverer recoverer,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<RecoverAccountCommandHandler> logger)
    {
        _userRepository = userRepository;
        _requestRepository = requestRepository;
        _passwordHasher = passwordHasher;
        _loginAttemptRepository = loginAttemptRepository;
        _recoverer = recoverer;
        _passwordSettings = passwordSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(
        RecoverAccountCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailIncludeDeletedAsync(request.Email, cancellationToken);
        if (user is null || !user.IsDeleted || user.PasswordHash is null)
        {
            return UserErrors.InvalidCredentials;
        }

        // The per-address ceiling, before the password hash runs. The answer is the
        // generic one: a refusal that differed would tell the caller the address
        // belongs to an account awaiting deletion.
        if (await IsSourceCeilingReachedAsync(user, request, cancellationToken))
        {
            await RecordFailureAsync(user, request, LoginFailureReasons.SourceLocked, cancellationToken);
            return UserErrors.InvalidCredentials;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            await RecordFailureAsync(user, request, LoginFailureReasons.InvalidPassword, cancellationToken);

            _logger.LogWarning(
                "Failed recovery attempt for pending-deletion account {UserId} from {IpAddress}",
                user.Id, request.IpAddress);
            return UserErrors.InvalidCredentials;
        }

        // Counted again after the hash: the ceiling is the only bound on these
        // accounts, and a burst from one address all passed it together. A
        // recovery restores the account and signs it in, so it is never let
        // through on the strength of the first count alone.
        if (await IsSourceCeilingReachedAsync(user, request, cancellationToken))
        {
            await RecordFailureAsync(user, request, LoginFailureReasons.SourceLocked, cancellationToken);
            return UserErrors.InvalidCredentials;
        }

        // Only an administrator's lock refuses a recovery that proved the
        // password. A lock raised by wrong passwords before the deletion request
        // is lifted by this recovery's success, which proved the password.
        if (user.LockKind(DateTime.UtcNow) == AccountLockKind.Admin)
        {
            return user.LockRefusal(AccountLockKind.Admin);
        }

        // Admin-soft-deleted accounts have no request row: recovery stays an
        // admin decision, and the caller learns nothing.
        var deletionRequest = await _requestRepository.GetActiveByUserIdAsync(user.Id, cancellationToken);
        if (deletionRequest is null || deletionRequest.Status != AccountDeletionStatus.PendingGrace)
        {
            return UserErrors.InvalidCredentials;
        }

        return await _recoverer.RecoverAsync(
            user, deletionRequest, AuthenticationMethods.Password, request.TwoFactorCode, request.IpAddress, request.UserAgent,
            request.DeviceId, cancellationToken);
    }

    /// <summary>
    /// The per-address ceiling, under Enforce only: Legacy (the rollback) drops it.
    /// </summary>
    private async Task<bool> IsSourceCeilingReachedAsync(
        User user, RecoverAccountCommand request, CancellationToken cancellationToken)
    {
        return _passwordSettings.ConsecutiveFailureMode == ConsecutiveFailureMode.Enforce
            && await AuthenticationHelper.IsSourceCeilingReachedAsync(
                _loginAttemptRepository, _passwordSettings, user.Id, request.IpAddress, cancellationToken);
    }

    /// <summary>
    /// Records a refused recovery against the account, as the sign-in page records
    /// its own, so the per-address ceiling counts both.
    /// </summary>
    private Task RecordFailureAsync(
        User user, RecoverAccountCommand request, string failureReason, CancellationToken cancellationToken)
    {
        var attempt = LoginAttempt.CreateFailure(
            request.Email, failureReason, request.IpAddress, request.UserAgent, user.Id);

        return _loginAttemptRepository.CreateAsync(attempt, cancellationToken);
    }
}
