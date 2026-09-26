# Modular Monolith Layout: Trees, Wiring, Persistence and Architecture Tests

Reference for `SKILL.md` → Layout (b), and for the architecture-test
assertions of both layouts. It holds trees and code only. Every rule these
apply is stated in `SKILL.md` (Layout (b), Dependency Graph, Module
Boundaries, Layer Responsibilities and its Persistence notes, Architecture
Tests, Testing Structure). The module names `Users`, `Orders` and
`Shipping` are placeholders.

## Solution tree

```
MyApp.sln
├── src/
│   ├── Host/
│   │   └── MyApp.Host/
│   ├── SharedKernel/
│   │   └── MyApp.SharedKernel/
│   ├── BuildingBlocks/
│   │   ├── MyApp.BuildingBlocks.Application/
│   │   ├── MyApp.BuildingBlocks.Infrastructure/
│   │   └── MyApp.BuildingBlocks.Api/
│   └── Modules/
│       ├── Users/
│       │   ├── MyApp.Users.Domain/
│       │   ├── MyApp.Users.Application/
│       │   ├── MyApp.Users.Infrastructure/
│       │   ├── MyApp.Users.Persistence/
│       │   ├── MyApp.Users.Api/
│       │   └── MyApp.Users.Contracts/
│       └── Orders/
│           └── (the same six projects)
└── tests/
    ├── MyApp.Host.IntegrationTests/
    ├── MyApp.BuildingBlocks.Application.UnitTests/
    ├── MyApp.BuildingBlocks.Infrastructure.IntegrationTests/
    ├── MyApp.SharedKernel.UnitTests/          when the SharedKernel holds logic
    ├── MyApp.ArchitectureTests/
    └── Modules/
        ├── Users/
        │   ├── MyApp.Users.Domain.UnitTests/
        │   ├── MyApp.Users.Application.UnitTests/
        │   ├── MyApp.Users.Infrastructure.IntegrationTests/
        │   ├── MyApp.Users.Persistence.IntegrationTests/
        │   └── MyApp.Users.Api.IntegrationTests/
        └── Orders/
            └── (the same five projects)
```

## Shared projects

```
MyApp.SharedKernel/
├── Primitives/                 EntityBase, AuditableEntityBase (framework-free)
└── Errors/                     custom ErrorType constants; catalog classes of technical
                                request rules (paging, sorting) (/domain-driven-design §5)

MyApp.BuildingBlocks.Application/
├── Behaviors/                  IPipelineBehavior<,>: logging, validation
├── Interfaces/                 ICurrentUser
├── Paging/                     PagedResult<T>, the shared list-query validator rules
├── Events/                     DomainEventNotification<TEvent> (Placement option A)
└── AssemblyReference.cs

MyApp.BuildingBlocks.Infrastructure/
├── Http/                       the shared outbound HTTP layer: IApiClient, its factory and
│                               body serializers (/backend-development → Outbound HTTP)
├── Outbox/                     generic outbox and dispatcher, AddOutbox<TDbContext>
│                               (/event-driven-architecture §8, §9)
├── Inbox/                      generic inbox, AddInbox<TDbContext>
└── Retry/                      retry table and retry worker (/event-driven-architecture §7)

MyApp.BuildingBlocks.Api/
├── Errors/                     the single error mapper (ToProblem) and status map
│                               (/backend-development → Status Map)
├── Identity/                   HttpCurrentUser, the ICurrentUser implementation
└── DependencyInjection.cs      AddBuildingBlocks: the error pipeline, the current-user port
                                and the shared outbound HTTP layer
```

The outbox, inbox and retry machinery is generic over a module's DbContext.
Each module closes it over its own DbContext in its persistence
registration, so every module gets its own dispatcher and retry worker
over its own schema (see Schema-per-module persistence).

## Module projects

The Domain tree of each module: `/domain-driven-design` → Domain Project
Structure.

