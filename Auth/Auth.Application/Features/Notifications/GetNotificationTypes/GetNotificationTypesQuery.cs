using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationTypes;

/// <summary>
/// Query for all notification types (create-template dialog + variable palettes).
/// </summary>
public record GetNotificationTypesQuery : IRequest<ErrorOr<List<NotificationTypeDto>>>;
