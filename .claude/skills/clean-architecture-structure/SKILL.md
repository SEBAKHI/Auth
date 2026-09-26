---
name: clean-architecture-structure
description: Load this skill when structuring a C#/.NET solution, choosing between a single-module layered solution and a modular monolith, organizing layers or modules, defining folder conventions, or setting up project references. Covers both supported layouts, layer and module responsibilities, project reference graphs, module boundaries and data ownership, architecture tests, testing structure, feature slices and the package baseline with its licenses. Specific to .NET solution and project layout — do NOT load for other stacks.
user-invocable: true
---

# C# Backend Architecture -- Clean Architecture Structure

## Governing Principle

- Dependencies flow inward. Project references are fixed by the
  **Dependency Graph** table of the solution's layout. That table is the
  single authority, and `MyApp.ArchitectureTests` enforces it
  (**Architecture Tests**).
- The Domain layer references nothing outside the P1 allowlist
  (`/dotnet-architecture` P1).
- No infrastructure in Domain or Application.
- Domain and Application define no custom exception types. Business-rule
  and validation failures are ErrorOr errors (P7).

## Solution Layouts

Two layouts are supported. The repository ADR records which one applies.
A solution never mixes them, and an existing codebase keeps its layout
until an ADR records the migration.

| Situation | Layout |
|---|---|
| One bounded context in the deployable | (a) Single-module layered solution |
| Two or more bounded contexts deployed together | (b) Modular monolith |
| A bounded context must be deployed or scaled on its own | A separate service; inside, it uses (a) or (b) |

A module is exactly one bounded context, and a bounded context is never
split across modules (`/domain-driven-design` §7).

Base types: in `MyApp.SharedKernel` in layout (b); in layout (a), in
`Domain/Primitives/` unless a repository ADR adopts `MyApp.SharedKernel`.

## Layout (a): Single-Module Layered Solution

- Five source projects — `MyApp.Domain`, `MyApp.Application`,
  `MyApp.Infrastructure`, `MyApp.Persistence`, `MyApp.Api` — plus the
  test projects of **Testing Structure**.
- Optional: `MyApp.SharedKernel` (adopted by ADR) and
  `MyApp.Api.Contracts`.
- `MyApp.SharedKernel` exists only when a repository ADR adopts it; by
  default the base types live in `Domain/Primitives/`. It holds
  framework-free base types only and is the innermost ring. It counts as
  part of the Domain ring: the same P1 allowlist applies, and it is the
  single project Domain may reference.
- `MyApp.Api.Contracts` holds the public HTTP request/response types. It
  references nothing and is referenced by the Api project and by external
  clients. When present, it replaces the Api project's `Contracts/`
  folder. Commands, queries and Application DTOs never live there.
- No `Common` project: shared code lives in the layer that owns it.

Read [references/single-module-layout.md](references/single-module-layout.md)
when you scaffold or reorganize a single-module solution: full project and
folder trees, and `Add{Layer}` registration.

## Layout (b): Modular Monolith

- Each module has the rings of layout (a): `MyApp.{Module}.Domain`,
  `.Application`, `.Infrastructure`, `.Persistence` and `.Api`. The
  module's `.Api` project holds the module's endpoints and its
  registration entry points, `Add{Module}Module(IServiceCollection,
  IConfiguration)` and `Map{Module}Endpoints(IEndpointRouteBuilder)`.
- Each module also has `MyApp.{Module}.Contracts`, its public surface
  (**Module Boundaries**).
- One host, `MyApp.Host`, is the composition root. It owns the HTTP
  pipeline (the error contract of `/backend-development`, authentication,
  rate limiting) and calls every module's registration entry points.
- Base types live in `MyApp.SharedKernel`, adopted in the layout ADR (P1).
- Technical code that every module uses and that holds no business rule
  lives in `MyApp.BuildingBlocks.Application`, `.Infrastructure` and
  `.Api`. That covers pipeline behaviors, the single error mapper and
  status map, and the outbox, inbox and retry machinery. These projects
  hold no bounded context. `MyApp.BuildingBlocks.Application` also
  declares `ICurrentUser`, `PagedResult<T>`, the shared list-query
  validator rules and the Placement option A adapter
  `DomainEventNotification<TEvent>`. `MyApp.BuildingBlocks.Infrastructure`
  also holds the shared outbound HTTP layer (`IApiClient`, its factory
  and body serializers), registered once by `AddBuildingBlocks`.
- A module's HTTP request and response types live in its `.Api` project
  (`Contracts/`); `MyApp.Api.Contracts` is layout (a) only.
