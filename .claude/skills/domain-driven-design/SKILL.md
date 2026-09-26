---
name: domain-driven-design
description: Load this skill when designing domain models, entities, value objects, aggregates, domain events, domain errors and their codes, or bounded contexts in a C#/.NET codebase. Covers DDD tactical patterns with ErrorOr as the result type, event Placement, the error catalog and its code format, and how bounded contexts map to modules. Invoke when structuring the Domain layer or defining business rules. The patterns assume C# and ErrorOr — do NOT load for other stacks.
user-invocable: true
---

# Domain-Driven Design (DDD)

## Governing Rule

The Domain layer is the heart of the system. It contains ALL business rules and logic.

- It references only what the Domain allowlist of `/dotnet-architecture` P1 permits. No infrastructure, persistence, web, messaging, serialization or IO framework enters the Domain.
- All domain objects must be rich: behavior lives WITH the data it operates on. Anemic domain models are PROHIBITED.

**Reference files** (examples only; every rule is stated in this file):

- [references/domain-model-examples.md](references/domain-model-examples.md) — Read this when you write an entity, value object or aggregate and want the canonical ErrorOr factory, record and behavior-method shapes, or a Placement option A or B code sample.
- [references/domain-errors.md](references/domain-errors.md) — Read this when you declare a `{Concept}Errors` class, choose an ErrorType, declare a custom ErrorType, or map a client contract's code vocabulary onto the catalog.

## Domain Project Structure

This is the single statement of the Domain project's folders. Every other project's layout: `/clean-architecture-structure`.

```
MyApp.Domain/                   (layout (b): MyApp.{Module}.Domain/)
    Primitives/                 EntityBase, AuditableEntityBase (in the SharedKernel project when the solution has one)
    Entities/                   aggregate roots and child entities (no Aggregates/ folder)
    ValueObjects/
    Events/                     Placement option A only
    Errors/                     {Concept}Errors.cs; layout (a): also the custom ErrorType constants
    Interfaces/Repositories/    I{AggregateRoot}Repository.cs
    Services/                   stateless domain services
    Enums/
    Constants/
    Specifications/
```

