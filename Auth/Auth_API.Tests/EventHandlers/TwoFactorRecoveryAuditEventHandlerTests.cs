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
    public async Task Reauthenticated_WritesTheSessionAndWhatTheProofProved_AsJson()
    {
        var session = Guid.NewGuid();
        var methods = Auth.Domain.ValueObjects.AuthenticationMethods.Password
            .With(Auth.Domain.ValueObjects.AuthenticationMethods.Totp);

        await new UserReauthenticatedAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<UserReauthenticatedAuditEventHandler>>())
            .Handle(new UserReauthenticatedEvent(Owner, session, methods, "203.0.113.7", "Agent/1.0"), CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.SessionReauthenticated);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        _written.IsSuccess.Should().BeTrue();
        _written.SessionId.Should().Be(session);
        // REVIEW-FIXES 152 F25: where the proof came from, as a sign-in's row says it. Deliberate
        // break, verified red: the handler's ipAddress argument dropped.
        _written.IpAddress.Should().Be("203.0.113.7");
        _written.UserAgent.Should().Be("Agent/1.0");
        var details = JsonDocument.Parse(_written.AdditionalData!).RootElement;
        details.GetProperty("methods").GetInt32().Should().Be(methods.Value);
        details.GetProperty("amr").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("pwd", "otp", "mfa");
    }

    [Fact]
    public async Task Reauthenticated_AFailedWrite_NeverReachesTheProof()
    {
        _repository.Setup(r => r.CreateAsync(It.IsAny<Auth.Domain.Entities.AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no database"));

        var act = () => new UserReauthenticatedAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<UserReauthenticatedAuditEventHandler>>())
            .Handle(
                new UserReauthenticatedEvent(Owner, Guid.NewGuid(), Auth.Domain.ValueObjects.AuthenticationMethods.Password, null, null),
                CancellationToken.None);

        await act.Should().NotThrowAsync("the session is already stamped");
    }

    [Fact]
    public async Task ReauthenticationFailed_WritesAFailureRowOfTheSameAction_WithTheReasonAndWhereItCameFrom()
    {
        // REVIEW-FIXES 152 F25: a right password and a refused second step. No new action, so the
        // console's catalog does not move; never the code that was given.
        var session = Guid.NewGuid();

        await new UserReauthenticationFailedAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<UserReauthenticationFailedAuditEventHandler>>())
            .Handle(
                new UserReauthenticationFailedEvent(Owner, session, "User.InvalidTwoFactorCode", "203.0.113.7", "Agent/1.0"),
                CancellationToken.None);

        _written!.Action.Should().Be(AuditActions.SessionReauthenticated);
        _written.ActionType.Should().Be(AuditActionTypes.Security);
        _written.IsSuccess.Should().BeFalse();
        _written.ErrorMessage.Should().Be("User.InvalidTwoFactorCode");
        _written.UserId.Should().Be(Owner);
        _written.SessionId.Should().Be(session);
        _written.IpAddress.Should().Be("203.0.113.7");
        _written.UserAgent.Should().Be("Agent/1.0");
    }

    [Fact]
    public async Task ReauthenticationFailed_AFailedWrite_NeverReachesTheRefusal()
    {
        _repository.Setup(r => r.CreateAsync(It.IsAny<Auth.Domain.Entities.AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no database"));

        var act = () => new UserReauthenticationFailedAuditEventHandler(
                _repository.Object, Mock.Of<ILogger<UserReauthenticationFailedAuditEventHandler>>())
            .Handle(new UserReauthenticationFailedEvent(Owner, Guid.NewGuid(), "User.InvalidTwoFactorCode", null, null), CancellationToken.None);

        await act.Should().NotThrowAsync("the refusal is already the answer");
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

    // F8 (a): each of the four handlers, not one of them. A write that fails is
    // logged and swallowed — the change it records has committed — while the
    // cancellation of the handler's own token still propagates.
    public static TheoryData<string> Handlers => new()
    {
        nameof(TwoFactorRecoveryCodesRegeneratedAuditEventHandler),
        nameof(TwoFactorAuthenticatorReplacedAuditEventHandler),
        nameof(TwoFactorResetAuditEventHandler),
        nameof(TwoFactorSteppedUpAuditEventHandler),
    };

    private static Task Run(string handler, IAuditLogRepository repository, CancellationToken cancellationToken) => handler switch
    {
        nameof(TwoFactorRecoveryCodesRegeneratedAuditEventHandler) => new TwoFactorRecoveryCodesRegeneratedAuditEventHandler(
                repository, Mock.Of<ILogger<TwoFactorRecoveryCodesRegeneratedAuditEventHandler>>())
            .Handle(new TwoFactorRecoveryCodesRegeneratedEvent(Owner, Owner, "owner@example.org", "Jane", null), cancellationToken),
        nameof(TwoFactorAuthenticatorReplacedAuditEventHandler) => new TwoFactorAuthenticatorReplacedAuditEventHandler(
                repository, Mock.Of<ILogger<TwoFactorAuthenticatorReplacedAuditEventHandler>>())
            .Handle(new TwoFactorAuthenticatorReplacedEvent(Owner, Owner, "owner@example.org", "Jane", null), cancellationToken),
        nameof(TwoFactorResetAuditEventHandler) => new TwoFactorResetAuditEventHandler(
                repository, Mock.Of<ILogger<TwoFactorResetAuditEventHandler>>())
            .Handle(new TwoFactorResetEvent(Owner, Administrator, "owner@example.org", "Jane"), cancellationToken),
        nameof(TwoFactorSteppedUpAuditEventHandler) => new TwoFactorSteppedUpAuditEventHandler(
                repository, Mock.Of<ILogger<TwoFactorSteppedUpAuditEventHandler>>())
            .Handle(new TwoFactorSteppedUpEvent(Owner, Guid.NewGuid(), SecondFactorMethod.Totp), cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(handler), handler, null),
    };

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task EveryHandler_SwallowsAFailedWrite(string handler)
    {
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var act = () => Run(handler, _repository.Object, CancellationToken.None);

        await act.Should().NotThrowAsync("the change the row records has already committed");
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task EveryHandler_LetsItsOwnCancellationThrough(string handler)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelled.Token));

        var act = () => Run(handler, _repository.Object, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