- The host registers MediatR once, over every module's Application
  assembly and `MyApp.BuildingBlocks.Application`, with the shared
  pipeline behaviors. Cross-module public queries go through that one
  `ISender`. A module's `Add{Module}Module` registers only that module's
  own services.
- Each module declares its own persistence-bound ports (unit of work,
  outbox, inbox, retry recorder) in its own Application project, like
  every other module port. The module's Persistence implements them over
  the generic machinery of `MyApp.BuildingBlocks.Infrastructure` (for
  example `Inbox<TDbContext>`) and registers them under the module's
  ports. A handler resolves only its own module's ports. The dispatcher
  and retry worker per module: `/event-driven-architecture` §8.

Read [references/modular-monolith-layout.md](references/modular-monolith-layout.md)
when you scaffold a modular monolith, add a module, wire the host and
building blocks, set up schema-per-module persistence and migrations, or
write module-boundary architecture tests.

## Dependency Graph

Two tables; each is the only statement of allowed references for its
layout.

Layout (a):

| Project | May reference |
|---|---|
| Domain | Nothing outside the P1 allowlist; SharedKernel when a repository ADR adopts it |
| Application | Domain |
| Infrastructure | Application (implements its ports), Domain |
| Persistence | Application, Domain |
| Api | Application, Domain (the status map reads the custom ErrorType constants); Infrastructure and Persistence for DI registration in the composition root only; Api.Contracts when present |
| SharedKernel (optional) | Nothing outside the P1 allowlist |
| Api.Contracts (optional) | Nothing |

Layout (b) (`{Module}` stands for `MyApp.{Module}`):

| Project | May reference |
|---|---|
| `{Module}.Domain` | Nothing outside the P1 allowlist; `MyApp.SharedKernel` |
| `{Module}.Application` | `{Module}.Domain`; any module's `.Contracts`, its own included; `MyApp.BuildingBlocks.Application` |
| `{Module}.Infrastructure` | `{Module}.Application`, `{Module}.Domain`; `MyApp.BuildingBlocks.Application`, `MyApp.BuildingBlocks.Infrastructure` |
| `{Module}.Persistence` | `{Module}.Application`, `{Module}.Domain`; its own `{Module}.Contracts` (read-side query services return the public query DTOs); `MyApp.BuildingBlocks.Application`, `MyApp.BuildingBlocks.Infrastructure` |
| `{Module}.Api` | `{Module}.Application`; `{Module}.Infrastructure` and `{Module}.Persistence` for registration only; `MyApp.BuildingBlocks.Api` |
| `{Module}.Contracts` | Nothing except the abstractions its types need (ErrorOr, `MediatR.Contracts`) |
| `MyApp.Host` | Every `{Module}.Api`; `MyApp.BuildingBlocks.*` |
| `MyApp.SharedKernel` | Nothing outside the P1 allowlist |
| `MyApp.BuildingBlocks.*` | The ring rules of layout (a) among themselves, with `MyApp.SharedKernel` as their Domain ring; never a module |

`MyApp.SharedKernel` counts as part of every module's Domain ring: a
project that may reference `{Module}.Domain` may also reference
`MyApp.SharedKernel`. `MyApp.BuildingBlocks.Application` likewise counts
as part of every module's Application ring: a project that may reference
`{Module}.Application` may also reference `MyApp.BuildingBlocks.Application`.

- In both layouts, controllers and endpoints use no Infrastructure or
  Persistence types.
- Each outer project exposes an `Add{Layer}` (layout (b):
  `Add{Module}Module`) registration extension, and the composition root
  calls it. Domain has none: the DI abstractions package is not on the
  Domain allowlist.
