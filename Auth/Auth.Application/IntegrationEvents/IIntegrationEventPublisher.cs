namespace Auth.Application.IntegrationEvents;

/// <summary>
/// Abstraction for publishing integration events to external consumers.
/// Implementations may use RabbitMQ, Azure Service Bus, or any message broker.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes an integration event for external consumers.
    /// </summary>
    /// <typeparam name="T">The integration event type.</typeparam>
    /// <param name="event">The event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync<T>(T @event, CancellationToken cancellationToken)
        where T : IntegrationEvent;
}
