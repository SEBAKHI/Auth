# Outbox, Inbox and Retry

Examples for `SKILL.md` §4 (inbox), §6, §7 (retry table, worker, parking), §8 (outbox writing, dispatching, module scope) and §9 (stages). Every rule they apply is stated in `SKILL.md`; this file adds none. SQL is PostgreSQL; other databases need their own equivalents of `ON CONFLICT` and `FOR UPDATE SKIP LOCKED`.

Placement of this machinery: in a single-module solution, Infrastructure and Persistence; in a modular monolith, `MyApp.BuildingBlocks.Infrastructure`, with each module's tables in that module's own schema (`/clean-architecture-structure` → Module Boundaries).

- The machinery is generic over the module's DbContext: `OutboxDispatcher<TDbContext>`, `EventRetryWorker<TDbContext>`, their stores and registries, and options named per module. Each module registers its own closed set, so it runs one dispatcher and one retry worker over its own schema (§9).
- Each module declares its `IOutbox`, `IInbox` and `IEventRetryRecorder` ports in its own Application namespace (`MyApp.{Module}.Application.Interfaces`), like its `IUnitOfWork`. Its Persistence implements them over the generic machinery, so the ports of two modules are different service types in the one container.
- A single-module solution uses the same types, closed over its one DbContext.

---

## 1. Tables

```sql
-- One outbox per module, in the module's schema.
CREATE TABLE {module_schema}.outbox_message (
    event_id        uuid         PRIMARY KEY,               -- the event's EventId
    event_type      text         NOT NULL,                  -- the type's full name, resolved only through the registry
    payload         jsonb        NOT NULL,                  -- secret fields encrypted (SKILL.md §2)
    occurred_at     timestamptz  NOT NULL,
    attempt_count   int          NOT NULL DEFAULT 0,        -- counted when a dispatcher claims the row
    next_attempt_at timestamptz  NOT NULL DEFAULT now(),    -- set by each claim: backoff with jitter, lease floor
    status          text         NOT NULL DEFAULT 'Pending', -- Pending | Parked
    sent_at         timestamptz  NULL
);
CREATE INDEX ix_outbox_due ON {module_schema}.outbox_message (next_attempt_at)
    WHERE sent_at IS NULL AND status = 'Pending';

-- The inbox: processed records. The unique key is (EventId, handler).
CREATE TABLE {module_schema}.inbox_record (
    event_id     uuid         NOT NULL,
    handler      text         NOT NULL,
    processed_at timestamptz  NOT NULL,
    PRIMARY KEY (event_id, handler)
);

-- The retry table. One record per (EventId, handler).
CREATE TABLE {module_schema}.retry_record (
    event_id        uuid        NOT NULL,
    handler         text        NOT NULL,           -- the handler type's full name
    event_type      text        NOT NULL,           -- the event type's full name
    payload         jsonb       NOT NULL,           -- secret fields encrypted (SKILL.md §2)
    attempt_count   int         NOT NULL,
    next_attempt_at timestamptz NOT NULL,
    last_error_type text        NOT NULL,
    status          text        NOT NULL,           -- Pending | InFlight | Parked
    locked_until    timestamptz NULL,
    PRIMARY KEY (event_id, handler)
);
```

---

## 2. Writing to the Outbox (stage 2 and later)

The row is added inside the originating unit of work, before the commit, so it commits or rolls back with the state change.

Placement option B, the command handler through the module's own port. The sample is outbox-only (SKILL.md §3): the command handler does not also publish inline, and the dispatcher delivers the row.

