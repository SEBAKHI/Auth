using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.UnpublishNotificationTemplate;

/// <summary>
/// Command to unpublish a template. Forbidden for the global template of a
/// system type — critical auth flows depend on it.
/// </summary>
public record UnpublishNotificationTemplateCommand(Guid TemplateId)
    : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid UnpublishedBy { get; init; }
}
