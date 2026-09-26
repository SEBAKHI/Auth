---
name: event-driven-architecture
description: Load this skill when designing or reviewing domain events, integration events, notification handlers, outbox, inbox or retry processing, or event consumers in a C#/.NET codebase, including communication between modules of a modular monolith. MediatR-specific parts apply when the project references MediatR (/dotnet-architecture scope note); the contract, idempotency, ordering and failure rules apply to any in-process or broker transport. Do NOT load for non-.NET stacks.
user-invocable: true
---

# Event-Driven Architecture

## Governing Rule

Events decouple producers from consumers. The producer publishes a fact about what happened. Consumers decide independently how to react. Events MUST be first-class citizens with clear types and contracts, so that every consumer knows exactly how to process each event.

## References

Every rule is stated in this file. The references hold examples only.

| Reference | Read this when |
|---|---|
| [references/handler-patterns.md](references/handler-patterns.md) | You write a notification handler, a publishing command handler, or the retry-recorder call. |
| [references/outbox-inbox-retry.md](references/outbox-inbox-retry.md) | You add the outbox and its dispatcher, the inbox, the retry store and worker, parking and alerting, or a broker transport. |
| [references/integration-events.md](references/integration-events.md) | You publish or consume an integration event between modules or services: contract project, versioning, dispatcher wiring, consumer placement. |

------------------------------------------------------------------------

# 1. Event Types and Scope

- **Domain event:** a fact inside one bounded context. Only handlers of that context subscribe to it. It never crosses a module or service boundary.
  - Typical side effects: audit records, cache invalidation, notifications to users, read-model updates.
- **Integration event:** a fact published for other bounded contexts, whether other modules of a modular monolith or other services. The consumer never references the publishing context's domain event types, Domain or Application.
- A reaction in another bounded context always consumes an integration event, whether that context is a module in the same process or a separate service.
- Where domain events live, who raises them and who publishes them: `/domain-driven-design` → §4 Domain Events (Placement).

------------------------------------------------------------------------

# 2. Contracts

- Events are immutable C# records, named in past tense.
  - Domain events: `{Entity}{PastTenseVerb}Event` (`/domain-driven-design` §4).
  - Integration events: `{Entity}{PastTenseVerb}IntegrationEvent`.
