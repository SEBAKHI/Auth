using Auth.Application.Interfaces;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.RetryNotificationOutboxMessage;

/// <summary>
/// Handler for requeuing a failed message: resets it to Pending/now and wakes
/// the dispatcher so the retry happens immediately.
/// </summary>
public class RetryNotificationOutboxMessageCommandHandler
    : IRequestHandler<RetryNotificationOutboxMessageCommand, ErrorOr<Success>>
{
    private readonly INotificationOutboxRepository _outboxRepository;
    private readonly INotificationDispatchSignal _dispatchSignal;
    private readonly ILogger<RetryNotificationOutboxMessageCommandHandler> _logger;

    public RetryNotificationOutboxMessageCommandHandler(
        INotificationOutboxRepository outboxRepository,
        INotificationDispatchSignal dispatchSignal,
        ILogger<RetryNotificationOutboxMessageCommandHandler> logger)
    {
        _outboxRepository = outboxRepository;
        _dispatchSignal = dispatchSignal;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RetryNotificationOutboxMessageCommand request,
        CancellationToken cancellationToken)
    {
        var message = await _outboxRepository.GetByIdAsync(request.MessageId, cancellationToken);
        if (message is null)
        {
            return NotificationErrors.OutboxMessageNotFound(request.MessageId);
        }

        var requeued = await _outboxRepository.RequeueAsync(request.MessageId, cancellationToken);
        if (!requeued)
        {
            return NotificationErrors.OutboxMessageNotRetryable;
        }

        _dispatchSignal.Notify();

        _logger.LogInformation(
            "Outbox message {MessageId} ({TypeCode}) requeued for retry by {RequestedBy}",
            request.MessageId, message.NotificationTypeCode, request.RequestedBy);

        return Result.Success;
    }
}