```csharp
// MyApp.{Module}.Application.Interfaces (layout (a): MyApp.Application.Interfaces)
public interface IOutbox
{
    /// <summary>
    /// Adds the event to the current unit of work's outbox. Written by the commit.
    /// In a module that keeps the inline fast path, the row is written due only after a
    /// grace delay (next_attempt_at = now() + lease; §3 below).
    /// </summary>
    void Add(INotification notification);

    /// <summary>Adapter variant of Placement option A only: a domain event that is not an INotification.</summary>
    void Add(IDomainEvent domainEvent);

    /// <summary>Inline fast path only: marks the row sent after the in-process publish returned (SKILL.md §3).</summary>
    Task MarkSentAsync(Guid eventId, CancellationToken ct);
}

// In the CancelOrderCommandHandler of /domain-driven-design → references/domain-model-examples.md §5,
// with IOutbox in place of IPublisher, after order.Cancel(...) succeeded:
outbox.Add(new OrderCancelledEvent(
    EventId: Guid.NewGuid(),
    OrderId: order.Id,
    Reason: command.Reason,
    TriggeredBy: currentUser.Id,
    OccurredAt: now));                     // the instant passed to order.Cancel

if (!await orders.UpdateAsync(order, ct))
    return OrderErrors.NotFound(order.Id); // 0 rows: no commit (/backend-development → Persistence)
await unitOfWork.SaveChangesAsync(ct);     // the state change and the outbox row commit together
```

Placement option A, a pre-commit step of the module's unit of work: an EF Core interceptor added to the module's DbContext options. It writes the collected domain events for the module's own critical handlers, and the integration events that the module's mapper returns for other modules ([integration-events.md](integration-events.md) §2):

```csharp
// {Module}.Persistence (layout (a): Persistence)
public sealed class DomainEventsToOutboxInterceptor(
    IOutboxSerializer serializer,
    IIntegrationEventMapper integrationEvents) : SaveChangesInterceptor   // the module's Application port
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        var context = eventData.Context!;
        var outboxMessages = context.Set<OutboxMessage>();

        // The event collection that the aggregate base type exposes (/domain-driven-design §4, option A).
        // The unit of work clears it after the commit.
        var domainEvents = context.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(entry => entry.Entity.DomainEvents)
            .ToList();

        foreach (var domainEvent in domainEvents)
        {
            outboxMessages.Add(serializer.ToOutboxMessage(domainEvent));          // the module's own handlers
            foreach (var integrationEvent in integrationEvents.Map(domainEvent))
                outboxMessages.Add(serializer.ToOutboxMessage(integrationEvent)); // other modules
        }

        return base.SavingChangesAsync(eventData, result, ct);
    }
}
```

Stored type names are resolved only through the module's registry of known event types, built once at startup:

```csharp
// MyApp.BuildingBlocks.Infrastructure: one registry per module (§9).
public sealed class EventTypeRegistry<TDbContext>(IEnumerable<Assembly> eventAssemblies)
    where TDbContext : DbContext
{
    private readonly FrozenDictionary<string, Type> _byFullName = eventAssemblies
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type.IsClass && !type.IsAbstract
            && (typeof(INotification).IsAssignableFrom(type)
                || typeof(IDomainEvent).IsAssignableFrom(type)))      // IDomainEvent: adapter variant only
        .ToFrozenDictionary(type => type.FullName!, StringComparer.Ordinal); // full names: two modules may reuse a simple name

    public Type Resolve(string eventType) => _byFullName[eventType];  // unknown names are never instantiated
}
```

Under the adapter variant of Placement option A a domain event is not an `INotification`. `IOutbox.Add` also accepts an `IDomainEvent`, the registry also includes the `IDomainEvent` types (the second condition above), and the dispatcher wraps each domain event with `DomainEventNotification.Wrap` (`/domain-driven-design` → `references/domain-model-examples.md` §6) before `Publish`: the store's `Deserialize` returns the wrapped notification. The retry recorder stores a wrapped domain event unwrapped, and the retry worker's `Deserialize` wraps it again.

A field that carries a one-time secret is encrypted before the row is written (`/security-mindset` → Cryptography Standards), and that row is deleted, not marked sent, once the secret has been delivered (SKILL.md §2).

---

## 3. Outbox Dispatcher

One row per loop iteration, one catch per iteration (`/failure-mode-design` → Exception-Handling Boundaries).

- The claim counts the attempt and sets `next_attempt_at` with the backoff formula of §5, never shorter than the lease. Other instances skip the row until then.
- A failed publish therefore needs no write in the catch: the row stays `Pending` and becomes due again after its backoff. A crash during the publish also counts as an attempt.
- A due row whose attempts are exhausted is parked, never dropped, and its log event drives the alert. §7 resubmits it.