- Fields, in this order: `EventId`, `{Entity}Id`, `{Entity}Version` (only when consumers must apply the entity's events in order, §5), the data consumers need, `TriggeredBy`, `OccurredAt`.
- Field types:
  - `EventId` is a `Guid`.
  - `{Entity}Id` and `TriggeredBy` use the BCL scalar of the repository's ID types (`Guid` in the examples), and `TriggeredBy` is nullable.
  - `{Entity}Version` is a `long`, the aggregate's sequence number.
  - `OccurredAt` is a `DateTimeOffset`.
- `EventId` is generated once, when the event is created. It is never regenerated on retry or dispatch.
- `TriggeredBy` is the authenticated user who caused the change.
  - For operations that establish identity (sign-up, sign-in), it is the account the operation established.
  - It is null when there is no such account (a background job, a failed sign-in for an unknown account), never an empty ID.
- `OccurredAt` is the UTC instant of the change, read from `TimeProvider.GetUtcNow()`, never from `DateTime.UtcNow`. Under Placement option A, the aggregate receives it as a parameter.
- Allowed types:
  - BCL scalars (bool, numeric types, string, Guid, DateTimeOffset, DateOnly, TimeOnly);
  - enums, and IDs as their BCL scalar;
  - immutable records made only of allowed types, declared with the event (for an integration event, in the same contract project; in `{Module}.Contracts` such a record is named `*Dto`, `/clean-architecture-structure` → Architecture Tests);
  - read-only collections of allowed types, copied at construction (`IReadOnlyList<T>`).
- No entities, value objects or other domain types: unwrap value objects (`user.Email.Value`).
- Domain events may use their own context's enums. Integration events declare their own enums or use strings.
- The same allowed types govern every type in `{Module}.Contracts` (`/clean-architecture-structure` → Module Boundaries).
- Events are self-contained: consumers never query back to understand one.
- An event carries only the personal data its consumers need.
- Events never carry credentials or secrets: passwords, password hashes, refresh, reset, invitation or verification tokens and codes, TOTP secrets, two-factor recovery codes, API keys, connection strings. Carry the owning record's ID instead.
- One exception: a handler whose job is to deliver a one-time secret to its owner (a reset link, a verification code) may receive that secret in the event while dispatch stays in memory.
  - Before the event is persisted anywhere (outbox, retry or parked records), that field is encrypted per `/security-mindset` → Cryptography Standards.
  - The row is deleted once the secret has been delivered.
- Event payloads are never logged. Log lines carry `EventId`, the event type and entity IDs (`/security-mindset` → Logging Content).
- Domain events reach handlers as `MediatR.INotification`, directly or wrapped by the Infrastructure dispatcher in the adapter type that Application declares, per the Placement decision (`/domain-driven-design` §4).
- Schema versions: a breaking change is a new type with a version suffix `V{n}` (for example `{Entity}{PastTenseVerb}IntegrationEventV2`, then `V3`); any other change is additive.

## Contract Shape

```csharp
public sealed record {Entity}{PastTenseVerb}Event(
    Guid EventId,
    Guid {Entity}Id,          // the BCL scalar of the entity's ID type
    /* long {Entity}Version, only when consumers need strict entity order (§5) */
    /* the data consumers need: allowed types only */
    Guid? TriggeredBy,        // the BCL scalar of the user ID type, nullable
    DateTimeOffset OccurredAt) : INotification; // Placement option A, adapter variant: the Domain marker interface instead
```

------------------------------------------------------------------------

# 3. Publishing

- Publish only after the state change has committed, and never for a failed operation.
  - Option B: the command handler publishes through `IPublisher` after `IUnitOfWork.SaveChangesAsync`, or, from stage 2 on, adds the event to the outbox before the commit (§8, §9).
  - Option A: the Infrastructure dispatcher publishes the events the aggregate collected, after the commit, and command handlers do not publish.

  See `/domain-driven-design` §4.
- From stage 2 on (§9), an event that has a critical handler is written to the outbox before the commit: under option B by the command handler through the module's `IOutbox` port, under option A by the unit of work's pre-commit step. It is not also published inline unless that inline publish then marks its row sent through the same port; such a fast-path row is written due only after a grace delay (the dispatcher lease), so the dispatcher claims it only after a delivery failure (§6). An event whose handlers are all best-effort is published inline, as above.
- Every call up to and including the commit takes the request's `CancellationToken` (`/backend-development` → Async and Cancellation).
- After the commit, the post-commit publish passes `CancellationToken.None`, and no other token: the command handler's publish under option B, the Infrastructure dispatcher's inline publish under option A. The state change is durable, and a client disconnect must not cancel its side effects. Side effects that must survive a process stop go through the outbox (§9).
- One command may publish several events, in the order they occurred.
- Command handler steps:
  1. Input validation already ran in the validation pipeline behavior (P6).
  2. Load the aggregate and call its behavior method; return its errors.
  3. Commit the unit of work.
  4. Publish (option B), only after the commit; or, from stage 2 on, add the event to the outbox before the commit of step 3 (§8, §9).

------------------------------------------------------------------------

# 4. Handlers

**Naming and placement.** Notification handlers are named `{Purpose}EventHandler`, where Purpose names the side effect (`Audit`, `WelcomeEmail`, `CacheInvalidation`), never the event, because one event may have several handlers. One class may implement `INotificationHandler<T>` for several events only when it performs the same single side effect for each. Placement: `/clean-architecture-structure` → Feature Slices. Handlers depend on Application and Domain types and interfaces, on other modules' `{Module}.Contracts` types (integration events and public queries), and on the abstractions of libraries that Application already uses (`INotificationHandler<T>`, `ILogger<T>`, `TimeProvider`, `IServiceScopeFactory`). They never depend on the API layer or on concrete Infrastructure or Persistence types.

- Each handler performs one side effect (single responsibility). One event may have several handlers.
- A handler passes the `CancellationToken` it receives to every async call, and bounds its external calls per `/failure-mode-design` → Retries, Timeouts and Circuit Breakers.

**Scope.** With inline dispatch, handlers run in the command's DI scope, after its transaction has committed. They MUST NOT rely on that scope:
- they read no ambient request context (current user, HttpContext, claims); they use `TriggeredBy` instead;
- they assume no open transaction;
- a handler that writes state creates its own DI scope (`IServiceScopeFactory.CreateAsyncScope()`), resolves the unit of work and repositories from it, and commits there, because the command's unit of work has already committed (`/backend-development` → Persistence); it sets that scope's current-user port to the event's `TriggeredBy` before it resolves services and saves, so audit stamps are the same inline and under a background dispatcher (`/domain-driven-design` §1; the port's override: `/backend-development` → Authorization);
- the processed record, the effect and the commit run in one transaction that the handler's unit of work opens, through its execution-strategy wrapper where the provider retries transient failures (`/backend-development` → Persistence).

