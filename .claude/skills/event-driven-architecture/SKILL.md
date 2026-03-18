---
name: event-driven-architecture
description: Load this skill when designing event-based communication, creating domain events, integration events, or notification handlers. Covers event types, contracts, ordering guarantees, idempotency, and the MediatR notification pipeline used in this codebase.
user-invocable: true
---

# Event-Driven Architecture

## Governing Rule

Events decouple producers from consumers. The producer publishes a fact about what happened. Consumers decide independently how to react. Events MUST be first-class citizens with clear types and contracts so that all consumers know exactly how to process each event.

------------------------------------------------------------------------

# 1. Event Types

This system uses two categories of events:

## Domain Events (Intra-Process)

- Dispatched via `MediatR.INotification` within the same process
- Handled by `INotificationHandler<T>` implementations
- Execute within the same request scope as the command that raised them
- Used for: audit logging, cache invalidation, sending notifications, updating read models

## Integration Events (Inter-Service) — Future

- Published to an external message broker when multi-service architecture is adopted
- Consumed by other services asynchronously
- Require idempotency and ordering guarantees
- Start simple: use an outbox pattern with database polling before adopting a full message bus
- NEVER over-engineer — the current MediatR approach is correct for a single-service architecture

------------------------------------------------------------------------

# 2. Event Contracts

Every event MUST follow these contract rules:

## Rules

- MUST be immutable C# records
- MUST implement `MediatR.INotification`
- MUST contain only primitive types, enums, and IDs — no domain entity references
- MUST include a timestamp (`DateTime OccurredAt`) for events that may be processed asynchronously
- MUST include the actor ID (`Guid TriggeredBy`) for audit trail
- MUST be self-contained — consumers MUST NOT need to query back to understand the event
- MUST use past-tense naming: `UserCreatedEvent`, `PasswordChangedEvent`
- MUST be defined alongside their Feature in the Application layer

## Contract Structure

```csharp
public record {Entity}{Action}Event(
    Guid EntityId,
    // ... all relevant data fields ...
    Guid TriggeredBy,
    DateTime OccurredAt
) : INotification;
```

## Naming Convention

```
{Entity}{Action}Event
Examples: UserCreatedEvent, RoleAssignedEvent, UserLoggedInEvent
```

------------------------------------------------------------------------

# 3. Event Publishing

## Rules

- Commands publish events via `IPublisher.Publish()` AFTER the state change succeeds
- NEVER publish events before the state change is committed
- NEVER publish events for operations that failed
- One command MAY publish multiple events
- Events MUST be published in the order they logically occurred

## Publishing Pattern in Command Handlers

```csharp
// 1. Validate input
// 2. Execute domain logic
// 3. Persist changes to database
// 4. Publish event(s) — ONLY after success
await _publisher.Publish(new UserCreatedEvent(
    UserId: user.Id,
    Email: user.Email,
    TriggeredBy: currentUserId,
    OccurredAt: DateTime.UtcNow
), cancellationToken);
```

------------------------------------------------------------------------

# 4. Event Consumers (Notification Handlers)

## Rules

- Each handler does ONE thing (Single Responsibility)
- Handlers MUST be idempotent — processing the same event twice produces the same result
- Handlers MUST NOT throw exceptions that would break the originating command's transaction
- Handlers SHOULD catch and log their own failures
- Handlers MUST accept `CancellationToken` and propagate it
- One event MAY have multiple handlers (one for audit, one for email, one for cache)

## Idempotency Strategies

- Check if the side effect already occurred before executing (e.g., audit log entry already exists for this event)
- Use unique constraints on event-derived keys to prevent duplicate processing
- Design operations to be naturally idempotent (upsert instead of insert)

## Handler Structure