```
MyApp.Orders.Contracts/
├── IntegrationEvents/          {Entity}{PastTenseVerb}IntegrationEvent.cs
├── Queries/                    GetOrderSummaryQuery.cs, OrderSummaryDto.cs
└── Commands/                   only the commands the repository ADR lists

MyApp.Orders.Application/
├── Interfaces/                 this module's ports (IUnitOfWork, IOutbox, IInbox,
│                               IEventRetryRecorder, IOrderQueries, ...)
├── Features/
│   └── Orders/
│       ├── PlaceOrder/
│       └── GetOrderSummary/    handler of the public query declared in Contracts
├── DTOs/
├── Mappings/                   entity → DTO only
├── EventHandlers/              including handlers of other modules' integration events
└── AssemblyReference.cs

MyApp.Orders.Infrastructure/
└── DependencyInjection.cs      AddOrdersInfrastructure

MyApp.Orders.Persistence/
├── OrdersDbContext.cs          default schema "orders"
├── Configurations/             this module's tables only
├── Repositories/
├── Queries/                    read-side query services that return DTOs
│                               (the public query DTOs of MyApp.Orders.Contracts included)
├── Messaging/                  OrdersOutbox, OrdersInbox, OrdersEventRetryRecorder: this
│                               module's ports over the generic BuildingBlocks machinery
├── Migrations/
└── DependencyInjection.cs      AddOrdersPersistence

MyApp.Orders.Api/
├── Endpoints/                  OrderEndpoints.cs (namespace MyApp.Orders.Api.Endpoints)
├── Contracts/                  this module's HTTP request/response types
└── OrdersModule.cs             AddOrdersModule, MapOrdersEndpoints, ApplicationAssembly
```

Each module declares its ports in its own namespace
(`MyApp.Orders.Application.Interfaces.IUnitOfWork`, and likewise its
`IOutbox`, `IInbox` and `IEventRetryRecorder`), so the registrations of two
modules are different service types in the one container. A handler in
`MyApp.Shipping.Application` resolves `MyApp.Shipping.Application.Interfaces.IInbox`,
which writes to Shipping's schema only.

## Host wiring

```csharp
// MyApp.BuildingBlocks.Application/AssemblyReference.cs (each module's Application project has the same class)
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;

    // each module's Application project also declares:
    // public static readonly FrozenSet<Assembly> EventAssemblies = new[] { Assembly, typeof(MyApp.Orders.Contracts.IntegrationEvents.OrderPlacedIntegrationEvent).Assembly }.ToFrozenSet(); // own events plus every Contracts assembly whose events this module's handlers consume
}
```

```csharp
// MyApp.Host/Program.cs
const string MediatRLicenseKey = "MediatR:LicenseKey"; // the value comes from configuration or an environment variable

var builder = WebApplication.CreateBuilder(args);

// The single MediatR registration: every module's Application assembly plus the building blocks.
builder.Services.AddMediatR(cfg =>
{
    cfg.LicenseKey = builder.Configuration[MediatRLicenseKey]; // MediatR 13+ only; delete this line on 12.x
    cfg.RegisterServicesFromAssemblies(
        MyApp.BuildingBlocks.Application.AssemblyReference.Assembly,
        UsersModule.ApplicationAssembly,
        OrdersModule.ApplicationAssembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

builder.Services
    .AddBuildingBlocks(builder.Configuration) // the error pipeline, the current-user port, the outbound HTTP layer; each module registers its own outbox, inbox and retry
    .AddUsersModule(builder.Configuration)
    .AddOrdersModule(builder.Configuration);

var app = builder.Build();
// HTTP pipeline (exception handler, status-code pages, authentication, rate limiter):
// /backend-development → Error Pipeline; /security-mindset → Rate Limiting.
app.MapUsersEndpoints();
app.MapOrdersEndpoints();
app.Run();
```

## Module registration and endpoints

```csharp
// MyApp.Orders.Api/OrdersModule.cs (namespace MyApp.Orders.Api): registration entry points only
public static class OrdersModule
{
    // The host reads the assembly here, so it references only this .Api project.
    public static Assembly ApplicationAssembly => MyApp.Orders.Application.AssemblyReference.Assembly;

    public static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR is registered once, by the host. The module registers only its own services.
        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);
        services.AddOrdersInfrastructure(configuration);
        services.AddOrdersPersistence(configuration); // includes this module's outbox, inbox and retry machinery
        // This module's authorization policies (/backend-development → Authorization).

        return services;
    }

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/v1/orders").MapOrderEndpoints();

        return endpoints;
    }
}
```

