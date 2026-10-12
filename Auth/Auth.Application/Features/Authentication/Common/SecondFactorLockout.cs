using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using MediatR;
using Microsoft.Extensions.Options;

namespace Auth.Application.Features.Authentication.Common;

/// <inheritdoc cref="ISecondFactorLockout"/>
/// <remarks>
/// The line names the account and the count. It never carries the email address,
/// a code, the client address, the device id or the user agent: the attempts are
/// in the sign-in history with those, for the people allowed to read them.
/// </remarks>
public sealed class SecondFactorLockout : ISecondFactorLockout
{
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecondFactorLockout> _logger;

    public SecondFactorLockout(
        IPublisher publisher,
        TimeProvider timeProvider,
        IOptionsSnapshot<PasswordSettings> passwordSettings,
        ILogger<SecondFactorLockout> logger)
    {
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
        HardLimit = passwordSettings.Value.EnforcedHardLimit;
    }

    /// <inheritdoc />
    public int? HardLimit { get; }

    /// <inheritdoc />
    public async Task ReportReservationAsync(Guid userId, int failedAttempts)
    {
        if (HardLimit is not { } limit || failedAttempts != limit)
        {
            return;
        }

        _logger.LogWarning(
            "{EventCode}: the second factor of account {UserId} accepts no code after {FailedAttempts} consecutive wrong codes",
            AccountLockSignalCodes.TwoFactorHardLocked, userId, failedAttempts);

        var hardLocked = new UserHardLockedEvent(
            EventId: Guid.NewGuid(),
            UserId: userId,
            Factor: HardLockedFactor.SecondFactor,
            FailedAttempts: failedAttempts,
            TriggeredBy: null,
            OccurredAt: _timeProvider.GetUtcNow());

        // After the write, and not cancellable by the caller: the count is
        // committed, so its audit row and its notices must not depend on the
        // client waiting. Nor may their failure reach the caller: this runs before
        // the code is checked, so an exception here (a handler that could not be
        // built, say; the handlers catch their own) would answer a correct code
        // with an error. The Warning line above already says the lock happened.
        try
        {
            await _publisher.Publish(hardLocked, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "{EventCode}: the hard lock of account {UserId}'s second factor could not be published (event {EventId}); its audit row and notices are missing",
                AccountLockSignalCodes.TwoFactorHardLocked, userId, hardLocked.EventId);
        }
    }
}