```csharp
public sealed class OutboxDispatcher<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<OutboxOptions> options,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    // Named options, one set per module: Lease, PollInterval, MaxAttempts, BaseDelay, MaxDelay.
    private readonly OutboxOptions _options = options.Get(typeof(TDbContext).Name);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            OutboxMessage? message = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope(); // fresh DI scope per message
                var store = scope.ServiceProvider.GetRequiredService<IOutboxStore<TDbContext>>();

                foreach (var parked in await store.ParkExhaustedAsync(_options, stoppingToken))
                    logger.LogError(OutboxLogEvents.Parked,            // the alert rule watches this event id
                        "Outbox row parked: {EventType} {EventId} after {Attempts} attempts",
                        parked.EventType, parked.EventId, parked.AttemptCount);

                message = await store.ClaimNextDueAsync(_options, stoppingToken); // counts the attempt
                if (message is null)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                    continue;
                }

                var notification = store.Deserialize(message);   // the module's event-type registry
                var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

                // In-process transport: handlers never throw (SKILL.md §7, item 4).
                // Stage 3 replaces this call with the broker publisher.
                await publisher.Publish(notification, stoppingToken);

                // A crash before this line re-dispatches the row: a duplicate (SKILL.md §6).
                await store.MarkSentAsync(message.EventId, stoppingToken); // or delete, for a secret-bearing row
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Outbox dispatch failed for {EventType} {EventId}, attempt {Attempt}",
                    message?.EventType, message?.EventId, message?.AttemptCount);
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }
}
```

Claim and park queries:

```sql
-- ClaimNextDueAsync: counts the attempt and schedules the next one before the publish.
-- On the right-hand side, attempt_count is the value before this claim.
UPDATE {module_schema}.outbox_message
SET    attempt_count   = attempt_count + 1,
       next_attempt_at = now() + GREATEST(@lease,
                             LEAST(@maxDelay, @baseDelay * power(2, attempt_count)) * (0.5 + random() / 2))
WHERE  event_id = (
    SELECT event_id FROM {module_schema}.outbox_message
    WHERE  sent_at IS NULL AND status = 'Pending'
      AND  attempt_count < @maxAttempts AND next_attempt_at <= now()
    ORDER  BY next_attempt_at
    LIMIT  1
    FOR UPDATE SKIP LOCKED)
RETURNING event_id, event_type, payload, attempt_count;

-- ParkExhaustedAsync: rows whose last allowed attempt did not end in MarkSentAsync.
UPDATE {module_schema}.outbox_message
SET    status = 'Parked'
WHERE  sent_at IS NULL AND status = 'Pending'
  AND  attempt_count >= @maxAttempts AND next_attempt_at <= now()
RETURNING event_id, event_type, attempt_count;
```

One way to keep the inline fast path at stage 2: write the row with `next_attempt_at = now() + lease`. After the commit, publish it in-process (SKILL.md §3), then mark it sent through `IOutbox.MarkSentAsync`. The dispatcher claims only due rows, so it sees a fast-path row only when the process stopped before marking it: a delivery failure (SKILL.md §6).

---

## 4. Inbox

`Inbox<TDbContext>` implements the module's `IInbox` port (§9). `TryAddAsync` runs inside the transaction that the handler's unit of work opened, so the processed record commits or rolls back with the effect (SKILL.md §4); it refuses to run without one, because the insert would otherwise auto-commit. The unique key decides; a concurrent duplicate waits for the first transaction and then inserts nothing.

