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
    private readonly IAccountLockout _accountLockout;
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
        IAccountLockout accountLockout,
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
        _accountLockout = accountLockout;
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

        // A lapsed lock (its expiry has passed) is repaired to active first, and
        // everything below judges the row as it is after the repair, not as it was
        // read: a concurrent failure may have locked it again in between. The
        // count is KEPT: waiting out a lock used to wipe it, so it never passed
        // five and a guesser had ~480 tries a day.
        if (user.LockKind(DateTime.UtcNow) == AccountLockKind.Lapsed)
        {
            var repaired = await _accountLockout.RepairLapsedLockAsync(user.Id, cancellationToken);
            if (repaired is null)
            {
                // Deleted in between: nothing left to sign in to.
                return UserErrors.InvalidCredentials;
            }

            user = repaired;
        }

        // Check account status
        var statusCheck = AuthenticationHelper.CheckAccountStatus(user);
        if (statusCheck.IsError)
        {
            await RecordFailureAsync(user.Id, request.Email,statusCheck.FirstError.Description,
                request.IpAddress, request.UserAgent, cancellationToken);

            return statusCheck.Errors;
        }

        // The lock. One classifier decides which lock the row holds
        // (AccountLockState.Kind), and each kind has one rule:
        //  - lapsed: repaired above; the sign-in goes on.
        //  - timed or hard (raised by wrong passwords): strangers are refused, a
        //    timed lock with its end, a hard one with the reset it needs. A
        //    FAMILIAR source (an address this account signed in from within
        //    thirty days, or a device with a live session) may still prove the
        //    password, because the lock exists to stop the strangers, not the
        //    owner. The door is sealed once the count passes the hard limit by a
        //    full timed-lock threshold, and the per-source ceiling below bounds it
        //    per address. Admission lifts nothing: only the completed sign-in does.
        //  - administrator: refused for everyone.
        var admission = await AdmitAsync(user.Id, user.LockState, request, cancellationToken);
        if (admission.Refusal is { } lockRefusal)
        {
            return lockRefusal;
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
        if (await AuthenticationHelper.IsSourceCeilingReachedAsync(
                _loginAttemptRepository, _passwordSettings, user.Id, request.IpAddress, cancellationToken))
        {
            await RecordFailureAsync(user.Id, request.Email, LoginFailureReasons.SourceLocked,
                request.IpAddress, request.UserAgent, cancellationToken);

            // No timestamp: the account is not locked, this address is, and
            // its window ends one lockout period after its last wrong guess —
            // a moment this branch does not know. The generic locked answer
            // is the honest one.
            return UserErrors.AccountLockedUntil(null);
        }

        // Said only once the address is let through to the password check.
        if (admission.AdmittedUnder != AccountLockKind.None)
        {
            _accountLockout.ReportFamiliarAdmission(user.Id, admission.AdmittedUnder, user.FailedLoginAttempts);
        }

        // Guard: external-only users (no password) cannot use password login
        if (user.PasswordHash is null)
        {
            await RecordFailureAsync(user.Id, request.Email,"No password set",
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // Verify password. The hash it verified is kept: a re-hash below may write
        // only over that one.
        var verifiedHash = user.PasswordHash;
        if (!_passwordHasher.VerifyPassword(request.Password, verifiedHash))
        {
            // Count it: every Max'-th consecutive failure locks for a while, and
            // the hard limit locks until a password proof, a reset or an
            // administrator.
            await _accountLockout.RecordPasswordFailureAsync(user.Id, cancellationToken);

            await RecordFailureAsync(user.Id, request.Email, LoginFailureReasons.InvalidPassword,
                request.IpAddress, request.UserAgent, cancellationToken);

            _logger.LogWarning("Failed login attempt for user {UserId} from {IpAddress}",
                user.Id, request.IpAddress);

            return UserErrors.InvalidCredentials;
        }

        // The password is right, but the lock and the per-address ceiling were read
        // before the hash, and requests that arrive together all read the same row.
        // So both are judged again, for EVERY request, as they are now: a burst of
        // wrong passwords may have locked the account, sealed the familiar door or
        // used up this address's ceiling while this one hashed, and a correct guess
        // inside that burst must not sign in where its siblings are refused, nor
        // lift the lock they set. An administrator's lock or a deactivation
        // committed during the hash refuses it too.
        if (await JudgeAfterThePasswordAsync(user.Id, admission, request, cancellationToken) is { } lateRefusal)
        {
            return lateRefusal;
        }

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
        user.RecordSuccessfulLogin(request.IpAddress, request.UserAgent);

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
    /// What the lock columns <paramref name="lockState"/> let this request do:
    /// nothing to stop it (no lock, or a lapsed one), a familiar source let through
    /// an automatic lock (<see cref="LockAdmission.AdmittedUnder"/>), or a refusal,
    /// recorded as such. Strangers are refused any live lock; a familiar source is
    /// let through a timed or hard lock until the door is sealed; nobody passes an
    /// administrator's lock.
    /// </summary>
    private async Task<LockAdmission> AdmitAsync(
        Guid userId, AccountLockState lockState, LoginCommand request, CancellationToken cancellationToken)
    {
        var kind = lockState.Kind(DateTime.UtcNow);
        if (kind is AccountLockKind.None or AccountLockKind.Lapsed)
        {
            return new LockAdmission(null, AccountLockKind.None);
        }

        var familiar = kind != AccountLockKind.Admin
            && !lockState.IsFamiliarDoorSealed(_passwordSettings.ConsecutiveFailureLimit, _passwordSettings.MaxFailedAttempts)
            && await AuthenticationHelper.IsFamiliarSourceAsync(
                _loginAttemptRepository, userId, request.IpAddress, request.DeviceId, cancellationToken);
        if (familiar)
        {
            return new LockAdmission(null, kind);
        }

        await RecordFailureAsync(userId, request.Email, LoginFailureReasons.AccountLocked,
            request.IpAddress, request.UserAgent, cancellationToken);

        return new LockAdmission(lockState.LockRefusal(kind), kind);
    }

    /// <summary>
    /// The second judgement of a sign-in whose password was right, on the row as it
    /// is now (a read of the lock columns alone): the status, the lock by the same
    /// rules as the first read, and, for a source let through a lock as familiar at
    /// either read, this address's wrong passwords counted again. Null when the
    /// sign-in may go on.
    /// </summary>
    /// <remarks>
    /// The window this closes is the hash itself: every request of a concurrent
    /// burst passes the first read together. The residual is that each of them
    /// still gets its password checked (an exact bound needs the attempt counted
    /// before the hash, OI-133).
    /// </remarks>
    private async Task<Error?> JudgeAfterThePasswordAsync(
        Guid userId, LockAdmission admission, LoginCommand request, CancellationToken cancellationToken)
    {
        if (await _userRepository.GetLockStateAsync(userId, cancellationToken) is not { } now)
        {
            // Deleted while this request hashed: nothing left to sign in to.
            return UserErrors.InvalidCredentials;
        }

        var statusCheck = AuthenticationHelper.CheckAccountStatus(now.Status);
        if (statusCheck.IsError)
        {
            await RecordFailureAsync(userId, request.Email, statusCheck.FirstError.Description,
                request.IpAddress, request.UserAgent, cancellationToken);

            return statusCheck.FirstError;
        }

        var late = await AdmitAsync(userId, now, request, cancellationToken);
        if (late.Refusal is { } refusal)
        {
            return refusal;
        }

        if (admission.AdmittedUnder == AccountLockKind.None && late.AdmittedUnder == AccountLockKind.None)
        {
            return null;
        }

        // A familiar source, at the first read or this one: the per-address
        // ceiling bounds the door, and its siblings' wrong passwords may have
        // reached it while this request hashed.
        if (await AuthenticationHelper.IsSourceCeilingReachedAsync(
                _loginAttemptRepository, _passwordSettings, userId, request.IpAddress, cancellationToken))
        {
            await RecordFailureAsync(userId, request.Email, LoginFailureReasons.SourceLocked,
                request.IpAddress, request.UserAgent, cancellationToken);

            return UserErrors.AccountLockedUntil(null);
        }

        if (admission.AdmittedUnder == AccountLockKind.None)
        {
            _accountLockout.ReportFamiliarAdmission(userId, late.AdmittedUnder, now.FailedAttempts);
        }

        return null;
    }

    /// <param name="Refusal">The answer when the lock refuses this request; null when it may go on.</param>
    /// <param name="AdmittedUnder">The automatic lock a familiar source was let through; None when no lock stood.</param>
    private readonly record struct LockAdmission(Error? Refusal, AccountLockKind AdmittedUnder);

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
