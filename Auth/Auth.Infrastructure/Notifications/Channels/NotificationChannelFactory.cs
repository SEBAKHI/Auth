using Auth.Application.Interfaces;
using Auth.Domain.Enums;

namespace Auth.Infrastructure.Notifications.Channels;

/// <summary>
/// Factory that resolves notification delivery strategies by channel.
/// Strategies are registered via DI and collected here (mirror of
/// ExternalAuthProviderFactory) — adding SMS/Push later means registering a new
/// INotificationChannel implementation, nothing more.
/// </summary>
public class NotificationChannelFactory : INotificationChannelFactory
{
    private readonly IReadOnlyDictionary<NotificationChannelType, INotificationChannel> _channels;

    public NotificationChannelFactory(IEnumerable<INotificationChannel> channels)
    {
        _channels = channels.ToDictionary(c => c.Channel, c => c);
    }

    /// <inheritdoc />
    public INotificationChannel? GetChannel(NotificationChannelType channel)
    {
        return _channels.TryGetValue(channel, out var implementation) ? implementation : null;
    }
}