```csharp
// MyApp.BuildingBlocks.Infrastructure
public class Inbox<TDbContext>(TDbContext db) where TDbContext : DbContext
{
    // The schema comes from the module's model, never from input, so the raw SQL is safe; the values are parameters.
    private readonly string _insertSql = $$"""
        INSERT INTO {{db.Model.GetDefaultSchema()}}.inbox_record (event_id, handler, processed_at)
        VALUES ({0}, {1}, now())
        ON CONFLICT (event_id, handler) DO NOTHING
        """;

    public async Task<bool> TryAddAsync(Guid eventId, string handler, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("IInbox.TryAddAsync requires the unit of work's open transaction.");

        var rows = await db.Database.ExecuteSqlRawAsync(_insertSql, [eventId, handler], ct);
        return rows == 1; // 1: first delivery, proceed; 0: duplicate, skip
    }

    public Task<bool> ExistsAsync(Guid eventId, string handler, CancellationToken ct) =>
        db.Set<InboxRecord>().AnyAsync(record => record.EventId == eventId && record.Handler == handler, ct);
}
```

The handler opens that transaction and runs its body through the unit of work's execution-strategy wrapper (`/backend-development` → Persistence; [handler-patterns.md](handler-patterns.md) §2).

---

## 5. Retry Recorder

Called from a critical handler's catch (see [handler-patterns.md](handler-patterns.md)) through the module's `IEventRetryRecorder` port (§9). Its own scope, its own transaction. The upsert keeps one record per `(EventId, handler)`, and parks the record when the attempt limit is reached.

```csharp
// MyApp.BuildingBlocks.Infrastructure
public class EventRetryRecorder<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOutboxSerializer serializer,
    IOptionsMonitor<EventRetryOptions> options,
    ILogger logger)
    where TDbContext : DbContext
{
    // Named options, one set per module: MaxAttempts, BaseDelay, MaxDelay, Lease, PollInterval, from configuration.
    private readonly EventRetryOptions _options = options.Get(typeof(TDbContext).Name);

    public async Task RecordAsync(INotification notification, Type handlerType, string errorType, CancellationToken ct)
    {
        var eventId = serializer.GetEventId(notification);          // the EventId member every contract has (SKILL.md §2)
        var eventType = serializer.GetEventTypeName(notification);  // the full name the registry resolves (SKILL.md §8)
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IRetryStore<TDbContext>>();

            var record = await store.UpsertFailureAsync(
                eventId,
                handlerType.FullName!,
                eventType,
                serializer.Serialize(notification),   // secret fields encrypted (SKILL.md §2)
                errorType,
                _options,
                ct);

            if (record.Status == RetryStatus.Parked)
                logger.LogError(RetryLogEvents.Parked,   // the alert rule watches this event id
                    "Retry parked for {Handler}, {EventType} {EventId} after {Attempts} attempts",
                    record.Handler, record.EventType, eventId, record.AttemptCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The one case where the side effect is lost (SKILL.md §6, §7 item 5): make it detectable.
            logger.LogCritical(RetryLogEvents.RecordLost, ex,   // the alert rule watches this event id
                "Retry record could not be written for {Handler}, {EventType} {EventId}",
                handlerType.FullName, eventType, eventId);
        }
    }
}
```

Upsert. The delay after attempt n is `min(MaxDelay, BaseDelay * 2^(n-1))`, scaled by a random factor between 0.5 and 1 (jitter):

```sql
INSERT INTO {module_schema}.retry_record
    (event_id, handler, event_type, payload, attempt_count, next_attempt_at, last_error_type, status)
VALUES (@eventId, @handler, @eventType, @payload, 1,
        now() + LEAST(@maxDelay, @baseDelay) * (0.5 + random() / 2),
        @errorType,
        CASE WHEN @maxAttempts <= 1 THEN 'Parked' ELSE 'Pending' END)
ON CONFLICT (event_id, handler) DO UPDATE SET
    payload         = EXCLUDED.payload,
    attempt_count   = retry_record.attempt_count + 1,
    next_attempt_at = now() + LEAST(@maxDelay, @baseDelay * power(2, retry_record.attempt_count))
                            * (0.5 + random() / 2),
    last_error_type = EXCLUDED.last_error_type,
    locked_until    = NULL,
    status = CASE WHEN retry_record.attempt_count + 1 >= @maxAttempts THEN 'Parked' ELSE 'Pending' END
RETURNING handler, event_type, attempt_count, status;
```

---

