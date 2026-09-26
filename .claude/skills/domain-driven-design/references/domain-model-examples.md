# Domain Model Examples

Canonical shapes for the rules in `../SKILL.md` §1–§4 and §6. This file adds no rule; where it and SKILL.md differ, SKILL.md wins.

The example domain is an order with lines. Folder comments follow SKILL.md → Domain Project Structure.

---

## 1. Base types and audit fields

```csharp
// Domain/Primitives/EntityBase.cs (in MyApp.SharedKernel when the solution has one)
public abstract class EntityBase
{
    protected EntityBase(Guid id) => Id = id;

    protected EntityBase() { } // persistence materialization only

    public Guid Id { get; private set; }
}

// Domain/Primitives/AuditableEntityBase.cs (in MyApp.SharedKernel when the solution has one)
public abstract class AuditableEntityBase : EntityBase
{
    protected AuditableEntityBase(Guid id) : base(id) { }

    protected AuditableEntityBase() { } // persistence materialization only

    // Set only by the persistence layer when changes are saved: no public setter, and the entity never reads the clock.
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; } // null when no authenticated account made the change
    public DateTimeOffset? ModifiedAt { get; private set; }
    public Guid? ModifiedBy { get; private set; }
}
```

The persistence layer stamps the audit fields when changes are saved (EF Core shown). The change tracker writes the private setters, so the entity keeps none public:

```csharp
// Persistence/Interceptors/AuditFieldsInterceptor.cs
// Layout (b): in the module's Persistence project, or generic in MyApp.BuildingBlocks.Infrastructure and added to each
// module's DbContext options. ICurrentUser is declared in MyApp.BuildingBlocks.Application.
public sealed class AuditFieldsInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? db)
    {
        if (db is null) return;

        var now = timeProvider.GetUtcNow();
        foreach (var entry in db.ChangeTracker.Entries<AuditableEntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(e => e.CreatedAt).CurrentValue = now;
                entry.Property(e => e.CreatedBy).CurrentValue = currentUser.Id;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(e => e.ModifiedAt).CurrentValue = now;
                entry.Property(e => e.ModifiedBy).CurrentValue = currentUser.Id;
            }
        }
    }
}
```

The interceptor reads the scoped current-user port, so it is registered as scoped and added in the context's options: `AddDbContext<ApplicationDbContext>((sp, options) => options.Use…(…).AddInterceptors(sp.GetRequiredService<AuditFieldsInterceptor>()))`. Without EF Core, the repository writes the audit columns from the same two sources in its INSERT and UPDATE statements.

