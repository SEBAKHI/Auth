using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using ErrorOr;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// One presented password, and where it came from.
/// </summary>
/// <param name="Email">The address the attempt is recorded under.</param>
/// <param name="Password">The password as typed.</param>
/// <param name="IpAddress">The client's address.</param>
/// <param name="UserAgent">The client's user agent.</param>
/// <param name="DeviceId">The browser's own identifier.</param>
public sealed record PasswordAttempt(
    string Email,
    string Password,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId)
{
    // The password is a secret; the synthesized ToString would print it into any log
    // line or assertion message the attempt reaches.
    public override string ToString() => $"PasswordAttempt {{ IpAddress = {IpAddress} }}";
}

/// <summary>A password that was right, on the account as it is after a repair.</summary>
/// <param name="User">The account, repaired when its lock had lapsed.</param>
/// <param name="VerifiedHash">The hash the password verified against: a re-hash may write only over that one.</param>
public sealed record PasswordAccepted(User User, string VerifiedHash)
{
    public override string ToString() => $"PasswordAccepted {{ UserId = {User.Id} }}";
}

/// <summary>
/// The door a password goes through, in the order that makes it hold: the lapsed lock repaired,
/// the status, the lock, the per-address ceiling, the hash with a wrong one counted, and every
/// judgement again on the row as it is after the hash. The password sign-in and a signed-in
/// person proving themself again share it, so a guess is bounded the same at either.
/// </summary>
/// <remarks>
/// <para>
/// The lock. One classifier decides which lock the row holds (<c>AccountLockState.Kind</c>), and
/// each kind has one rule:
/// </para>
/// <list type="bullet">
/// <item>lapsed: repaired; the attempt goes on.</item>
/// <item>timed or hard (raised by wrong passwords): strangers are refused, a timed lock with its
/// end, a hard one with the reset it needs. A FAMILIAR source (an address this account signed in
/// from within thirty days, or a device with a live session) may still prove the password, because
/// the lock exists to stop the strangers, not the owner. The door is sealed once the count passes
/// the hard limit by a full timed-lock threshold, and the per-source ceiling bounds it per address.
/// Admission lifts nothing: only a completed sign-in does.</item>
/// <item>administrator: refused for everyone.</item>
/// </list>
/// <para>
/// Nothing here lifts a lock or clears a count, and nothing here issues anything: what the caller
/// does with an accepted password is its own.
/// </para>
/// </remarks>
public sealed class PasswordDoor
{
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccountLockout _accountLockout;
    private readonly PasswordSettings _passwordSettings;
    private readonly ILogger<PasswordDoor> _logger;

    public PasswordDoor(
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IPasswordHasher passwordHasher,
        IAccountLockout accountLockout,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<PasswordDoor> logger)
    {
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _passwordHasher = passwordHasher;
        _accountLockout = accountLockout;
        _passwordSettings = passwordSettings.Value;
        _logger = logger;
    }

    /// <summary>Takes <paramref name="attempt"/> through the door of <paramref name="user"/>.</summary>
    /// <returns>
    /// The account and the verified hash; or the refusal, each recorded as a failed attempt:
    /// <see cref="UserErrors.InvalidCredentials"/> for a wrong password, no password on the account,
    /// or an account deleted meanwhile; the status's error; or the lock's.
    /// </returns>
    public async Task<ErrorOr<PasswordAccepted>> CheckAsync(
        User user,
        PasswordAttempt attempt,
        CancellationToken cancellationToken)
    {
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
            await RecordFailureAsync(user.Id, attempt, statusCheck.FirstError.Description, cancellationToken);

            return statusCheck.Errors;
        }

