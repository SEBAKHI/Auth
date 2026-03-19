namespace Auth.Application.IntegrationEvents;

/// <summary>
/// Base class for all integration events published for inter-service communication.
/// Integration events cross service boundaries and are consumed by external services.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>
    /// Unique identifier for this event instance.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// UTC timestamp when this event occurred.
    /// </summary>
    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// The type name of the event, used for routing and deserialization by consumers.
    /// </summary>
    public abstract string EventType { get; }
}
