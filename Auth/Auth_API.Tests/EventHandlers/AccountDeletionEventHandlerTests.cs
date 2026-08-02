using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Modules.UserManagement.EventHandlers;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.EventHandlers;

public class AccountDeletionRequestedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();

    [Fact]
    public async Task Handle_CreatesAuditEntry_AttributedToTheUser()
    {
        var handler = new AccountDeletionRequestedAuditEventHandler(
            _repoMock.Object, new Mock<ILogger<AccountDeletionRequestedAuditEventHandler>>().Object);
        var userId = Guid.NewGuid();
        var evt = new AccountDeletionRequestedEvent(
            userId, "user@example.com", "Jane", AccountDeletionSource.InApp, DateTime.UtcNow.AddDays(30));

        await handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(
            It.Is<AuditLog>(log =>
                log.Action == "user.deletion_requested" && log.UserId == userId && log.EntityId == userId),
            It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class AccountDeletionCancelledAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();

    [Fact]
    public async Task Handle_CreatesAuditEntry()
    {
        var handler = new AccountDeletionCancelledAuditEventHandler(
            _repoMock.Object, new Mock<ILogger<AccountDeletionCancelledAuditEventHandler>>().Object);
        var userId = Guid.NewGuid();
        var evt = new AccountDeletionCancelledEvent(userId, "user@example.com", "Jane", DateTime.UtcNow);

        await handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(
            It.Is<AuditLog>(log => log.Action == "user.deletion_cancelled" && log.UserId == userId),
            It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class AccountDeletionCompletedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();

    [Fact]
    public async Task Handle_CreatesDestructionEvidence_SystemAttributedAndZeroPii()
    {
        AuditLog? captured = null;
        _repoMock
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => captured = log)
            .Returns(Task.CompletedTask);
        var handler = new AccountDeletionCompletedAuditEventHandler(
            _repoMock.Object, new Mock<ILogger<AccountDeletionCompletedAuditEventHandler>>().Object);
        var userId = Guid.NewGuid();
        var evt = new AccountDeletionCompletedEvent(
            userId, "user@example.com", "Jane Doe", "2026.07", ExternalRevocationFailed: true);

        await handler.Handle(evt, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be("user.deletion_completed");
        captured.UserId.Should().Be(WellKnownUserIds.System,
            "the destroyed account must not be the actor of its own destruction evidence");
        captured.EntityId.Should().Be(userId);
        captured.AdditionalData.Should().Contain("2026.07").And.Contain("true");
        captured.AdditionalData.Should().NotContain("user@example.com",
            "destruction evidence must carry zero PII");
        captured.AdditionalData.Should().NotContain("Jane");
    }
}

public class AccountDeletionNotificationEventHandlerTests
{
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    public AccountDeletionNotificationEventHandlerTests()
    {
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);
    }

    [Fact]
    public async Task Requested_SendsAcknowledgmentWithGraceDeadlineAndRecoveryLink()
    {
        NotificationRequest? captured = null;
        _notificationServiceMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(Result.Success);
        var handler = new AccountDeletionRequestedNotificationEventHandler(
            _notificationServiceMock.Object,
            TestHelpers.CreateOptions(new AccountDeletionSettings()),
            TestHelpers.CreateOptions(new EmailSettings { FrontendBaseUrl = "https://accounts.example.com/" }),
            new Mock<ILogger<AccountDeletionRequestedNotificationEventHandler>>().Object);
        var userId = Guid.NewGuid();

        await handler.Handle(
            new AccountDeletionRequestedEvent(
                userId, "user@example.com", "Jane", AccountDeletionSource.PublicWeb, DateTime.UtcNow.AddDays(30)),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.TypeCode.Should().Be(NotificationTypeCodes.AccountDeletionRequested);
        captured.RecipientAddress.Should().Be("user@example.com");
        captured.RecipientUserId.Should().Be(userId);
        captured.Variables["GraceDays"].Should().Be(30);
        captured.Variables["RecoveryLink"].Should().Be("https://accounts.example.com/account-recovery");
    }

    [Fact]
    public async Task Cancelled_SendsRecoveryConfirmationToTheUser()
    {
        var handler = new AccountDeletionCancelledNotificationEventHandler(
            _notificationServiceMock.Object,
            new Mock<ILogger<AccountDeletionCancelledNotificationEventHandler>>().Object);
        var userId = Guid.NewGuid();

        await handler.Handle(
            new AccountDeletionCancelledEvent(userId, "user@example.com", "Jane", DateTime.UtcNow),
            CancellationToken.None);

        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.TypeCode == NotificationTypeCodes.AccountDeletionCancelled
                && r.RecipientAddress == "user@example.com"
                && r.RecipientUserId == userId),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Completed_SendsToTheSnapshotAddressWithoutAUserReference()
    {
        var handler = new AccountDeletionCompletedNotificationEventHandler(
            _notificationServiceMock.Object,
            new Mock<ILogger<AccountDeletionCompletedNotificationEventHandler>>().Object);

        await handler.Handle(
            new AccountDeletionCompletedEvent(
                Guid.NewGuid(), "user@example.com", "Jane", "2026.07", ExternalRevocationFailed: false),
            CancellationToken.None);

        _notificationServiceMock.Verify(s => s.SendAsync(
            It.Is<NotificationRequest>(r =>
                r.TypeCode == NotificationTypeCodes.AccountDeletionCompleted
                && r.RecipientAddress == "user@example.com"
                && r.RecipientUserId == null),
            It.IsAny<CancellationToken>()), Times.Once());
    }
}
