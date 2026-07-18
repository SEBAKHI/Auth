using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationOutboxMessageById;

/// <summary>
/// Query for one delivery-log entry including the exact rendered content.
/// </summary>
public record GetNotificationOutboxMessageByIdQuery(Guid MessageId)
    : IRequest<ErrorOr<NotificationOutboxMessageDetailDto>>;
