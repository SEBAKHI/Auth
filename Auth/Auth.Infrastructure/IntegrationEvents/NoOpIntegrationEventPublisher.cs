using Auth.Application.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.IntegrationEvents;

/// <summary>
/// No-op integration event publisher that logs events but discards them.
/// Serves as a placeholder until a real message broker (RabbitMQ, Azure Service Bus) is configured.
/// Swap this registration for a real implementation when inter-service communication is needed.
/// </summary>
public class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly ILogger<NoOpIntegrationEventPublisher> _logger;

    public NoOpIntegrationEventPublisher(ILogger<NoOpIntegrationEventPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
        where T : IntegrationEvent
    {
        _logger.LogDebug(
            "Integration event generated (no-op): {EventType} [{EventId}]",
            @event.EventType,
            @event.Id);

        return Task.CompletedTask;
    }
}
