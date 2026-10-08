using System.Text.Json;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Modules.Authentication.EventHandlers;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// S08 PR B: the audit rows of the recovery layer — new recovery codes, a new
/// authenticator, an administrator's reset (the account as UserId, the
/// administrator as PerformedBy) — and of a step-up (design D-B8: an audit row and
/// no notice). A failure to write a row never reaches the caller of a change that
/// has already committed.
/// </summary>
public class TwoFactorRecoveryAuditEventHandlerTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Administrator = Guid.NewGuid();

    private readonly Mock<IAuditLogRepository> _repository = new();
    private AuditLog? _written;

    public TwoFactorRecoveryAuditEventHandlerTests()
    {
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => _written = log)
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task RecoveryCodesRegenerated_WritesASecurityRow()
    {
        await new TwoFactorRecoveryCodesRegeneratedAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<TwoFactorRecoveryCodesRegeneratedAuditEventHandler>>())
            .Handle(new TwoFactorRecoveryCodesRegeneratedEvent(Owner, Owner, "owner@example.org", "Jane", null), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.TwoFactorRecoveryCodesRegenerated);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        _written.UserId.Should().Be(Owner);
        _written.PerformedBy.Should().Be(Owner);
    }

    [Fact]
    public async Task AuthenticatorReplaced_WritesASecurityRow()
    {
        await new TwoFactorAuthenticatorReplacedAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<TwoFactorAuthenticatorReplacedAuditEventHandler>>())
            .Handle(new TwoFactorAuthenticatorReplacedEvent(Owner, Owner, "owner@example.org", "Jane", null), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.TwoFactorAuthenticatorReplaced);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
    }

    [Fact]
    public async Task ResetByAdministrator_NamesTheAccountAndTheAdministrator()
    {
        await new TwoFactorResetAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<TwoFactorResetAuditEventHandler>>())
            .Handle(new TwoFactorResetEvent(Owner, Administrator, "owner@example.org", "Jane"), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.TwoFactorResetByAdministrator);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        _written.UserId.Should().Be(Owner, "UserId is the subject: the account that lost its factor");
        _written.PerformedBy.Should().Be(Administrator, "PerformedBy is the actor");
    }

    [Fact]
    public async Task SteppedUp_WritesTheSessionAndTheMethod_AsJson()
    {
        var session = Guid.NewGuid();

        await new TwoFactorSteppedUpAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<TwoFactorSteppedUpAuditEventHandler>>())
            .Handle(new TwoFactorSteppedUpEvent(Owner, session, SecondFactorMethod.RecoveryCode), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.TwoFactorSteppedUp);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        _written.SessionId.Should().Be(session);
        JsonDocument.Parse(_written.AdditionalData!).RootElement.GetProperty("method").GetString()
            .Should().Be("RecoveryCode", "serialized by System.Text.Json, never interpolated (ARR-1058)");
    }

    [Fact]
    public void SteppedUp_SendsNoNotice()
    {
        // D-B8: the seeded templates have no step-up branch, and nothing about the
        // account changed — so no notification handler subscribes to the event.
        typeof(TwoFactorChangedNotificationEventHandler).GetInterfaces()
            .Should().NotContain(typeof(INotificationHandler<TwoFactorSteppedUpEvent>));

        var subscribers = typeof(TwoFactorSteppedUpAuditEventHandler).Assembly.GetTypes()
            .Where(type => typeof(INotificationHandler<TwoFactorSteppedUpEvent>).IsAssignableFrom(type));
        subscribers.Should().Equal(typeof(TwoFactorSteppedUpAuditEventHandler));
    }

    [Fact]
    public async Task AFailedWrite_DoesNotReachTheCaller()
    {
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var act = () => new TwoFactorResetAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<TwoFactorResetAuditEventHandler>>())
            .Handle(new TwoFactorResetEvent(Owner, Administrator, "owner@example.org", "Jane"), CancellationToken.None);

        await act.Should().NotThrowAsync("the reset has already committed and revoked the account's sessions");
    }
}
