using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
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
            var pendingDeletionSignal = await GetPendingDeletionSignalAsync(
                request.Email, request.Password, cancellationToken);
            if (pendingDeletionSignal is not null)
            {
                return pendingDeletionSignal.Value;
            }

            // Record failed attempt even if user doesn't exist (prevent enumeration)
            await RecordFailureAsync(null, request.Email,"User not found",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // Check account status
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            await RecordFailureAsync(user.Id, request.Email,statusCheck.FirstError.Description,
                request.IpAddress, request.UserAgent, cancellationToken);

            return statusCheck.Errors;
        }

        // Check lockout — for strangers only, and only for the automatic lock.
        // The lock raised by wrong passwords made the counter a weapon: a
        // stranger who knew the address could lock the owner out — of password
        // and provider sign-in alike — with five requests, and keep it so for as
        // long as they cared to repeat them. So when the failure counter raised
        // the lock (a timed expiry and a count at the threshold; an
        // administrator's Lock leaves the counter alone), a FAMILIAR source may
        // still prove the password: an address this account signed in from
        // within thirty days, or a device with a live session. Everyone else is
        // refused as before — and so is everyone when the lock is administrative.
        if (user.IsLockedOut())
        {
            var familiar = user.IsLockedByFailedAttempts(_passwordSettings.MaxFailedAttempts)
                && await AuthenticationHelper.IsFamiliarSourceAsync(
                    _loginAttemptRepository, user.Id, request.IpAddress, request.DeviceId, cancellationToken);
            if (!familiar)
            {
                await RecordFailureAsync(user.Id, request.Email, LoginFailureReasons.AccountLocked,
                    request.IpAddress, request.UserAgent, cancellationToken);

                return UserErrors.AccountLockedUntil(user.LockoutEnd);
            }

            _logger.LogInformation(
                "Locked account {UserId} attempted from a familiar source; admitting to password verification",
                user.Id);
        }
        // Auto-unlock if lockout has expired
        else if (user.Status == UserStatus.Locked)
        {
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByEmailAsync(request.Email, cancellationToken))!;
        }

        // Per-source ceiling: the same number of WRONG PASSWORDS from one address
        // refuses that address for the lockout window, locked account or not. It
        // is what keeps the familiar-source door above from being an unlimited
        // guessing slot for an attacker who shares the owner's network, and it
        // stops a guessing address before its next Argon2 verify rather than
        // after. Only genuine password failures count: this refusal, the
        // account-locked refusal and open two-factor ceremonies are recorded but
        // never extend the window, so an address is free again exactly one
        // window after its last wrong guess. No address (proxy-less tests, odd
        // clients) means nothing to attribute to, so nothing extra to refuse.
        if (request.IpAddress is not null)
        {
            var wrongPasswordsFromThisSource = await _loginAttemptRepository.CountFailedAttemptsForUserFromIpAsync(
                user.Id, request.IpAddress, _passwordSettings.LockoutDuration, cancellationToken);
            if (wrongPasswordsFromThisSource >= _passwordSettings.MaxFailedAttempts)
            {
                await RecordFailureAsync(user.Id, request.Email, LoginFailureReasons.SourceLocked,
                    request.IpAddress, request.UserAgent, cancellationToken);

                // No timestamp: the account is not locked, this address is, and
                // its window ends one lockout period after its last wrong guess —
                // a moment this branch does not know. The generic locked answer
                // is the honest one.
                return UserErrors.AccountLockedUntil(null);
            }
        }

        // Guard: external-only users (no password) cannot use password login
        if (user.PasswordHash is null)
        {
            await RecordFailureAsync(user.Id, request.Email,"No password set",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // Verify password
        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            // Record failed attempt and potentially lock account
            await _userRepository.RecordFailedLoginAsync(
                user.Id,
                _passwordSettings.MaxFailedAttempts,
                _passwordSettings.LockoutDuration,
                cancellationToken);

            await RecordFailureAsync(user.Id, request.Email, LoginFailureReasons.InvalidPassword,
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Failed login attempt for user {UserId} from {IpAddress}",
                user.Id, request.IpAddress);

            return UserErrors.InvalidCredentials;
        }

        // The password is right. If the account was still under the automatic
        // lock — reachable here only from a familiar source — clear it now, in
        // full. Leaving Status=Locked while the success below resets the counter
        // and expiry would read as an INDEFINITE lock: credential renewal
        // refuses it (User.CanRenewCredentials) and every new device is shut out
        // with no end. The strangers who raised it keep their per-source
        // refusals; the account itself is the owner's again.
        if (user.Status == UserStatus.Locked)
        {
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByEmailAsync(request.Email, cancellationToken))!;
        }

        // Check if email is confirmed (only after successful password verification)
        if (!user.EmailConfirmed)
        {
            await RecordFailureAsync(user.Id, request.Email,"Email not confirmed",
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Login blocked for user {UserId} - email not confirmed", user.Id);

            return UserErrors.EmailNotConfirmed;
        }

        // Check if password needs rehash (parameters changed). The hash cannot be
        // null here — it verified a few lines up — but the unlock above may have
        // re-read the row, which resets the compiler's null-state narrowing.
        if (_passwordHasher.NeedsRehash(user.PasswordHash!))
        {
            var newHash = _passwordHasher.HashPassword(request.Password);
            await _userRepository.UpdatePasswordAsync(user.Id, newHash, user.Id, cancellationToken);
            _logger.LogInformation("Rehashed password for user {UserId} due to parameter changes", user.Id);
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
            var challengeToken = await _twoFactorChallengeService.CreateChallengeAsync(
                user, request.IpAddress, request.UserAgent, cancellationToken);

            return new LoginResponse
            {
                RequiresTwoFactor = true,
                TwoFactorChallengeToken = challengeToken
            };
        }

        // Record successful login on entity (raises UserLoggedInEvent)
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

        // Delegate token generation to shared builder
        var loginResponse = await _loginResponseBuilder.BuildAsync(
            user, request.IpAddress, request.UserAgent, request.DeviceId, cancellationToken);

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
    /// Returns the pending-deletion error (with the grace deadline) when the
    /// email belongs to an account awaiting deletion AND the password is
    /// correct — deletion state is never revealed without valid credentials.
    /// </summary>
    private async Task<Error?> GetPendingDeletionSignalAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var deleted = await _userRepository.GetByEmailIncludeDeletedAsync(email, cancellationToken);
        if (deleted is not { IsDeleted: true } || deleted.PasswordHash is null)
        {
            return null;
        }

        var active = await _accountDeletionRequestRepository.GetActiveByUserIdAsync(deleted.Id, cancellationToken);
        if (active is not { Status: AccountDeletionStatus.PendingGrace })
        {
            return null;
        }

        if (!_passwordHasher.VerifyPassword(password, deleted.PasswordHash))
        {
            return null;
        }

        return UserErrors.AccountPendingDeletion(active.GraceEndsAtUtc);
    }

    /// <summary>
    /// Records a sign-in this handler rejected. Only failures are written here:
    /// a success is recorded by the response builder, and a two-factor gate opens
    /// its ceremony row in the challenge service.
    /// </summary>
    private async Task RecordFailureAsync(
        Guid? userId,
        string email,
        string failureReason,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var attempt = LoginAttempt.CreateFailure(email, failureReason, ipAddress, userAgent, userId);

        await _loginAttemptRepository.CreateAsync(attempt, cancellationToken);
    }
}