A handler behaves the same when a background dispatcher invokes it in a fresh scope.

**Isolation.** A handler invoked by an in-process publisher never lets a failure reach the publisher.
- It catches every exception except the cancellation of its own token, with the cancellation filter of `/failure-mode-design` → Exception-Handling Boundaries: `catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`, where `cancellationToken` is the token the handler received. It logs the failure once with context, handles it per §7, and does not rethrow.
- Handlers run after the commit, so an exception cannot roll the change back. It would fail the command's response and skip the remaining handlers.
- This is the one sanctioned per-handler catch-all (`/failure-mode-design` → Exception-Handling Boundaries).

**Idempotency.** The key is `(EventId, handler)`.
- A handler that changes state inserts a processed record under a unique constraint on `(EventId, handler)`, in the same transaction as its own change. A duplicate violates the constraint and is skipped. A check without the constraint is not enough. The processed-record table is the inbox.
- A side effect outside the database (email, HTTP) records itself after success, and passes `EventId` as the provider's idempotency key where the provider supports one. Otherwise the effect is at-least-once, and the handler's XML summary says so.
- Prefer natural idempotency (upsert, set-to-value) wherever it applies.

**Critical or best-effort.** Every handler states its class in its XML summary.
- Critical: losing the effect breaks a requirement. Examples: security audit events (authentication success and failure, credential changes, privilege changes), compliance records, and read models with no rebuild path.
- Every consumer of an integration event (§8) is critical, unless the repository ADR accepts best-effort for that consumer.
- A critical handler's event is written to the outbox in the originating transaction (§3; §9, durability stage). The handler's failures are recorded for retry, parked after the retry limit, and alerted (§7).
- Best-effort: the handler logs the failure and drops the event.
- Never move a side effect into the command handler (P6), and never publish before the commit (§3).

------------------------------------------------------------------------

# 5. Ordering

- Handlers of the same event are independent. None relies on another handler of the same event having run, or on their relative order.
- The solution uses MediatR's default `ForeachAwaitPublisher`, which runs handlers sequentially. Never register `TaskWhenAllPublisher` or another concurrent publisher: handlers share the scope's services, including a DbContext, which does not support concurrent operations.
- Within one command, the command handler orders its `Publish` calls. Across commands there is no ordering guarantee once dispatch is asynchronous (§9, durability stage and later).
- A consumer that depends on an earlier event checks its precondition. When the precondition is not met, it records the event for retry (§7), because the earlier event may still be in flight; the event is parked after the retry limit.
- When a consumer must apply one entity's events strictly in order, the events carry `{Entity}Version` (§2), and the consumer applies version n only after n−1.
- Never rely on handler execution order for correctness.

------------------------------------------------------------------------

# 6. Delivery and Re-Processing

- Inline dispatch runs each handler once per publish.
- With an outbox or a broker, delivery is at-least-once, with one stated exception: a critical handler that can write neither its effect nor its retry record loses that effect (§7, item 5). The Critical log entry and its alert make the loss detectable.
- Re-processing occurs only after a failure. That is either a handler failure, or a delivery failure: a dispatcher crash before an outbox row is marked sent, or a lost broker acknowledgement or lock.
- Delivery failures produce duplicates even when every handler succeeded, so idempotency (§4) is unconditional. Never gate it on a retry flag.
- Never replay events as a general strategy: this is not event sourcing. A handler that needs history queries the read store.

------------------------------------------------------------------------

# 7. Failure Handling and Retry

**In-process dispatch** (inline, outbox dispatcher, or a broker bridge that republishes into MediatR):

