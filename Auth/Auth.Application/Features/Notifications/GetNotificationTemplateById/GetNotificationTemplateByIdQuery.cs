using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTemplateById;

/// <summary>
/// Query for the full editor view of one notification template.
/// </summary>
public record GetNotificationTemplateByIdQuery(Guid TemplateId)
    : IRequest<ErrorOr<NotificationTemplateDetailDto>>;
