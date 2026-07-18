using Auth.Application.Features.Notifications.RetryNotificationOutboxMessage;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Notifications.Commands;

/// <summary>
/// Tests for requeuing failed delivery-log messages.
/// </summary>
public class RetryNotificationOutboxMessageCommandHandlerTests
{
    private readonly Mock<INotificationOutboxRepository> _outboxRepoMock = new();
    private readonly Mock<INotificationDispatchSignal> _signalMock = new();
    private readonly RetryNotificationOutboxMessageCommandHandler _handler;

    public RetryNotificationOutboxMessageCommandHandlerTests()
    {
        _handler = new RetryNotificationOutboxMessageCommandHandler(
            _outboxRepoMock.Object,
            _signalMock.Object,
            new Mock<ILogger<RetryNotificationOutboxMessageCommandHandler>>().Object);
    }

    private static NotificationOutboxMessage CreateMessage(NotificationDeliveryStatus status)
    {
        return new NotificationOutboxMessage(
            Guid.NewGuid(), "password-reset", NotificationChannelType.Email,
            null, "user@example.com", null, null, "en", null, null, null,
            "Subject", "<p>Body</p>", null, status, 5,
            DateTime.UtcNow, null, null, "smtp timeout", DateTime.UtcNow, null);
    }

    [Fact]
    public async Task Handle_DeadMessage_RequeuesAndSignalsDispatcher()
    {
        var message = CreateMessage(NotificationDeliveryStatus.Dead);
        _outboxRepoMock
            .Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        _outboxRepoMock
            .Setup(r => r.RequeueAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.Handle(
            new RetryNotificationOutboxMessageCommand(message.Id) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeFalse();
        _signalMock.Verify(s => s.Notify(), Times.Once);
    }

    [Fact]
    public async Task Handle_SentMessage_ReturnsNotRetryableConflict()
    {
        var message = CreateMessage(NotificationDeliveryStatus.Sent);
        _outboxRepoMock
            .Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        _outboxRepoMock
            .Setup(r => r.RequeueAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.Handle(
            new RetryNotificationOutboxMessageCommand(message.Id) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.OutboxMessageNotRetryable");
        _signalMock.Verify(s => s.Notify(), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownMessage_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _outboxRepoMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationOutboxMessage?)null);

        var result = await _handler.Handle(
            new RetryNotificationOutboxMessageCommand(id) { RequestedBy = Guid.NewGuid() },
            CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("Notification.OutboxMessageNotFound");
    }
}
