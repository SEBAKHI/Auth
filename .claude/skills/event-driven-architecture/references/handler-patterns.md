# Handler Patterns

Examples for `SKILL.md` §3, §4, §5 and §7. Every rule they apply is stated in `SKILL.md`; this file adds none. Names (`IOutbox`, `IInbox`, `IEventRetryRecorder`, `IAuditTrail`) are example Application ports of the handler's own module: in a modular monolith each module declares them in its own namespace (`MyApp.{Module}.Application.Interfaces`), and a handler resolves only its own module's ports (`SKILL.md` §8, Module scope). Their implementation over the generic machinery is in [outbox-inbox-retry.md](outbox-inbox-retry.md).

A fuller command handler that publishes (`CreateUserCommandHandler`) and a best-effort `WelcomeEmailEventHandler` live in `/backend-development` → `references/handler-examples.md`.

---

## 1. Publishing Command Handler (Placement option B, stage 1)

Placement option B: the command handler publishes after the commit. Under option A the handler only persists, and the Infrastructure dispatcher publishes (`/domain-driven-design` §4).

The canonical option B handler is `CancelOrderCommandHandler` in `/domain-driven-design` → `references/domain-model-examples.md` §5, over the `Order` aggregate of §2 there. It follows the command handler steps of `SKILL.md` §3:

1. Input validation already ran in the validation pipeline behavior (P6).
2. It loads the aggregate, calls its behavior method with the actor and the instant read once from `TimeProvider.GetUtcNow()`, and returns its errors.
3. It commits the unit of work. Every call up to and including the commit takes the request token.
4. Only after the commit, it publishes `OrderCancelledEvent` with `CancellationToken.None` (post-commit: not tied to the request). `EventId` is generated there, once; `OccurredAt` is the instant of step 2; `TriggeredBy` is the current user's ID, null when there is no authenticated account.

Several events: one awaited `Publish` call each, in the order they occurred.

From stage 2 on (§9), an event with a critical handler is written to the outbox before the commit, and the dispatcher publishes it; it is also published inline only on the fast path, which then marks its row sent (`SKILL.md` §3). See [outbox-inbox-retry.md](outbox-inbox-retry.md) §2 and §3.

---

## 2. Critical Handler: Own Scope, Inbox, Retry Recorder

One class handles two events because it performs the same single side effect (a security audit record) for each.

```csharp
/// <summary>
/// Writes the security audit record for credential changes and sign-ins.
/// Critical (security audit event): the events reach it through the outbox, and its
/// failures are recorded for retry, parked after the retry limit, and alerted (§7).
/// </summary>
public sealed class AuditEventHandler(
    IServiceScopeFactory scopeFactory,
    IEventRetryRecorder retryRecorder,
    ILogger<AuditEventHandler> logger)
    : INotificationHandler<PasswordChangedEvent>,
      INotificationHandler<UserSignedInEvent>
{
    private static readonly string HandlerKey = typeof(AuditEventHandler).FullName!;

    public Task Handle(PasswordChangedEvent notification, CancellationToken ct) =>
        WriteAsync(notification, notification.EventId, notification.UserId, "PasswordChanged",
            notification.TriggeredBy, notification.OccurredAt, ct);

    public Task Handle(UserSignedInEvent notification, CancellationToken ct) =>
        WriteAsync(notification, notification.EventId, notification.UserId, "SignedIn",
            notification.TriggeredBy, notification.OccurredAt, ct);

    private async Task WriteAsync(
        INotification notification, Guid eventId, Guid userId, string action,
        Guid? triggeredBy, DateTimeOffset occurredAt, CancellationToken ct)
    {
        try
        {
            // Own scope: the command's unit of work has already committed (§4 Scope). Before resolving
            // services, the scope's current-user port is set to triggeredBy (/backend-development → Authorization).
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var inbox = services.GetRequiredService<IInbox>();
            var auditTrail = services.GetRequiredService<IAuditTrail>();
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();

            // Execution strategy: a transient failure reruns the whole body (/backend-development → Persistence).
            await unitOfWork.ExecuteAsync(async token =>
            {
                await unitOfWork.StartAsync(token); // opens the transaction the inbox insert requires

                // Processed record under the unique (EventId, handler) constraint.
                if (!await inbox.TryAddAsync(eventId, HandlerKey, token))
                    return; // duplicate delivery: the effect is already committed

                auditTrail.Add(new AuditRecord(eventId, action, userId, triggeredBy, occurredAt));

                // The processed record and the effect commit in one transaction.
                await unitOfWork.SaveChangesAsync(token);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Log once: EventId, the event type and entity IDs; never the payload.
            logger.LogError(ex, "{Handler} failed for {EventType} {EventId}, user {UserId}",
                HandlerKey, notification.GetType().Name, eventId, userId);

            // Critical: record for retry. Do NOT rethrow: this would fail the committed
            // command's response and skip the remaining handlers.
            await retryRecorder.RecordAsync(notification, typeof(AuditEventHandler), ex.GetType().Name, ct);
        }
    }
}
```

Handler ports used above, declared by the handler's own module:

