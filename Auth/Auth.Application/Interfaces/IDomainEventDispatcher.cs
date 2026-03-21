using Auth.Domain.Primitives;

namespace Auth.Application.Interfaces;

/// <summary>
/// Dispatches domain events collected by aggregate roots after persistence.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches all pending domain events from the aggregate root, then clears them.
    /// </summary>
    Task DispatchEventsAsync(AggregateRoot aggregateRoot, CancellationToken cancellationToken);
}
