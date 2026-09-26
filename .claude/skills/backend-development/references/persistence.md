# Persistence Examples

Reference material for `SKILL.md` → Persistence. The rules are stated there; repository interface rules are `/domain-driven-design` §6, and query safety is `/security-mindset` → Injection. The examples show mechanics only: business rules (sufficient funds, positive amounts, allowed state changes) live in aggregates, whose methods return `ErrorOr`, and SQL never decides a business outcome.

## Normalization Checklist

```
┌─────────────────────────────────────────────────────────────┐
│              DATABASE NORMALIZATION REQUIREMENTS            │
├─────────────────────────────────────────────────────────────┤
│  RULE                                                       │
│  Transactional (write-side) schemas: at least BCNF.         │
│  Check 4NF and 5NF whenever a table holds two or more       │
│  independent many-valued facts about the same key.          │
│                                                             │
│  1NF - First Normal Form                                    │
│  □ Every column holds one atomic value per row              │
│  □ No repeating groups; every row identified by a key       │
│                                                             │
│  2NF - Second Normal Form                                   │
│  □ Meet all 1NF requirements                                │
│  □ No non-prime column (one outside every candidate key)    │
│    depends on a proper subset of a candidate key            │
│                                                             │
│  3NF - Third Normal Form                                    │
│  □ Meet all 2NF requirements                                │
│  □ For every non-trivial functional dependency X → A,       │
│    X is a superkey or A is part of a candidate key          │
│    (no transitive dependency on a key)                      │
│                                                             │
│  BCNF - Boyce-Codd Normal Form                              │
│  □ Meet all 3NF requirements                                │
│  □ For every non-trivial functional dependency X → Y,       │
│    X is a superkey                                          │
│                                                             │
│  4NF - Fourth Normal Form                                   │
│  □ Meet all BCNF requirements                               │
│  □ For every non-trivial multi-valued dependency X ↠ Y,     │
│    X is a superkey (no table holds two or more              │
│    independent multi-valued facts about a key)              │
│                                                             │
│  5NF - Fifth Normal Form (Project-Join Normal Form)         │
│  □ Meet all 4NF requirements                                │
│  □ Every join dependency is implied by the candidate keys   │
│                                                             │
│  EXCEPTIONS                                                 │
│  Denormalized read models and projections are permitted     │
│  ONLY when an ADR records:                                  │
│  • the performance requirement that demands them            │
│  • the mechanism that keeps them consistent with the        │
│    normalized write model                                   │
└─────────────────────────────────────────────────────────────┘
```

## Parameterized Queries (Dapper)

```csharp
// ❌ BAD: SQL injection
var sql = $"SELECT Id FROM Users WHERE Email = '{email}'";

// ✅ GOOD: parameterized. Dapper's plain (sql, param) async overloads take no CancellationToken:
// pass the token through CommandDefinition.
var userId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
    "SELECT Id FROM Users WHERE Email = @Email",
    new { Email = email }, // the normalized address as a string, never the value object
    transaction,
    cancellationToken: ct));
```

The example selects an id only. Materializing the entity straight from a row would bypass its factory and private constructor (`/domain-driven-design` §1), so a Dapper repository that loads an aggregate reads flat row records and rebuilds the aggregate through its persistence constructor.

## Unit of Work (Dapper)

The unit of work owns the connection and the transaction; only it opens, commits or rolls back.

```csharp
// Registered once per request scope, as itself and as the Application port, so both resolve to the same instance:
//   services.AddScoped<DapperUnitOfWork>();
//   services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DapperUnitOfWork>());
public sealed class DapperUnitOfWork(IOptions<DatabaseOptions> options) : IUnitOfWork, IAsyncDisposable
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;

    public DbConnection Connection => _connection ?? throw new InvalidOperationException("Call StartAsync first.");
    public DbTransaction Transaction => _transaction ?? throw new InvalidOperationException("Call StartAsync first.");

    // Idempotent: the first call in the scope opens the connection and begins the transaction; later calls do nothing.
    public async Task StartAsync(CancellationToken ct)
    {
        if (_transaction is not null) return;
        _connection = new SqlConnection(options.Value.ConnectionString);
        await _connection.OpenAsync(ct);
        _transaction = await _connection.BeginTransactionAsync(ct);
    }

    // IUnitOfWork: the only commit in the solution. It takes the request token.
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        if (_transaction is null) return; // no repository wrote anything
        try
        {
            await _transaction.CommitAsync(ct);
        }
        finally
        {
            await DisposeAsync(); // a failed commit rolls back on dispose; no explicit Rollback that could throw and hide the commit exception
        }
    }

    // IUnitOfWork: no execution strategy is configured on this connection, so the unit runs once.
    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct) => work(ct);

    // A transaction still open here was never committed: the handler returned an error or a command threw.
    // Disposing an uncommitted transaction rolls it back.
    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null) await _transaction.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _transaction = null;
        _connection = null;
    }
}
```