```csharp
// MyApp.Orders.Api/Endpoints/OrderEndpoints.cs
namespace MyApp.Orders.Api.Endpoints;

internal static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder orders)
    {
        orders.MapGet("/{id:guid}", GetOrderAsync);

        return orders;
    }

    private static async Task<IResult> GetOrderAsync(Guid id, ISender sender, CancellationToken cancellationToken) =>
        (await sender.Send(new GetOrderQuery(id), cancellationToken)).Match<IResult>(
            order => TypedResults.Ok(order),
            errors => errors.ToProblem(ProblemMapping.NoBody)); // bound from the route: no pointer (/backend-development → Error Bodies)
}
```

## Public query across modules

The owning module declares the query in its Contracts project and handles
it inside its Application project.

```csharp
// MyApp.Orders.Contracts/Queries/GetOrderSummaryQuery.cs
public sealed record GetOrderSummaryQuery(Guid OrderId) : IRequest<ErrorOr<OrderSummaryDto>>;

// MyApp.Orders.Contracts/Queries/OrderSummaryDto.cs
public sealed record OrderSummaryDto(Guid OrderId, string Status, decimal Total, string Currency);
```

```csharp
// MyApp.Orders.Application/Interfaces/IOrderQueries.cs
// A read-side query service. OrderQueries in MyApp.Orders.Persistence/Queries/ implements it;
// Persistence may reference its own module's Contracts, so it returns the public query DTO.
public interface IOrderQueries
{
    Task<OrderSummaryDto?> GetSummaryAsync(Guid orderId, CancellationToken cancellationToken);
}
```

```csharp
// MyApp.Orders.Application/Features/Orders/GetOrderSummary/GetOrderSummaryQueryHandler.cs
internal sealed class GetOrderSummaryQueryHandler(IOrderQueries orders)
    : IRequestHandler<GetOrderSummaryQuery, ErrorOr<OrderSummaryDto>>
{
    public async Task<ErrorOr<OrderSummaryDto>> Handle(GetOrderSummaryQuery query, CancellationToken cancellationToken)
    {
        var summary = await orders.GetSummaryAsync(query.OrderId, cancellationToken);
        if (summary is null)
        {
            return OrderErrors.NotFound(query.OrderId);
        }

        return summary;
    }
}
```

Another module reads through the same `ISender`. `GetOrderSummaryQuery`
declares only its NotFound error, so the caller maps every error of the
result to one error of its own catalog without branching on the error
type:

```csharp
// MyApp.Shipping.Application/Features/Shipments/CreateShipment/CreateShipmentCommandHandler.cs
var order = await sender.Send(new GetOrderSummaryQuery(command.OrderId), cancellationToken);
if (order.IsError)
{
    // another module's public-query errors are never forwarded (/clean-architecture-structure → Module Boundaries, rule 2)
    return ShipmentErrors.UnknownOrder(command.OrderId);
}
// Create the shipment through its factory, commit, and return the DTO.
```

```csharp
// MyApp.Shipping.Domain/Errors/ShipmentErrors.cs
public static class ShipmentErrors
{
    // The referenced order is missing: a Validation error of Shipping's catalog, never NotFound.
    // A method, because it carries metadata: each call builds a new Error and dictionary (/domain-driven-design §5).
    public static Error UnknownOrder(Guid orderId) => Error.Validation(
        code: "Shipment.UnknownOrder",
        description: $"Order {orderId} does not exist, so no shipment can be created for it.",
        metadata: new Dictionary<string, object> { ["property"] = "OrderId" });
}
```

Integration event contracts, their outbox writing, dispatch and consumers:
`/event-driven-architecture` §8.

## Schema-per-module persistence (EF Core)

