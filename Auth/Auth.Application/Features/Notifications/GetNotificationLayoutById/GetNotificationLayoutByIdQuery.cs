using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationLayoutById;

/// <summary>
/// Query for one notification layout (editor view).
/// </summary>
public record GetNotificationLayoutByIdQuery(Guid LayoutId) : IRequest<ErrorOr<NotificationLayoutDto>>;