```csharp
// MyApp.{Module}.Application.Interfaces (layout (a): MyApp.Application.Interfaces)
public interface IInbox
{
    /// <summary>
    /// Inserts the processed record for (eventId, handler) inside the unit of work's open
    /// transaction, which it requires, so the record commits or rolls back with the effect.
    /// Returns false when the unique constraint reports an existing record (a duplicate).
    /// </summary>
    Task<bool> TryAddAsync(Guid eventId, string handler, CancellationToken ct);

    /// <summary>
    /// True when a processed record for (eventId, handler) exists. Used only before a side
    /// effect outside the database (section 5); state changes rely on TryAddAsync's constraint.
    /// </summary>
    Task<bool> ExistsAsync(Guid eventId, string handler, CancellationToken ct);
}

public interface IEventRetryRecorder
{
    /// <summary>
    /// Writes or updates the retry record for (EventId, handler) in its own transaction.
    /// Never throws for a write failure: it logs at Critical instead (§7, item 5).
    /// </summary>
    Task RecordAsync(INotification notification, Type handlerType, string errorType, CancellationToken ct);
}
```

`IUnitOfWork.StartAsync` (idempotent begin) and `IUnitOfWork.ExecuteAsync` (the execution-strategy wrapper) are the unit of work's members in `/backend-development` → Persistence.

---

## 3. Best-Effort Handler

```csharp
/// <summary>
/// Removes the cached order summary when an order is cancelled.
/// Best-effort: a failure is logged and the event dropped; the entry expires on its own.
/// Naturally idempotent (removing a missing key is a no-op).
/// </summary>
public sealed class CacheInvalidationEventHandler(
    ICacheService cache,
    ILogger<CacheInvalidationEventHandler> logger)
    : INotificationHandler<OrderCancelledEvent>
{
    public async Task Handle(OrderCancelledEvent notification, CancellationToken ct)
    {
        try
        {
            // ICacheService (Application port) applies /failure-mode-design → Failure Responses for the cache.
            // CacheKeys is the bounded context's own class (/backend-development → Caching).
            await cache.RemoveAsync(CacheKeys.OrderSummary(notification.OrderId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "{Handler} failed for {EventType} {EventId}, order {OrderId}",
                nameof(CacheInvalidationEventHandler), nameof(OrderCancelledEvent),
                notification.EventId, notification.OrderId);
            // Best-effort: no retry record. Do NOT rethrow.
        }
    }
}
```

---

## 4. Ordered Consumer: Precondition and `{Entity}Version`

The events carry `OrderVersion`, the aggregate's sequence number. Version n is applied only after n−1; an event whose predecessor has not arrived is recorded for retry, not dropped (§5).

```csharp
/// <summary>
/// Maintains the order-status read model. Critical: the read model has no rebuild path.
/// Applies each order's events strictly in OrderVersion order (§5).
/// </summary>
public sealed class OrderStatusReadModelEventHandler(
    IServiceScopeFactory scopeFactory,
    IEventRetryRecorder retryRecorder,
    ILogger<OrderStatusReadModelEventHandler> logger)
    : INotificationHandler<OrderStatusChangedEvent>
{
    private static readonly string HandlerKey = typeof(OrderStatusReadModelEventHandler).FullName!;

    public async Task Handle(OrderStatusChangedEvent notification, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var inbox = services.GetRequiredService<IInbox>();
            var readModel = services.GetRequiredService<IOrderStatusReadModel>();
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();

            await unitOfWork.ExecuteAsync(async token =>   // execution strategy, as in section 2
            {
                await unitOfWork.StartAsync(token);        // the inbox insert runs in this transaction

                if (!await inbox.TryAddAsync(notification.EventId, HandlerKey, token))
                    return; // duplicate

                var appliedVersion = await readModel.GetVersionAsync(notification.OrderId, token); // 0 when absent
                if (notification.OrderVersion <= appliedVersion)
                    return; // already applied; disposing the scope rolls back the uncommitted inbox insert

                if (notification.OrderVersion != appliedVersion + 1)
                {
                    // Precondition not met: version n-1 may still be in flight. Record, never drop.
                    await retryRecorder.RecordAsync(notification, typeof(OrderStatusReadModelEventHandler),
                        "PreconditionNotMet", token);
                    return; // no commit: the inbox insert rolls back, so the retry is not seen as a duplicate
                }

                // The store's update is conditional on the applied version (n-1), so a
                // concurrent delivery cannot apply the same version twice.
                await readModel.ApplyAsync(notification.OrderId, notification.Status, notification.OrderVersion, token);
                await unitOfWork.SaveChangesAsync(token);
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "{Handler} failed for {EventType} {EventId}, order {OrderId}",
                HandlerKey, nameof(OrderStatusChangedEvent), notification.EventId, notification.OrderId);
            await retryRecorder.RecordAsync(notification, typeof(OrderStatusReadModelEventHandler),
                ex.GetType().Name, ct);
        }
    }
}
```

---

## 5. External Side Effect With a Provider Idempotency Key

A side effect outside the database records itself after success, and passes `EventId` as the provider's idempotency key when the provider supports one (§4 Idempotency).

```csharp
// Inside the try block of a critical handler, in its own scope:
if (await inbox.ExistsAsync(notification.EventId, HandlerKey, ct))
    return; // already delivered

await paymentGateway.RefundAsync(
    new RefundRequest(notification.PaymentId, notification.Amount),
    idempotencyKey: notification.EventId.ToString(), // the provider deduplicates a repeat call
    ct);

// Record after success, in the unit of work's transaction (section 2).
await unitOfWork.ExecuteAsync(async token =>
{
    await unitOfWork.StartAsync(token);
    await inbox.TryAddAsync(notification.EventId, HandlerKey, token);
    await unitOfWork.SaveChangesAsync(token);
}, ct);
```

Without a provider key, a crash between the call and the record repeats the call on the next delivery: the effect is at-least-once, and the handler's XML summary says so.
