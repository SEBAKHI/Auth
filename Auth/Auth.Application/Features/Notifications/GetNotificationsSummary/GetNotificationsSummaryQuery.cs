using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.GetNotificationsSummary;

/// <summary>
/// Query for the notifications section overview: template, layout and delivery
/// counts plus what is currently published.
/// </summary>
public record GetNotificationsSummaryQuery : IRequest<ErrorOr<NotificationsSummaryDto>>;
