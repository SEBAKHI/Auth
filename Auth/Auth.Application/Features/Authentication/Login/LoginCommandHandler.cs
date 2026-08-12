using Auth.Application.Interfaces;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.DTOs;
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

        // Check lockout
        if (user.IsLockedOut())
        {
            await RecordFailureAsync(user.Id, request.Email,"Account locked",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.AccountLockedUntil(user.LockoutEnd);
        }

        // Auto-unlock if lockout has expired
        if (user.Status == UserStatus.Locked)
        {
            await _userRepository.UnlockAsync(user.Id, user.Id, cancellationToken);
            user = (await _userRepository.GetByEmailAsync(request.Email, cancellationToken))!;
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

            await RecordFailureAsync(user.Id, request.Email,"Invalid password",
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Failed login attempt for user {UserId} from {IpAddress}",
                user.Id, request.IpAddress);

            return UserErrors.InvalidCredentials;
        }

        // Check if email is confirmed (only after successful password verification)
        if (!user.EmailConfirmed)
        {
            await RecordFailureAsync(user.Id, request.Email,"Email not confirmed",
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Login blocked for user {UserId} - email not confirmed", user.Id);

            return UserErrors.EmailNotConfirmed;
        }

        // Check if password needs rehash (parameters changed)
        if (_passwordHasher.NeedsRehash(user.PasswordHash))
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
