---
name: quality-assurance
description: Load this skill when writing tests, reviewing test coverage, setting up testing strategies, or ensuring code quality. Covers the testing pyramid, the mandatory coverage gate and its exclusions, test naming, integration-test rules and, for C#/.NET, the test stack and its licenses.
user-invocable: true
---

# Quality Assurance

The gate, exclusions, naming and integration-test rules apply to any stack. C#/.NET specifics are labelled; .NET layer and project names follow `/clean-architecture-structure`.

**Reference:** [`references/csharp-test-examples.md`](references/csharp-test-examples.md). Read this when you write C# unit or integration tests, or configure coverage (coverlet) in a .NET solution.

Tests owned by other skills:
- Test projects and what each one covers: `/clean-architecture-structure` → Testing Structure.
- Architecture tests (layers, the P1 allowlist, module isolation): `/clean-architecture-structure` → Architecture Tests.
- Error-contract tests (one per error path, full body): `/backend-development` → Contract Tests.
- Browser tests of a frontend SPA: `/frontend-playbook` rules/testing.md.

---

## Testing Pyramid

| Level | Recommended share | Character |
|---|---|---|
| Unit | 70% | Many, fast, cheap |
| Integration | 20% | Some, medium speed |
| End-to-end | 10% | Few, slow, expensive; only where Integration Tests rule 5 requires them |

The ratios are recommendations for the test mix. The mandatory bar is the Coverage Gate.

---

## Coverage Gate

**MANDATORY.** Line coverage ≥ 90% **and** branch coverage ≥ 90%.

- Measured on the merged run of unit and integration tests, plus browser tests whose coverage is collected (`/frontend-playbook` rules/testing.md).
- Evaluated for each production source project separately, never as a sum across projects, a layer or the solution (coverlet `ThresholdStat=minimum`):
  - C#/.NET: each csproj; in a modular monolith, each module's ring project and each shared project (SharedKernel, BuildingBlocks, Host);
  - JS/TS: each workspace package or app.
- The threshold has no exceptions. Only the code listed under Coverage Exclusions leaves the denominator.
- CI fails the build below the gate, and a failed build blocks deployment.

What each test project covers is defined once, in `/clean-architecture-structure` → Testing Structure.

---

## Coverage Exclusions

- Excludable:
  - tool-generated code: source-generator output, `*.g.cs`, and EF Core migration designer and model-snapshot files;
  - data-only DTO and contract types whose members are only auto-properties.
- Not excludable. The integration test projects cover these in the merged run:
  - `Program.cs` and the composition root;
  - EF entity configurations;
  - migration Up/Down;
  - every adapter over a third-party library, including its timeout, retry, fallback and error-translation paths.
- Exclude in source with `[ExcludeFromCodeCoverage(Justification = "<reason>")]`, which is reviewed like any other change.
- Path or glob filters (coverlet `Exclude`/`ExcludeByFile`) are allowed only for tool-generated files, in one committed settings file (`Directory.Build.props` or a `.runsettings` file).
- Any other way of shrinking the denominator is a gate violation.

---

## What to Test

- All public methods.
- Every branch that encodes a business rule or an error path has a test. The gate is the floor, not the target.
- All edge cases and boundary conditions.
- Error handling paths.
- Integration points.

---

## Test Naming

- Handlers: the class is `{RequestName}HandlerTests`, and its methods are `Handle_{Scenario}_{ExpectedBehavior}`. Example: `CreateUserCommandHandlerTests.Handle_WithDuplicateEmail_ReturnsDuplicateEmailError`.
- Entities, value objects and other classes: `{Method}_{Scenario}_{ExpectedBehavior}`.
- Endpoint integration tests: `{Endpoint}_{Scenario}_Returns{Status}With{ErrorName}`, where `{ErrorName}` is the error's name in PascalCase (the code itself may contain dots or underscores).
- Business failures are asserted as catalog errors, never as null and never as exceptions: `result.IsError`, `result.FirstError.Code == UserErrors.X.Code`, `result.FirstError.Type == ErrorType.Y`. The catalog itself: `/domain-driven-design` §5 Domain Errors.
- Examples:
  - `Handle_WithNewEmail_ReturnsCreatedUser`;
  - `Handle_WithDuplicateEmail_ReturnsDuplicateEmailError`;
  - `Handle_WithNonExistentId_ReturnsNotFoundError`;
  - `Handle_WithWrongPassword_ReturnsInvalidCredentialsError`.

Canonical C# handler tests in this shape: [`references/csharp-test-examples.md`](references/csharp-test-examples.md) → Handler Unit Tests.

---

## Integration Tests

1. Persistence and repository query code runs against the production database engine in a container (for example Testcontainers). It never runs against the EF Core in-memory provider, a SQLite stand-in or DbSet mocks.
2. API tests use `WebApplicationFactory<Program>`, replace only external adapters, and assert full error bodies (`/backend-development` → Contract Tests).
3. Each test owns its data: it rolls back its transaction, or the database is reset between tests.
4. External HTTP dependencies are faked at the `HttpMessageHandler` level or by a local stub server. CI never calls live third parties.
5. End-to-end tests run against a deployed instance, through its public interface. They are required only when the repository ADR says so.

---

## Test Stack (C#/.NET)

- xUnit v3 for new solutions. Tests pass `TestContext.Current.CancellationToken` (analyzer xUnit1051). Under v2, use a fresh `new CancellationTokenSource().Token`.
- Moq, version 4.20.2 or later (4.20.0 and 4.20.1 shipped SponsorLink; 4.20.2 removed it), pinned in central package management (`Directory.Packages.props`).
- Assertions: xUnit's `Assert`; or FluentAssertions pinned to `[7.0.0,8.0.0)` (Apache-2.0); or FluentAssertions 8 and later only with a purchased commercial license, because 8+ is free only for non-commercial use. The choice is recorded in an ADR, as for MediatR (`/clean-architecture-structure` → Package Baseline and Licenses).
- At least one test per handler asserts that the caller's token reaches every async call up to and including the commit, and that a post-commit publish receives `CancellationToken.None` (`/event-driven-architecture` §3); a handler that writes the event to the outbox asserts `IOutbox.Add` before the commit and no publish before it (`/event-driven-architecture` §9). Under Placement option A, the handler test asserts the commit token, and the Infrastructure dispatcher's test asserts `CancellationToken.None` on its publish.

Every code block in the reference is labelled with the stack it assumes, for example `C# (xUnit v3 + Moq + FluentAssertions 7.x)`. Coverage configuration (coverlet thresholds, the merged run, generated-file filters): [`references/csharp-test-examples.md`](references/csharp-test-examples.md) → Coverage Configuration.
