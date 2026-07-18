using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.RetryNotificationOutboxMessage;

/// <summary>
/// Command to requeue a failed (Retry/Dead) delivery-log message for immediate
/// dispatch. The stored rendered content is resent as-is.
/// </summary>
public record RetryNotificationOutboxMessageCommand(Guid MessageId) : IRequest<ErrorOr<Success>>
{
    public Guid RequestedBy { get; init; }
}