```csharp
// MyApp.Orders.Persistence/OrdersDbContext.cs
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public const string Schema = "orders";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
        // The BuildingBlocks outbox, inbox and retry configurations, in this schema (/event-driven-architecture §8).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OutboxMessage).Assembly);
    }
}
```

```csharp
// MyApp.Orders.Persistence/DependencyInjection.cs
public static class DependencyInjection
{
    private const string ConnectionStringName = "Orders";
    private const string OutboxSectionName = "Orders:Outbox";
    private const string EventRetrySectionName = "Orders:EventRetry";

    public static IServiceCollection AddOrdersPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseSqlServer( // the provider the ADR names
                configuration.GetConnectionString(ConnectionStringName),
                sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, OrdersDbContext.Schema)));
        services.AddScoped<IUnitOfWork, OrdersUnitOfWork>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderQueries, OrderQueries>();

        // The generic machinery closed over this module's DbContext: one outbox dispatcher and one
        // retry worker over this module's schema. Signatures and the registry's contents:
        // /event-driven-architecture → references/outbox-inbox-retry.md §9 and references/integration-events.md §3 ...
        // Persistence never references another module's Contracts: EventAssemblies comes from Application (/event-driven-architecture → references/outbox-inbox-retry.md §9)
        services.AddOutbox<OrdersDbContext>(configuration.GetSection(OutboxSectionName),
            MyApp.Orders.Application.AssemblyReference.EventAssemblies);
        services.AddInbox<OrdersDbContext>(configuration.GetSection(EventRetrySectionName),
            MyApp.Orders.Application.AssemblyReference.Assembly /* its notification handlers */,
            MyApp.Orders.Application.AssemblyReference.EventAssemblies);
        // ... bound to this module's own ports (MyApp.Orders.Application.Interfaces).
        services.AddScoped<IOutbox, OrdersOutbox>();
        services.AddScoped<IInbox, OrdersInbox>();
        services.AddScoped<IEventRetryRecorder, OrdersEventRetryRecorder>();

        return services;
    }
}
```

Migrations are added per module DbContext:

```
dotnet ef migrations add <Name> --context OrdersDbContext --project src/Modules/Orders/MyApp.Orders.Persistence --startup-project src/Host/MyApp.Host
```

A deployment step applies them, one bundle per module DbContext, run with
that module's migration principal (set up as in the SQL of the next
section). The host calls no `Database.Migrate()` at startup outside
Development.

```
dotnet ef migrations bundle --context OrdersDbContext --project src/Modules/Orders/MyApp.Orders.Persistence --startup-project src/Host/MyApp.Host --output artifacts/migrations/orders-efbundle
artifacts/migrations/orders-efbundle --connection "$ORDERS_MIGRATION_CONNECTION"   # the migration principal's connection string, from the deployment's secret store
```

## Schema-per-module persistence (Dapper, ADO.NET)

Each module connects with its own connection string, whose login maps to
the module's runtime principal. The deployment step connects with the
module's migration principal. SQL Server syntax; the logins and their
secrets are provisioned outside source control:

```sql
CREATE USER orders_migrator FOR LOGIN orders_migrator; -- migration principal: used only by the deployment step
CREATE USER orders_module FOR LOGIN orders_module;     -- runtime principal
GO
CREATE SCHEMA orders AUTHORIZATION orders_migrator;    -- the migration principal owns this schema and no other
GO
-- DDL needs ALTER on the target schema too, so the migration principal can create objects only in "orders".
GRANT CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE FUNCTION TO orders_migrator;
-- The runtime principal reads and writes its own schema and has no DDL rights.
GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::orders TO orders_module;
```

## Architecture tests

The examples use xUnit v3, and NetArchTest.Rules for the type-level
checks; any architecture-test library works.

- The reference checks read each project's compiled assembly references
  and compare exact names, so `MyApp.Api` never matches
  `MyApp.Api.Contracts`. A reference that no code uses leaves no trace in
  the assembly; the check fails on its first use.
- Together, the forbidden-reference dictionaries and the Host test are
  the exact complement of the layout's Dependency Graph table (`SKILL.md`
  → Dependency Graph). The
  rows that allow nothing outside an allowlist (Domain, SharedKernel,
  `{Module}.Contracts`, `MyApp.Api.Contracts`) are checked against that
  allowlist instead.