        var admission = await AdmitAsync(user.Id, user.LockState, attempt, cancellationToken);
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
                _loginAttemptRepository, _passwordSettings, user.Id, attempt.IpAddress, cancellationToken))
        {
            await RecordFailureAsync(user.Id, attempt, LoginFailureReasons.SourceLocked, cancellationToken);

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
            await RecordFailureAsync(user.Id, attempt, "No password set", cancellationToken);

            return UserErrors.InvalidCredentials;
        }

        // Verify password. The hash it verified is kept: a re-hash may write only
        // over that one.
        var verifiedHash = user.PasswordHash;
        if (!_passwordHasher.VerifyPassword(attempt.Password, verifiedHash))
        {
            // Count it: every Max'-th consecutive failure locks for a while, and
            // the hard limit locks until a password proof, a reset or an
            // administrator.
            await _accountLockout.RecordPasswordFailureAsync(user.Id, cancellationToken);

            await RecordFailureAsync(user.Id, attempt, LoginFailureReasons.InvalidPassword, cancellationToken);

            _logger.LogWarning("Failed login attempt for user {UserId} from {IpAddress}",
                user.Id, attempt.IpAddress);

            return UserErrors.InvalidCredentials;
        }

        // The password is right, but the lock and the per-address ceiling were read
        // before the hash, and requests that arrive together all read the same row.
        // So both are judged again, for EVERY request, as they are now: a burst of
        // wrong passwords may have locked the account, sealed the familiar door or
        // used up this address's ceiling while this one hashed, and a correct guess
        // inside that burst must not get through where its siblings are refused, nor
        // lift the lock they set. An administrator's lock or a deactivation
        // committed during the hash refuses it too.
        if (await JudgeAfterThePasswordAsync(user.Id, admission, attempt, cancellationToken) is { } lateRefusal)
        {
            return lateRefusal;
        }

        return new PasswordAccepted(user, verifiedHash);
    }

    /// <summary>
    /// Records an attempt this door's caller rejected. Only failures are written here: a success
    /// is recorded by the response builder, and a two-factor gate opens its ceremony row in the
    /// challenge service.
    /// </summary>
    public async Task RecordFailureAsync(
        Guid? userId,
        PasswordAttempt attempt,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var row = LoginAttempt.CreateFailure(attempt.Email, failureReason, attempt.IpAddress, attempt.UserAgent, userId);

        await _loginAttemptRepository.CreateAsync(row, cancellationToken);
    }

    /// <summary>
    /// What the lock columns <paramref name="lockState"/> let this attempt do: nothing to stop it
    /// (no lock, or a lapsed one), a familiar source let through an automatic lock
    /// (<see cref="LockAdmission.AdmittedUnder"/>), or a refusal, recorded as such. Strangers are
    /// refused any live lock; a familiar source is let through a timed or hard lock until the door
    /// is sealed; nobody passes an administrator's lock.
    /// </summary>
    private async Task<LockAdmission> AdmitAsync(
        Guid userId, AccountLockState lockState, PasswordAttempt attempt, CancellationToken cancellationToken)
    {
        var kind = lockState.Kind(DateTime.UtcNow);
        if (kind is AccountLockKind.None or AccountLockKind.Lapsed)
        {
            return new LockAdmission(null, AccountLockKind.None);
        }

        var familiar = kind != AccountLockKind.Admin
            && !lockState.IsFamiliarDoorSealed(_passwordSettings.ConsecutiveFailureLimit, _passwordSettings.MaxFailedAttempts)
            && await AuthenticationHelper.IsFamiliarSourceAsync(
                _loginAttemptRepository, userId, attempt.IpAddress, attempt.DeviceId, cancellationToken);
        if (familiar)
        {
            return new LockAdmission(null, kind);
        }

        await RecordFailureAsync(userId, attempt, LoginFailureReasons.AccountLocked, cancellationToken);

        return new LockAdmission(lockState.LockRefusal(kind), kind);
    }

    /// <summary>
    /// The second judgement of an attempt whose password was right, on the row as it is now (a
    /// read of the lock columns alone): the status, the lock by the same rules as the first read,
    /// and, for a source let through a lock as familiar at either read, this address's wrong
    /// passwords counted again. Null when the attempt may go on.
    /// </summary>
    /// <remarks>
    /// The window this closes is the hash itself: every request of a concurrent burst passes the
    /// first read together. The residual is that each of them still gets its password checked (an
    /// exact bound needs the attempt counted before the hash, OI-133).
    /// </remarks>
    private async Task<Error?> JudgeAfterThePasswordAsync(
        Guid userId, LockAdmission admission, PasswordAttempt attempt, CancellationToken cancellationToken)
    {
        if (await _userRepository.GetLockStateAsync(userId, cancellationToken) is not { } now)
        {
            // Deleted while this request hashed: nothing left to sign in to.
            return UserErrors.InvalidCredentials;
        }

        var statusCheck = AuthenticationHelper.CheckAccountStatus(now.Status);
        if (statusCheck.IsError)
        {
            await RecordFailureAsync(userId, attempt, statusCheck.FirstError.Description, cancellationToken);

            return statusCheck.FirstError;
        }

        var late = await AdmitAsync(userId, now, attempt, cancellationToken);
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
                _loginAttemptRepository, _passwordSettings, userId, attempt.IpAddress, cancellationToken))
        {
            await RecordFailureAsync(userId, attempt, LoginFailureReasons.SourceLocked, cancellationToken);

            return UserErrors.AccountLockedUntil(null);
        }

        if (admission.AdmittedUnder == AccountLockKind.None)
        {
            _accountLockout.ReportFamiliarAdmission(userId, late.AdmittedUnder, now.FailedAttempts);
        }

        return null;
    }

    /// <param name="Refusal">The answer when the lock refuses this attempt; null when it may go on.</param>
    /// <param name="AdmittedUnder">The automatic lock a familiar source was let through; None when no lock stood.</param>
    private readonly record struct LockAdmission(Error? Refusal, AccountLockKind AdmittedUnder);
}
