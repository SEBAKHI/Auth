using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.RollbackNotificationTemplate;

/// <summary>
/// Command to roll the published pointer back to a previous version. All
/// translations of that version return together — no cross-version mixing.
/// </summary>
public record RollbackNotificationTemplateCommand(Guid TemplateId, Guid TargetVersionId)
    : IRequest<ErrorOr<NotificationTemplateDetailDto>>
{
    public Guid RolledBackBy { get; init; }
}
