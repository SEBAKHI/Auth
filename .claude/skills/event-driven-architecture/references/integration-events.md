# Integration Events

Examples for `SKILL.md` §2 (contracts) and §8 (integration events across modules and services). Every rule they apply is stated in `SKILL.md`; this file adds none. The outbox, inbox and retry machinery is in [outbox-inbox-retry.md](outbox-inbox-retry.md); handler shapes are in [handler-patterns.md](handler-patterns.md).

Module layout, project references and the architecture tests that enforce them: `/clean-architecture-structure` → Solution Layouts, Module Boundaries, Architecture Tests. The `Order` aggregate used below is the one in `/domain-driven-design` → `references/domain-model-examples.md` §2.

---

## 1. Contract Project (modular monolith)

The publishing module's `{Module}.Contracts` project holds its integration events in `IntegrationEvents/`. It is the only project of that module that another module references.

```
MyApp.Sales.Contracts/
    IntegrationEvents/
        OrderPlacedIntegrationEvent.cs       the event and its OrderPlacedLineDto
        OrderPlacedIntegrationEventV2.cs     a breaking change is a new type with a V{n} suffix
        OrderCancelledIntegrationEvent.cs
    (public queries and their DTOs: /clean-architecture-structure → Module Boundaries)
```

```csharp
namespace MyApp.Sales.Contracts.IntegrationEvents;

/// <summary>An order was placed. Published by the Sales module through its outbox.</summary>
public sealed record OrderPlacedIntegrationEvent(
    Guid EventId,
    Guid OrderId,
    Guid UserId,
    IReadOnlyList<OrderPlacedLineDto> Lines,   // copied at construction
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : INotification; // MediatR.Contracts: in-process transport between modules

/// <summary>A record of allowed types, declared with the event; in {Module}.Contracts it is named *Dto.</summary>
public sealed record OrderPlacedLineDto(
    Guid ProductId,
    int Quantity,
    decimal UnitPrice,
    string Currency);                          // the Money value object unwrapped; the currency as a string
```

Additive change: a new data member, placed with the other data members before `TriggeredBy`, keeps the type.

```csharp
public sealed record OrderPlacedIntegrationEvent(
    Guid EventId,
    Guid OrderId,
    Guid UserId,
    IReadOnlyList<OrderPlacedLineDto> Lines,
    string? SalesChannel,                      // new; nullable, because rows and messages written before the change lack it
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : INotification;
```

Breaking change (a removed or retyped member, a changed meaning): a new type, `OrderPlacedIntegrationEventV2`, next to the original.

---

## 2. Writing the Event (publishing module)

Inside the originating unit of work, before the commit. Placement option B, the command handler through the module's own `IOutbox` port (`MyApp.Sales.Application.Interfaces.IOutbox`, [outbox-inbox-retry.md](outbox-inbox-retry.md) §2):

```csharp
// PlaceOrderCommandHandler, after the order's behavior method succeeded:
outbox.Add(new OrderPlacedIntegrationEvent(
    EventId: Guid.NewGuid(),
    OrderId: order.Id,
    UserId: order.UserId,
    Lines: [.. order.Lines.Select(line => new OrderPlacedLineDto(
        line.ProductId, line.Quantity, line.UnitPrice.Amount, line.UnitPrice.Currency))],
    TriggeredBy: currentUser.Id,
    OccurredAt: timeProvider.GetUtcNow()));

await unitOfWork.SaveChangesAsync(ct); // the order and the outbox row commit together
```

Placement option A: the module's integration-event mapper turns the domain events that the aggregates collected into integration events. The interface and its implementation both live in the module's Application project, which references its own Contracts:

```csharp
// MyApp.Sales.Application/IntegrationEvents/
public interface IIntegrationEventMapper
{
    IReadOnlyList<INotification> Map(IDomainEvent domainEvent); // empty when no other module needs the event
}

internal sealed class IntegrationEventMapper : IIntegrationEventMapper
{
    // A lookup by domain event type, never a switch (P8).
    private static readonly FrozenDictionary<Type, Func<IDomainEvent, INotification>> Maps =
        new Dictionary<Type, Func<IDomainEvent, INotification>>
        {
            [typeof(OrderCancelledEvent)] = domainEvent => ToIntegrationEvent((OrderCancelledEvent)domainEvent),
        }.ToFrozenDictionary();

    public IReadOnlyList<INotification> Map(IDomainEvent domainEvent) =>
        Maps.TryGetValue(domainEvent.GetType(), out var map) ? [map(domainEvent)] : [];

    private static OrderCancelledIntegrationEvent ToIntegrationEvent(OrderCancelledEvent domainEvent) => new(
        EventId: Guid.NewGuid(),                 // a new event: its own EventId, generated once, here
        OrderId: domainEvent.OrderId,
        Reason: domainEvent.Reason,
        TriggeredBy: domainEvent.TriggeredBy,
        OccurredAt: domainEvent.OccurredAt);
}
```

