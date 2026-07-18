using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationLayouts;

/// <summary>
/// Query for all notification layouts (global first, then app-specific).
/// </summary>
public record GetNotificationLayoutsQuery : IRequest<ErrorOr<List<NotificationLayoutDto>>>;