## Unit of Work (EF Core)

`DbContext.SaveChangesAsync` alone opens and commits its own transaction, so a statement that runs before it (an inbox insert through `ExecuteSqlAsync`) auto-commits on its own. A handler that must commit a record together with its change (the inbox, `/event-driven-architecture` §4) therefore begins the transaction explicitly. Under `EnableRetryOnFailure` (`/failure-mode-design`), a user-initiated transaction must run inside the execution strategy, so the whole unit (begin, inbox insert, effect, commit) is one retriable delegate.

```csharp
// Application port (layout (a): Application/Interfaces/; layout (b): each module's Application/Interfaces/).
public interface IUnitOfWork
{
    Task StartAsync(CancellationToken ct);                                          // idempotent begin
    Task SaveChangesAsync(CancellationToken ct);                                    // the only commit
    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct);    // one retriable unit
}

// Persistence, registered as scoped.
public sealed class EfUnitOfWork(ApplicationDbContext db) : IUnitOfWork, IAsyncDisposable
{
    // Idempotent: the first call in the scope begins the transaction; later calls do nothing.
    public async Task StartAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) await db.Database.BeginTransactionAsync(ct);
    }

    // Without StartAsync, SaveChangesAsync uses EF Core's own implicit transaction.
    // A tracked UPDATE or DELETE that hits 0 rows throws DbUpdateConcurrencyException here (500). This is the one
    // translation point: it is caught here only when the repository ADR records its translation to the catalog error,
    // and that ADR states how the error reaches the handler (SKILL.md → Persistence).
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        if (db.Database.CurrentTransaction is { } transaction) await transaction.CommitAsync(ct);
    }

    // Each attempt starts clean: no open transaction and no changes tracked by a failed attempt.
    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await DisposeAsync();
            db.ChangeTracker.Clear();
            await work(token);
        }, ct);

    // An uncommitted transaction (the handler returned an error, or a statement threw) rolls back on dispose.
    public async ValueTask DisposeAsync()
    {
        if (db.Database.CurrentTransaction is { } transaction) await transaction.DisposeAsync();
    }
}

// A handler whose change must commit with its inbox record:
//   await unitOfWork.ExecuteAsync(async token =>
//   {
//       await unitOfWork.StartAsync(token);
//       if (!await inbox.TryAddAsync(message.Id, token)) return;   // duplicate: nothing else runs
//       /* the effect, through repositories */
//       await unitOfWork.SaveChangesAsync(token);                  // inbox row and effect commit together
//   }, cancellationToken);
```

A unit of work that adds domain-event dispatch to its commit (`/domain-driven-design` → `references/domain-model-examples.md`, option A) implements `StartAsync` and `ExecuteAsync` exactly as above.

## Repository (Dapper)

The repository enlists in the unit of work's transaction. It never opens, commits or rolls back. The aggregate is the canonical `Order` of `/domain-driven-design` (`references/domain-model-examples.md` §2); value objects are bound member by member, because Dapper cannot bind a class such as `Money` as one parameter.

