using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.DiscardNotificationTemplateDraft;

/// <summary>
/// Command to discard the pending draft version and its translations.
/// </summary>
public record DiscardNotificationTemplateDraftCommand(Guid TemplateId)
    : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid DiscardedBy { get; init; }
}