1. The handler's catch (§4) logs the failure once.
   - For a critical handler it also writes a retry record to the retry table, in its own transaction.
   - The record holds `EventId`, the event type, the serialized payload (secrets per §2), the handler type, the attempt count, the next attempt time and the last error type.
   - Writing the record is idempotent on `(EventId, handler)`.
2. A retry worker re-invokes only that handler, in a fresh DI scope, with exponential backoff and jitter, up to a configured attempt limit. The worker is a `BackgroundService` with one catch per loop iteration (`/failure-mode-design` → Exception-Handling Boundaries).
3. After the last attempt the record is parked (the dead-letter state). A parked record is never retried automatically; it raises an alert and is resubmitted only by an operator.
4. Handlers never throw, so an in-process dispatcher treats an event as delivered once all its handlers have returned.
5. If the retry record itself cannot be written, log at Critical and raise an alert. This is the one case where the side effect is lost (§6), and it must be detectable.

**Broker consumers** (a messaging framework invokes the consumer directly):

6. The consumer has no catch-all. A failure reaches the framework, whose retry and dead-letter handling apply. Its dead-letter queue is the parked state, and it raises an alert. The consumer is idempotent through the inbox (§4).

------------------------------------------------------------------------

# 8. Integration Events Across Modules and Services

Solution layouts, and which module may reference what: `/clean-architecture-structure` → Solution Layouts, Module Boundaries.

- **Contract location.**
  - Modular monolith: the publishing module's `{Module}.Contracts` project, folder `IntegrationEvents/`. It is the only project of that module that another module may reference (`/clean-architecture-structure` → Module Boundaries; an architecture test enforces it).
  - Separate services: each service declares its own copy of the contract and matches events by name and version. There is no shared event library.
- **Writing.** The integration event is added to the publishing module's outbox inside the originating unit of work, before the commit:
  - by the command handler, through an Application port; or
  - by a pre-commit step of the unit of work that passes the collected domain events to the module's integration-event mapper (an interface and its implementation, both in the module's Application project, which references its own Contracts) and writes the integration events it returns.

  Never by a handler that runs after the commit.
- **Dispatching.** After the commit, a background dispatcher delivers unsent rows and marks them sent:
  - between modules of one process: through `IPublisher`, in a fresh DI scope per message;
  - between processes: to a broker (§9);
  - a row whose delivery fails is retried with exponential backoff and jitter up to a configured attempt limit, then parked and alerted like a retry record (§7, item 3); it is never dropped.
- **Delivery guarantee.** Every integration event goes through the outbox, between modules of one process as well as between processes. There is no delivery without an outbox.
- **Module scope.** In a modular monolith the outbox, the inbox and the retry table are per module, in the module's schema. Their ports follow `/clean-architecture-structure` → Layout (b). Each module runs one outbox dispatcher and one retry worker over its own schema. The event-type registry that deserializes stored events keys each type by its full name. Its assemblies, including the Contracts assemblies whose events the module consumes, are listed by the module's Application project; Persistence never references another module's Contracts (`/clean-architecture-structure` → Dependency Graph).
- **Consuming.** Every consumer is critical (§4) unless the repository ADR accepts best-effort for that consumer.
  - It deduplicates through the consuming module's inbox, keyed by `(EventId, handler)`.
  - Its failures are retried, parked and alerted (§7).
  - A consumer never reads the publishing context's tables (`/domain-driven-design` §7).

------------------------------------------------------------------------

# 9. Evolution Stages

The repository ADR records the current stage. Move to the next stage only for a concrete need, never a hypothetical one.

| Stage | Approach |
|---|---|
| 1. Inline | Events are published in-process after the commit (§3). They are lost if the process stops between the commit and the publish, or if a request cancellation interrupts the commit call after the database has committed. |
| 2. Durability | Events are written to an outbox table in the originating transaction, and a background dispatcher publishes unsent rows and marks them sent. Inline dispatch after the commit may stay as the fast path (§3): the row is written due only after a grace delay and marked sent after the inline publish, and the dispatcher recovers rows that a crash left unsent. An outbox without a dispatcher is not a stage. |
| 3. Broker | Events cross a process boundary: the dispatcher's transport becomes a broker. |

A solution that has a critical handler (§4) or integration events (§8) runs stage 2 or later. Stage 1 serves only solutions whose handlers are all best-effort.
