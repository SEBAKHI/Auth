---
name: backend-development
description: Load this skill when implementing backend APIs, services, repositories, or server-side code in a C#/.NET solution. Covers SOLID in the handler shape, REST API design, list queries and where authorization checks run, the HTTP error contract (RFC 9457 Problem Details with an always-present code) and bare success bodies, token delivery, the outbound HTTP client, async and cancellation, persistence, caching and logging. All examples are C#. Do NOT load for Node, Python, Go, Ruby, or other non-.NET backends — its patterns will not transfer.
user-invocable: true
---

# Backend Development

C#/.NET server code in the handler shape: the HTTP API and its error contract, token delivery, outbound HTTP, async, persistence, caching and logging. The ten mandatory principles (P1–P10) are in `/dotnet-architecture`, and every rule here applies them. All C# code lives in `references/`; each section links the file it needs.

## Architecture

- Project references, for both solution layouts (Infrastructure and Persistence are separate projects): `/clean-architecture-structure` → Dependency Graph (the single authority).

## SOLID

P3, in the handler shape:
- **Single responsibility:** one handler per use case. Side effects run in notification handlers (`/event-driven-architecture` §4). No CRUD service class that gathers several use cases.
- **Open/closed:** a new variant of a behavior is a new implementation of its abstraction, chosen by lookup (P8). Existing code is not edited to add it.
- **Liskov substitution:** a subtype honors every contract of its base. When it cannot, the two types share an interface instead of an inheritance chain.
- **Interface segregation:** an interface holds only what its callers use. Repositories may split into read and write interfaces (`/domain-driven-design` §6).
- **Dependency inversion:** a handler receives abstractions through its constructor and never constructs a concrete dependency.

Examples: [`references/solid-examples.md`](references/solid-examples.md). Read this when you need a worked example of a SOLID principle in the handler-based shape.

## API Design

### REST Naming

- Resources are plural nouns, lowercase, with hyphens between words, under a versioned prefix: `/api/v1/users`, `/api/v1/order-lines`.
- Methods: `GET /users` lists, `GET /users/{id}` reads one, `POST /users` creates, `PUT /users/{id}` replaces, `PATCH /users/{id}` updates part, `DELETE /users/{id}` deletes.
- A relationship is a nested resource: `GET /users/{id}/orders`.

### Endpoints

- An endpoint sends one command or query through `ISender` and maps the result with the single mapper (`errors.ToProblem()` for a command bound from the body; `errors.ToProblem(ProblemMapping.NoBody)` for a request bound only from the route or query string; Error Bodies). It holds no business logic and no status logic (P6, P7).
- A body-bound endpoint binds the command, or a request type whose member names and nesting equal the command's; an endpoint that reshapes members passes the mapper an explicit map from the command's property paths to the body's (`ToProblem(bodyPathFor: …)`), from which it builds each pointer.
- Minimal APIs and controller actions return `IResult` and use the same mapper.

Examples: [`references/handler-examples.md`](references/handler-examples.md). Read this when you write a command, query or notification handler, a resource-level authorization check, a pipeline behavior, an endpoint, cache-aside or structured-logging code.

### List Queries