### Both layouts

```csharp
// MyApp.ArchitectureTests/Assemblies.cs
internal static class Assemblies
{
    // The Domain allowlist of /dotnet-architecture P1, as the repository ADR applies it. MyApp.SharedKernel counts
    // as part of the Domain ring (layout (a): only when a repository ADR adopts it).
    public static readonly FrozenSet<string> DomainAllowlist = new[] { "ErrorOr", "MyApp.SharedKernel" }.ToFrozenSet();

    // Layout (b): the abstractions that the types of {Module}.Contracts need, and nothing else.
    public static readonly FrozenSet<string> ContractsAllowlist = new[] { "ErrorOr", "MediatR.Contracts" }.ToFrozenSet();

    // The solution projects that an assembly references, by exact name.
    public static FrozenSet<string> ReferencedProjects(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("MyApp.", StringComparison.Ordinal))
            .ToFrozenSet();

    // The referenced assemblies outside the BCL and the given allowlist.
    public static string[] ReferencesOutside(string assemblyName, FrozenSet<string> allowlist) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name is not ("System" or "netstandard" or "mscorlib")
                           && !name.StartsWith("System.", StringComparison.Ordinal)
                           && !allowlist.Contains(name))
            .ToArray();

    public static string Describe(TestResult result) => string.Join(", ", result.FailingTypeNames ?? []);
}
```

### Layout (a)

The module-isolation, `{Module}.Contracts` and data-ownership tests of
layout (b) do not apply.

```csharp
// MyApp.ArchitectureTests/LayeredSolutionTests.cs
public sealed class LayeredSolutionTests
{
    // The Dependency Graph table of layout (a), as its exact complement: the projects each project must not reference.
    private static readonly FrozenDictionary<string, FrozenSet<string>> ForbiddenRings = new Dictionary<string, string[]>
    {
        ["Application"] = ["Infrastructure", "Persistence", "Api", "Api.Contracts"],
        ["Infrastructure"] = ["Persistence", "Api", "Api.Contracts"],
        ["Persistence"] = ["Infrastructure", "Api", "Api.Contracts"],
        ["Api"] = [], // Api may reference every other project of the solution
    }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet());

    public static TheoryData<string> Rings => new(ForbiddenRings.Keys);

    [Theory]
    [MemberData(nameof(Rings))]
    public void Ring_follows_the_dependency_graph(string ring)
    {
        var forbidden = ForbiddenRings[ring].Select(r => $"MyApp.{r}");
        Assert.Empty(Assemblies.ReferencedProjects($"MyApp.{ring}").Intersect(forbidden));
    }

    [Theory]
    [InlineData("MyApp.Domain")]
    [InlineData("MyApp.SharedKernel")] // delete when no repository ADR adopts it
    public void Domain_ring_references_only_the_allowlist(string project) =>
        Assert.Empty(Assemblies.ReferencesOutside(project, Assemblies.DomainAllowlist));

    [Fact] // delete when the solution has no MyApp.Api.Contracts
    public void Api_contracts_reference_nothing() =>
        Assert.Empty(Assemblies.ReferencesOutside("MyApp.Api.Contracts", FrozenSet<string>.Empty));

    [Fact]
    public void Endpoints_use_no_infrastructure_or_persistence()
    {
        var result = Types.InAssembly(Assembly.Load("MyApp.Api"))
            .That().ResideInNamespace("MyApp.Api.Endpoints") // or MyApp.Api.Controllers
            .ShouldNot().HaveDependencyOnAny("MyApp.Infrastructure", "MyApp.Persistence")
            .GetResult();

        Assert.True(result.IsSuccessful, Assemblies.Describe(result));
    }
}
```

### Layout (b)

