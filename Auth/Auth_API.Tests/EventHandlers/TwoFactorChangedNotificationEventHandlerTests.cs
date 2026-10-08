using Auth_API.Modules.Authentication.EventHandlers;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

/// <summary>
/// The notice that tells the owner their second factor was switched on or off —
/// the only signal they get when the change was not theirs — and the promise that
/// a delivery failure never reaches the caller of a change that has already
/// committed.
/// </summary>
public class TwoFactorChangedNotificationEventHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 14, 0, TimeSpan.Zero);

    private readonly Mock<INotificationService> _notificationServiceMock = new();
    private readonly Mock<ILogger<TwoFactorChangedNotificationEventHandler>> _loggerMock = new();
    private readonly TwoFactorChangedNotificationEventHandler _handler;
    private NotificationRequest? _sent;

    public TwoFactorChangedNotificationEventHandlerTests()
    {
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((request, _) => _sent = request)
            .ReturnsAsync(Result.Success);

        _handler = new TwoFactorChangedNotificationEventHandler(
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(new EmailSettings { FrontendBaseUrl = "https://accounts.example.com/" }),
            new FixedTimeProvider(Now),
            _loggerMock.Object);
    }

    public static TheoryData<string, Func<TwoFactorChangedNotificationEventHandler, Guid, Task>> Changes => new()
    {
        {
            TwoFactorChangeKinds.Enabled,
            (handler, userId) => handler.Handle(
                new TwoFactorEnabledEvent(userId, userId, "owner@example.com", "Jane Doe", "Chrome on Windows"),
                CancellationToken.None)
        },
        {
            TwoFactorChangeKinds.Disabled,
            (handler, userId) => handler.Handle(
                new TwoFactorDisabledEvent(userId, userId, "owner@example.com", "Jane Doe", "Chrome on Windows"),
                CancellationToken.None)
        },
        // S08 PR B.
        {
            TwoFactorChangeKinds.RecoveryCodesRegenerated,
            (handler, userId) => handler.Handle(
                new TwoFactorRecoveryCodesRegeneratedEvent(userId, userId, "owner@example.com", "Jane Doe", "Chrome on Windows"),
                CancellationToken.None)
        },
        {
            TwoFactorChangeKinds.AuthenticatorReplaced,
            (handler, userId) => handler.Handle(
                new TwoFactorAuthenticatorReplacedEvent(userId, userId, "owner@example.com", "Jane Doe", "Chrome on Windows"),
                CancellationToken.None)
        },
    };

    [Fact]
    public async Task Handle_ResetByAnAdministrator_TellsTheOwner_WithoutADevice()
    {
        var owner = Guid.NewGuid();
        var administrator = Guid.NewGuid();

        await _handler.Handle(new TwoFactorResetEvent(owner, administrator, "owner@example.com", "Jane Doe"), CancellationToken.None);

        _sent!.RecipientUserId.Should().Be(owner);
        _sent.RecipientAddress.Should().Be("owner@example.com");
        _sent.TriggeredBy.Should().Be(administrator);
        _sent.Variables["ChangeKind"].Should().Be(TwoFactorChangeKinds.ResetByAdministrator);
        _sent.Variables["DeviceName"].Should().BeNull("the administrator's browser is not one of the owner's");
    }

    /// <summary>
    /// The templates branch on these exact strings (seeded in deploy 1); a kind
    /// they do not know falls to the generic "changed" wording. So every kind the
    /// handler sends is one the seed branches on, and the list is the seed's.
    /// </summary>
    [Fact]
    public void EveryChangeKind_IsOneTheSeededTemplatesBranchOn()
    {
        var kinds = typeof(TwoFactorChangeKinds).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        kinds.Should().BeEquivalentTo(
            ["enabled", "disabled", "recovery-codes-regenerated", "authenticator-replaced", "reset-by-administrator"]);

        var templates = File.ReadAllText(Path.Combine(
            Auth_API.Tests.Infrastructure.ApiSourceScan.SolutionDirectory(),
            "Auth_DB", "dbo", "Scripts", "SeedData", "12_NotificationTemplates.sql"));
        foreach (var kind in kinds)
        {
            templates.Should().Contain($"{{% when \"{kind}\" %}}", $"the seeded templates must branch on '{kind}'");
        }
    }

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Handle_SendsTwoFactorChanged_WithKind(
        string expectedKind,
        Func<TwoFactorChangedNotificationEventHandler, Guid, Task> handle)
    {
        var userId = Guid.NewGuid();

        await handle(_handler, userId);

        _sent.Should().NotBeNull();
        _sent!.TypeCode.Should().Be(NotificationTypeCodes.TwoFactorChanged);
        _sent.RecipientAddress.Should().Be("owner@example.com");
        _sent.RecipientName.Should().Be("Jane Doe");
        _sent.RecipientUserId.Should().Be(userId);
        _sent.ApplicationId.Should().BeNull("the platform's own security notice uses its global templates");
        _sent.TriggeredBy.Should().Be(userId);

        // The five variables of the type's catalog, and nothing else.
        _sent.Variables.Keys.Should().BeEquivalentTo(
            ["UserName", "ChangeKind", "OccurredAtUtc", "DeviceName", "ManageSecurityLink"]);
        _sent.Variables["UserName"].Should().Be("Jane Doe");
        _sent.Variables["ChangeKind"].Should().Be(expectedKind);
        _sent.Variables["OccurredAtUtc"].Should().Be("2026-10-02 09:14:00Z");
        _sent.Variables["DeviceName"].Should().Be("Chrome on Windows");
        _sent.Variables["ManageSecurityLink"].Should().Be("https://accounts.example.com/profile?tab=security",
            "the ordinary security tab of the app Email:FrontendBaseUrl names, as in the password notices — never a one-click undo a mail scanner would trigger");
    }

    [Fact]
    public async Task Handle_UnknownDevice_LeavesTheVariableEmpty()
    {
        await _handler.Handle(
            new TwoFactorDisabledEvent(Guid.NewGuid(), Guid.NewGuid(), "owner@example.com", "Jane Doe", null),
            CancellationToken.None);

        _sent!.Variables["DeviceName"].Should().BeNull();
    }

    [Fact]
    public async Task Handle_SendThrows_DoesNotPropagate()
    {
        // Writing the outbox can throw. The change has already committed, and the
        // audit handler of the same event runs after this one: an exception here
        // would skip it and turn the user's completed change into an error.
        var userId = Guid.NewGuid();
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));

        var act = () => _handler.Handle(
            new TwoFactorDisabledEvent(userId, userId, "owner@example.com", "Jane Doe", null),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(userId.ToString()) && v.ToString()!.Contains("disabled")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SendReturnsAnError_IsLoggedAndNotPropagated()
    {
        var userId = Guid.NewGuid();
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Notification.SendFailed", "template missing"));

        await _handler.Handle(
            new TwoFactorEnabledEvent(userId, userId, "owner@example.com", "Jane Doe", null),
            CancellationToken.None);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(userId.ToString()) && v.ToString()!.Contains("template missing")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CancelledByItsOwnToken_IsNotSwallowed()
    {
        // Cancellation is not a delivery failure: it is neither logged as one nor
        // hidden from the caller that cancelled.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => _handler.Handle(
            new TwoFactorEnabledEvent(Guid.NewGuid(), Guid.NewGuid(), "owner@example.com", "Jane Doe", null),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
