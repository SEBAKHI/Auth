using System.Text.Json;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// S24 PR B: the audit rows of the emailed code switched on and off — a Security row
/// on the account, its details serialized by System.Text.Json (ARR-1058) — and a
/// failure to write one never reaches the caller of a change that has committed.
/// </summary>
public class TwoFactorEmailCodeAuditEventHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private readonly Mock<IAuditLogRepository> _repository = new();
    private AuditLog? _written;

    public TwoFactorEmailCodeAuditEventHandlerTests()
    {
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => _written = log)
            .Returns(Task.CompletedTask);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enabled_WritesASecurityRow_SayingWhetherItReplacedTheApp(bool replaced)
    {
        await new TwoFactorEmailCodeEnabledAuditEventHandler(_repository.Object, Mock.Of<ILogger<TwoFactorEmailCodeEnabledAuditEventHandler>>())
            .Handle(new TwoFactorEmailCodeEnabledEvent(Owner, Owner, "owner@example.org", "Jane", null, replaced), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.TwoFactorEmailCodeEnabled);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        _written.UserId.Should().Be(Owner);
        _written.PerformedBy.Should().Be(Owner);
        JsonDocument.Parse(_written.AdditionalData!).RootElement.GetProperty("replacedAuthenticator").GetBoolean().Should().Be(replaced);
    }

    [Theory]
    [InlineData(TwoFactorEmailCodeDisableReasons.User)]
    [InlineData(TwoFactorEmailCodeDisableReasons.ReplacedByAuthenticator)]
    public async Task Disabled_WritesASecurityRow_WithItsReason(string reason)
    {
        await new TwoFactorEmailCodeDisabledAuditEventHandler(_repository.Object, Mock.Of<ILogger<TwoFactorEmailCodeDisabledAuditEventHandler>>())
            .Handle(new TwoFactorEmailCodeDisabledEvent(Owner, Owner, "owner@example.org", "Jane", null, reason), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.TwoFactorEmailCodeDisabled);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        JsonDocument.Parse(_written.AdditionalData!).RootElement.GetProperty("reason").GetString().Should().Be(reason);
    }

    [Fact]
    public async Task AFailedWrite_DoesNotReachTheCaller_ButTheHandlersOwnCancellationDoes()
    {
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var enabled = () => new TwoFactorEmailCodeEnabledAuditEventHandler(_repository.Object, Mock.Of<ILogger<TwoFactorEmailCodeEnabledAuditEventHandler>>())
            .Handle(new TwoFactorEmailCodeEnabledEvent(Owner, Owner, "owner@example.org", "Jane", null, false), CancellationToken.None);
        var disabled = () => new TwoFactorEmailCodeDisabledAuditEventHandler(_repository.Object, Mock.Of<ILogger<TwoFactorEmailCodeDisabledAuditEventHandler>>())
            .Handle(new TwoFactorEmailCodeDisabledEvent(Owner, Owner, "owner@example.org", "Jane", null, TwoFactorEmailCodeDisableReasons.User), CancellationToken.None);

        await enabled.Should().NotThrowAsync("the emailed code is already on");
        await disabled.Should().NotThrowAsync("the emailed code is already off");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));
        var stopped = () => new TwoFactorEmailCodeEnabledAuditEventHandler(_repository.Object, Mock.Of<ILogger<TwoFactorEmailCodeEnabledAuditEventHandler>>())
            .Handle(new TwoFactorEmailCodeEnabledEvent(Owner, Owner, "owner@example.org", "Jane", null, false), cancelled.Token);
        await stopped.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Review of #107: the app's switch-on and switch-off audit handlers run before the
    /// owner's one notice of a replacement (and, switched on, before the recovery codes
    /// reach the caller), so a failed write is logged there too, never propagated.
    /// </summary>
    [Fact]
    public async Task TheAppsOwnHandlers_LogAFailedWrite_AndDoNotReachTheCaller()
    {
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var enabledLogger = new Mock<ILogger<TwoFactorEnabledAuditEventHandler>>();
        var disabledLogger = new Mock<ILogger<TwoFactorDisabledAuditEventHandler>>();

        var enabled = () => new TwoFactorEnabledAuditEventHandler(_repository.Object, enabledLogger.Object)
            .Handle(new TwoFactorEnabledEvent(Owner, Owner, "owner@example.org", "Jane", null, ReplacedEmailCode: true), CancellationToken.None);
        var disabled = () => new TwoFactorDisabledAuditEventHandler(_repository.Object, disabledLogger.Object)
            .Handle(new TwoFactorDisabledEvent(Owner, Owner, "owner@example.org", "Jane", null, ReplacedByEmailCode: true), CancellationToken.None);

        await enabled.Should().NotThrowAsync("the app is already on");
        await disabled.Should().NotThrowAsync("the app is already off");
        VerifyLoggedError(enabledLogger);
        VerifyLoggedError(disabledLogger);
    }

    private static void VerifyLoggedError<T>(Mock<ILogger<T>> logger) =>
        logger.Verify(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
}
