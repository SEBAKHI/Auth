---
name: domain-driven-design
description: Load this skill when designing domain models, creating entities, value objects, aggregates, or domain events. Covers DDD tactical patterns as implemented in this codebase with ErrorOr integration. Invoke when structuring the Domain layer or defining business rules.
user-invocable: true
---

# Domain-Driven Design (DDD)

## Governing Rule

The Domain layer is the heart of the system. It contains ALL business rules and logic. It depends on NOTHING external. All domain objects must be rich — behavior lives WITH the data it operates on. Anemic domain models are PROHIBITED.

------------------------------------------------------------------------

# 1. Entities

Entities have identity (Id) that persists across state changes. Inherit from `EntityBase`.

## Rules

- MUST inherit from `EntityBase` (in `Domain/Primitives/`)
- MUST have a private/protected parameterless constructor for ORM/deserialization
- MUST have a factory method or public constructor that enforces invariants
- MUST NOT expose public setters — state changes through behavior methods only
- MUST validate all inputs in methods that change state
- MUST return `ErrorOr<T>` from methods that can fail due to business rules
- Auditable entities MUST inherit from `AuditableEntityBase` (adds CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)

## Structure

```
Domain/Entities/
    User.cs
    Organization.cs
    Role.cs
    Permission.cs
```

------------------------------------------------------------------------

# 2. Value Objects

Value Objects have no identity. They are defined entirely by their attributes. Two Value Objects with the same attributes are equal.

## Rules

- MUST be immutable (all properties `{ get; }` only, set via constructor)
- MUST override `Equals()` and `GetHashCode()` based on all properties
- MUST validate invariants in the constructor or factory method
- MUST use a static factory method returning `ErrorOr<T>` for creation when validation is needed
- MUST NOT have an `Id` property
- Consider using C# `record` types for automatic equality semantics

## When to Use

- Email addresses, phone numbers, monetary amounts, date ranges
- Any concept where identity does not matter, only the value
- Any group of properties that always travel together
- Replacing primitive obsession (string for Email, decimal for Money)

## Structure

```
Domain/ValueObjects/
    Email.cs
    PhoneNumber.cs
    DateRange.cs
    Money.cs
```

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

## Current Aggregates in This Codebase

- `User` is an Aggregate Root (owns UserRoles, UserPermissions, UserSessions, UserExternalLogins)
- `Organization` is an Aggregate Root (owns OrganizationUsers, OrganizationApplications, OrganizationInvitations)
- `Application` is an Aggregate Root (owns its configuration and API keys)

## Cross-Aggregate References

```
// CORRECT — reference by ID
public Guid OrganizationId { get; private set; }

// PROHIBITED — direct object reference to another aggregate
public Organization Organization { get; set; }
```

------------------------------------------------------------------------

# 4. Domain Events

Domain Events signal that something important happened in the domain. They trigger side effects without coupling the source to the consumer.

## Rules

- MUST be immutable records implementing `MediatR.INotification`
- MUST be named in past tense (`UserCreatedEvent`, `PasswordChangedEvent`)
- MUST contain all data needed by consumers (no lazy loading)
- MUST be defined in the Application layer alongside their Feature (current convention)
- MUST NOT contain domain entity references — only primitive types and IDs
- Handlers MUST NOT throw exceptions that break the originating transaction
- See `/event-driven-architecture` for full event contracts and ordering rules

## Naming Convention

```
{Entity}{Action}Event
Examples: UserCreatedEvent, RoleAssignedEvent, PasswordChangedEvent
```

## Structure

```
Application/Features/{Feature}/{Action}/
    {Entity}{Action}Event.cs
```

------------------------------------------------------------------------

# 5. Domain Errors

Domain Errors represent business rule violations. They use the ErrorOr library.

## Rules

- MUST be static members of a static class per domain concept
- MUST use `Error.NotFound()`, `Error.Validation()`, `Error.Conflict()`, `Error.Forbidden()` factory methods
- MUST have a unique code in format `{Entity}.{ErrorName}`
- MUST have a human-readable description
- MUST be defined in `Domain/Errors/`
- MUST NOT use exceptions for business rule violations — exceptions are for infrastructure failures only