- One Domain project per bounded context (in layout (b), the module's Domain project); its tree is flat.
- There is no `Exceptions/` folder: Domain and Application define no custom exception types (P7).

------------------------------------------------------------------------

# 1. Entities

Entities have identity (Id) that persists across state changes.

- MUST inherit from `EntityBase`. Base types (`EntityBase`, `AuditableEntityBase`) live in the SharedKernel project when the solution has one (a modular monolith always does), otherwise in `Domain/Primitives/` (P10)
- Auditable entities MUST inherit from `AuditableEntityBase` (adds CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
- Audit fields are set by the persistence layer when changes are saved (EF Core: a SaveChanges interceptor that reads `TimeProvider` and the current-user port), never by callers, and never by the entity reading the clock. In a DI scope that a notification handler, dispatcher or worker creates, the current-user port returns the event's `TriggeredBy`: the handler sets it on the scope before resolving services, and it is never read from the request (`/event-driven-architecture` §4; the port's override: `/backend-development` → Authorization). The stamp is therefore the same inline and under the outbox dispatcher. Example: [references/domain-model-examples.md](references/domain-model-examples.md) §1.
- MUST have a private/protected parameterless constructor for the persistence layer (ORM or Dapper materialization)
- MUST create instances through a static factory method returning `ErrorOr<T>` that enforces invariants. A public constructor is permitted only when no invariant can fail on its inputs
- Entities are created only by their factory (or the invariant-free public constructor above) or by the persistence layer through the private constructor
- Mapping code maps entity to DTO only, never DTO to entity
- Caches store DTOs or read models, never entities
- MUST NOT expose public setters — state changes through behavior methods only
- MUST validate all inputs in methods that change state
- MUST return `ErrorOr<T>` from methods that can fail due to business rules

------------------------------------------------------------------------

# 2. Value Objects

Value Objects have no identity. They are defined entirely by their attributes. Two Value Objects with the same attributes are equal.

## Rules

- MUST be immutable (all properties `{ get; }` only, set via constructor)
- MUST override `Equals()` and `GetHashCode()` based on all properties (class-based value objects; records follow the record rule below)
- MUST validate invariants in the static factory method; the constructor is private (a public constructor is permitted only when no invariant can fail on its inputs)
- MUST use a static factory method returning `ErrorOr<T>` for creation when validation is needed
- MUST NOT have an `Id` property
- Records are allowed only as a non-positional `sealed record class`, or as a `readonly record struct` whose `default` value is itself valid (a struct's `default` bypasses every constructor and factory)
  - Never a mutable `record struct`, and never a positional record (its primary constructor is always public and its synthesized properties are settable)
  - Declare each property explicitly as `{ get; }` (no `init`) so that `with` cannot bypass validation
  - Make the constructor private and create instances only through the static ErrorOr factory (or the invariant-free public constructor the rule above permits)
  - Records synthesize `Equals`/`GetHashCode`: do not declare `override Equals(object)`. When a member is a collection, implement `Equals(T? other)` (record class) or `Equals(T other)` (record struct — `T?` there declares a separate `Nullable<T>` overload and leaves the synthesized reference comparison in place) and `GetHashCode()` yourself

## When to Use

- Email addresses, phone numbers, monetary amounts, date ranges
- Any concept where identity does not matter, only the value
- Any group of properties that always travel together
- Replacing primitive obsession (string for Email, decimal for Money)

------------------------------------------------------------------------

# 3. Aggregates

An Aggregate is a cluster of Entities and Value Objects treated as a single unit for data changes. The root Entity is the Aggregate Root.

## Rules

- MUST have a single Aggregate Root (the entry point)
- External objects MUST NOT hold references to inner Aggregate members directly
- All changes to the Aggregate MUST go through the Aggregate Root
- Aggregate boundaries define transaction boundaries
- Reference other Aggregates by ID only, never by direct object reference
- Keep Aggregates small — include only what must be consistent within a single transaction

A repository's list of aggregate roots and their boundaries is a fact of that repository. Record it in `<repo>/.claude/CLAUDE.md`, pointing at the code as the source of truth — never in this skill.

```csharp
// CORRECT — reference by ID
public Guid UserId { get; private set; }

// PROHIBITED — direct object reference to another aggregate
public User User { get; set; }
```

------------------------------------------------------------------------

# 4. Domain Events

## Rules

- Domain events record that something important happened inside one bounded context. They are immutable records, named in past tense `{Entity}{PastTenseVerb}Event` (`UserCreatedEvent`, `PasswordChangedEvent`).
- Contract (fields, allowed types, secrets): `/event-driven-architecture` §2. Publishing, handlers, ordering and retry: `/event-driven-architecture` §3–§7.
- Events are published only after the originating transaction commits, under both Placement options. Side effects are therefore not atomic with the state change; durability: `/event-driven-architecture` §9.

## Placement

Placement is a per-repository decision, and this section is its single owner. Record it in `<repo>/.claude/CLAUDE.md` or an ADR, naming the option and, for Option A, the variant (contracts or adapter). Two coherent options:

| Option | Location | Raised by | MediatR coupling | Trade-off |
|--------|----------|-----------|------------------|-----------|
| **A — Domain-raised** | `Domain/Events/` (Domain Project Structure) | Aggregate roots collect them; an Infrastructure dispatcher publishes them after the transaction commits | Domain references `MediatR.Contracts` (interfaces only), permitted by the Domain allowlist (Governing Rule), **or** Domain owns a marker interface, and the Infrastructure dispatcher wraps each event in an `INotification` adapter declared in Application (layout (b): `MyApp.BuildingBlocks.Application`) | The contracts variant adds an allowlisted package to Domain; the adapter keeps Domain free of MediatR at the cost of one generic wrapper |
| **B — Handler-raised** | `Application/Features/{Feature}/{Action}/{Entity}{PastTenseVerb}Event.cs` — the use-case slice of `/clean-architecture-structure` (for example `Features/Users/CreateUser/UserCreatedEvent.cs`); in a modular monolith, inside that module's Application project | Command handlers create them and publish them through `IPublisher` after the transaction commits; from stage 2 on (`/event-driven-architecture` §9), an event that goes through the outbox is added to the module's outbox before the commit, and also published after the commit only where the repository keeps the inline fast path | None in Domain | Allowed only when emitting the event is not itself a business rule (the event only reports that the command succeeded). If an entity must decide whether the event occurs, the repository chooses Option A |

- Under Option A, command handlers only persist; they MUST NOT publish. The Infrastructure dispatcher publishes the events the aggregate collected, after the commit.
- Under Option A, aggregate behavior and factory methods that raise events receive the actor id and the current time as parameters (the handler reads the time from `TimeProvider`), and the aggregate generates `EventId` when it creates the event. The Domain never reads the clock or the current user.
- Under Option B, the command handler publishes through `IPublisher` after the commit. From stage 2 on (`/event-driven-architecture` §9, per §3), it instead adds each event that goes through the outbox to the module's outbox before the commit, and also publishes it after the commit only where the repository keeps the inline fast path.

------------------------------------------------------------------------

# 5. Domain Errors

Domain Errors represent business rule violations. They use the ErrorOr library. Examples of every member shape: [references/domain-errors.md](references/domain-errors.md).

## Rules

- The `{Concept}Errors` classes in `Domain/Errors/` are the bounded context's single error catalog. Every code that the context emits is declared there (in layout (b), the shared technical request-rule codes of the next bullet excepted), including:
  - the codes that Application raises (authentication failures such as `User.InvalidCredentials`, validation-pipeline codes);
  - its `Failure` and `Unexpected` errors.
- Codes of technical request rules that every context applies the same way (paging and sorting parameters) are declared once, in a `{Concept}Errors` class in `MyApp.SharedKernel/Errors/` (layout (a): `Domain/Errors/`). The shared validator rules in `MyApp.BuildingBlocks.Application` (layout (a): the Application project) use them.
- Members are static members of a static class per concept:
  - `static readonly` fields for errors that take no parameters and carry no metadata;
  - static methods for errors that take parameters or carry metadata. Each call builds a new `Error` and a new metadata dictionary, because ErrorOr's `Error.Metadata` is a mutable dictionary and a shared instance would be static mutable state (P4).
- ErrorOr's typed factory is chosen by meaning, from this closed list:
  - `Error.Validation()`: the input violates a rule.
  - `Error.NotFound()`: the addressed resource does not exist.
  - `Error.Conflict()`: the current state or a uniqueness rule forbids the operation.
  - `Error.Unauthorized()`: the caller is not authenticated. A wrong secret re-entered inside an already authenticated request (the current password in a password change, a step-up confirmation) is `Error.Validation()` with the property path in metadata, never Unauthorized, because clients answer a 401 on a credentialed request by refreshing the session and replaying the request (`/frontend-playbook` rules/session.md), which spends a refresh token and hides the field error.
  - `Error.Forbidden()`: the authenticated caller lacks permission.
  - `Error.Failure()` / `Error.Unexpected()`: a fault of the system itself, neither the caller's input nor a dependency outage, that a handler or port reports as a value instead of throwing. The description is a fixed, generic message; exception text, connection details and stack traces go to the log only.
  - `Error.Custom()`: only with a numeric type and an HTTP status recorded in the repository ADR and added to the single map (`/backend-development` → Status Map). The numeric type constants are declared once: in `Domain/Errors/` in a single-module solution, and in `MyApp.SharedKernel` in a modular monolith.
- A dependency outage is never `Failure` or `Unexpected`. It propagates as an exception, or, when the client contract names it with a catalog code, it is a custom unavailable type (`/backend-development` → Error Pipeline).
- User-caused conditions are never `Failure` or `Unexpected`.
- Every error declares its code explicitly. An ErrorOr default code (the code ErrorOr assigns when a factory call names none: `General.Failure`, `General.Unexpected`, `General.Validation`, `General.Conflict`, `General.NotFound`, `General.Unauthorized`, `General.Forbidden`) is never declared or emitted, for any ErrorType.
- Code format: when the consuming client publishes a contract that prescribes error codes, the contract's exact string. Otherwise `{Entity}.{ErrorName}`, where `{Entity}` is the concept name (the error class name without `Errors`). This applies to every member, `Failure` and `Unexpected` included. The concept name `Http` is reserved for transport codes (`/backend-development` → Error Codes on the Wire).
- A code is unique within the scope in which the client resolves it:
  - the whole API, for `{Entity}.{ErrorName}` codes. In a modular monolith the API spans every module, so no two modules declare a catalog class with the same concept name. When two contexts model the same concept, each qualifies the name with its module (for example `SalesOrderErrors` and `ShippingOrderErrors`);
  - the endpoint's error domain, when the contract scopes codes per domain.
- One business rule has exactly one declaration, and every endpoint that surfaces the rule reuses it. Two different rules never share a member, even when a per-domain contract gives them the same string.
- Codes are culture-invariant and never localized. Publication and stability: `/backend-development` → Error Codes on the Wire.
- The code is the wire code: the API emits it unchanged (`/backend-development` → Error Bodies).
- Validation failures use catalog codes in the same format. The offending property path travels in the error's metadata (`/backend-development` → Validation Pipeline), never as the code.
- Codes for errors the framework produces (transport codes) are not catalog errors (`/backend-development` → Error Codes on the Wire).
- Every error has a human-readable description, meant for developers and logs.
- No exceptions for business rules. Exceptions are for infrastructure failures and programmer errors, raised with BCL guard types. The Domain defines no custom exception types.

The build-time checks on the catalog (published, no default code, no duplicate code string): `/backend-development` → Contract Tests.

------------------------------------------------------------------------

# 6. Repository Interfaces

Repository interfaces are defined in the Domain layer. Implementation placement: `/clean-architecture-structure`.

- MUST be defined in `Domain/Interfaces/Repositories/`: one `I{AggregateRoot}Repository` per aggregate root, or, when Interface Segregation splits it, `I{AggregateRoot}ReadRepository` and `I{AggregateRoot}WriteRepository`. Both are write-side repositories (P5): they return entities, and neither serves list or paged reads (`/clean-architecture-structure` → Layer Responsibilities)
- MUST use `CancellationToken` on all async methods
- MUST return domain Entities, never DTOs or view models
- MUST NOT expose `IQueryable` (leaks ORM concerns into the domain)
- Implementation details (Dapper, EF Core, stored procedures) are hidden behind the interface

------------------------------------------------------------------------

# 7. Bounded Contexts

Each bounded context has its own ubiquitous language and model.

- A single-module solution holds exactly one bounded context. Two or more contexts deployed together form a modular monolith, in which each context is exactly one module (`/clean-architecture-structure` → Solution Layouts).
- A repository's context map is a fact of that repository. Derive it from the repository's own contracts and language, record it in `<repo>/.claude/CLAUDE.md` or an ADR, and never copy it from another repository.
- Entities in different contexts may model the same real-world concept differently. They never share a model or a table.
- Contexts exchange only IDs and published contracts: reads go through the owning module's public queries, and reactions go through integration events (`/clean-architecture-structure` → Module Boundaries; `/event-driven-architecture` §8). No context reads or writes another context's data or repositories.
- Domain events never leave the context that raised them.

------------------------------------------------------------------------

# 8. Service Layer Guidance

## Domain Services

For business logic that doesn't naturally belong to a single Entity or Value Object:

- Place in `Domain/Services/` (if pure domain logic, no external dependencies)
- Define interfaces in `Domain/Interfaces/`
- MUST be stateless

## Application Services (Command/Query Handlers)

- Orchestrate domain objects — they do NOT contain business rules
- Business rules belong in Entities, Value Objects, or Domain Services
- A rule that needs a lookup beyond one aggregate (uniqueness across users) is checked by the command handler through a repository query and backed by a unique index (`/backend-development` → Persistence); every rule decidable from the aggregate's own state lives in the aggregate.
- Handlers call domain methods and return `ErrorOr<T>`
- Under Option B command handlers publish after the commit; under Option A they only persist (§4 Placement; `/event-driven-architecture` §3).

```csharp
// CORRECT — handler orchestrates, entity enforces rules
var result = order.Cancel(reason, currentUserId, _timeProvider.GetUtcNow());
if (result.IsError) return result.Errors;

// PROHIBITED — business rule in the handler
if (order.Status == OrderStatus.Shipped) return OrderErrors.AlreadyShipped;
```

Credential verification is not a domain rule. The handler verifies the current password through an Application-layer hashing abstraction, implemented in Infrastructure over a maintained Argon2id library (`/security-mindset` → Authentication) with the library's constant-time verify. It hashes the new password and calls `user.ChangePassword(newPasswordHash, ...)`. On a mismatch it returns a Validation error with its own code (for example `UserErrors.CurrentPasswordIncorrect()`, code `User.CurrentPasswordIncorrect`, a method because it carries metadata) and the property path in metadata (`CurrentPassword`), never `User.InvalidCredentials`. Plaintext passwords never enter the Domain layer.

------------------------------------------------------------------------

# 9. Anti-Patterns — PROHIBITED

| Anti-Pattern | Description | Correct Approach |
|-------------|-------------|-----------------|
| **Anemic Domain Model** | Entity with only getters/setters, all logic in services | Rich entities with behavior methods |
| **God Aggregate** | Aggregate that encompasses too many entities | Keep boundaries tight — only what must be transactionally consistent |
| **Primitive Obsession** | Using `string` for Email, `decimal` for Money | Use Value Objects |
| **Direct Cross-Aggregate References** | Holding object references to entities in other Aggregates | Use IDs |
| **Business Logic in Handlers** | Command handlers containing domain rules | Handlers orchestrate; entities enforce (lookups beyond one aggregate: §8) |
| **Shared Mutable State** | Static mutable fields or singletons with state | Immutable domain objects, scoped services |
| **Leaky Abstractions** | Repository returning DTOs or exposing IQueryable | Return entities, hide query implementation |
