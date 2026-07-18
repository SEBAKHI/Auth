using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.CreateNotificationTemplate;

/// <summary>
/// Command to create a new notification template (starts as an empty draft v1).
/// ApplicationId = null creates the global fallback template for the type/channel.
/// </summary>
public record CreateNotificationTemplateCommand(
    Guid NotificationTypeId,
    Guid? ApplicationId,
    NotificationChannelType Channel,
    string DefaultLanguage) : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid CreatedBy { get; init; }
}