## Structure

```
Domain/Errors/
    UserErrors.cs
    AuthErrors.cs
    OrganizationErrors.cs
    PasswordResetErrors.cs
    TwoFactorErrors.cs
    ExternalAuthErrors.cs
```

## Example Pattern

```csharp
public static class UserErrors
{
    public static Error NotFound(Guid id) =>
        Error.NotFound("User.NotFound", $"User with ID '{id}' was not found");

    public static readonly Error DuplicateEmail =
        Error.Conflict("User.DuplicateEmail", "A user with this email already exists");

    public static readonly Error InvalidCredentials =
        Error.Validation("User.InvalidCredentials", "Invalid email or password");
}
```

------------------------------------------------------------------------

# 6. Repository Interfaces

Repository interfaces are defined in the Domain layer. Implementations live in Infrastructure/Persistence.

## Rules

- MUST be defined in `Domain/Interfaces/Repositories/`
- MUST use `CancellationToken` on all async methods
- MUST return domain Entities, never DTOs or view models
- MUST NOT expose `IQueryable` (leaks ORM concerns into the domain)
- Read and Write repositories MAY be separated (Interface Segregation Principle)
- Implementation details (Dapper, EF Core, stored procedures) are hidden behind the interface

## Structure

```
Domain/Interfaces/Repositories/
    IUserRepository.cs
    IRoleRepository.cs
    IPermissionRepository.cs
    IAuditLogRepository.cs

Infrastructure/Persistence/
    UserRepository.cs        // Dapper implementation
    RoleRepository.cs
    PermissionRepository.cs
    AuditLogRepository.cs
```

------------------------------------------------------------------------

# 7. Bounded Contexts

Each bounded context has its own ubiquitous language and encapsulates a distinct area of the domain.

## Current Bounded Contexts

| Context | Entities | Responsibility |
|---------|----------|----------------|
| **Identity** | User, LoginAttempt, Session, RefreshToken | Authentication, user identity, sessions |
| **Authorization** | Role, Permission, UserRole, UserPermission, RolePermission | Access control, permission enforcement |
| **Organization** | Organization, OrganizationUser, OrganizationInvitation | Multi-tenancy, team membership |
| **Application** | Application, ApiKey | Client apps, API key management |
| **Audit** | AuditLog | Event tracking, compliance logging |
| **External Auth** | ExternalAuthProvider, UserExternalLogin | Third-party identity federation |

## Rules

- Entities in different contexts MAY share the same real-world concept but have different models
- Cross-context communication uses Domain Events or Application Services, never direct repository access from another context
- Shared concepts (like UserId) cross boundaries as primitive IDs, not entity references
- Each context owns its own data — no shared tables between contexts

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
- Handlers call domain methods and return `ErrorOr<T>`
- Handlers publish Domain Events for side effects

```
// CORRECT — handler orchestrates, entity enforces rules
var result = user.ChangePassword(currentPassword, newPassword);
if (result.IsError) return result.Errors;

// PROHIBITED — business logic in the handler
if (currentPassword != user.PasswordHash) return Error.Validation(...);
```

------------------------------------------------------------------------

# 9. Anti-Patterns — PROHIBITED

| Anti-Pattern | Description | Correct Approach |
|-------------|-------------|-----------------|
| **Anemic Domain Model** | Entity with only getters/setters, all logic in services | Rich entities with behavior methods |
| **God Aggregate** | Aggregate that encompasses too many entities | Keep boundaries tight — only what must be transactionally consistent |
| **Primitive Obsession** | Using `string` for Email, `decimal` for Money | Use Value Objects |
| **Direct Cross-Aggregate References** | Holding object references to entities in other Aggregates | Use IDs |
| **Business Logic in Handlers** | Command handlers containing domain rules | Handlers orchestrate; entities enforce |
| **Shared Mutable State** | Static mutable fields or singletons with state | Immutable domain objects, scoped services |
| **Leaky Abstractions** | Repository returning DTOs or exposing IQueryable | Return entities, hide query implementation |
