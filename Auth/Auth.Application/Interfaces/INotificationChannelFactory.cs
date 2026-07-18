using Auth.Domain.Enums;

namespace Auth.Application.Interfaces;

/// <summary>
/// Resolves the delivery strategy for a channel from the DI-registered
/// <see cref="INotificationChannel"/> implementations.
/// </summary>
public interface INotificationChannelFactory
{
    /// <summary>
    /// Gets the channel strategy, or null when no implementation is registered.
    /// </summary>
    INotificationChannel? GetChannel(NotificationChannelType channel);
}