```csharp
public sealed class OrderRepository(DapperUnitOfWork unitOfWork) : IOrderRepository
{
    public async Task AddAsync(Order order, CancellationToken ct)
    {
        await unitOfWork.StartAsync(ct);
        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO Orders (Id, UserId, Status) VALUES (@Id, @UserId, @Status)",
            new { order.Id, order.UserId, Status = order.Status.ToString() }, unitOfWork.Transaction, cancellationToken: ct));
        await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO OrderLines (Id, OrderId, ProductId, Quantity, UnitPrice, Currency) " +
            "VALUES (@Id, @OrderId, @ProductId, @Quantity, @UnitPrice, @Currency)",
            order.Lines.Select(line => new
            {
                line.Id, OrderId = order.Id, line.ProductId, line.Quantity,
                UnitPrice = line.UnitPrice.Amount, Currency = line.UnitPrice.Currency,
            }),
            unitOfWork.Transaction, cancellationToken: ct));
    }

    // An UPDATE that must hit exactly one row reports whether it did: a 0-row statement does not fail on its own,
    // so the port (/domain-driven-design → references/domain-model-examples.md §4) declares this method as Task<bool>. It writes every column the aggregate's behavior changed
    // (here Cancel: status, time and actor).
    public async Task<bool> UpdateAsync(Order order, CancellationToken ct)
    {
        await unitOfWork.StartAsync(ct);
        var rows = await unitOfWork.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Orders SET Status = @Status, CancelledAt = @CancelledAt, CancelledBy = @CancelledBy WHERE Id = @Id",
            new { order.Id, Status = order.Status.ToString(), order.CancelledAt, order.CancelledBy },
            unitOfWork.Transaction, cancellationToken: ct));
        return rows == 1;
    }
}
```

The handler calls the repositories, then `IUnitOfWork.SaveChangesAsync` once, so writes across repositories commit or roll back together. The complete cancel handler is `/domain-driven-design` → `references/domain-model-examples.md` §5; the affected-row check below is the one that handler performs:

```csharp
// actorId: the authenticated caller's id, guarded as in the complete handler
// (ICurrentUser.Id is Guid?: references/handler-examples.md → Authorization).
var cancelled = order.Cancel(command.Reason, actorId, timeProvider.GetUtcNow()); // the aggregate decides
if (cancelled.IsError) return cancelled.Errors;

if (!await orders.UpdateAsync(order, cancellationToken))
    return OrderErrors.NotFound(order.Id); // no commit: the uncommitted transaction rolls back when the scope ends

await unitOfWork.SaveChangesAsync(cancellationToken);
```

## Repository (EF Core)

A repository loads and stores aggregates. It returns entities and serves the write side, plus a query handler that loads one aggregate only to map it to a DTO.

```csharp
public sealed class UserRepository(ApplicationDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct) => await db.Users.AddAsync(user, ct);
}
// With EF Core, IUnitOfWork.SaveChangesAsync calls DbContext.SaveChangesAsync(ct) once per request (Unit of Work (EF Core)).
// The repository cannot report an affected-row count: a 0-row tracked UPDATE or DELETE surfaces at that commit as
// DbUpdateConcurrencyException (500), translated only at the unit of work's documented point when the ADR records it.
```

## Read-Side Query Service (EF Core)

List and paged reads go through a read-side query service that returns DTOs, never through a repository (`/clean-architecture-structure` → Layer Responsibilities). The port is declared in Application (`Interfaces/`) and implemented in Persistence (`Queries/`).

```csharp
// Application/Interfaces/IUserQueries.cs
public interface IUserQueries
{
    Task<PagedResult<UserSummaryDto>> SearchAsync(UserSearchCriteria criteria, CancellationToken ct);
}

// Persistence/Queries/UserQueries.cs
internal sealed class UserQueries(ApplicationDbContext db) : IUserQueries
{
    public async Task<PagedResult<UserSummaryDto>> SearchAsync(UserSearchCriteria criteria, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(criteria.Search))
            query = query.Where(u => u.Name.Contains(criteria.Search)); // translated to a parameterized query

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderBy(u => u.Name).ThenBy(u => u.Id)                  // a unique tiebreaker keeps pages stable
            .Skip((criteria.Page - 1) * criteria.PageSize).Take(criteria.PageSize)
            .Select(u => new UserSummaryDto(u.Id, u.Name))           // projected in SQL: no entity is materialized
            .ToListAsync(ct);

        return new PagedResult<UserSummaryDto>(items, criteria.Page, criteria.PageSize, totalCount,
            (int)Math.Ceiling(totalCount / (double)criteria.PageSize));
    }
}
// Page and PageSize were checked by the query's validator (SKILL.md → List Queries).
```

## Uniqueness Under Concurrency

A unique index backs every uniqueness rule; the handler's existence check is only the common-case answer. Two requests can both pass the check, and the loser gets the index violation as an unhandled `DbException` (500). A repository that must return 409 in that race records the duplicate-key translation to the catalog Conflict error in its ADR.
