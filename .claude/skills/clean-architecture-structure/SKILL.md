---
name: clean-architecture-structure
description: Load this skill when structuring a C# solution, organizing layers, defining folder conventions, or setting up project dependencies. Covers the 5-layer clean architecture model (Domain, Application, Infrastructure, Persistence, API), folder trees, layer responsibilities and restrictions, dependency graph, testing structure, and feature-based organization.
user-invocable: true
---

# C# Backend Architecture -- Clean Architecture Structure

## Governing Principle

Dependency flows inward.

API → Application → Domain\
Infrastructure → Application/Domain

The **Domain layer depends on nothing**.

------------------------------------------------------------------------

# Solution Structure

    MyApp.sln
    │
    ├── src/
    │   ├── MyApp.Domain/
    │   ├── MyApp.Application/
    │   ├── MyApp.Infrastructure/
    │   ├── MyApp.Persistence/
    │   └── MyApp.API/
    │
    ├── tests/
    │   ├── MyApp.Domain.Tests/
    │   ├── MyApp.Application.Tests/
    │   ├── MyApp.Infrastructure.Tests/
    │   └── MyApp.API.Tests/

------------------------------------------------------------------------

# 1. Domain Layer

Contains pure business logic.

    MyApp.Domain/
    │
    ├── Entities/
    ├── ValueObjects/
    ├── Aggregates/
    ├── Enums/
    ├── Events/
    ├── Exceptions/
    ├── Interfaces/
    ├── Specifications/
    └── Constants/

## Responsibilities

-   Business entities
-   Value objects
-   Domain events
-   Repository interfaces
-   Domain rules
-   Domain exceptions

## Restrictions

Must NOT contain:

-   EF Core
-   Controllers
-   DTOs
-   Infrastructure code
-   External frameworks

------------------------------------------------------------------------

# 2. Application Layer

Implements use cases and orchestrates domain logic.

    MyApp.Application/
    │
    ├── Interfaces/
    ├── DTOs/
    ├── Features/
    │   ├── Users/
    │   │   ├── Commands/
    │   │   ├── Queries/
    │   │   ├── Handlers/
    │   │   └── Validators/
    │   │
    │   └── Orders/
    │
    ├── Mappings/
    ├── Behaviors/
    ├── Exceptions/
    └── Services/

## Responsibilities

-   Use case implementation
-   CQRS commands and queries
-   Validation
-   Workflow orchestration
-   DTO mapping

## Dependencies

Depends only on:

-   Domain

------------------------------------------------------------------------

# 3. Infrastructure Layer

Implements external systems.

    MyApp.Infrastructure/
    │
    ├── Services/
    │   ├── Email/
    │   ├── Storage/
    │   └── ExternalAPIs/
    │
    ├── Identity/
    ├── Security/
    ├── BackgroundJobs/
    ├── Logging/
    └── Configuration/

## Responsibilities

-   Third-party integrations
-   Email services
-   File storage
-   API integrations
-   Logging
-   Security services

------------------------------------------------------------------------

# 4. Persistence Layer

Database implementation.

    MyApp.Persistence/
    │
    ├── Context/
    │   └── ApplicationDbContext.cs
    │
    ├── Configurations/
    ├── Repositories/
    ├── Migrations/
    ├── Seed/
    └── Interceptors/

## Responsibilities

-   EF Core
-   Database access
-   Repository implementations
-   Unit of Work pattern
-   Migrations

## Dependencies

Depends on:

-   Domain
-   Application

------------------------------------------------------------------------

# 5. API Layer

Presentation layer.

    MyApp.API/
    │
    ├── Controllers/
    ├── Filters/
    ├── Middleware/
    ├── Extensions/
    ├── DependencyInjection/
    ├── Contracts/
    ├── Configuration/
    ├── Program.cs
    └── appsettings.json

## Responsibilities

-   HTTP endpoints
-   Authentication
-   Middleware
-   Request/response mapping
-   Dependency injection wiring

## Dependencies

Depends on:

-   Application
-   Infrastructure

------------------------------------------------------------------------

# Dependency Graph

            API
             │
             ▼
        Application
             │
             ▼
            Domain

    Infrastructure ──► Domain
    Persistence ─────► Application + Domain

------------------------------------------------------------------------

# Testing Structure

    tests/
    │
    ├── Unit/
    │   ├── Domain/
    │   └── Application/
    │
    ├── Integration/
    │   ├── Persistence/
    │   └── API/
    │
    └── ArchitectureTests/

------------------------------------------------------------------------

# Optional Shared Layers

For large systems:

    MyApp.SharedKernel/
    MyApp.Common/
    MyApp.Contracts/

------------------------------------------------------------------------

# Feature-Based Alternative

Instead of organizing Application by technical type, organize by
feature.

    Features/
    │
    ├── Users/
    │   ├── CreateUser/
    │   ├── UpdateUser/
    │   └── GetUser/
    │
    └── Orders/
        ├── CreateOrder/
        ├── CancelOrder/
        └── GetOrders/

Benefits:

-   Better scalability
-   Fewer merge conflicts
-   Clear ownership of features

------------------------------------------------------------------------

# Production Recommendations

Typical production stack:

-   MediatR (CQRS)
-   FluentValidation
-   AutoMapper
-   Serilog
-   HealthChecks
-   Centralized Exception Middleware
-   Structured Logging
-   Layered Dependency Injection

------------------------------------------------------------------------

# Core Principle

Separate **stable business logic** from **volatile infrastructure
concerns**.

Domain must remain isolated and independent.
