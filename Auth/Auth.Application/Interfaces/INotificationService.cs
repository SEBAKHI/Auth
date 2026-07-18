using Auth.Application.Notifications;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Sends notifications from database-managed templates. Resolves the template by
/// (application, type, channel) with app-to-global fallback, selects the
/// translation from the recipient's language chain, renders with the layout, and
/// dispatches through the channel strategy. New notification types require no
/// interface change — only a seeded type and a published template.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Renders and sends one notification. Returns an error (never throws) when
    /// no published template exists, rendering fails, or delivery fails.
    /// </summary>
    Task<ErrorOr<Success>> SendAsync(NotificationRequest request, CancellationToken cancellationToken);
}