```csharp
// MyApp.ArchitectureTests/ModularSolution.cs
internal static class ModularSolution
{
    // One entry per module; add the module here when you add it to the solution.
    public static readonly FrozenSet<string> Modules = new[] { "Users", "Orders" }.ToFrozenSet();

    public static readonly FrozenSet<string> Rings =
        new[] { "Domain", "Application", "Infrastructure", "Persistence", "Api", "Contracts" }.ToFrozenSet();

    // The own-module entries of the Dependency Graph table of layout (b), as their exact complement:
    // the rings of its own module that each ring must not reference.
    public static readonly FrozenDictionary<string, FrozenSet<string>> ForbiddenOwnRings = new Dictionary<string, string[]>
    {
        ["Application"] = ["Infrastructure", "Persistence", "Api"],
        ["Infrastructure"] = ["Persistence", "Api", "Contracts"],
        ["Persistence"] = ["Infrastructure", "Api"],
        ["Api"] = ["Domain", "Contracts"],
    }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet());

    // The shared-project entries of the same table, as their exact complement: the shared projects each module
    // ring must not reference (MyApp.BuildingBlocks.Application counts as part of the Application ring).
    public static readonly FrozenDictionary<string, FrozenSet<string>> ForbiddenSharedProjectsPerRing = new Dictionary<string, string[]>
    {
        ["Application"] = ["MyApp.BuildingBlocks.Infrastructure", "MyApp.BuildingBlocks.Api", "MyApp.Host"],
        ["Infrastructure"] = ["MyApp.BuildingBlocks.Api", "MyApp.Host"],
        ["Persistence"] = ["MyApp.BuildingBlocks.Api", "MyApp.Host"],
        ["Api"] = ["MyApp.BuildingBlocks.Infrastructure", "MyApp.Host"],
    }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet());

    // The ring rules of layout (a) among the shared projects, with MyApp.SharedKernel as their Domain ring:
    // the shared projects each BuildingBlocks project must not reference.
    public static readonly FrozenDictionary<string, FrozenSet<string>> ForbiddenSharedProjects = new Dictionary<string, string[]>
    {
        ["MyApp.BuildingBlocks.Application"] = ["MyApp.BuildingBlocks.Infrastructure", "MyApp.BuildingBlocks.Api"],
        ["MyApp.BuildingBlocks.Infrastructure"] = ["MyApp.BuildingBlocks.Api"],
        ["MyApp.BuildingBlocks.Api"] = [],
    }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet());

    public static string Project(string module, string ring) => $"MyApp.{module}.{ring}";

    public static bool IsModuleProject(string name) =>
        Modules.Any(module => name.StartsWith($"MyApp.{module}.", StringComparison.Ordinal));
}
```