- Parameters: `page` (1-based); `pageSize` (1 to a documented maximum, published in the OpenAPI document); `sortBy` (a per-endpoint allowlist, published in the OpenAPI document as an enum); `sortDirection` (`asc` or `desc`); `search` (free text). One spelling per parameter across the API.
- These get 400 with a `code`, so a misspelled key never returns 200 with unfiltered data:
  - an out-of-range `pageSize` or an unknown `sortBy`: failures of the query's validator, with the shared paging and sorting codes (`/domain-driven-design` §5); where `errors` is emitted, their entries have no `pointer`;
  - a query key that the endpoint does not declare: rejected before any handler runs (an endpoint filter compares the keys with the endpoint's declared parameters), with the 400 transport code and no `errors`.
- `sortBy` binds as `string?` and `pageSize` as `int`, with no DataAnnotations: a C# enum or `[Range]` would turn a violation into a binding 400 with the transport code. The allowlist and the maximum are published by an OpenAPI operation transformer that sets `enum` and `maximum` from the same constants the validator uses.
- Enums travel as names; a global `JsonStringEnumConverter` covers JSON bodies.
- The response is `PagedResult<T>`: `{ "items", "page", "pageSize", "totalCount", "totalPages" }`, with integers as JSON numbers.
- The OpenAPI document declares numbers as numbers. With the web defaults (`AllowReadingFromString`), Microsoft.AspNetCore.OpenApi emits `[integer, string]`. Set `JsonNumberHandling.Strict`, so generated clients get `number`.

### Authorization

- Permission checks (roles, claims, scopes) are endpoint authorization policies, declared as endpoint metadata (`RequireAuthorization`, `[Authorize(Policy = …)]`) and run by the host's `UseAuthorization`; no endpoint body contains a check (P6). The module's `Add{Module}Module` registers its policies (layout (a): the Api project's `AddApi` extension, called by its composition root). A denial is the framework 403 with its transport code (Error Codes on the Wire).
- Resource-level checks (ownership, state-dependent access) run in the handler after the resource is loaded. They are authorization, not domain rules. They return a catalog `Forbidden` error, or the catalog `NotFound` error when the resource's existence must not be disclosed.
- Handlers read the caller through an `ICurrentUser` Application port: layout (a) declares it in Application and implements it in Api; layout (b) declares it in `MyApp.BuildingBlocks.Application` and implements it in `MyApp.BuildingBlocks.Api`. The implementation returns the value a DI scope set explicitly before it falls back to the request's user: a notification handler's, dispatcher's or worker's scope overrides it with the event's `TriggeredBy` (`/event-driven-architecture` §4), and the request-based value applies only when no override is set.
- What to check: `/security-mindset` → Authorization.

## Response Format (MANDATORY)

**Precedence.** The members and rules below are fixed for every API. A consuming client's published contract fixes the code values (`/domain-driven-design` §5) and may add vetted, documented extension members. It never removes or reshapes the members below. An API that already publishes a different error format keeps it until an ADR records a versioned migration. One API never emits two error formats.

Code for this whole section: [`references/error-contract-pipeline.md`](references/error-contract-pipeline.md). Read this when you wire the error pipeline, add a transport code or a custom ErrorType, classify a dependency as an outage, add the shedding gate, map validation failures to `errors[].pointer`, or write the contract tests.

### Success Bodies

- Return the resource or result DTO itself, with the correct 2xx status. Never wrap it in `success`, `data`, `message` or `error` fields.
- A creation returns `201 Created` with `Location`. When there is nothing to return: `204 No Content`. Never a bare "OK" message.
- Endpoints that page return `PagedResult<T>(Items, Page, PageSize, TotalCount, TotalPages)`. `PagedResult<T>` is declared once: layout (a) in the Application project (`DTOs/`), layout (b) in `MyApp.BuildingBlocks.Application`. A public query in `{Module}.Contracts` that pages returns its own `*Dto` page record, because Contracts references nothing else.

### Error Bodies

Every error response is `application/problem+json` (RFC 9457, which obsoletes RFC 7807). It is written by `IProblemDetailsService`, or, for MVC's automatic model-state 400, by `ProblemDetailsFactory`. Both paths run the same `CustomizeProblemDetails` and produce the same body, so they count as the one writer. That covers handler results, validation failures, malformed bodies, unhandled exceptions, authentication challenges, authorization denials, unmatched routes, wrong methods or media types, rate limits, dependency outages and deliberate shedding. The one limit is content negotiation (Error Pipeline).

| Member | Rule |
|---|---|
| `code` | Always present. The stable machine identifier that clients branch on and translate. For a handler error it is the first error's `Error.Code`, unchanged. For a framework error it is a transport code. |
| `errors` | Optional. Emitted only by an API whose ADR publishes field-level validation, and then only on a handler result of type Validation with two or more failures; absent on every other response, framework-produced 400s included. One entry per failure, in rule-declaration order, `{ "code", "pointer" }`. `pointer` is an RFC 6901 JSON Pointer, in URI-fragment form, to the offending request-body member, named with the API's JSON naming policy (`#/items/0/unitPrice`). A failure that is not tied to a body member (a query parameter, a route value, or no member) has no `pointer`. `errors[0].code` equals `code`, so a client that reads only `code` still works. |
| `status` | Equals the status line (RFC 9457 §3.1.2). It is advisory. |
| `type`, `title` | The framework defaults. `ProblemDetailsDefaults` sets the RFC 9110 section link for the status and its standard title. A status with no default entry (for example 429) gets its reason phrase as `title` and no `type`. The defaults change between .NET versions, so tests capture them from a real run. Never override them in `CustomizeProblemDetails`, and never put the code in them. A per-problem-type URI is optional; when used, it is set explicitly where the problem is created. |
| `detail` | Optional. Safe for end users, and localized only if the API localizes. Never exception text, a stack trace, SQL or an upstream body. |
| `instance` | The request path, set once in `CustomizeProblemDetails`. |
| `traceId` | Added by the framework. Never replaced by a new GUID. |
| Other extensions | Only vetted, documented values that a client needs. Never `Error.Metadata` copied wholesale. |

### Status Map

The only ErrorType-to-status map in the solution. It is implemented once in the API layer (layout (b): `MyApp.BuildingBlocks.Api`) as a lookup table, and a unit test asserts that every declared type is a key.

| ErrorType | Status |
|---|---|
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| Failure, Unexpected | 500 |
| The custom unavailable type, when the repository ADR declares one (Error Pipeline) | 503 |
| Each other `Error.Custom` type in the repository ADR | The status that the ADR records |

A result carries errors of one ErrorType, and a result that mixes types is a defect. The status comes from the first error. Outages that propagate as exceptions never reach this map; the exception handler's outage classifier maps them (Error Pipeline).

### Error Codes on the Wire

- Code format and uniqueness: `/domain-driven-design` §5. The wire carries `Error.Code` unchanged, and clients compare codes case-sensitively.
- The service keeps no table from its own errors to other codes. Translation tables exist only in two places:
  - an upstream service's codes: the anti-corruption layer, with one table per upstream operation (Outbound HTTP);
  - framework statuses: the transport codes.
- Transport codes are one fixed set, declared once in the API layer, with one code per framework-produced status: 400 (malformed body, binding failure, undeclared query key), 401, 403, 404, 405, 413, 415, 429, 503 (dependency outage or deliberate shedding) and 500. Any other framework-produced 4xx takes the 400 code; 502 and 504 take the 503 code; any other 5xx takes the 500 code.
- Transport-code spelling: a client contract that prescribes codes prescribes these too. Otherwise they are `Http.BadRequest`, `Http.Unauthenticated`, `Http.Forbidden`, `Http.NotFound`, `Http.MethodNotAllowed`, `Http.ContentTooLarge`, `Http.UnsupportedMediaType`, `Http.RateLimited`, `Http.Unavailable` and `Http.Unexpected`.
- No transport code and no catalog code equals an ErrorOr default code (`/domain-driven-design` §5). The single mapper treats an error that carries one as a programming error: it throws `InvalidOperationException`, so the response is the 500 problem and the defect is logged.
- A reason recorded by a challenge (for example an expired token) may replace the default transport code for that response. That reason code is published too.
- Credential rejections name their reason: token, refresh and session endpoints return a distinct code for expired, revoked, already-used and unknown credentials.
- A handler never returns another module's public-query errors as its own result; it returns an error from its own catalog (`/clean-architecture-structure` → Module Boundaries, rule 2).
- In an API whose ADR publishes field-level validation, the published entry of each Validation code names the request-body member it concerns (a JSON Pointer), or states that it concerns none, so a client can place a single failure, which carries no `errors`.
- Codes are public contract. A code is added to the published list before it is emitted. Once published, it is never renamed, removed or reused for another meaning; each of these is a breaking change.

### Validation Pipeline

- One FluentValidation validator per request type. It runs in the validation pipeline behavior (`TResponse : IErrorOr`), which never throws.
- The behavior returns each failure as `Error.Validation(code: failure.ErrorCode, description: failure.ErrorMessage, metadata: new Dictionary<string, object> { ["property"] = failure.PropertyName })`. Failures keep rule-declaration order, and the first one is the primary error.
- Every rule declares `.WithErrorCode(...)` with a catalog code (`/domain-driven-design` §5). Without it, the validator's name (for example `NotEmptyValidator`) reaches the wire. A property name is never a code.
- Only the API mapper turns `property` into `errors[].pointer`.
- Requiredness, format and range are FluentValidation rules. DataAnnotations never carry business validation.
- Request types leave requiredness to the validator, so that a missing member reaches it and is reported with its catalog code (and its pointer, where `errors` is emitted):
  - no C# `required` members;
  - `JsonSerializerOptions.RespectRequiredConstructorParameters` and `RespectNullableAnnotations` stay off in the API's JSON options (Microsoft recommends them for new apps, but they turn a missing member into a binding 400 that carries only the transport code);
  - MVC controllers set `SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true`.
- The model-state 400 covers only malformed bodies and binding failures. It carries the transport code and no `errors`.
- A handler that raises a Validation error itself uses a catalog method that puts the property path in `metadata["property"]` (`/domain-driven-design` §5).

### Error Pipeline

- `AddProblemDetails`, with one `CustomizeProblemDetails` that:
  - sets `instance`;
  - adds `code` when absent: the reason the request recorded, else the transport code for the status;
  - adds `Retry-After` to a 503 that has none, from a configured default;
  - never overrides `type` or `title`.
- `UseExceptionHandler` with one `IExceptionHandler`; no hand-written exception middleware.
- `UseStatusCodePages` placed before authentication, so empty 4xx/5xx bodies are written.
- The rate limiter rejects with 429 (its default is 503), sets `Retry-After` when the lease carries it, and writes no body of its own.
- Controllers: `InvalidModelStateResponseFactory` through `ProblemDetailsFactory`.
- Minimal APIs on .NET 10+: a validation problem that the framework writes (`HttpValidationProblemDetails`) is replaced by a plain problem in `CustomizeProblemDetails`.
- A dependency outage is reported with 503, never as `Error.Failure`:
  - by default the exception propagates, and the exception handler's outage classifier maps it to 503 with the `Unavailable` transport code. It classifies `HttpRequestException`, `TimeoutException`, a `TaskCanceledException` whose inner exception is a `TimeoutException`, Polly's `ExecutionRejectedException` (an open circuit, a resilience timeout), and the exception types that the repository ADR adds for its other dependencies (`/failure-mode-design` → Classify Every Dependency Use). Everything else falls through to the framework's 500 problem;
  - when the client contract gives an outage a catalog code, the handler returns an error of the custom unavailable type that the repository ADR declares, and the Status Map sends it to 503.
- Deliberate shedding (a degradation level, outbox-backlog throttling; `/failure-mode-design` → Graceful Degradation, Failure Responses) goes through one gate: a middleware after `UseStatusCodePages` that sets 503 and `Retry-After` from its configuration and writes no body, so `UseStatusCodePages` writes the problem.
- Content negotiation: the framework's writer emits a problem body only when `Accept` is absent or admits JSON, problem+json or a wildcard. For any other `Accept`, framework-produced errors (status-code pages, the exception handler) get no problem body, and so no `code`. Therefore:
  - error responses are never forced to `application/json` with `[Produces]`;
  - an endpoint that returns another media type (a file export) documents `application/problem+json` for its errors, and its clients send `Accept: <media type>, application/problem+json`.
- The developer exception page is for Development only.
- OpenAPI declares every error response as `application/problem+json` with `ProblemDetails`. One schema transformer adds `code` (required), `errors` and `traceId`.
- Localization: only `detail`, looked up by `code` in one place, with `Content-Language`.
- Upstream correlation: send the trace id (HttpClient propagates `traceparent`) and the upstream's correlation header, and log it; never expect it back in an upstream error body.
- Inbound correlation: clients send W3C `traceparent`. The API adopts it as the request's trace (the ASP.NET Core default), allows it in CORS, and accepts no other client correlation header.
- Rate-limit policy (which endpoints, partitions, limits): `/security-mindset` → Rate Limiting. Exception-handling boundaries: `/failure-mode-design` → Exception-Handling Boundaries.

### Contract Tests

- One integration test per error path, asserting the full body. The paths are:
  - handler error;
  - validation with one failure and with two or more failures, including a request that omits a required property;
  - malformed body; unhandled exception; undeclared query key;
  - empty 401, 403, unknown route, 405, 413, 415 and 429;
  - 503 from `HttpRequestException`, a resilience timeout, an open circuit and the shedding gate;
  - an endpoint that returns another media type, called with `Accept: <media type>, application/problem+json`.

  Each test asserts:
  - the status line and the media type;
  - `code`;
  - for validation: in an API that publishes field-level validation, `errors` with `code`, and `pointer` for body members, on a result with two or more failures, and no `errors` on a single failure; in any other API, no `errors`;
  - `status`, and that `traceId` is present;
  - `Retry-After` on every 503;
  - that no exception data appears;
  - `type` and `title` equal to the values captured from a real run.
- These contract tests fail the build:
  - every catalog code, in every module, is published, and none is an ErrorOr default code;
  - no two catalog members, across all modules, declare the same code string (for a per-domain client contract: within each error domain);
  - every FluentValidation rule component has an explicit, published error code (enumerate `validator.CreateDescriptor().Rules.SelectMany(r => r.Components)`);
  - the transport codes are published, and none is an ErrorOr default code;
  - the OpenAPI `ProblemDetails` schema has `code`, `errors` and `traceId`;
  - every anti-corruption row resolves to a published code;
  - the status map covers every declared ErrorType.
- Test naming and the integration-test rules: `/quality-assurance` → Test Naming, Integration Tests.

### Prohibited

- Machine meaning in human fields: a code in `title` or `type`, or a client that parses `detail`.
- A member that is the only carrier of the code but is present only sometimes, for example codes that appear only in `errors`, which is emitted only when there are two or more failures.
- Hand-written error bodies in middleware, filters or controllers (`new { error = "..." }`, ad-hoc 429 objects). Every error body is produced by `IProblemDetailsService` or `ProblemDetailsFactory`.
- Overriding the framework's `type` and `title` in `CustomizeProblemDetails`, or documenting body values that the pipeline does not produce.
- Errors inside a 2xx response, a `success` flag, or a success envelope such as `ApiResponse<T>`.
- Deciding an ErrorOr error's status anywhere other than the single map. Framework statuses are set only in the pipeline configuration: the exception handler (503 or 500), the shedding gate (503), the rate limiter (429) and the undeclared-query-key filter (400).
- A validation rule without `.WithErrorCode`.
- An ErrorOr default code on any error, or a transport code spelled in ErrorOr's `General.` namespace.
- A text member in `errors` entries.
- `Error.Failure` for a dependency outage.
- Error responses forced to `application/json` with `[Produces]`.
- Forwarding an upstream service's error codes or bodies unchanged.

## Token Delivery

- The access token is returned in the response body, and the client sends it in the `Authorization` header.
- Browser clients receive the refresh token only in a cookie, never in a response body. The cookie's attributes follow `/security-mindset` → Cookies, Tokens and CSRF. Its `Path` is the one auth path under which the refresh and logout endpoints live. Logout receives the cookie and revokes that token family.
- Endpoints that read the cookie are CSRF-protected per `/security-mindset` → Cookies, Tokens and CSRF.
- Refresh tokens are stored in `selector.verifier` form (`/security-mindset` → Secrets and Credential Storage). Every refresh rotates the token. Presenting an already-used token revokes its whole token family (reuse detection).
- Rejections by the token, refresh and session endpoints return the error contract, with a `code` that distinguishes at least expired, already used, revoked and unknown credentials (Error Codes on the Wire). Sign-in rejections follow `/security-mindset` → Authentication.
- Returning the refresh token in the body is allowed only for non-browser clients, and the repository documents that choice.

## Outbound HTTP (MANDATORY)

- Every `HttpClient` comes from `IHttpClientFactory`.
  - One typed (or named) client per external service. Each has its own base address, credentials and TLS trust.
  - Each client has exactly one resilience handler, which implements `/failure-mode-design` → Retries, Timeouts and Circuit Breakers: `AddStandardResilienceHandler`, or `AddResilienceHandler` for a custom pipeline.
  - Never `new HttpClient()` per call. Never `AddPolicyHandler`: Microsoft.Extensions.Http.Polly is deprecated.
- The handler's options bind from configuration (the standard handler: `.Configure(IConfigurationSection)`).
- The idempotency condition of that policy is met with `options.Retry.DisableForUnsafeHttpMethods()`, which retries safe methods only (it also excludes PUT and DELETE). A client that must also retry PUT, DELETE, or requests that carry an `Idempotency-Key` the receiver honors replaces it with a `ShouldHandle` predicate that allows exactly those.
- Circuit-breaker logging is configured where the pipeline is built, once per client: the standard handler's `.Configure((options, sp) => …)` resolves `ILogger<T>` from `sp`; a custom pipeline uses `AddResilienceHandler(name, (builder, context) => …)` with `context.ServiceProvider`.
- `HttpClient.Timeout` is the platform client timeout of that policy.
- `IApiClient` is the shared layer that every typed client composes over its own `HttpClient`, through `IApiClientFactory`. It provides:
  - body serialization by media type, as strategies (P8);
  - logging and tracing;
  - an `ApiClientResponse` for every response it receives.

  It never catches transport exceptions. `IApiClient`, `ApiRequestOptions` and `ApiClientResponse<T>` are Infrastructure types and never appear in an Application port.
- The typed client is the anti-corruption layer. It implements an Application port, returns `ErrorOr<T>`, and never forwards an upstream code or body. Its outcomes:
  - an upstream answer that its table maps (one table per upstream operation): the mapped catalog error;
  - a transport error, a timeout, an open circuit, or a transient status (408, 429, 5xx) left after the retries: an outage. The exception propagates (`EnsureSuccessStatusCode` throws `HttpRequestException` for a status);
  - any answer the table cannot map (an unknown code, another 4xx, an unreadable body): also an outage. The client throws `HttpRequestException` with the upstream status and code in its message, never the body, and the central handler logs it once;
  - when the client contract names the outage with a catalog code, the client returns the custom unavailable error instead of throwing, and logs the upstream status and code once, at Warning (Error Pipeline).
- A `BaseAddress` ends with `/`, and request URIs are relative, with no leading slash.
- Timeouts, retries, TLS trust and credentials are set per client, never per call. A caller that needs a shorter deadline passes a token with a timeout.
- Credentials are attached by a `DelegatingHandler` registered on the client. It reads `IOptions` bound to configuration that is backed by a secret store. Callers never pass credentials, source code never contains them, and logs never record them. OAuth client credentials use a token-acquisition handler that authenticates to the token endpoint with HTTP Basic (RFC 6749 §2.3.1).
- TLS validation is never disabled (`/security-mindset` → Cookies, Tokens and CSRF):
  - a private CA is trusted on its own client only (`SslOptions.CertificateChainPolicy` with `X509ChainTrustMode.CustomRootTrust` and that CA in `CustomTrustStore`);
  - a development certificate is trusted only in a Development-only registration that fails startup in any other environment.

Code: [`references/outbound-http.md`](references/outbound-http.md). Read this when you add or change a client for an external HTTP service: registration, the single resilience handler, credentials, TLS trust, body serializers, anti-corruption tables.

## Async and Cancellation

- Every async method accepts and propagates a `CancellationToken`, end to end, up to and including the unit-of-work commit.
- Exception: the post-commit publish (`/event-driven-architecture` §3).
- A caller that needs a shorter deadline links a timeout token; when it fires, the caller rethrows it as `TimeoutException` (an outage, 503).

## Persistence

- **MANDATORY:** Transactional (write-side) schemas are at least BCNF. Check 4NF and 5NF whenever a table holds two or more independent many-valued facts about one key. A denormalized read model is allowed only with an ADR that states the performance requirement and the mechanism that keeps it consistent.
- Queries are parameterized (`/security-mindset` → Injection). With Dapper, pass the token through `CommandDefinition`: its plain `(sql, param)` async overloads take none.
- The unit of work owns the connection and the transaction. Repositories enlist in it and never open, commit or roll back. The handler calls `IUnitOfWork.SaveChangesAsync` once, after all repository calls.
- An UPDATE or DELETE that must hit exactly one row checks the affected-row count. On 0 rows, the handler returns a catalog error and does not commit.
- With EF Core the commit checks the count itself and throws `DbUpdateConcurrencyException` (500), unless the repository ADR records its translation to the catalog error, as for the uniqueness race.
- A unit of work that must commit an inbox record with the handler's change owns an explicit transaction and runs the whole unit through the provider's execution strategy (`IUnitOfWork.ExecuteAsync`), so a retry replays the unit and never one statement.
- Business rules live in aggregates; a rule that needs a lookup beyond one aggregate follows `/domain-driven-design` §8. SQL never decides a business outcome.
- A unique index backs every uniqueness rule. A request that loses the race gets the index violation (500), unless the repository ADR records its translation to the catalog Conflict error.
- Where a query handler reads from (a read-side query service or a repository): `/clean-architecture-structure` → Layer Responsibilities.
- Repository interface rules: `/domain-driven-design` §6.

Code: [`references/persistence.md`](references/persistence.md). Read this when you write a repository, a read-side query service, a unit of work or raw SQL, or design a transactional schema (normalization checklist).

## Caching

- Caches hold DTOs or read models, never entities (`/domain-driven-design` §1).
- Use cache-aside inside query handlers. Keys come from one `CacheKeys` class per bounded context (layout (b): one per module, in its Application project), and every key starts with the context's name (`orders:summary:{id}`).
- Keys never contain personal data; use an HMAC-SHA-256 of the normalized value (`/security-mindset` → Cryptography Standards).
- Cache failures follow `/failure-mode-design` → Failure Responses.

## Logging

- Log once, where the event is handled:
  - request outcomes, in the logging pipeline behavior (a rejection at Warning, with its error code);
  - unhandled exceptions, by the exception-handling path. On .NET 10 and later, the middleware no longer logs exceptions that an `IExceptionHandler` handles, so the handler logs them. On .NET 8 and 9 it must not log them a second time.
- Use structured messages with named placeholders. `TraceId` and `SpanId` come from the activity; never mint a GUID for correlation.
- Levels follow `LogLevel`: Trace, Debug, Information, Warning, Error, Critical. Serilog's Fatal maps to Critical.
- What may appear in a log: `/security-mindset` → Logging Content. `Microsoft.Extensions.Compliance.Redaction` can enforce it through data-classification attributes.

Cache-aside and logging code: `references/handler-examples.md` (linked under API Design → Endpoints).
