using Auth.Application.Interfaces;
using Auth.Domain.Primitives;
using MediatR;

namespace Auth.Infrastructure;

/// <summary>
/// Dispatches domain events from aggregate roots via MediatR's IPublisher.
/// </summary>
public class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;

    public MediatRDomainEventDispatcher(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public async Task DispatchEventsAsync(AggregateRoot aggregateRoot, CancellationToken cancellationToken = default)
    {
        var domainEvents = aggregateRoot.DomainEvents.ToList();
        aggregateRoot.ClearDomainEvents();

        foreach (var domainEvent in domainEvents)
        {
            await _publisher.Publish(domainEvent, cancellationToken);
        }
    }
}
