using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc cref="IAccountLockout"/>
/// <remarks>
/// A line names the account, the count and the expiry. It never carries the email
/// address, the password, the client address, the device id or the user agent: the
/// sign-in that caused it is already in LoginAttempts with those, for the people
/// allowed to read them.
/// </remarks>
public sealed class AccountLockout : IAccountLockout
{
    private readonly IUserRepository _userRepository;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly PasswordSettings _passwordSettings;
    private readonly ILogger<AccountLockout> _logger;

    public AccountLockout(
        IUserRepository userRepository,
        IPublisher publisher,
        TimeProvider timeProvider,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<AccountLockout> logger)
    {
        _userRepository = userRepository;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _passwordSettings = passwordSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordPasswordFailureAsync(Guid userId, CancellationToken cancellationToken)
    {
        var transition = await _userRepository.RecordFailedLoginAsync(
            userId,
            _passwordSettings.EnforcedHardLimit,
            _passwordSettings.MaxFailedAttempts,
            _passwordSettings.LockoutDuration,
            cancellationToken);

        if (transition is null)
        {
            // Nothing counted: a live administrator's lock, a row that is no longer
            // active, or one deleted in between. Nothing changed, so nothing to say.
            return;
        }

        var now = UtcNow();
        if (transition.StartedHardLock(now))
        {
            _logger.LogWarning(
                "{EventCode}: account {UserId} locked with no expiry after {FailedAttempts} consecutive failed passwords",
                AccountLockSignalCodes.HardLocked, userId, transition.After.FailedAttempts);

            // After the write, and not cancellable by the caller: the lock is
            // committed, so its audit row must not depend on the client waiting.
            await _publisher.Publish(
                new UserHardLockedEvent(
                    EventId: Guid.NewGuid(),
                    UserId: userId,
                    Factor: HardLockedFactor.Password,
                    FailedAttempts: transition.After.FailedAttempts,
                    TriggeredBy: null,
                    OccurredAt: _timeProvider.GetUtcNow()),
                CancellationToken.None);
        }
        else if (transition.StartedTimedLock(now))
        {
            _logger.LogWarning(
                "{EventCode}: account {UserId} locked until {LockoutEndUtc} after {FailedAttempts} consecutive failed passwords",
                AccountLockSignalCodes.Locked, userId, transition.After.LockoutEndUtc, transition.After.FailedAttempts);
        }
    }

    /// <inheritdoc />
    public Task RepairLapsedLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Under Enforce the count survives the lapse: that is what makes the hard
        // limit reachable at all. Waiting out a lock used to wipe it, so the count
        // never passed five and a guesser had about 480 tries a day for ever.
        return _userRepository.RepairLapsedLockAsync(
            userId,
            resetFailureCount: _passwordSettings.ConsecutiveFailureMode == ConsecutiveFailureMode.Legacy,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClearAutomaticLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var transition = await _userRepository.ClearAutomaticLockAsync(userId, cancellationToken);
        ReportUnlock(transition, "a password reset or change");
    }

    /// <inheritdoc />
    public async Task RecordSignInAsync(
        Guid userId, string? ipAddress, bool passwordProven, CancellationToken cancellationToken)
    {
        var transition = await _userRepository.RecordSuccessfulLoginAsync(
            userId, ipAddress, passwordProven, cancellationToken);
        ReportUnlock(transition, "a sign-in that proved the password");
    }

    /// <inheritdoc />
    public void ReportFamiliarAdmission(Guid userId, AccountLockKind kind, int failedAttempts)
    {
        _logger.Log(
            kind == AccountLockKind.Hard ? LogLevel.Warning : LogLevel.Information,
            "{EventCode}: account {UserId} under a {LockKind} lock let through to the password check from a familiar source after {FailedAttempts} consecutive failed passwords",
            AccountLockSignalCodes.FamiliarAdmitted, userId, kind, failedAttempts);
    }

    private void ReportUnlock(AccountLockTransition? transition, string exit)
    {
        if (transition is null)
        {
            return;
        }

        var now = UtcNow();
        if (!transition.LiftedAutomaticLock(now))
        {
            return;
        }

        var previous = transition.Before.Kind(now);
        _logger.Log(
            previous == AccountLockKind.Hard ? LogLevel.Warning : LogLevel.Information,
            "{EventCode}: account {UserId} {LockKind} lock lifted by {Exit} after {FailedAttempts} consecutive failed passwords",
            AccountLockSignalCodes.Unlocked, transition.UserId, previous, exit, transition.Before.FailedAttempts);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
