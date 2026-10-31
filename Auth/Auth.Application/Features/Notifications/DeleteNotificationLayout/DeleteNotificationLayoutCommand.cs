using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Notifications.DeleteNotificationLayout;

/// <summary>
/// Deletes an application's notification layout; that application's emails then use the global
/// layout of the channel. The global layout is refused (<c>Notification.CannotDeleteGlobalLayout</c>).
/// </summary>
public sealed record DeleteNotificationLayoutCommand(Guid LayoutId) : IRequest<ErrorOr<Deleted>>
{
    /// <summary>The administrator deleting it (set by the endpoint).</summary>
    public Guid DeletedBy { get; init; }
}