In a scope that a notification handler, dispatcher or worker creates, the actor is the event's `TriggeredBy`, never the request's user (SKILL.md §1). The handler sets it on the new scope before it resolves any service, so the interceptor stamps the same value inline and under the outbox dispatcher. The port implementation returns that scoped value before it falls back to the request (`/backend-development` → Authorization, which owns the override's shape; the member name below is illustrative):

```csharp
// Application: a notification handler that writes state (/event-driven-architecture §4)
await using var scope = scopeFactory.CreateAsyncScope();
scope.ServiceProvider.GetRequiredService<ICurrentUserOverride>().Set(notification.TriggeredBy); // before any resolve
var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
// … load, change and save through this scope: CreatedBy/ModifiedBy = notification.TriggeredBy
```

## 2. Entity and aggregate root

A static factory that returns `ErrorOr<T>`, a private constructor for persistence, private setters, and behavior methods that return `ErrorOr<T>`. Another aggregate is referenced by ID only.

```csharp
// Domain/Entities/Order.cs — the aggregate root
public sealed class Order : EntityBase
{
    private readonly List<OrderLine> _lines = [];

    private Order() { } // persistence materialization only

    private Order(Guid id, Guid userId) : base(id)
    {
        UserId = userId;
        Status = OrderStatus.Draft;
    }

    public Guid UserId { get; private set; } // another aggregate: ID only
    public OrderStatus Status { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    public static ErrorOr<Order> Create(Guid userId)
    {
        if (userId == Guid.Empty) return OrderErrors.UserRequired;

        return new Order(Guid.NewGuid(), userId);
    }

    // Every change to a line goes through the root.
    public ErrorOr<Success> AddLine(Guid productId, int quantity, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice); // programmer error: BCL guard, not a business rule

        if (Status != OrderStatus.Draft) return OrderErrors.NotEditable;
        if (_lines.Count > 0 && _lines[0].UnitPrice.Currency != unitPrice.Currency) return OrderErrors.CurrencyMismatch;

        var line = OrderLine.Create(productId, quantity, unitPrice);
        if (line.IsError) return line.Errors;

        _lines.Add(line.Value);
        return Result.Success;
    }

    // The actor and the time are parameters: the Domain never reads the clock or the current user.
    public ErrorOr<Success> Cancel(string reason, Guid actorId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) return OrderErrors.CancellationReasonRequired;
        if (Status == OrderStatus.Shipped) return OrderErrors.AlreadyShipped;
        if (Status == OrderStatus.Cancelled) return OrderErrors.AlreadyCancelled;

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = actorId;
        return Result.Success;
    }
}

// Domain/Entities/OrderLine.cs — a child entity, created only through the root
public sealed class OrderLine : EntityBase
{
    private OrderLine() { } // persistence materialization only

    private OrderLine(Guid id, Guid productId, int quantity, Money unitPrice) : base(id)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public Money UnitPrice { get; private set; } = null!;

    internal static ErrorOr<OrderLine> Create(Guid productId, int quantity, Money unitPrice)
    {
        if (quantity is < 1 or > OrderConstants.MaxQuantityPerLine) return OrderErrors.InvalidQuantity;

        return new OrderLine(Guid.NewGuid(), productId, quantity, unitPrice);
    }
}

// Domain/Constants/OrderConstants.cs
public static class OrderConstants
{
    public const int MaxQuantityPerLine = 100;
}

// Domain/Errors/OrderErrors.cs — member shapes: domain-errors.md
public static class OrderErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("Order.NotFound", $"Order '{id}' was not found.");

    public static readonly Error UserRequired =
        Error.Validation("Order.UserRequired", "An order needs a user.");

    public static readonly Error InvalidQuantity =
        Error.Validation("Order.InvalidQuantity", "The line quantity is out of range.");

    public static readonly Error CancellationReasonRequired =
        Error.Validation("Order.CancellationReasonRequired", "A cancellation needs a reason.");

    public static readonly Error NotEditable =
        Error.Conflict("Order.NotEditable", "Only a draft order can be changed.");

    public static readonly Error CurrencyMismatch =
        Error.Conflict("Order.CurrencyMismatch", "All lines of an order use one currency.");

    public static readonly Error AlreadyShipped =
        Error.Conflict("Order.AlreadyShipped", "A shipped order cannot be cancelled.");

    public static readonly Error AlreadyCancelled =
        Error.Conflict("Order.AlreadyCancelled", "The order is already cancelled.");
}
```

## 3. Value objects

### A non-positional `sealed record class`

Each property is `{ get; }` with no `init`, so `with` cannot bypass the factory. The record synthesizes `Equals` and `GetHashCode`; none is declared.

```csharp
// Domain/ValueObjects/Email.cs (EmailErrors in Domain/Errors/, EmailConstants in Domain/Constants/)
public sealed record class Email
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public static ErrorOr<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return EmailErrors.Empty;

        var normalized = value.Trim();
        if (normalized.Length > EmailConstants.MaxLength) return EmailErrors.TooLong;

        var at = normalized.IndexOf('@');
        if (at <= 0 || at != normalized.LastIndexOf('@') || at == normalized.Length - 1) return EmailErrors.Invalid;

        return new Email(normalized);
    }
}
```

### A class-based value object

It overrides `Equals` and `GetHashCode` over all properties. Operations return new instances.

```csharp
// Domain/ValueObjects/Money.cs (MoneyErrors in Domain/Errors/)
public sealed class Money : IEquatable<Money>
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; } // ISO 4217 alphabetic code

    public static ErrorOr<Money> Create(decimal amount, string? currency)
    {
        if (amount < 0) return MoneyErrors.Negative;
        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper)) return MoneyErrors.InvalidCurrency;

        return new Money(amount, currency);
    }

    public ErrorOr<Money> Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Currency != Currency) return MoneyErrors.CurrencyMismatch;

        return new Money(Amount + other.Amount, Currency);
    }

    public bool Equals(Money? other) =>
        other is not null && Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => Equals(obj as Money);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);
}
```

## 4. Repository interface

```csharp
// Domain/Interfaces/Repositories/IOrderRepository.cs
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
    Task<bool> UpdateAsync(Order order, CancellationToken ct); // false when no row was updated (/backend-development → Persistence)
}
```

It returns entities and exposes no `IQueryable`. The implementation lives where `/clean-architecture-structure` places it.

---

## 5. Placement option B: the command handler publishes after the commit

Stage 1 shown. From stage 2 on, an event that goes through the outbox is added to the module's outbox before the commit instead (SKILL.md §4; `/event-driven-architecture` §3, §9).

The event lives in the use-case slice: `Application/Features/Orders/CancelOrder/OrderCancelledEvent.cs`. Its fields follow `/event-driven-architecture` §2.

```csharp
public sealed record OrderCancelledEvent(
    Guid EventId,
    Guid OrderId,
    string Reason,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : INotification;

public sealed record CancelOrderCommand(Guid OrderId, string Reason) : IRequest<ErrorOr<Success>>;

public sealed class CancelOrderCommandHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IRequestHandler<CancelOrderCommand, ErrorOr<Success>>
{
    public async Task<ErrorOr<Success>> Handle(CancelOrderCommand command, CancellationToken ct)
    {
        // Input validation already ran in the validation pipeline behavior (P6).
        // ICurrentUser.Id is Guid? (/backend-development → references/handler-examples.md, Authorization). This endpoint requires an authenticated
        // caller, so a missing id is a programming error: a BCL exception, never a catalog error.
        var actorId = currentUser.Id
            ?? throw new InvalidOperationException("Cancelling an order requires an authenticated caller.");

        var order = await orders.GetByIdAsync(command.OrderId, ct);
        if (order is null) return OrderErrors.NotFound(command.OrderId);
        // Resource-level authorization: /backend-development → Authorization.

        var now = timeProvider.GetUtcNow();
        var result = order.Cancel(command.Reason, actorId, now); // the entity enforces the rules
        if (result.IsError) return result.Errors;

        if (!await orders.UpdateAsync(order, ct)) return OrderErrors.NotFound(order.Id); // affected-row check: no commit (/backend-development → Persistence)
        await unitOfWork.SaveChangesAsync(ct); // the commit takes the request token

        await publisher.Publish(
            new OrderCancelledEvent(
                EventId: Guid.NewGuid(),
                OrderId: order.Id,
                Reason: command.Reason,
                TriggeredBy: currentUser.Id,
                OccurredAt: now),
            CancellationToken.None); // post-commit: not tied to the request (/event-driven-architecture §3)

        return Result.Success;
    }
}
```

## 6. Placement option A: the aggregate raises, the Infrastructure dispatcher publishes

### Domain

```csharp
// Domain/Primitives (or MyApp.SharedKernel)
// Adapter variant: a Domain marker interface. Contracts variant: public interface IDomainEvent : MediatR.INotification { }
public interface IDomainEvent { }

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class AggregateRootBase : EntityBase, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRootBase(Guid id) : base(id) { }

    protected AggregateRootBase() { }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

// Domain/Events/OrderCancelledEvent.cs
public sealed record OrderCancelledEvent(
    Guid EventId,
    Guid OrderId,
    string Reason,
    Guid? TriggeredBy,
    DateTimeOffset OccurredAt) : IDomainEvent;
```

`Order` inherits `AggregateRootBase` instead of `EntityBase`, and `Cancel` raises the event from the parameters it already receives. The aggregate generates `EventId`:

```csharp
    public ErrorOr<Success> Cancel(string reason, Guid actorId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason)) return OrderErrors.CancellationReasonRequired;
        if (Status == OrderStatus.Shipped) return OrderErrors.AlreadyShipped;
        if (Status == OrderStatus.Cancelled) return OrderErrors.AlreadyCancelled;

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        CancelledBy = actorId;
        Raise(new OrderCancelledEvent(Guid.NewGuid(), Id, reason, actorId, now));
        return Result.Success;
    }
```

### Application: the handler only persists

The handler of section 5 without `IPublisher` and without the `Publish` call. It ends with `await unitOfWork.SaveChangesAsync(ct); return Result.Success;`.

### Application, Persistence and Infrastructure: collect before the commit, publish after it (EF Core shown)

```csharp
// Application port, implemented in Infrastructure (layout (b): declared in MyApp.BuildingBlocks.Application)
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents);
}

// Application (layout (b): MyApp.BuildingBlocks.Application), adapter variant only: one generic wrapper.
// It depends only on MediatR.INotification and the Domain marker; the Infrastructure dispatcher applies it.
// Handlers implement INotificationHandler<DomainEventNotification<OrderCancelledEvent>>.
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification
    where TEvent : IDomainEvent;

public static class DomainEventNotification
{
    public static INotification Wrap(IDomainEvent domainEvent) =>
        (INotification)Activator.CreateInstance(
            typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType()),
            domainEvent)!;
}

// Persistence: the unit of work (layout (b): each module's own, over its own DbContext)
// Stage 1 shown. From stage 2 on (/event-driven-architecture §3, §9), it dispatches inline only the events it does not
// write to the outbox before the commit, or marks their outbox rows sent after the inline publish: never both unmarked.
public sealed class UnitOfWork(ApplicationDbContext db, IDomainEventDispatcher dispatcher) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        var aggregates = db.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();
        var domainEvents = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToList();

        await db.SaveChangesAsync(ct); // the commit takes the request token

        aggregates.ForEach(aggregate => aggregate.ClearDomainEvents());
        await dispatcher.DispatchAsync(domainEvents); // only after the commit
    }
}

// Infrastructure: the dispatcher
public sealed class DomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            await publisher.Publish(
                DomainEventNotification.Wrap(domainEvent), // contracts variant: publish domainEvent itself
                CancellationToken.None); // post-commit: not tied to the request (/event-driven-architecture §3)
        }
    }
}
```

Handlers, their isolation, idempotency and retry: `/event-driven-architecture` §4–§7. Durability across a process stop (outbox): `/event-driven-architecture` §9.
