# SOLID Examples in the Handler Shape

Worked examples for `SKILL.md` → SOLID. The rules are stated there; this file only illustrates them. Neutral domains, C#.

## Single Responsibility

One reason to change per class: one use case per handler, side effects in notification handlers.

```csharp
// ❌ BAD: one class, several reasons to change (a use case, a side effect, a report)
public class UserService
{
    public User CreateUser(UserDto dto) { /* ... */ }
    public void SendWelcomeEmail(User user) { /* ... */ }
    public string GenerateReport(List<User> users) { /* ... */ }
}

// ✅ GOOD: one use case per handler; the side effect runs in a notification handler
public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, ErrorOr<UserDto>> { /* ... */ }
public sealed class WelcomeEmailEventHandler : INotificationHandler<UserCreatedEvent> { /* ... */ }
public sealed class GetUserReportQueryHandler : IRequestHandler<GetUserReportQuery, ErrorOr<UserReportDto>> { /* ... */ }
```

## Open/Closed

A new variant is a new implementation of the abstraction. The code that chooses among variants is a lookup (P8), so it does not change when a variant is added.

```csharp
public interface IPaymentProcessor
{
    PaymentMethod Method { get; } // the key this implementation serves
    Task<ErrorOr<PaymentReceipt>> ProcessAsync(Payment payment, CancellationToken ct);
}

public sealed class CardPaymentProcessor : IPaymentProcessor { /* Method => PaymentMethod.Card */ }
public sealed class BankTransferProcessor : IPaymentProcessor { /* Method => PaymentMethod.BankTransfer */ }
public sealed class WalletPaymentProcessor : IPaymentProcessor { /* new: registered in DI, nothing else changes */ }

// Chosen by lookup over the registered implementations, never by switch (P8).
public sealed class PaymentProcessorResolver(IEnumerable<IPaymentProcessor> processors) : IPaymentProcessorResolver
{
    private readonly FrozenDictionary<PaymentMethod, IPaymentProcessor> _byMethod =
        processors.ToFrozenDictionary(p => p.Method);

    public IPaymentProcessor For(PaymentMethod method) => _byMethod[method];
}
```

## Liskov Substitution

A subtype honors every contract of its base. When it cannot, the types share an interface instead.

```csharp
// ❌ BAD: Square changes the meaning of Rectangle's setters
public class Rectangle
{
    public virtual int Width { get; set; }
    public virtual int Height { get; set; }
}

public class Square : Rectangle
{
    public override int Width
    {
        set { base.Width = base.Height = value; } // a caller that sets Width and Height gets a surprise
    }
}

// ✅ GOOD: no inheritance chain; both honor the same small contract
public interface IShape
{
    int Area { get; }
}

public sealed class Rectangle(int width, int height) : IShape { public int Area => width * height; }
public sealed class Square(int side) : IShape { public int Area => side * side; }
```

## Interface Segregation

An interface holds only what its callers use. Repository interfaces follow `/domain-driven-design` §6.

```csharp
// ❌ BAD: a fat interface; every caller depends on bulk loads and raw SQL it never uses
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
    Task BulkInsertAsync(IEnumerable<Order> orders, CancellationToken ct);
    Task<IReadOnlyList<Order>> ExecuteQueryAsync(string sql, CancellationToken ct);
}

// ✅ GOOD: read and write interfaces, each shaped by its callers. Names and scope: /domain-driven-design §6
// (I{AggregateRoot}ReadRepository and I{AggregateRoot}WriteRepository are both write-side repositories: they return
// entities and serve no list or paged reads).
public interface IOrderReadRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
}

public interface IOrderWriteRepository
{
    Task AddAsync(Order order, CancellationToken ct);
}
```

## Dependency Inversion

A handler receives abstractions through its constructor. The concrete types are registered in the composition root (`/clean-architecture-structure` → Dependency Graph).

```csharp
// ❌ BAD: the handler constructs a concrete persistence type
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, ErrorOr<OrderDto>>
{
    private readonly SqlOrderRepository _repository = new SqlOrderRepository();
}

// ✅ GOOD: the handler depends on the Domain abstraction, injected by the container
public sealed class PlaceOrderCommandHandler(IOrderWriteRepository orders, IUnitOfWork unitOfWork)
    : IRequestHandler<PlaceOrderCommand, ErrorOr<OrderDto>>
{
    /* Handle(...) works only through IOrderWriteRepository and IUnitOfWork */
}
```