- Registration is the only reason the Api project (layout (b): a
  module's `.Api`) references Infrastructure or Persistence.

## Module Boundaries

1. Only `{Module}.Contracts` is visible to other modules. It holds
   integration event contracts, the module's public queries with their
   DTOs (or, without MediatR, the module's public query interface
   `I{Module}Queries`), and the public commands that rule 4's ADR lists.
   Its types are
   immutable. Their members use only the types that
   `/event-driven-architecture` §2 allows in integration events, and they
   implement only the abstractions of the Dependency Graph row (ErrorOr,
   `MediatR.Contracts`); they are never domain types.
2. Cross-module reads use the owning module's public queries. With
   MediatR, these are requests named `{Name}Query`, declared in
   `{Module}.Contracts`, handled inside the owning module and sent
   through `ISender`; without MediatR, the interface `I{Module}Queries`,
   declared in `{Module}.Contracts` and implemented inside the module. A public query's errors belong to the owning
   module's contract: a calling handler never returns them as its own
   result. It returns an error from its own catalog, chosen by meaning; a
   referenced resource that is missing is the caller's Validation or
   Conflict error, never NotFound.
3. Cross-module reactions use integration events
   (`/event-driven-architecture` §8).
4. A handler never changes another module's state synchronously. The one
   exception is a public command that the repository ADR lists, together
   with the compensation for the case where the callee has committed and
   the caller then fails.
5. Each module owns its data:
   - its tables live in its own database schema;
   - they are mapped only by its own persistence (one DbContext, or one
     connection and unit-of-work scope, per module), with its own
     migrations and its own migration history table in its schema
     (EF Core: `MigrationsHistoryTable(name, schema)`);
   - a module whose persistence has no ORM model to test (Dapper,
     ADO.NET) connects with its own database principal, which is granted
     only its schema;
   - no module reads or writes another module's schema: no cross-schema
     joins, views or foreign keys;
   - a module that needs another module's data calls a public query, or
     keeps a local read model fed by integration events.
6. A module's domain events never leave the module
   (`/event-driven-architecture` §1).

## Layer Responsibilities

In layout (b), each module project has the responsibilities of its ring
below; `MyApp.Host`, `MyApp.SharedKernel` and `MyApp.BuildingBlocks.*`
are described under **Layout (b)**.

| Ring | Responsibilities | Must NOT contain |
|---|---|---|
| Domain | Entities, value objects, domain events (Placement option A), repository interfaces, domain services, domain rules, domain errors. Folder tree: `/domain-driven-design` → Domain Project Structure | EF Core, controllers, DTOs, infrastructure code, frameworks outside the P1 allowlist, custom exception types |
| Application | Use cases as CQRS commands and queries with their handlers and validators (**Feature Slices**); workflow orchestration; DTOs and their mappings (entity → DTO only, `/domain-driven-design` §1); ports in `Interfaces/`, implemented by Infrastructure or Persistence, except the current-user port, which the Api implements (`/backend-development` → Authorization); in layout (a), the pipeline behaviors in `Behaviors/` | Infrastructure code (only ports); an `Exceptions/` folder or custom exception types |
| Infrastructure | Implementations of Application ports for external systems: email, file storage, external APIs (`/backend-development` → Outbound HTTP), identity and security services, background jobs, logging | — |
| Persistence | Data access (EF Core by default); migrations; implementations of the repository interfaces (`/domain-driven-design` §6) and of the persistence ports in `Application/Interfaces/`: the unit of work, and the read-side query services, which return DTOs (P5). Query handlers read through these query services. A query handler may load one aggregate through its repository only to map that entity to a DTO; list and paged reads never go through a repository | — |
| Api | HTTP endpoints, authentication, middleware, request/response mapping; in layout (a), the single error mapper and status map (`/backend-development` → Status Map) and the composition root; the current-user port's implementation (layout (b): `MyApp.BuildingBlocks.Api`) | Infrastructure or Persistence types in controllers and endpoints |

Persistence notes:

- The trees in the references show EF Core, the default. With
  Dapper/ADO.NET, replace `Context/`, `Migrations/` and `Interceptors/`
  with a connection factory, SQL/query classes and the repository's
  chosen migration tool. Repository interfaces and dependency direction
  are unchanged. Record the choice in the repository ADR.
- `Configurations/` maps only this context's tables. Layout (b): each
  module's DbContext (or connection scope) maps only its own schema.
- Migrations are applied by a deployment step (an EF Core migration
  bundle, or the migration tool the repository ADR names), using a
  migration principal that owns only the schema it migrates (layout (b):
  one per module). The runtime principal has no DDL rights, and the host
  applies no migrations at startup outside Development.
- Persistence practices (unit of work, SQL, normalization, uniqueness
  races): `/backend-development` → Persistence.

## Feature Slices

- The Application project of layout (a), and each module's Application
  project in layout (b), holds exactly one bounded context
  (`MyApp.BuildingBlocks.Application` holds none).
- Slices are therefore `Features/{Feature}/{Action}/`: the use case's
  Command or Query, its Handler and Validator, and, under Placement
  option B, its event. Example: `Features/Users/CreateUser/` holds
  `CreateUserCommand.cs`, `CreateUserCommandHandler.cs`,
  `CreateUserCommandValidator.cs` and, under option B,
  `UserCreatedEvent.cs`.
- `DTOs/` and `Mappings/` (entity → DTO only) sit at the project root
  and serve that context only.
- `EventHandlers/` holds the context's notification handlers
  (`/event-driven-architecture` §4), including handlers of other
  modules' integration events.
- Slices are the default layout inside Application: one folder per use
  case gives clear ownership and fewer merge conflicts.
- A codebase that already uses another layout keeps it until an ADR
  records the migration, and one solution never mixes layouts.

## Architecture Tests

`MyApp.ArchitectureTests` fails the build on any violation of:

1. the **Dependency Graph** table of the solution's layout;
2. the Domain allowlist (P1);
3. layout (b) module isolation:
   - no project references another module's project except
     `{Other}.Contracts`;
   - `{Module}.Contracts` references no module project;
   - no type of one module depends on another module's namespaces,
     except `{Other}.Contracts`;
   - SharedKernel and BuildingBlocks reference no module;
   - every public type in `{Module}.Contracts` is an integration event
     (`*IntegrationEvent`, or `*IntegrationEventV{n}` after a breaking
     change, `/event-driven-architecture` §2), a public query (`*Query`),
     a record used by their members (`*Dto`), an enum, a command that
     **Module Boundaries** rule 4's ADR lists, or, without MediatR, the
     module's public query interface `I{Module}Queries`;
4. layout (b) data ownership:
   - EF Core: a test over each module's model asserts that it maps only
     tables in its own schema;
   - without an ORM model: an integration test in the module's
     `*.Persistence.IntegrationTests` (it needs the database engine)
     asserts that each module's database principal cannot read another
     module's schema;
5. controllers and endpoints use no Infrastructure or Persistence types.

Example assertions for both layouts are in
[references/modular-monolith-layout.md](references/modular-monolith-layout.md)
(section "Architecture tests").

## Testing Structure

Every production project is covered by a test project in this table,
plus `MyApp.ArchitectureTests`. Layout (a) has one test project per
source project that holds logic. Layout (b) has the same per module
(`MyApp.{Module}.Domain.UnitTests`, …), plus the rows marked (b) for the
shared projects.

| Test project | Covers |
|---|---|
| `*.Domain.UnitTests` | Entities, value objects, domain services, domain errors |
| `*.Application.UnitTests` | Handlers, validators, notification handlers; in layout (a) also the pipeline behaviors |
| `*.Infrastructure.IntegrationTests` | External adapters, including their timeout, retry, fallback and error-translation paths |
| `*.Persistence.IntegrationTests` | Repositories, configurations, migrations, outbox/inbox/retry tables |
| `*.Api.IntegrationTests` | Endpoints and authentication; in layout (a) also the single error mapper and the framework-produced error responses |
| `MyApp.Host.IntegrationTests` (b) | The composition root and the shared HTTP pipeline: the single error mapper and status map in `MyApp.BuildingBlocks.Api`, framework-produced error responses |
| `MyApp.BuildingBlocks.Application.UnitTests` (b) | Shared pipeline behaviors |
| `MyApp.BuildingBlocks.Infrastructure.IntegrationTests` (b) | Outbox dispatcher, inbox, retry worker |
| `MyApp.SharedKernel.UnitTests` (when the SharedKernel holds logic) | Base types |
| `MyApp.ArchitectureTests` | **Architecture Tests** (above) |

`{Module}.Contracts` and `MyApp.Api.Contracts` hold data-only types
(`/quality-assurance` → Coverage Exclusions). Coverage gate:
`/quality-assurance` → Coverage Gate.

## Package Baseline and Licenses

- MediatR (P5, P6, P9, P10) and ErrorOr (P5–P7): new solutions adopt
  both (`/dotnet-architecture` scope note). In an existing solution,
  their clauses apply only when the solution references them.
- MediatR 13.0.0 and later is dual-licensed: RPL-1.5, or a Lucky Penny
  commercial license activated by a license key (a community license
  exists with eligibility limits). 12.x and earlier are Apache-2.0.
- FluentValidation for request validation, run by the validation
  pipeline behavior (`/backend-development` → Validation Pipeline).
- Mapping is entity → DTO only (`/domain-driven-design` §1),
  hand-written or source-generated, chosen by ADR. AutoMapper is
  optional: 15.0 and later has the same dual license as MediatR; 14.x
  and earlier are MIT.
- Adding or upgrading a dual-licensed package is a license decision
  recorded in an ADR: pin the last permissive major, comply with
  RPL-1.5, or buy a license. A license key comes from configuration or
  an environment variable, never from source.
- Unexpected and infrastructure exceptions are handled centrally by
  `UseExceptionHandler` with one `IExceptionHandler`
  (`/backend-development` → Error Pipeline). Business errors never
  throw.
- Serilog, or another structured logging provider, is recommended.
- Health checks are required by `/final-review-checklist` §5; ASP.NET
  Core's built-in health checks satisfy it.
- Test packages and their licenses: `/quality-assurance` → Test Stack
  (C#/.NET).

## Core Principle

Separate **stable business logic** from **volatile infrastructure
concerns**.

Domain must remain isolated and independent.