The pre-commit step of the module's unit of work calls the mapper for each collected domain event and adds what it returns to the outbox, in the same transaction as the state change (the full interceptor is in [outbox-inbox-retry.md](outbox-inbox-retry.md) §2):

```csharp
foreach (var integrationEvent in integrationEvents.Map(domainEvent))
    outboxMessages.Add(serializer.ToOutboxMessage(integrationEvent));
```

Either way the event is never written by a handler that runs after the commit.

---

## 3. Dispatcher Wiring

Each module's outbox lives in that module's schema, and the module runs one dispatcher over it. The dispatcher publishes each row through `IPublisher`, in a fresh DI scope per message ([outbox-inbox-retry.md](outbox-inbox-retry.md) §3). With one MediatR registration over every module's Application assembly (`/clean-architecture-structure` → Layout (b)), that `Publish` reaches the consuming modules' handlers.

The module's event-type registry covers every event type stored in its schema: the events it writes to its outbox (its integration events and, for its critical handlers, its own domain events), and the events its critical handlers receive, which its retry table stores. The module's Application project lists those assemblies, because Application may reference any module's Contracts and Persistence may reference only its own (`/clean-architecture-structure` → Dependency Graph):

```csharp
// MyApp.Sales.Application/AssemblyReference.cs
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;

    // Own events plus every Contracts assembly whose events this module's handlers consume.
    // Under Placement option A, add the module's Domain assembly for its domain events.
    public static readonly FrozenSet<Assembly> EventAssemblies = new[]
    {
        Assembly,                                                                   // its own domain events
        typeof(MyApp.Sales.Contracts.IntegrationEvents.OrderPlacedIntegrationEvent).Assembly, // its integration events
    }.ToFrozenSet();
}
```

The module's persistence registration, which `AddSalesModule` calls, registers the machinery over the module's own DbContext and binds the module's ports to it ([outbox-inbox-retry.md](outbox-inbox-retry.md) §9). It names no other module's type:

```csharp
// MyApp.Sales.Persistence/DependencyInjection.cs
public static IServiceCollection AddSalesPersistence(this IServiceCollection services, IConfiguration configuration)
{
    // ... SalesDbContext (the "sales" schema), repositories, query services, the unit of work ...

    // Per-module outbox table and dispatcher, inbox, retry table and worker, all over SalesDbContext.
    services.AddOutbox<SalesDbContext>(configuration.GetSection("Sales:Outbox"),
        MyApp.Sales.Application.AssemblyReference.EventAssemblies);
    services.AddInbox<SalesDbContext>(configuration.GetSection("Sales:EventRetry"),
        MyApp.Sales.Application.AssemblyReference.Assembly,          // its notification handlers
        MyApp.Sales.Application.AssemblyReference.EventAssemblies);

    // Sales' own ports (MyApp.Sales.Application.Interfaces), implemented over that machinery.
    services.AddScoped<IOutbox, SalesOutbox>();
    services.AddScoped<IInbox, SalesInbox>();
    services.AddScoped<IEventRetryRecorder, SalesEventRetryRecorder>();
    return services;
}
```

A consuming module lists the Contracts it consumes in its own Application project, and its Persistence passes that list on. A consume-only module registers only the inbox side:

```csharp
// MyApp.Shipping.Application/AssemblyReference.cs
public static readonly FrozenSet<Assembly> EventAssemblies = new[]
{
    Assembly,
    typeof(MyApp.Sales.Contracts.IntegrationEvents.OrderPlacedIntegrationEvent).Assembly, // consumed from Sales
}.ToFrozenSet();

// MyApp.Shipping.Persistence/DependencyInjection.cs: no reference to MyApp.Sales.Contracts
services.AddInbox<ShippingDbContext>(configuration.GetSection("Shipping:EventRetry"),
    MyApp.Shipping.Application.AssemblyReference.Assembly,
    MyApp.Shipping.Application.AssemblyReference.EventAssemblies);
services.AddScoped<IInbox, ShippingInbox>();
services.AddScoped<IEventRetryRecorder, ShippingEventRetryRecorder>();
```

