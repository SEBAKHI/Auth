using System.Text.Json;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Authentication;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// X12, LK8.5: the hard lock leaves an audit row (<c>user.hard-locked</c>, the account
/// as subject, no actor), and a failure to write it never reaches the sign-in that
/// caused it: the handler catches everything but its own cancellation and logs one
/// Error line with <c>audit.write-failed</c>, the only record of the missing row.
/// </summary>
/// <remarks>Deliberate break: let the repository's exception escape the handler.</remarks>
public class UserHardLockedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly LockSignalTests.CapturingLogger<UserHardLockedAuditEventHandler> _log = new();

    private static UserHardLockedEvent Event() => new(
        Guid.NewGuid(), Guid.NewGuid(), HardLockedFactor.Password, 100, null, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_WritesTheHardLockAsASecurityRow()
    {
        AuditLog? written = null;
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var notification = Event();

        await new UserHardLockedAuditEventHandler(_audit.Object, _log).Handle(notification, CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.UserHardLocked);
        written.Action.Should().Be("user.hard-locked");
        written.ActionType.Should().Be(AuditActionTypes.Security);
        written.UserId.Should().Be(notification.UserId);
        written.PerformedBy.Should().BeNull();
        using var data = JsonDocument.Parse(written.AdditionalData!);
        data.RootElement.GetProperty("factor").GetString().Should().Be("Password");
        data.RootElement.GetProperty("failedAttempts").GetInt32().Should().Be(100);
        data.RootElement.GetProperty("eventId").GetGuid().Should().Be(notification.EventId);
    }

    [Fact]
    public async Task Handle_TheSecondFactorsHardLock_IsItsOwnRow()
    {
        // X12 PR B: the same event and the same action for the second factor; the
        // factor in the row tells an administrator which reset is waiting.
        AuditLog? written = null;
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var notification = new UserHardLockedEvent(
            Guid.NewGuid(), Guid.NewGuid(), HardLockedFactor.SecondFactor, 100, null, DateTimeOffset.UtcNow);

        await new UserHardLockedAuditEventHandler(_audit.Object, _log).Handle(notification, CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be("user.hard-locked");
        written.UserId.Should().Be(notification.UserId);
        written.PerformedBy.Should().BeNull("wrong codes identify nobody");
        using var data = JsonDocument.Parse(written.AdditionalData!);
        data.RootElement.GetProperty("factor").GetString().Should().Be("SecondFactor");
        data.RootElement.GetProperty("failedAttempts").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task Handle_WhenTheWriteFails_LogsOnceAndDoesNotThrow()
    {
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => new UserHardLockedAuditEventHandler(_audit.Object, _log).Handle(Event(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        var line = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        line.Values["EventCode"].Should().Be(UserHardLockedAuditEventHandler.AuditWriteFailedEventCode);
        UserHardLockedAuditEventHandler.AuditWriteFailedEventCode.Should().Be("audit.write-failed");
    }

    [Fact]
    public async Task Handle_WhenItsOwnTokenIsCancelled_LetsTheCancellationThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _audit
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => new UserHardLockedAuditEventHandler(_audit.Object, _log).Handle(Event(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _log.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }
}
