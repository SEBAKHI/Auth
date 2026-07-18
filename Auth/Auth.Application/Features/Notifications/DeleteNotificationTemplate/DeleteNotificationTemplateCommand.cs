using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.DeleteNotificationTemplate;

/// <summary>
/// Command to delete a template with its whole version history. Forbidden for
/// the global template of a system type; app-scoped overrides are deletable
/// because the global fallback always exists.
/// </summary>
public record DeleteNotificationTemplateCommand(Guid TemplateId) : IRequest<ErrorOr<Deleted>>
{
    public Guid DeletedBy { get; init; }
}