## 6. Retry Worker

Re-invokes only the recorded handler, in a fresh DI scope.

- The worker marks the record `InFlight` when it claims it. A handler that fails again calls the recorder, whose upsert moves the record back to `Pending` (or `Parked`) with a higher attempt count. A record still `InFlight` with the claimed attempt count after the handler returned has succeeded, and is deleted.
- The module's handler-type registry (keyed by full name) resolves the recorded handler type. The worker builds that type in the scope and invokes it through its closed `INotificationHandler<T>`. It never enumerates the registered handlers or compares their types.

```csharp
public sealed class EventRetryWorker<TDbContext>(
    IServiceScopeFactory scopeFactory,
    HandlerTypeRegistry<TDbContext> handlerTypes,
    IOptionsMonitor<EventRetryOptions> options,
    ILogger<EventRetryWorker<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly EventRetryOptions _options = options.Get(typeof(TDbContext).Name);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IRetryStore<TDbContext>>();

                var record = await store.ClaimNextDueAsync(_options.Lease, stoppingToken); // Pending, due, unlocked
                if (record is null)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                    continue;
                }

                var notification = store.Deserialize(record);   // the module's event-type registry
                var handler = ActivatorUtilities.CreateInstance(
                    scope.ServiceProvider, handlerTypes.Resolve(record.Handler));
                var handlerInterface = typeof(INotificationHandler<>).MakeGenericType(notification.GetType());

                await (Task)handlerInterface.GetMethod(nameof(INotificationHandler<INotification>.Handle))!
                    .Invoke(handler, [notification, stoppingToken])!;

                await store.DeleteIfSucceededAsync(record.EventId, record.Handler, record.AttemptCount, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Retry worker iteration failed");
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }
}
```

```sql
-- DeleteIfSucceededAsync
DELETE FROM {module_schema}.retry_record
WHERE event_id = @eventId AND handler = @handler
  AND status = 'InFlight' AND attempt_count = @claimedAttemptCount;
```

A worker that crashes mid-invocation leaves an `InFlight` record whose lease expires; the claim query treats an expired `InFlight` record as due again, and the handler's inbox absorbs a repeat.

---

## 7. Parking and Operator Resubmission

A parked retry record or outbox row is never claimed again automatically. Its `Parked` log event drives the alert. An operator resubmits it after fixing the cause:

```sql
-- A parked retry record
UPDATE {module_schema}.retry_record
SET    status = 'Pending', attempt_count = 0, next_attempt_at = now(), locked_until = NULL
WHERE  event_id = @eventId AND handler = @handler AND status = 'Parked';

-- A parked outbox row
UPDATE {module_schema}.outbox_message
SET    status = 'Pending', attempt_count = 0, next_attempt_at = now()
WHERE  event_id = @eventId AND status = 'Parked';
```

---

## 8. Broker Transport (stage 3)

Publishing side: the outbox dispatcher's in-process `Publish` call becomes a broker send of `event_type` and `payload`, with the message id set to `EventId`. Nothing else in the dispatcher changes; the row is marked sent after the broker confirms, and a failed send is retried and parked as in §3.

Consuming side, two shapes:

- **Broker bridge.** A receiver deserializes the message and republishes it into MediatR in a fresh DI scope. Handlers follow the in-process rules (SKILL.md §4, §7 items 1–5); the bridge acknowledges once `Publish` returns.
- **Framework-invoked consumer.** The messaging framework calls the consumer directly. It has no catch-all: a failure reaches the framework's retry and dead-letter handling (SKILL.md §7, item 6).