---

## 4. Consumer Placement (consuming module)

The consuming module's Application project references `MyApp.Sales.Contracts`, never Sales' Domain or Application. Its handler sits in `EventHandlers/` (`/clean-architecture-structure` → Feature Slices) and follows `SKILL.md` §4: critical by default, own DI scope, the consuming module's inbox, and the retry recorder.

```csharp
using MyApp.Sales.Contracts.IntegrationEvents;
using MyApp.Shipping.Application.Interfaces; // Shipping's own IInbox, IEventRetryRecorder and IUnitOfWork

namespace MyApp.Shipping.Application.EventHandlers;

/// <summary>
/// Opens a shipment request for each placed order.
/// Critical (integration-event consumer): failures are recorded for retry (§7).
/// </summary>
public sealed class ShipmentRequestEventHandler(
    IServiceScopeFactory scopeFactory,
    IEventRetryRecorder retryRecorder,
    ILogger<ShipmentRequestEventHandler> logger)
    : INotificationHandler<OrderPlacedIntegrationEvent>
{
    private static readonly string HandlerKey = typeof(ShipmentRequestEventHandler).FullName!;

    public async Task Handle(OrderPlacedIntegrationEvent notification, CancellationToken ct)
    {
        try
        {
            // Before resolving services, the scope's current-user port is set to notification.TriggeredBy,
            // so the audit stamps match inline and dispatcher runs (SKILL.md §4 Scope).
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var inbox = services.GetRequiredService<IInbox>();          // Shipping's port: Shipping's schema
            var requests = services.GetRequiredService<IShipmentRequestRepository>();
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();

            // Execution strategy and one transaction for the inbox row and the effect (/backend-development → Persistence).
            await unitOfWork.ExecuteAsync(async token =>
            {
                await unitOfWork.StartAsync(token);

                if (!await inbox.TryAddAsync(notification.EventId, HandlerKey, token))
                    return; // duplicate

                var request = ShipmentRequest.Open(notification.OrderId, notification.UserId, notification.OccurredAt);
                if (request.IsError)
                {
                    // The consumer's precondition is not met (§5): the rejection is a returned error, never
                    // thrown. Log it once and record the event for retry; after the retry limit it is
                    // parked and alerted (§7), never dropped. No commit: the inbox insert rolls back.
                    logger.LogWarning("{Handler} rejected {EventType} {EventId}, order {OrderId}: {Code}",
                        HandlerKey, nameof(OrderPlacedIntegrationEvent), notification.EventId,
                        notification.OrderId, request.FirstError.Code);
                    await retryRecorder.RecordAsync(notification, typeof(ShipmentRequestEventHandler),
                        request.FirstError.Code, token);
                    return;
                }

                requests.Add(request.Value);
                await unitOfWork.SaveChangesAsync(token);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "{Handler} failed for {EventType} {EventId}, order {OrderId}",
                HandlerKey, nameof(OrderPlacedIntegrationEvent), notification.EventId, notification.OrderId);
            await retryRecorder.RecordAsync(notification, typeof(ShipmentRequestEventHandler), ex.GetType().Name, ct);
        }
    }
}
```

---

## 5. Separate Services

Each service declares its own copy of the contract; there is no shared event library. Events are matched by name and version: the message carries the type name, and the consumer maps it to its local type.

```
Broker message
    message id:  the EventId
    type:        OrderPlacedIntegrationEvent      (OrderPlacedIntegrationEventV2 after a breaking change)
    body:        the event's JSON
```

```csharp
// The consuming service's own copy, in its own namespace.
namespace MyApp.Shipping.Application.IntegrationEvents.Inbound;

public sealed record OrderPlacedIntegrationEvent(
    Guid EventId,
    Guid OrderId,
    Guid UserId,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt);   // the members this consumer needs; unknown JSON members are ignored
```

The broker consumer follows [outbox-inbox-retry.md](outbox-inbox-retry.md) §8: a broker bridge into MediatR (in-process rules), or a framework-invoked consumer with no catch-all.
