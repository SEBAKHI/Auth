using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.PublishNotificationTemplate;

/// <summary>
/// Command to publish the pending draft. Every translation is rendered against
/// the type's sample data first (syntax + unknown-variable gate), so a broken
/// template can never go live.
/// </summary>
public record PublishNotificationTemplateCommand(Guid TemplateId)
    : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid PublishedBy { get; init; }
}
