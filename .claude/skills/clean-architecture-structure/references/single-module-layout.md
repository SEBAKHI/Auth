# Single-Module Layout: Trees and Registration

Reference for `SKILL.md` → Layout (a). It holds trees and code only. Every
rule these apply is stated in `SKILL.md` (Dependency Graph, Layer
Responsibilities, Feature Slices, Testing Structure). The architecture-test
assertions for this layout are in
[modular-monolith-layout.md](modular-monolith-layout.md#architecture-tests)
(Architecture tests → Both layouts, Layout (a)).

## Solution tree

```
MyApp.sln
├── src/
│   ├── MyApp.Domain/
│   ├── MyApp.Application/
│   ├── MyApp.Infrastructure/
│   ├── MyApp.Persistence/
│   ├── MyApp.Api/
│   ├── MyApp.SharedKernel/            optional, adopted by ADR
│   └── MyApp.Api.Contracts/           optional
└── tests/
    ├── MyApp.Domain.UnitTests/
    ├── MyApp.Application.UnitTests/
    ├── MyApp.Infrastructure.IntegrationTests/
    ├── MyApp.Persistence.IntegrationTests/
    ├── MyApp.Api.IntegrationTests/
    ├── MyApp.SharedKernel.UnitTests/  when the SharedKernel holds logic
    └── MyApp.ArchitectureTests/
```

## Domain

The Domain tree is owned by `/domain-driven-design` → Domain Project
Structure. When `MyApp.SharedKernel` is adopted, the base types of
`Primitives/` live there instead.

## Application

```
MyApp.Application/
├── Interfaces/                    ports implemented by Infrastructure or Persistence
│                                  (IUnitOfWork, I{Entity}Queries, IEmailSender, ...)
├── Features/
│   ├── Users/
│   │   ├── CreateUser/
│   │   │   ├── CreateUserCommand.cs
│   │   │   ├── CreateUserCommandHandler.cs
│   │   │   ├── CreateUserCommandValidator.cs
│   │   │   └── UserCreatedEvent.cs         Placement option B only
│   │   ├── UpdateUser/
│   │   └── GetUser/
│   │       ├── GetUserQuery.cs
│   │       ├── GetUserQueryHandler.cs
│   │       └── GetUserQueryValidator.cs
│   └── Orders/
│       ├── CreateOrder/
│       ├── CancelOrder/
│       └── GetOrders/
├── DTOs/                          shared by this context's slices only
├── Mappings/                      entity → DTO only
├── EventHandlers/                 {Purpose}EventHandler.cs
├── Behaviors/                     IPipelineBehavior<,>: logging, validation
├── Services/
└── DependencyInjection.cs         AddApplication
```

## Infrastructure

```
MyApp.Infrastructure/
├── Services/
│   ├── Email/
│   ├── Storage/
│   └── ExternalApis/              typed clients (/backend-development → Outbound HTTP)
├── Identity/
├── Security/
├── BackgroundJobs/
├── Logging/
├── Configuration/
└── DependencyInjection.cs         AddInfrastructure
```

## Persistence (EF Core, the default)

```
MyApp.Persistence/
├── Context/
│   └── ApplicationDbContext.cs
├── Configurations/                this context's tables only
├── Repositories/                  implementations of I{AggregateRoot}Repository
├── Queries/                       read-side query services that return DTOs
├── Migrations/
├── Seed/
├── Interceptors/
└── DependencyInjection.cs         AddPersistence
```

With Dapper/ADO.NET, `Context/`, `Migrations/` and `Interceptors/` give way
to a connection factory, SQL/query classes and the migration tool the
repository ADR names.

## Api

```
MyApp.Api/
├── Endpoints/                     or Controllers/
├── Errors/                        the single error mapper and status map
│                                  (/backend-development → Status Map)
├── Filters/
├── Middleware/
├── Extensions/
├── Contracts/                     request/response types; replaced by MyApp.Api.Contracts when present
├── Configuration/
├── DependencyInjection.cs         AddApi: error pipeline, authorization policies, ICurrentUser
├── Program.cs                     composition root
└── appsettings.json
```

## Optional projects

```
MyApp.SharedKernel/
└── Primitives/                    EntityBase, AuditableEntityBase (framework-free)

MyApp.Api.Contracts/               public HTTP request/response types; references nothing
```

## `Add{Layer}` registration

Application, Infrastructure, Persistence and Api each expose one
extension; the composition root calls them. Domain has none.

```csharp
// MyApp.Application/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, string? mediatRLicenseKey)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = mediatRLicenseKey; // MediatR 13+ only; delete this line on 12.x
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
```

```csharp
// MyApp.Infrastructure/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateOnStart();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        // Typed clients for external HTTP services: /backend-development → Outbound HTTP.

        return services;
    }
}
```

```csharp
// MyApp.Persistence/DependencyInjection.cs
public static class DependencyInjection
{
    private const string ConnectionStringName = "Default";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString(ConnectionStringName))); // the provider the ADR names
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserQueries, UserQueries>();

        return services;
    }
}
```

```csharp
// MyApp.Api/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        // The error pipeline: /backend-development → Error Pipeline.
        // Authentication and the rate limiter, bound from configuration: /security-mindset → Rate Limiting.
        // Authorization policies and the current-user port: /backend-development → Authorization.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>(); // the Application port, implemented in Api

        return services;
    }
}
```

```csharp
// MyApp.Api/Program.cs
const string MediatRLicenseKey = "MediatR:LicenseKey"; // the value comes from configuration or an environment variable

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication(builder.Configuration[MediatRLicenseKey])
    .AddInfrastructure(builder.Configuration)
    .AddPersistence(builder.Configuration)
    .AddApi(builder.Configuration);
// Authorization policies and the current-user port: /backend-development → Authorization.

var app = builder.Build();
// HTTP pipeline and endpoint mapping: /backend-development → Error Pipeline, API Design.
app.Run();
```