```csharp
/// <summary>
/// Keeps this service's local read model of placed orders; it never reads the publisher's tables.
/// Critical (integration-event consumer). Invoked by the messaging framework: no catch-all;
/// the framework's retry and dead-letter queue apply.
/// </summary>
public sealed class PlacedOrderReadModelEventHandler(
    IInbox inbox,                          // this service's own ports
    IPlacedOrderReadModel readModel,
    IUnitOfWork unitOfWork)
{
    private static readonly string HandlerKey = typeof(PlacedOrderReadModelEventHandler).FullName!;

    public Task ConsumeAsync(OrderPlacedIntegrationEvent message, CancellationToken ct) =>
        unitOfWork.ExecuteAsync(async token =>     // execution strategy (/backend-development → Persistence)
        {
            await unitOfWork.StartAsync(token);   // the transaction the inbox insert requires (§4)
            if (!await inbox.TryAddAsync(message.EventId, HandlerKey, token))
                return; // redelivery after a lost acknowledgement or lock

            await readModel.UpsertAsync(message.OrderId, message.UserId, message.OccurredAt, token);
            await unitOfWork.SaveChangesAsync(token); // processed record and effect commit together
        }, ct);
}
```

The framework's dead-letter queue is the parked state, and its alert is configured on that queue.

---

## 9. Registration per Module

Each module's persistence registration calls these extensions once, over its own DbContext ([integration-events.md](integration-events.md) §3). A single-module solution calls them once, over its one DbContext. A consume-only module calls only `AddInbox`.

`eventAssemblies` is the module's `MyApp.{Module}.Application.AssemblyReference.EventAssemblies`: its own event assemblies plus every Contracts assembly whose events its handlers consume. Application may reference any module's Contracts; Persistence never references another module's Contracts, so it never names their types (`/clean-architecture-structure` → Dependency Graph).

```csharp
// MyApp.BuildingBlocks.Infrastructure
public static IServiceCollection AddOutbox<TDbContext>(
    this IServiceCollection services, IConfiguration section, IEnumerable<Assembly> eventAssemblies)
    where TDbContext : DbContext
{
    services.TryAddSingleton<IOutboxSerializer, OutboxSerializer>();       // shared: it holds no module state
    services.Configure<OutboxOptions>(typeof(TDbContext).Name, section);   // named options, one set per module
    services.TryAddSingleton(new EventTypeRegistry<TDbContext>(eventAssemblies)); // every event type in the module's schema
    services.AddScoped<IOutboxStore<TDbContext>, OutboxStore<TDbContext>>();
    services.AddHostedService<OutboxDispatcher<TDbContext>>();             // one dispatcher per module
    return services;
}

// The consuming side: the inbox store, the retry table and one retry worker per module.
// It registers the module's EventTypeRegistry<TDbContext> unless AddOutbox already did.
public static IServiceCollection AddInbox<TDbContext>(
    this IServiceCollection services, IConfiguration section,
    Assembly handlerAssembly, IEnumerable<Assembly> eventAssemblies)
    where TDbContext : DbContext
{
    services.TryAddSingleton<IOutboxSerializer, OutboxSerializer>();
    services.TryAddSingleton(new EventTypeRegistry<TDbContext>(eventAssemblies)); // the retry table's event types
    services.Configure<EventRetryOptions>(typeof(TDbContext).Name, section);
    services.AddSingleton(new HandlerTypeRegistry<TDbContext>(handlerAssembly)); // keyed by full name
    services.AddScoped<IRetryStore<TDbContext>, RetryStore<TDbContext>>();
    services.AddHostedService<EventRetryWorker<TDbContext>>();                   // one retry worker per module
    return services;
}
```

The module's own ports, in its Persistence project. The public members inherited from the generic machinery implement each interface:

```csharp
// MyApp.Sales.Persistence: IOutbox, IInbox and IEventRetryRecorder are MyApp.Sales.Application.Interfaces types.
internal sealed class SalesOutbox(SalesDbContext db, IOutboxSerializer serializer)
    : Outbox<SalesDbContext>(db, serializer), IOutbox { }

internal sealed class SalesInbox(SalesDbContext db)
    : Inbox<SalesDbContext>(db), IInbox { }

internal sealed class SalesEventRetryRecorder(
    IServiceScopeFactory scopeFactory,
    IOutboxSerializer serializer,
    IOptionsMonitor<EventRetryOptions> options,
    ILogger<SalesEventRetryRecorder> logger)
    : EventRetryRecorder<SalesDbContext>(scopeFactory, serializer, options, logger), IEventRetryRecorder { }
```