```csharp
// MyApp.ArchitectureTests/ModuleIsolationTests.cs
public sealed class ModuleIsolationTests
{
    public static TheoryData<string> Modules => new(ModularSolution.Modules);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Rings_follow_the_dependency_graph(string module)
    {
        foreach (var (ring, forbiddenRings) in ModularSolution.ForbiddenOwnRings)
        {
            var forbidden = forbiddenRings.Select(r => ModularSolution.Project(module, r))
                .Concat(ModularSolution.ForbiddenSharedProjectsPerRing[ring]);
            Assert.Empty(Assemblies.ReferencedProjects(ModularSolution.Project(module, ring)).Intersect(forbidden));
        }
    }

    [Fact]
    public void Host_references_only_module_api_and_building_blocks() =>
        Assert.Empty(Assemblies.ReferencedProjects("MyApp.Host")
            .Where(name => !name.StartsWith("MyApp.BuildingBlocks.", StringComparison.Ordinal)
                           && !(ModularSolution.IsModuleProject(name) && name.EndsWith(".Api", StringComparison.Ordinal))));

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_references_only_the_allowlist(string module) =>
        Assert.Empty(Assemblies.ReferencesOutside(ModularSolution.Project(module, "Domain"), Assemblies.DomainAllowlist));

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_reference_only_their_abstractions(string module) =>
        Assert.Empty(Assemblies.ReferencesOutside(ModularSolution.Project(module, "Contracts"), Assemblies.ContractsAllowlist));

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_uses_no_other_module_except_its_contracts(string module)
    {
        var otherModules = ModularSolution.Modules.Where(m => m != module).ToArray();
        foreach (var ring in ModularSolution.Rings)
        {
            // Only Application may use {Other}.Contracts; every other ring, Contracts included, uses no other module's project.
            var forbidden = otherModules
                .SelectMany(other => ModularSolution.Rings
                    .Where(r => ring != "Application" || r != "Contracts")
                    .Select(r => ModularSolution.Project(other, r)))
                .ToArray();

            var result = Types.InAssembly(Assembly.Load(ModularSolution.Project(module, ring)))
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();
            Assert.True(result.IsSuccessful, Assemblies.Describe(result));
        }
    }

    [Fact]
    public void SharedKernel_references_only_the_allowlist() =>
        Assert.Empty(Assemblies.ReferencesOutside("MyApp.SharedKernel", Assemblies.DomainAllowlist));

    [Fact]
    public void BuildingBlocks_follow_the_ring_rules_and_use_no_module()
    {
        foreach (var (project, forbidden) in ModularSolution.ForbiddenSharedProjects)
        {
            var referenced = Assemblies.ReferencedProjects(project);
            Assert.Empty(referenced.Intersect(forbidden));
            Assert.DoesNotContain(referenced, ModularSolution.IsModuleProject);
        }
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Endpoints_use_no_infrastructure_or_persistence(string module)
    {
        var result = Types.InAssembly(Assembly.Load(ModularSolution.Project(module, "Api")))
            .That().ResideInNamespace($"MyApp.{module}.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny(ModularSolution.Project(module, "Infrastructure"), ModularSolution.Project(module, "Persistence"))
            .GetResult();

        Assert.True(result.IsSuccessful, Assemblies.Describe(result));
    }
}
```

```csharp
// MyApp.ArchitectureTests/ContractsContentTests.cs
public sealed class ContractsContentTests
{
    // The public commands that the repository ADR lists (Module Boundaries rule 4), by full type name.
    private static readonly FrozenSet<string> AdrListedCommands = FrozenSet<string>.Empty;

    public static TheoryData<string> Modules => new(ModularSolution.Modules);

    [Theory]
    [MemberData(nameof(Modules))]
    public void Contracts_hold_only_the_public_surface(string module)
    {
        var offenders = Assembly.Load(ModularSolution.Project(module, "Contracts")).GetExportedTypes()
            .Where(t => !t.IsEnum
                        && !Regex.IsMatch(t.Name, "IntegrationEvent(V[0-9]+)?$") // a V{n} version suffix included
                        && !t.Name.EndsWith("Query", StringComparison.Ordinal)
                        && !t.Name.EndsWith("Dto", StringComparison.Ordinal)
                        && !AdrListedCommands.Contains(t.FullName!)
                        && !(t.IsInterface && t.Name == $"I{module}Queries")) // without MediatR: the public query interface
            .Select(t => t.FullName);

        Assert.Empty(offenders);
    }
}
```

```csharp
// MyApp.ArchitectureTests/DataOwnershipTests.cs (EF Core; one test per module DbContext)
public sealed class DataOwnershipTests
{
    [Fact]
    public void Orders_model_maps_only_its_own_schema()
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer().Options; // builds the model, opens no connection
        using var context = new OrdersDbContext(options);

        var foreign = context.Model.GetEntityTypes()
            .Where(e => (e.GetTableName() is not null && e.GetSchema() != OrdersDbContext.Schema)
                        || (e.GetViewName() is not null && e.GetViewSchema() != OrdersDbContext.Schema))
            .Select(e => e.DisplayName());

        Assert.Empty(foreign);
    }
}
```

```csharp
// MyApp.Orders.Persistence.IntegrationTests/DatabasePrincipalTests.cs (Dapper, ADO.NET; SQL Server)
public sealed class DatabasePrincipalTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Orders_principal_cannot_read_another_module_schema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(database.ConnectionStringFor("Orders"));
        await connection.OpenAsync(cancellationToken);

        await Assert.ThrowsAsync<SqlException>(() => connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM users.users", cancellationToken: cancellationToken)));
    }
}
```
