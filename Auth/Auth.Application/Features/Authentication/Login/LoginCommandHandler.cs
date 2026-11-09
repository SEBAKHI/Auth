using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Login;

/// <summary>
/// Handler for the login command.
/// </summary>
public class LoginCommandHandler : IRequestHandler<LoginCommand, ErrorOr<LoginResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IAccountDeletionRequestRepository _accountDeletionRequestRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginResponseBuilder _loginResponseBuilder;
    private readonly ITwoFactorChallengeService _twoFactorChallengeService;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly PasswordDoor _passwordDoor;
    private readonly PasskeyOnlyAdministratorRule _passkeyOnlyAdministrator;
    private readonly PasswordSettings _passwordSettings;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IAccountDeletionRequestRepository accountDeletionRequestRepository,
        IPasswordHasher passwordHasher,
        ILoginResponseBuilder loginResponseBuilder,
        ITwoFactorChallengeService twoFactorChallengeService,
        IDomainEventDispatcher eventDispatcher,
        PasswordDoor passwordDoor,
        PasskeyOnlyAdministratorRule passkeyOnlyAdministrator,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<LoginCommandHandler> logger)
    {
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _accountDeletionRequestRepository = accountDeletionRequestRepository;
        _passwordHasher = passwordHasher;
        _loginResponseBuilder = loginResponseBuilder;
        _twoFactorChallengeService = twoFactorChallengeService;
        _eventDispatcher = eventDispatcher;
        _passwordDoor = passwordDoor;
        _passkeyOnlyAdministrator = passkeyOnlyAdministrator;
        _passwordSettings = passwordSettings.Value;
        _logger = logger;
    }

    public async Task<ErrorOr<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        // Get user by email
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user == null)
        {
            // A pending-deletion account is invisible to the normal lookup; on
            // VALID credentials, surface the recovery path instead of a lie.
            // Wrong or absent credentials keep the generic response.
            var pendingDeletionAnswer = await CheckPendingDeletionAsync(request, cancellationToken);
            if (pendingDeletionAnswer is not null)
            {
                return pendingDeletionAnswer.Value;
            }

            // Record failed attempt even if user doesn't exist (prevent enumeration)
            await RecordFailureAsync(null, request.Email,"User not found",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // The lapsed lock, the status, the lock, the per-address ceiling, the hash and
        // the second judgement.
        var attempt = new PasswordAttempt(
            request.Email, request.Password, request.IpAddress, request.UserAgent, request.DeviceId);
        var accepted = await _passwordDoor.CheckAsync(user, attempt, cancellationToken);
        if (accepted.IsError)
        {
            return accepted.Errors;
        }

        user = accepted.Value.User;
        var verifiedHash = accepted.Value.VerifiedHash;

        // An automatic lock a familiar source was let through is NOT lifted yet:
        // the statement that records the completed sign-in lifts it (BuildAsync,
        // passwordProven), so a sign-in that stops at the email check or at the
        // second factor leaves it as is.

        // Check if email is confirmed (only after successful password verification)
        if (!user.EmailConfirmed)
        {
            await RecordFailureAsync(user.Id, request.Email,"Email not confirmed",
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Login blocked for user {UserId} - email not confirmed", user.Id);

            return UserErrors.EmailNotConfirmed;
        }

        // Check if password needs rehash (parameters changed).
        // The same password, re-hashed: not a password change, so the change stamp
        // stays (it opens the administrator's reset of a hard-locked second factor,
        // X12, and this sign-in has not passed the second factor yet). Written only
        // over the hash that was verified, so a reset that lands while this sign-in
        // hashes is never undone by the old password.
        if (_passwordHasher.NeedsRehash(verifiedHash))
        {
            var newHash = _passwordHasher.HashPassword(request.Password);
            await _userRepository.RehashPasswordAsync(user.Id, verifiedHash, newHash, cancellationToken);
            _logger.LogInformation("Rehashed password for user {UserId} due to parameter changes", user.Id);
        }

        // D-S20-4 (ii-s): an administrator whose only usable second factor is a passkey
        // signs in with the passkey alone. Asked after the password was proved and before
        // any challenge or token; the refusal records no attempt and lifts no lock.
        if (await _passkeyOnlyAdministrator.RefusalAsync(user.Id, PasskeyOnlyAdministratorDoors.Password, cancellationToken) is { } passkeyRequired)
        {
            return passkeyRequired;
        }

        // Two-factor gate: no tokens are issued until the code is verified.
        // The client completes the login via the 2fa/verify endpoint.
        //
        // No login attempt is recorded here. The challenge service opens the
        // ceremony's row and the verify endpoint settles it, so this sign-in
        // leaves one row however it ends. Recording a second one here is what
        // used to paint every clean two-factor sign-in as a failed attempt.
        if (user.TwoFactorEnabled)
        {
            var challenge = await _twoFactorChallengeService.CreateChallengeAsync(
                user, request.IpAddress, request.UserAgent, AuthenticationMethods.Password, cancellationToken);

            return challenge.ToPendingLogin();
        }

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(AuthenticationMethods.Password, request.IpAddress, request.UserAgent);

        // Delegate token generation to shared builder
        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, AuthenticationMethods.Password,
            passwordProven: true, cancellationToken, persistent: request.StaySignedIn);

        if (loginResponse.IsError)
        {
            // The builder refused (the account is at its concurrent session
            // limit). The pending UserLoggedInEvent is dropped rather than
            // dispatched: nothing logged in.
            return loginResponse.Errors;
        }

        await _eventDispatcher.DispatchEventsAsync(user, cancellationToken);

        return loginResponse;
    }

    /// <summary>
    /// The answer for an email that belongs to an account awaiting deletion, or
    /// null when it does not. The pending-deletion error (with the grace deadline)
    /// needs the correct password: deletion state is never revealed without valid
    /// credentials, and every other outcome is the generic invalid-credentials
    /// answer.
    /// </summary>
    /// <remarks>
    /// Such an account is never locked: a lock nobody can lift by signing in would
    /// outlast the grace period and become a deletion its owner never meant to let
    /// happen. Guessing is bounded per address instead, by the same ceiling as any
    /// sign-in, checked before the password hash runs; and every wrong password is
    /// recorded against the account, so the ceiling can count it.
    /// </remarks>
    private async Task<Error?> CheckPendingDeletionAsync(LoginCommand request, CancellationToken cancellationToken)
    {
        var deleted = await _userRepository.GetByEmailIncludeDeletedAsync(request.Email, cancellationToken);
        if (deleted is not { IsDeleted: true } || deleted.PasswordHash is null)
        {
            return null;
        }

        var active = await _accountDeletionRequestRepository.GetActiveByUserIdAsync(deleted.Id, cancellationToken);
        if (active is not { Status: AccountDeletionStatus.PendingGrace })
        {
            return null;
        }

        if (await IsPendingDeletionCeilingReachedAsync(deleted.Id, request, cancellationToken))
        {
            await RecordFailureAsync(deleted.Id, request.Email, LoginFailureReasons.SourceLocked,
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        if (!_passwordHasher.VerifyPassword(request.Password, deleted.PasswordHash))
        {
            await RecordFailureAsync(deleted.Id, request.Email, LoginFailureReasons.InvalidPassword,
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // Counted again after the hash: the ceiling is the only bound on these
        // accounts, and a burst from one address all passed it together.
        if (await IsPendingDeletionCeilingReachedAsync(deleted.Id, request, cancellationToken))
        {
            await RecordFailureAsync(deleted.Id, request.Email, LoginFailureReasons.SourceLocked,
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        return UserErrors.AccountPendingDeletion(active.GraceEndsAtUtc);
    }

    /// <summary>
    /// The per-address ceiling of an account pending deletion, under Enforce only:
    /// Legacy (the rollback) drops it.
    /// </summary>
    private async Task<bool> IsPendingDeletionCeilingReachedAsync(
        Guid userId, LoginCommand request, CancellationToken cancellationToken)
    {
        return _passwordSettings.ConsecutiveFailureMode == ConsecutiveFailureMode.Enforce
            && await AuthenticationHelper.IsSourceCeilingReachedAsync(
                _loginAttemptRepository, _passwordSettings, userId, request.IpAddress, cancellationToken);
    }

    /// <summary>
    /// Records a sign-in this handler rejected, through the door's one writer: a success is
    /// recorded by the response builder, and a two-factor gate opens its ceremony row in the
    /// challenge service.
    /// </summary>
    private Task RecordFailureAsync(
        Guid? userId,
        string email,
        string failureReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken) =>
        _passwordDoor.RecordFailureAsync(
            userId, new PasswordAttempt(email, string.Empty, ipAddress, userAgent, null), failureReason, cancellationToken);
}
