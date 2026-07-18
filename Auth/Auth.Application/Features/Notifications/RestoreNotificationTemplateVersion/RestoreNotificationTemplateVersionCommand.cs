using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.RestoreNotificationTemplateVersion;

/// <summary>
/// Command to create a new draft as a copy of a historical version. Fails when a
/// draft is already pending (unsaved edits are never silently discarded).
/// </summary>
public record RestoreNotificationTemplateVersionCommand(Guid TemplateId, Guid SourceVersionId)
    : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid RestoredBy { get; init; }
}
