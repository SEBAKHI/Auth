using Auth.Application.Notifications;
using Auth.Domain.Enums;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Strategy interface for one notification delivery channel (Email, SMS, Push).
/// Implementations are registered in DI and resolved through
/// <see cref="INotificationChannelFactory"/> — never via type switches.
/// </summary>
public interface INotificationChannel
{
    /// <summary>
    /// Gets the channel this strategy delivers.
    /// </summary>
    NotificationChannelType Channel { get; }

    /// <summary>
    /// Delivers a rendered notification. Returns an error (never throws) on failure.
    /// </summary>
    Task<ErrorOr<Success>> SendAsync(RenderedNotification notification, CancellationToken cancellationToken);
}