```csharp
public class AuditEventHandler : INotificationHandler<UserCreatedEvent>
{
    public async Task Handle(UserCreatedEvent notification, CancellationToken ct)
    {
        try
        {
            // Idempotency check
            // Execute side effect
            // Log success
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle {Event} for {EntityId}",
                nameof(UserCreatedEvent), notification.EntityId);
            // Do NOT rethrow — this would break the originating command
            // Optionally: queue for retry
        }
    }
}
```

------------------------------------------------------------------------

# 5. Ordering Guarantees

## Rules

- Within a single command, events are published in sequence — MediatR dispatches them in order of `Publish()` calls
- Handlers for the SAME event type execute in DI registration order
- For cross-command ordering (e.g., UserCreated before RoleAssigned), the command handler is responsible for sequencing the `Publish()` calls
- NEVER rely on handler execution order for correctness across different event types — if order matters, make it explicit in the publishing command
- All consumers MUST handle events in the correct order — if an event depends on a prior event having been processed, the consumer MUST verify preconditions

## Ensuring Correct Order

```csharp
// In a single handler that needs multiple events in order:
await _publisher.Publish(new UserCreatedEvent(...), ct);      // First
await _publisher.Publish(new RoleAssignedEvent(...), ct);     // Second — depends on user existing
```

------------------------------------------------------------------------

# 6. No Re-Processing Unless on Failure

## Rules

- Events are processed exactly once under normal conditions
- Re-processing occurs ONLY when a handler fails and a retry mechanism triggers
- NEVER replay events as a general strategy — this is NOT event sourcing
- If a handler needs historical data, query the read store — do not replay past events
- On failure retry: the handler MUST be idempotent so re-processing produces the same result

------------------------------------------------------------------------

# 7. Failure Handling

## If a Notification Handler Fails

1. **Log the error** with full context (event type, entity ID, timestamp, exception details)
2. **Do NOT rethrow** — this would break the originating command's response
3. **Queue for retry** if the side effect is critical (e.g., audit logging, compliance events)
4. For truly critical handlers where failure is unacceptable, consider making the operation part of the command's transaction instead of a separate event handler

## Resilient Handler Pattern

```csharp
public async Task Handle(SomeEvent notification, CancellationToken ct)
{
    try
    {
        // handler logic
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to handle {Event} for {EntityId}",
            nameof(SomeEvent), notification.EntityId);
        // Optionally: persist to a dead-letter table for retry
    }
}
```

------------------------------------------------------------------------

# 8. Current Event Inventory

| Event | Published By | Consumed By | Purpose |
|-------|-------------|-------------|---------|
| `UserCreatedEvent` | `CreateUserCommandHandler` | `AuditEventHandler` | Audit log entry for user creation |
| `UserLoggedInEvent` | `LoginCommandHandler` | `AuditEventHandler` | Audit log entry for login |
| `UserLoggedOutEvent` | `LogoutCommandHandler` | `AuditEventHandler` | Audit log entry for logout |
| `PasswordChangedEvent` | `ChangePasswordCommandHandler` | `AuditEventHandler` | Audit log entry for password change |
| `RoleAssignedEvent` | `AssignRoleCommandHandler` | `AuditEventHandler` | Audit log entry for role assignment |

> **When adding new events, update this inventory table.**

------------------------------------------------------------------------

# 9. Evolution Path

The architecture is designed to evolve incrementally. Do NOT skip steps.

| Stage | When | Approach |
|-------|------|----------|
| **Current** | Single service | MediatR in-process notifications — synchronous within request |
| **Stage 2** | Need durability | Add outbox pattern — persist events to `OutboxMessages` table in the same transaction as state change |
| **Stage 3** | Need async processing | Add background processor — poll outbox and dispatch to handlers asynchronously |
| **Stage 4** | True multi-service | Add message broker (RabbitMQ / Azure Service Bus) — only when inter-service communication is required |

> **NEVER over-engineer. The current MediatR approach is correct for a single-service architecture. Move to the next stage only when there is a concrete need, not a hypothetical one.**
