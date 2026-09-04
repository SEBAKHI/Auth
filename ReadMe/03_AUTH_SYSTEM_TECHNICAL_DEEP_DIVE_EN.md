# AuthSystem — Technical Deep Dive

## Architecture, Implementation & Operations Guide

This document is written for an architect or senior engineer who has never seen this codebase and is
deciding whether the design holds up. It describes the system **as it is today**, not as it might be.
Where something is configured but does nothing, that is stated plainly rather than omitted; section 19
collects every such case in one place.

Every acronym is expanded the first time it appears. Every number in this document was counted from the
source tree, not estimated.

---

## Table of Contents

1. [System Architecture](#1-system-architecture)
2. [The Two Processes and the Gateway Handshake](#2-the-two-processes-and-the-gateway-handshake)
3. [Security Implementation](#3-security-implementation)
4. [Identity-Provider Capabilities](#4-identity-provider-capabilities)
5. [Authorization Model](#5-authorization-model)
6. [Data and Persistence](#6-data-and-persistence)
7. [Subsystems and Background Work](#7-subsystems-and-background-work)
8. [Performance Architecture](#8-performance-architecture)
9. [API Design & Patterns](#9-api-design--patterns)
10. [Design Principles](#10-design-principles)
11. [Deployment & Operations](#11-deployment--operations)
12. [Monitoring & Observability](#12-monitoring--observability)
13. [Disaster Recovery & Business Continuity](#13-disaster-recovery--business-continuity)
14. [Integration Capabilities](#14-integration-capabilities)
15. [Secret Management & Key Rotation](#15-secret-management--key-rotation)
16. [Testing](#16-testing)
17. [Configuration Reference](#17-configuration-reference)
18. [API Endpoints Reference](#18-api-endpoints-reference)
19. [Configured but Inert](#19-configured-but-inert)

---

## 1. System Architecture

### What actually ships

**AuthSystem is four running things and one database.** An HTTP (HyperText Transfer Protocol) API
(Application Programming Interface) holds all the behaviour; a reverse proxy sits in front of it at the
network edge; and two browser applications are the only interfaces a human being ever touches. All
persistent state lives in one SQL Server database.

| Unit | What it is | Where it lives in the repository |
|---|---|---|
| Auth API | ASP.NET Core web application — every business operation | `Auth/Auth_API` |
| API Gateway | Reverse proxy built on YARP (Yet Another Reverse Proxy) | `Auth/API_Gateway` |
| Console application | Administrator interface, a React single-page application (SPA) | `Auth_UI/apps/console` |
| Accounts application | End-user self-service interface, also a React SPA | `Auth_UI/apps/accounts` |
| Database | SQL Server, deployed from a SQL Server Data Tools (SSDT) project | `Auth/Auth_DB` |

There is no other server-side component. There is no container image, no message broker, no cache
server, and no second datastore.

### The solution and its projects

The solution file declares **eleven projects: ten C# projects and one SQL Server database project.**
Every C# project targets `net10.0` (.NET 10). The database project is built by a separate .NET Framework
toolchain and targets the SQL Server 2019 schema provider.

*In code:* `Auth/Auth.sln`; target frameworks in each `.csproj`; `Auth/Auth_DB/Auth_DB.sqlproj`.

| Project | What it is for | Which projects it references |
|---|---|---|
| `Auth.Domain` | Entities, aggregates, value objects, domain events, error catalogs, repository interfaces | **nothing** |
| `Auth.Application` | Commands, queries and their handlers; data transfer objects (DTOs); settings classes; validators | `Auth.Domain` |
| `Auth.Infrastructure` | Dapper repositories, cryptography, token issuing, notification rendering, configuration providers | `Auth.Domain`, `Auth.Application`, `Auth.Shared`, `Auth_Localization` |
| `Auth_API` | The web host — controllers, middleware, event handlers, and all dependency-injection wiring | `Auth.Infrastructure`, `Auth_Localization` |
| `API_Gateway` | The YARP reverse proxy | `Auth.Shared`, `Auth_Localization` |
| `Auth.Shared` | Startup, secret and data-protection helpers plus the security-headers middleware shared by both hosts | **nothing** |
| `Auth_Localization` | Embedded translation resources for 7 languages, and the localization middleware | **nothing** |
| `Auth.Sdk` | A redistributable client library for third-party .NET applications | **nothing** |
| `Auth_Setup` | A 23-line console utility that prints a password hash and the SQL statement to apply it | `Auth.Infrastructure` |
| `Auth_API.Tests` | The single backend test project | `Auth_API`, `Auth.Infrastructure` |
| `Auth_DB` | The SSDT database project (schema, seeds, upgrade scripts) | — |

Two edges in that table are architecturally notable and are stated here rather than hidden behind an
idealized diagram.

**`Auth_API` does not reference `Auth.Application` or `Auth.Domain` at all.** It reaches both of them
transitively, through `Auth.Infrastructure`, even though its own source uses their namespaces on almost
every page. The consequence is that the API host cannot be compiled against the inner layers without the
outer one, so "the API depends only on abstractions" is not enforced by the project graph — it is a
convention. The same is true of `Auth.Shared`.
*In code:* `Auth/Auth_API/Auth_API.csproj` lines 26-27.

**`Auth.Infrastructure` references `Auth_Localization`.** This is the one edge that points from a lower
layer into a resources project, and it exists because notification rendering resolves translated strings
inside Infrastructure. It is a deliberate exception to the inward-only rule, not an accident.
*In code:* `Auth/Auth.Infrastructure/Auth.Infrastructure.csproj` line 30.

**`Auth.Sdk` is an orphan.** No project in the solution references it. It is in the solution file and
nothing else builds against it. Section 14 states what that means for its known defects.

### Clean Architecture as it is actually practiced

Dependencies flow inward: `Auth.Domain` ← `Auth.Application` ← `Auth.Infrastructure` ← `Auth_API`.
Nothing references the API host except the test project. **38 repository interfaces** are declared in
`Auth.Domain` and implemented by **38 repository classes** in `Auth.Infrastructure`, so
`Auth.Application` never sees Dapper or SQL Server types.

Two facts an architect should know before reading the code:

- **There are no per-layer registration extension methods.** There is no `AddApplication()`,
  `AddInfrastructure()` or `AddPersistence()`. Every service registration in the system happens inline in
  one file of top-level statements, 1,075 lines long.
  *In code:* `Auth/Auth_API/Program.cs`.
- **There is no Unit of Work.** No `IUnitOfWork` type exists. A handler does not own a transaction. A
  repository opens one itself, with `connection.BeginTransaction()`, only when a single operation must
  write several tables atomically — six repository files do this.

### How a request is handled

Business logic lives in **190 MediatR handlers**, not in services. There is no `AuthService`,
`UserService` or `RoleService` class in this repository.

The 190 handlers are 120 command handlers and 70 query handlers, organized into **17 feature areas**
under `Auth/Auth.Application/Features/<Area>/<UseCase>/`. Each use case is its own folder holding a
command or query record, its handler, and usually a validator.

The dispatch path is the same for every endpoint:

1. **The controller receives the HTTP request.** It injects `ISender` — MediatR's send-only interface —
   rather than any service class. 23 of the 25 routable controllers work this way; the two exceptions are
   the internal gateway-settings controller and the image-upload controller.
2. **The controller sends a command or a query.** Every one is a C# `record` implementing
   `IRequest<ErrorOr<T>>` — CQRS (Command Query Responsibility Segregation) with the result type baked
   into the contract.
3. **`LoggingBehavior` runs first.** It logs entry at Information level, logs a Warning when the result is
   an error, and logs a second Warning when the handler took longer than **500 milliseconds**.
   *In code:* `Auth/Auth.Application/Behaviors/LoggingBehavior.cs` line 49.
4. **`ValidationBehavior` runs second.** It executes every registered FluentValidation validator for that
   request in parallel and short-circuits with validation errors if any fail.
   *In code:* `Auth/Auth.Application/Behaviors/ValidationBehavior.cs` lines 34-49.
5. **The handler runs** and returns `ErrorOr<T>` — either the value or a list of errors. Business rule
   violations are returned as errors, never thrown as exceptions.
6. **The controller converts the result to HTTP.** One shared base class maps error types to status codes
   and builds the `ProblemDetails` body. Section 18 gives the exact shape.
   *In code:* `Auth/Auth_API/Common/ApiController.cs` lines 32-57.

**Those two behaviors are the only ones registered, in that order.** There is no transaction behavior, no
caching behavior, no authorization behavior and no performance behavior.
*In code:* `Auth/Auth_API/Program.cs` lines 670-671.

### Domain events

There are **34 domain event records** and **38 notification handlers** that consume them. Two dispatch
mechanisms coexist, which is worth knowing before you go looking for one:

- **Aggregate-raised.** An aggregate appends an event to a private list; a dispatcher copies, clears and
  publishes them. Only 11 raise-sites exist in the whole Domain layer, all inside `User` and
  `NotificationTemplate`.
- **Directly published.** 27 handlers inject `IPublisher` and publish the event themselves. This is the
  majority path.

Handlers are named `[EventName][Audit|Notification]EventHandler`, for example
`UserCreatedAuditEventHandler`.

Two events have **no handler at all**: `WebhookKeyCreatedEvent` and `WebhookKeyRevokedEvent`. Creating a
webhook key therefore writes no audit entry, unlike creating an API key. See section 19.

### The HTTP middleware order

This is the exact order in the API host, after the application is built. Each stage either transforms the
request or rejects it.

1. **`UseHsts`** — sends the HTTP Strict Transport Security (HSTS) header. **Skipped in Development.**
2. **`UseForwardedHeaders`** — reads `X-Forwarded-For` and `X-Forwarded-Proto`. No trusted-proxy list is
   configured, so these headers are accepted from any caller that can reach the API directly.
3. **`SecurityHeadersMiddleware`** — writes the response security headers listed in section 3.
4. **`UseSerilogRequestLogging`** — one structured log line per request.
5. **`UseAuthLocalization`** — picks the response language.
6. **`ExceptionHandlingMiddleware`** — converts unhandled exceptions into `application/problem+json`.
7. **`GatewayTokenValidationMiddleware`** — rejects with **403** any request that did not arrive through
   the gateway. Section 2 explains the handshake and its exemptions.
8. **`MapOpenApi`** — serves the OpenAPI document. **Development only.**
9. **`UseHttpsRedirection`**.
10. **`UseStaticFiles`** — serves the uploaded-image store.
11. **`UseCors`** — Cross-Origin Resource Sharing policy, rebuilt from live configuration per request.
12. **`UseRateLimiter`**.
13. **`UseAuthentication`** — validates the bearer token.
14. **`JwtBlacklistValidationMiddleware`** — rejects with **401** a token that has been revoked since it
    was issued. This runs *after* authentication and *before* authorization, deliberately.
15. **`UseAuthorization`** — evaluates permission policies.
16. **`MapHealthChecks("/health")`** and **`MapHealthChecks("/ready")`**.
17. **`MapControllers`**.

*In code:* `Auth/Auth_API/Program.cs` lines 910-1020.

### The browser applications

The two applications live in `Auth_UI/`, a pnpm workspace containing **two applications and five shared
packages**. They are React SPAs — single-page applications, meaning the browser downloads one JavaScript
bundle and renders every screen locally, calling the API for data. There is no server-rendered admin UI
in this repository.

| | Console | Accounts |
|---|---|---|
| Workspace name | `@authsystem/console` | `@authsystem/accounts` |
| Folder | `Auth_UI/apps/console` | `Auth_UI/apps/accounts` |
| Who uses it | An administrator running the platform | An end user managing their own account |
| Development URL | `https://localhost:5173` | `https://localhost:5174` |
| Talks to | the Auth API, default `https://localhost:5101` | the same API |

Both development servers pin their port with `strictPort: true` and are **intended to be HTTPS-only**,
deliberately, so the browser keeps the identity-provider session cookie. Their certificates come from the
`DEV_HTTPS_CERT` and `DEV_HTTPS_KEY` environment variables.

**Without those two variables the server still starts — it warns and falls back to plain HTTP.** That
fallback is the failure mode the HTTPS rule exists to prevent: Chrome's schemeful same-site rule makes
`http://localhost` and `https://localhost` different sites, so it silently refuses to store the
`SameSite=Lax` identity-provider cookie minted by `POST /auth/login`. Sign-in appears to succeed,
`/auth/authorize` bounces straight back to `/login`, and there is no error anywhere to point at. Set both
variables before the first sign-in attempt.
*In code:* `Auth_UI/apps/console/vite.config.ts` line 15; `Auth_UI/apps/accounts/vite.config.ts` line 14;
`Auth_UI/dev-https.ts`.

The five shared packages are:

| Package | What it holds |
|---|---|
| `@authsystem/api` | The typed API client, token store, cross-tab session sync, error mapping |
| `@authsystem/auth` | Session context, route guards, and the shared sign-in screens |
| `@authsystem/i18n` | The 7 display languages and text direction |
| `@authsystem/ui` | The shadcn/ui component library and the shared data table |
| `@authsystem/account` | Profile and organization screens mounted by **both** applications |

**Stack:** React 19, Vite 8, TypeScript 6, Tailwind CSS 4, shadcn/ui on the Radix base, lucide icons,
React Router 7, TanStack Query and Table, react-hook-form with zod, i18next, sonner, recharts.

**How they authenticate.** Both applications sign a user in by posting credentials to the API's login
endpoint and holding the returned tokens in the browser. The access token is kept **in memory only** and
is never written to disk; the refresh token is persisted in `localStorage` under the key
`auth.refreshToken` so a page reload can silently re-establish the session. Because the refresh token is
single-use and the server treats a second presentation as theft, every tab of the origin coordinates
renewal through a `BroadcastChannel` lock rather than racing its own refresh. The same sign-in also sets
the server's identity-provider session cookie, which is what lets a *third-party* application complete
the authorization-code flow described in section 4 without asking the user for a password again.
*In code:* `Auth_UI/packages/api/src/token-store.ts`, `tab-sync.ts`, `client.ts`.

**What they build to.** Each application builds to its own `dist/` folder using Vite's default output
location — neither Vite configuration overrides it, and `dist` is git-ignored. Each application's
`public/web.config` is copied into that folder during the build, and it is what configures Internet
Information Services (IIS) to serve the SPA: rewrite every unmatched path to `index.html`, never cache
`index.html`, cache `/assets` for a year, and send a per-application Content Security Policy (CSP).
*In code:* `Auth_UI/apps/console/public/web.config`; `Auth_UI/apps/accounts/public/web.config`.

**Nothing in the repository deploys those folders.** No script, publish profile or pipeline targets
`apps/*/dist`. Uploading the built files is a manual step.

---

## 2. The Two Processes and the Gateway Handshake

The API and the gateway are two separate ASP.NET Core processes. The gateway is not decoration: it is the
only intended way in, and the API actively refuses traffic that did not come through it.

### The reverse proxy

The gateway is built on **YARP 2.3.0**. Its routes are pure configuration — nothing is registered in
code. There are **24 routes**, all pointing at a single cluster named `auth-cluster`.

*In code:* `Auth/API_Gateway/appsettings.json` (`ReverseProxy` section); `Auth/API_Gateway/Program.cs`
lines 110-111.

**The route list is a deny-by-default allowlist, one entry per feature.** A path that no route matches is
not forwarded at all. Route patterns use `v{version:int}`, so they match any numeric API version — a new
API *version* needs no gateway change, but a new *feature* does.

| Rate-limiter policy | Routes |
|---|---|
| `auth` | `/api/v{version:int}/auth/{**catch-all}` |
| `api` (19 routes) | `users`, `roles`, `permissions`, `apikeys`, `applications`, `organizations`, `invitations`, `audit-logs`, `dashboard`, `images`, `webhookkeys`, `platform`, `notification-templates`, `notification-types`, `notification-layouts`, `notification-outbox`, `privacy-policy`, plus `/privacy/{**catch-all}` and `/privacy` |
| `admin` | `/api/v{version:int}/admin/{**catch-all}` |
| none — global limiter only | `/.well-known/{**catch-all}`, `/openapi/{**catch-all}`, `/uploads/{**catch-all}` |

**One route is deliberately absent.** `/api/v{version}/internal/gateway-settings` is not forwarded,
because the gateway calls it directly, server to server.

**The health endpoints are also absent from the allowlist.** `/health` and `/ready` on either process are
reachable only by addressing that process directly. They are not reachable through the gateway.

### What the gateway adds to every proxied request

| Header | Behaviour |
|---|---|
| `X-Gateway-Token` | Added on every proxied request, but only when `Gateway:Token` is non-empty |
| `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto` | Standard forwarded-header transforms |
| `X-Correlation-ID` | Removed, then re-added: the caller's value if present, otherwise a new GUID |

*In code:* `Auth/API_Gateway/Program.cs` lines 112-140.

### The token handshake

**The API rejects any request that does not carry the expected gateway token.** The comparison is
constant-time on the raw bytes, after a length check, so it does not leak the token through timing. A
mismatch produces **403** with an `application/problem+json` body.

Three things soften that rule, all configured:

- `Gateway:ValidationEnabled` turns the check off entirely. It is `true` in the base configuration and
  **`false` in Development**, which is why a developer can call the API directly.
- `Gateway:ExemptPaths` lists path prefixes that skip the check. Shipped value:
  `/.well-known/`, `/health`, `/ready`, `/swagger`, `/openapi`, `/uploads/`.
- `Gateway:TokenHeaderName` names the header. The gateway hardcodes `X-Gateway-Token` on its side, so
  changing this key on the API side alone would break the handshake.

A blank entry in `ExemptPaths` would prefix-match every request and silently disable enforcement
API-wide. Blank entries are therefore ignored, and a dedicated test locks that behaviour down.
*In code:* `Auth/Auth_API/Common/Middleware/GatewayTokenValidationMiddleware.cs` lines 65-70, 87-115,
132-149; `Auth/Auth_API.Tests/Middleware/GatewayExemptPathGuardTests.cs`.

### The settings pull

**The gateway does not own its own rate limits or CORS origins.** It polls the API for them and applies
them by version stamp, so an administrator changing a limit in the console changes the edge behaviour
without redeploying the gateway. The poll interval defaults to 30 seconds. If the pull fails, the gateway
keeps its last known values and logs once per outage rather than per attempt.
*In code:* `Auth/API_Gateway/Configuration/GatewayRuntimeSettingsPoller.cs`.

### The maintenance rule this creates

A test in the backend suite fails the build if any API controller prefix has no gateway route. This
exists because images, dashboard and webhook keys each shipped without one, and the symptom is a whole
feature returning 404 with no error anywhere.

**If you add a controller, add a gateway route in the same change.**
*In code:* `Auth/Auth_API.Tests/Gateway/GatewayRouteCoverageTests.cs`.

---

## 3. Security Implementation

Throughout this section, each value is marked **hardcoded** (a literal in code, no configuration key
reads it) or **configurable**, and the configuration key is given with exact casing and nesting.

### Password hashing: Argon2id

AuthSystem uses **Argon2id exclusively** — the algorithm recommended by OWASP and winner of the
international Password Hashing Competition.

#### Why Argon2id over alternatives?

| Algorithm | Status | Key Limitation |
|-----------|--------|----------------|
| MD5 | **Never use** | Crackable in seconds |
| SHA-256 | **Not for passwords** | Too fast — no brute-force resistance |
| bcrypt | **Secure but surpassed** | CPU-only — does not resist GPU-based attacks as effectively |
| PBKDF2 | **Secure but surpassed** | Lacks memory-hardness; weaker against modern hardware |
| **Argon2id** | **Current gold standard** | No practical attacks known when properly configured |

> Argon2id combines the best properties of Argon2i (resistance to side-channel attacks) and Argon2d
> (resistance to GPU cracking). It requires significant memory per attempt, making mass password-guessing
> attacks economically impractical.

#### Parameters

| Parameter | Shipped value | Hardcoded or configurable | Configuration key |
|---|---|---|---|
| Memory cost | 19,456 KB (19 MiB) | configurable | `Password:Argon2MemorySize` |
| Iterations | 2 | configurable | `Password:Argon2Iterations` |
| Parallelism | 1 | configurable | `Password:Argon2Parallelism` |
| Salt length | 16 bytes, from a cryptographically secure random generator | configurable, read-only in the console | `Password:SaltSize` |
| Hash length | 32 bytes | configurable, read-only in the console | `Password:HashSize` |
| Algorithm and version | Argon2id, encoded version 19 | **hardcoded** | — |
| Verification comparison | `CryptographicOperations.FixedTimeEquals` | **hardcoded** | — |

All three Argon2 parameters are **restart-required**: the hasher is built once as a singleton at startup,
so changing them in the console has no effect until the process restarts.

**Stored format:** `$argon2id$v=19$m={memory},t={iterations},p={parallelism}[,keyid={id}]${salt}${hash}`

**Rehashing:** on a successful password login, if any of the three parameters differ from current
settings — or the stored pepper key id differs from the desired one — the password is rehashed with the
current settings and stored.

The same Argon2id hasher is reused for every other secret in the system that is *verified* rather than
looked up. There are six such secrets: API keys, two-factor recovery codes, email-verification one-time
passwords, account-deletion one-time passwords, organization ownership-transfer codes, and the step-up
codes described in section 15.

#### Optional hardening: pepper

**A pepper is a server-side secret mixed into every password hash.** Think of the salt as a per-user
ingredient stored next to the hash, and the pepper as a house ingredient stored somewhere the database is
not.

- **What it defends.** A database-only breach. Stolen hashes cannot be brute-forced without the pepper,
  because the pepper never lives in the database.
- **How to turn it on.** Set `Password:Pepper:Enabled` to `true`. It ships **off**.
- **What it costs.** The first startup after enabling it generates 32 bytes of key material and persists
  it to the secret store. If that write fails, startup fails — deliberately.
- **What happens if you lose it.** Verification **fails closed** for every hash carrying that key id. Not
  "falls back", not "warns" — those users cannot log in until the key is restored. Back it up exactly as
  you back up the signing keys.
- **How existing hashes migrate.** Un-peppered hashes are upgraded transparently on the next successful
  login. Old pepper key ids are retained so their hashes keep verifying.
- *Configuration keys:* `Password:Pepper:Enabled` (restart-required), `Password:Pepper:CurrentKeyId`,
  `Password:Pepper:Keys:{id}`. The last two are secret-owned and must never appear in `appsettings.json`.

#### Optional hardening: breached-password screening

**New passwords can be checked against the Have I Been Pwned (HIBP) breach corpus.**

- **How the check is made private.** Only the first five characters of the password's SHA-1 hash leave
  the server. This is k-anonymity: the service returns a bucket of candidate suffixes and the comparison
  happens locally.
- **How to turn it on.** Set `Password:BreachedPasswordCheck:Enabled` to `true`. It ships **off**, and it
  is restart-required because it changes which HTTP client is registered.
- **What it does when it fires.** `Mode` is `Enforce` (reject the password) or `Warn` (accept it and
  return an `X-Password-Warning` response header). Default `Enforce`.
- **What it does when the service is unreachable.** `FailOpen` is `true` by default, so the password is
  accepted. Set it to `false` to fail closed.
- **Where it applies.** Registration, change-password, reset-password, and administrator user creation.
- *Configuration keys:* `Password:BreachedPasswordCheck:Enabled`, `:Mode`, `:FailOpen`,
  `:RejectThreshold`, `:TimeoutMs`.

### Password policy

| Rule | Shipped value | Configuration key |
|---|---|---|
| Minimum length | **8** (the class default is 8 too; the console permits 6–128) | `Password:MinimumLength` |
| Require uppercase | true | `Password:RequireUppercase` |
| Require lowercase | true | `Password:RequireLowercase` |
| Require digit | true | `Password:RequireDigit` |
| Require special character | true | `Password:RequireSpecialCharacter` |
| Which characters count as special | `!@#$%^&*()-_=+[]{}\|;:'",.<>?/\` | **hardcoded** |
| Banned substrings, case-insensitive | `password`, `123456`, `qwerty`, `abc123`, `letmein`, `admin`, `welcome`, `monkey`, `dragon`, `master`, `login` | **hardcoded** |
| Password history depth | **3** | `Password:HistoryCount` |

**The five composition rules are public.** `GET /api/v1/Platform/password-policy` returns the minimum
length and the four character-class switches anonymously, so the sign-up, invitation, reset and
change-password forms show a live checklist while the person types instead of learning the rules one
refusal at a time. Nothing else in this table or the lockout table below is disclosed; the banned
substrings, the breach check and the history check are judged only on submit, and every reason comes
back in the response's `errors` array.

**Password history blocks four values, not three.** The check compares a candidate against the stored
history hashes *and* against the current password, so with `HistoryCount` at 3 a user cannot reuse any of
their last four passwords.

**There is no password expiry.** No login path evaluates password age. The `Password:ExpirationDays` key
was removed because nothing computed it. See section 19 for the column that survives it.

### Account lockout

| Protection | Value | Configuration key |
|---|---|---|
| Failed password attempts before lockout | **5** | `Password:MaxFailedAttempts` |
| Lockout duration | **15 minutes** | `Password:LockoutDurationMinutes` |
| Failed two-factor attempts before lockout | 5 | **hardcoded** |
| Two-factor lockout duration | 15 minutes | **hardcoded** |

Lockout is applied in a single SQL `UPDATE` that increments the failure counter and, at the threshold,
sets both the lockout expiry and the user's status. The account unlocks itself on the next login attempt
after the lockout window passes.

Since September 2026 that automatic lock is not absolute. A *familiar source* — a client address with a
successful sign-in for the account in the last 30 days, or a device holding a live session — may still sign in
(password or provider) while the lock stands, and a success clears the lock in full. An administrator's lock
(no expiry, counter untouched) is never relaxed, and a completed password reset clears only the automatic
lock. Independently, each client address is refused after the same five wrong passwords against the account
within one window, before the password is verified; refusals do not extend the window.

**The email-unconfirmed check happens after password verification, deliberately** — checking it first
would let an attacker enumerate which addresses are registered.

### Sessions

**Sessions do not idle out.** A session ends when its refresh token expires (7 days), when the user logs
out, or when it is revoked. `Session:IdleTimeoutMinutes` exists in configuration and **no code reads
it**; the same is true of `Session:LifetimeHours`, `Session:ExtendOnActivity` and
`Session:ExtensionHours`. The real session lifetime is `Jwt:RefreshTokenLifetimeDays`.

**Out of the box there is no cap on concurrent sessions.** `Session:MaxConcurrentSessions` ships as `0`,
which means unlimited. Set it to a positive number to impose a cap. What happens at the cap depends on a
second key:

- `Session:TerminateOldestOnMax` = `true` (the default) — the least-recently-used sessions are ended and
  the new sign-in proceeds. Eviction runs *after* the new session row is written, so the current sign-in
  always survives.
- `Session:TerminateOldestOnMax` = `false` — the new sign-in is **refused** before any token is minted,
  and a failed login attempt is recorded.

Two further session keys are read and do work: `Session:TerminateSessionsOnPasswordChange` and
`Session:TerminateSessionsOnPasswordReset`, both `true`, both overridable per request.

### Token authentication: JWT with RS256

A JWT (JSON Web Token) is a signed, self-describing token: the API can trust its contents without a
database lookup because the signature covers them.

| Token type | What it is | Lifetime | Configuration key |
|---|---|---|---|
| **Access token** | A signed JWT carrying the user's identity and authority | **15 minutes** | `Jwt:AccessTokenLifetimeMinutes` |
| **Refresh token** | 64 random bytes, base64-encoded. The server stores only a keyed hash of it | **7 days** | `Jwt:RefreshTokenLifetimeDays` |

**The refresh token is not a JWT and is not signed.** It is opaque random material, hashed with
HMAC-SHA256 (a hash-based message authentication code) before storage, so a database leak does not yield
usable tokens.

#### What the access token actually contains

| Claim | Contents | Present when |
|---|---|---|
| `sub` | User id | always |
| `email` | User email | always |
| `jti` | A new GUID per token | always |
| `iat` | Issued-at, Unix seconds | always |
| `name`, `given_name`, `family_name` | Display names | always |
| `sid` | Session id — **constant across refreshes** | when a session id is supplied |
| `locale`, `timezone`, `theme` | User preferences | when set on the user |
| `roles` | One claim per role code | per role |
| `permissions` | One claim per permission code | per permission |
| `org_perm` | One claim per organization permission, value `"{organizationId}:{code}"` | per organization membership permission |
| `exp`, `nbf`, `iss`, `aud` | Standard registered claims | always |

**The audience claim is not always the same value.** For a token minted through the authorization-code
flow, `aud` is the *application's* code. For every other path it is the platform default `Jwt:Audience`.
That matters: an application-scoped token does **not** validate through
`JwtTokenService.ValidateAccessToken`, which pins the platform audience.

#### Why RS256 (asymmetric) over HS256 (symmetric)?

```text
Symmetric (HS256):
  Same key to sign AND verify = must share secret with all verifiers

Asymmetric (RS256):
  Private key: Signs tokens (kept secret on auth server)
  Public key: Verifies tokens (safely distributed to any service)
  Result: Other services verify tokens without knowing the signing secret
```

The signing key is RSA-2048. The bearer handler pins the algorithm list to `RS256` only, so a token
presented with a different algorithm is rejected rather than downgraded. Clock skew tolerance is 60
seconds (`Jwt:ClockSkewSeconds`, restart-required).

#### Refresh-token rotation and reuse detection

**Refresh tokens rotate on every use by default** (`Jwt:RotateRefreshTokens`). The old token is marked
revoked with the reason `Rotated`, pointing at its replacement.

**Presenting a rotated token is treated as theft.** The system revokes every token the account holds,
publishes a reuse-detected event, and returns an error. A token revoked in *bulk* (for example by a
logout-everywhere) is deliberately **not** treated as reuse — it returns a plain "session ended" error —
because otherwise one stale tab would cascade into a full account revocation.

#### Revoking a token that has already been issued

An access token is valid until it expires, unless it is blacklisted. The blacklist supports three scopes:
a single token (`jti`), a whole session (`sid`), and every token a user holds issued before a given
instant.

**The blacklist is an in-process dictionary, backed by a database table.** It is written through a
channel and rehydrated from `RevokedTokens` **at startup**. There is no periodic re-read; the only timer
is a five-minute cleanup pass. The multi-instance consequence is stated in section 11.

### Two-factor authentication

**Only TOTP (time-based one-time password) plus recovery codes exist.** There is no SMS second factor and
no email second factor. The shared secret is encrypted at rest with the per-user key described below, and
recovery codes are hashed with Argon2id.

### Rate limiting

**There are two independent limiters, in two processes, with different partition keys.** Reading them as
one list is the single most common misunderstanding of this system.

#### Auth API (in-process)

The API defines exactly two named policies and **no global limiter, by design** — a general policy was
removed because no endpoint used it. Both partition on the client IP address resolved from
`X-Forwarded-For`, falling back to the socket address.

| Policy | Permits | Window | Queue limit | Configuration keys |
|---|---|---|---|---|
| `login` | **20** | 60 s | 0 | `RateLimiting:LoginPermitLimit`, `RateLimiting:LoginWindowSeconds` |
| `password-reset` | **10** | 60 s | 0 | `RateLimiting:PasswordResetPermitLimit`, `RateLimiting:PasswordResetWindowSeconds` |

The `login` policy covers far more than sign-in. It is also applied to registration, external login, the
OAuth token endpoint, forgot-password, every email-verification endpoint, the account-deletion request,
confirm and recover endpoints, two-factor verification, the two secret-management challenge endpoints,
the two anonymous invitation endpoints, and two endpoints on the users controller. **Those endpoints
therefore share one per-IP bucket**, which is worth knowing when a test or a script trips a 429 it did
not expect.

**Not rate-limited at the API layer at all:** `POST /auth/refresh`, `POST /auth/revoke`,
`GET /auth/authorize`, `POST /auth/introspect`, and every two-factor setup, enable and disable endpoint.

#### API Gateway (edge)

The gateway defines four policies, partitioning on the **socket** address rather than `X-Forwarded-For` —
it is the edge, and trusting the header there would let a caller pick a fresh partition per request.

| Policy | Permits | Window | Queue limit | Configuration key prefix (owned by the API) |
|---|---|---|---|---|
| Global limiter | 1,000 | 60 s | 100, oldest-first | `GatewayRateLimiting:Global*` |
| `auth` | 20 | 60 s | 0 | `GatewayRateLimiting:Auth*` |
| `api` | 100 | 60 s | **10** | `GatewayRateLimiting:Api*` |
| `admin` | 120 | 60 s | 0 | `GatewayRateLimiting:Admin*` |

Those numbers live in **three** places that must agree — the settings registry default, the API's
`GatewayRateLimiting` section, and the gateway's own `RateLimiting` section. A parity test fails the
build if they drift, because drift is invisible at runtime: the console would display one limit while the
gateway enforced another.

#### The two hosts return different 429 bodies

This is a real interoperability trap for client authors.

**Gateway rejection** — sets a `Retry-After` response header *and* returns:

```json
{ "type": "...", "title": "...", "status": 429, "detail": "...", "retryAfter": 60 }
```

`retryAfter` is an integer.

**API rejection** — sets **no** `Retry-After` header, and returns:

```json
{ "error": "...", "retryAfter": 60.0 }
```

`retryAfter` is a floating-point number of seconds.

**A client that reads only the `Retry-After` header gets nothing back from the API** and must read the
JSON field instead.

### Security headers

One middleware, shared by both hosts, writes these on every response.

| Header | Value | Overwrite behaviour |
|---|---|---|
| `X-Frame-Options` | `DENY` | always overwritten |
| `X-Content-Type-Options` | `nosniff` | always |
| `X-XSS-Protection` | `1; mode=block` | always |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | always |
| `Content-Security-Policy` | `default-src 'self'; frame-ancestors 'none'` | **only when not already set** |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | always |
| `Server`, `X-Powered-By` | removed | — |

Two conditions matter:

- **The CSP is never overwritten.** An endpoint that sets its own Content Security Policy wins. Endpoints
  that return HTML rely on this.
- **HSTS is applied only outside Development.** When it is applied: max-age 365 days, `includeSubDomains`,
  `preload`.

*In code:* `Auth/Auth.Shared/Http/SecurityHeadersMiddleware.cs`; `Auth/Auth_API/Program.cs` lines
889-898, 912-916.

### Cross-Origin Resource Sharing

CORS controls which browser origins may call the API. The policy provider rebuilds from **live**
configuration and caches by a fingerprint of the origin list plus the credentials flag, so changing
origins in the console takes effect without a restart.

- `Cors:AllowedOrigins` — the explicit origin list. Empty-string entries are treated as removal
  tombstones and dropped.
- `Cors:AllowCredentials` — shipped `true`.
- **Startup fails outside Development if the origin list is empty.**
- **`"*"` does not mean "allow everything".** It is treated as "not an explicit list", which outside
  Development yields a **deny-all** policy.

### Field-level encryption at rest

Three fields are encrypted in the database with a key that is unique per user: the TOTP secret, the phone
number, and the external provider's refresh token.

| Property | Value |
|---|---|
| Cipher | AES-256-GCM |
| Stored payload | `"v2:"` followed by Base64 of nonce ‖ tag ‖ ciphertext |
| Nonce / tag / key sizes | 12 / 16 / 32 bytes |
| Additional authenticated data | `"{purpose}:{userId}"` |
| Key wrapping | The per-user data encryption key (DEK) is itself protected by the Data Protection key ring |
| Unwrapped key cache | In-process, 15-minute sliding expiry |

**The additional authenticated data means a ciphertext fails closed if it is moved.** Copy an encrypted
value into another user's row, or another column, and decryption fails rather than returning the wrong
plaintext.

**SQL Server Always Encrypted is not used.** No column in the schema is declared `ENCRYPTED WITH`.
Protection is entirely application-side. A deployed connection string may carry
`Column Encryption Setting=Enabled`; that flag is inert here.

### Vulnerabilities mitigated

| Vulnerability | How AuthSystem addresses it |
|---|---|
| **SQL injection** | Parameterized queries via Dapper — user input is never concatenated into SQL |
| **XSS** (cross-site scripting) | Content Security Policy headers on both the API and the two SPAs; React escapes rendered text by default |
| **CSRF** (cross-site request forgery) | The API is token-authenticated: a browser sends a bearer token, not an ambient cookie, so classic CSRF does not apply to the API surface. The one cookie the system sets is the identity-provider session cookie `auth_idp` — `HttpOnly`, `Secure`, `SameSite=Lax`, host-only. **No anti-forgery token is issued or validated anywhere in this repository.** |
| **Brute force** | Two separate controls. **Rate limit:** 20 requests per 60 seconds per client IP on the `login` policy. **Account lockout:** 5 failed password verifications lock the account for 15 minutes — for strangers; a source the account recently signed in from still may, and each address is capped at the same five on its own (see Account lockout). |
| **Session hijacking** | Refresh-token rotation with reuse detection; the identity-provider cookie is `HttpOnly` and `Secure`; the access token is never written to disk by either SPA |
| **Protocol downgrade** | HTTPS redirection always on; HSTS outside Development |

---

## 4. Identity-Provider Capabilities

**AuthSystem is already a working OAuth 2.0 authorization server.** This is shipped behaviour, not a
roadmap item: a third-party application can sign users in through it today.

**It is not yet an OpenID Connect (OIDC) provider, and this section does not claim it is.** It issues no
`id_token`, which is the one thing OIDC adds on top of OAuth 2.0. What it borrows from OIDC is the
discovery convention — the fixed `/.well-known/` paths described below — so that a client library can
find the token endpoint and the public signing key without being told. An OIDC client library pointed at
this system will complete the code exchange and then find no `id_token` waiting for it. The discovery
document is written to signal exactly that, and the omission is deliberate rather than an oversight.

### The flow

The only supported flow is **authorization code with PKCE** (Proof Key for Code Exchange). PKCE means the
client generates a random verifier, sends only its hash up front, and proves possession of the original
when redeeming the code — so an intercepted code is useless.

| Fact | Value | Hardcoded or configurable |
|---|---|---|
| Authorize endpoint | `GET /api/v1/auth/authorize`, anonymous, **no rate-limit policy** | — |
| Token endpoint | `POST /api/v1/auth/token`, anonymous, `login` rate-limit policy, form-encoded | — |
| Grants supported | `authorization_code`, `refresh_token` — anything else is refused | hardcoded |
| Client authentication | **none** — public clients only, PKCE is mandatory | — |
| `response_type` accepted | `code` only | hardcoded |
| `code_challenge_method` accepted | **`S256` only** — `plain` is refused | hardcoded |
| Verifier / challenge format | `^[A-Za-z0-9\-._~]{43,128}$` | hardcoded |
| `state` maximum length | 512 characters | hardcoded |
| Authorization-code lifetime | **60 seconds** | `IdentityProvider:AuthorizationCodeLifetimeSeconds` |
| Code material | 64 random bytes, stored as an HMAC-SHA256 hash | — |
| Code binding | application id, user id, the exact redirect URI, the challenge, the client IP | — |
| Code consumption | Atomic and single-use; a second attempt is logged as a replay | — |
| PKCE verification | Base64-URL of SHA-256 of the verifier, compared in constant time | — |

**Failures split into two kinds, deliberately.** An unknown or inactive `client_id`, or a redirect URI
that is not registered for that application, returns **400 without redirecting** — the system will not
bounce a user to an unverified location. Everything else redirects back to the registered URI with an
OAuth error code and the original `state`.

### Single sign-on across applications

After a successful sign-in the API sets a session cookie, and a later `/authorize` call for a *different*
application reuses it instead of prompting again.

| Property | Value |
|---|---|
| Cookie name | **`auth_idp`** (`IdentityProvider:IdpSessionCookieName`, restart-required) |
| `HttpOnly`, `Secure`, `SameSite` | `true`, `true`, `Lax` — all hardcoded |
| `Domain` | **not set** — the cookie is host-only and never a parent-domain cookie |
| Absolute lifetime | 7 days (`IdentityProvider:IdpSessionLifetimeDays`) |
| Server-side record | An `IdpSessions` row storing only an HMAC-SHA256 hash of the token |
| Never in a response body | The token is `[JsonIgnore]`; only the controller moves it into the cookie |

**Step-up re-authentication is supported.** `prompt=login` always forces a fresh sign-in; otherwise the
smaller of the application's own `ReauthenticationMaxAgeMinutes` and the request's `max_age` is compared
against the age of the session cookie. Step-up is evaluated **before** the application-entitlement check,
deliberately, so a stale cookie cannot be used to enumerate which applications a user may reach.

### Discovery

Three endpoints are unversioned and anonymous, because the OIDC specification requires fixed paths.

| Path | Returns |
|---|---|
| `GET /.well-known/openid-configuration` | The discovery document |
| `GET /.well-known/jwks.json` | The JSON Web Key Set — the public signing key |
| `GET /.well-known/public-key.pem` | The same key as PEM text |

The discovery document advertises exactly what is implemented:
`response_types_supported = ["code"]`, `grant_types_supported = ["authorization_code","refresh_token"]`,
`code_challenge_methods_supported = ["S256"]`, and
`token_endpoint_auth_methods_supported = ["none"]`. It does not claim an `id_token` signing algorithm and
does not advertise scopes, because neither is implemented.

**The key set publishes exactly one key.** See section 15 for what that means for rotation.

---

## 5. Authorization Model

### How an endpoint is gated

An endpoint declares the permission it needs as an attribute. That attribute is not a static policy — it
names one, and a policy provider builds the policy on demand for any name beginning with `Permission:`.

```text
[RequirePermission("users:read")]
   -> policy name "Permission:users:read"
      -> RequireAuthenticatedUser() + PermissionRequirement("users:read")
         -> handler reads the "permissions" claims off the JWT
```

**The check is claim-only for platform permissions — no database call.** The permissions are already in
the token.

### Wildcard semantics

Wildcards are **prefix matches**, and the boundary is a colon.

1. The literal permission `*` grants everything.
2. An exact match grants, case-insensitively.
3. A held permission ending in `:*` matches when the required code starts with that prefix followed by a
   colon, or equals the prefix exactly.

**So `crm:*` matches `crm:leads:read` and `crm`, but not `crmx:read`.** Mid-string wildcards such as
`a:*:c` are not supported.

**The trap this creates:** `auth:users:*` does **not** satisfy `users:read`. They are different prefixes.

### Organization scope

Organizations are a shipped bounded context with 26 use cases and 7 tables of their own.

- The token carries one `org_perm` claim per (organization, permission) pair, with the value
  `"{organizationId}:{code}"`, split on the first colon only.
- The fallback path fires only when the required code starts with `org:` **and** the request carries a
  route value naming the organization (`orgId`, else `id`).
- **When the token carries zero `org_perm` claims for that organization**, one live database lookup of the
  caller's membership permissions is attempted. This covers an organization created during the current
  session, whose permissions are not yet in the token. If the token carries *some* claims for that
  organization but not the required one, **no** lookup happens and the request is denied.
- Eight organization permission codes are in use: `org:update`, `org:members:read`,
  `org:members:manage`, `org:members:invite`, `org:apps:read`, `org:apps:manage`,
  `org:permissions:read`, `org:permissions:manage`.
- Three built-in membership roles exist: `org-owner` (holding `org:*`), `org-admin`, `org-member`.

### The seeding gap — read this before designing roles

**50 distinct permission codes are enforced across 138 attribute usages. A clean database publish seeds
45 permission rows and 8 roles — and 34 of the 50 enforced codes have no seed row at all.**

Six of those 34 exist in **no SQL file anywhere in the repository**, so they cannot be granted by any
means short of hand-writing rows:

```text
apikeys:validate  webhookkeys:create  webhookkeys:read
webhookkeys:revoke  webhookkeys:rotate  webhookkeys:validate
```

A seed script exists that would supply 28 of the 34 — `08_AdditionalPermissions.sql`, holding 47 codes —
but **it is never included by the post-deployment script**, so it does not run on a clean publish.

The practical consequences:

- **"Create a role and grant it `users:read`" cannot be done on a clean publish.** There is no such row
  to grant.
- **The only account that reaches those endpoints is `super-admin`,** through its global `*` permission.
- **The seeded `admin`, `auditor` and `user-manager` roles cannot administer the system.** They hold
  `auth:`-prefixed codes, and because wildcards are prefix matches, `auth:users:*` does not satisfy
  `users:read`.

The eight seeded roles are `super-admin`, `admin`, `user-manager`, `auditor`, `user`, `org-owner`,
`org-admin`, `org-member`.

---

## 6. Data and Persistence

### The schema

**52 tables**, grouped into six folders in the database project.

| Group | Tables | What lives there |
|---|---:|---|
| `Authentication/` | 7 | Sessions, refresh tokens, login attempts, known devices, external logins |
| `Core/` | 11 | Users, roles, permissions, applications and their join tables |
| `Notifications/` | 6 | Notification types, templates, versions, translations, layouts, outbox |
| `Organizations/` | 7 | Organizations, memberships, invitations, ownership transfer |
| `Security/` | 16 | Audit logs, API keys, webhook keys, two-factor, authorization codes, deletion records, encryption keys |
| `System/` | 5 | Platform settings, privacy-policy versions and artifacts, settings overrides |
| **Total** | **52** | |

### Data access

**Dapper over hand-written SQL is the only data-access mechanism.** There is no object-relational mapper
(ORM) that generates queries, no change tracker, and no migrations engine. 39 of the 44 files in the
persistence folder use Dapper directly.

- **Nine stored procedures are defined; only four are ever called.** The four that are:
  `sp_GetUserById`, `sp_GetUserByEmail`, `sp_CreateRefreshToken`, `sp_RevokeAllUserTokens`. They are
  invoked as `EXEC` text, never with `CommandType.StoredProcedure`. The other five — including
  `sp_ValidateCredentials` and `sp_ValidateRefreshToken`, whose names suggest they are central — are
  called by nothing.
- **The SQL is SQL Server-specific.** Bracketed identifiers, `MERGE` upserts, `DATEADD`, and the four
  `EXEC` calls. Porting to another engine means rewriting all 38 repositories and the database project,
  not swapping an implementation.
- **The connection factory returns an already-open connection.** Calling `Open()` on it throws, and the
  failure surfaces as a confusing HTTP 400 rather than an obvious error.
  *In code:* `Auth/Auth.Infrastructure/Persistence/SqlConnectionFactory.cs` lines 22-24.
- **Sorting is allow-listed.** Every list endpoint takes `sortBy` and `sortDirection`, and the accepted
  field names are constants in the Domain layer. An unrecognized value returns 400 rather than reaching
  the SQL.

### Domain model shape

- 51 entity classes, of which **only 8 are aggregate roots**: `Application`, `NotificationLayout`,
  `NotificationTemplate`, `NotificationType`, `Organization`, `Permission`, `Role`, `User`.
- **Only 3 value objects** exist: `Email`, `PermissionCode`, `PhoneNumber`. Each has a private
  constructor, a validating `Create` factory returning `ErrorOr<T>`, and an unvalidated `From` factory
  used only when rehydrating from the database.
- 21 static error catalogs supply every domain error; handlers return them rather than throwing.

### How the schema is deployed

The database project is an SSDT project that builds to a **DACPAC** (data-tier application package) — a
single file describing the desired schema. Publishing it compares that description against the live
database and generates the change script.

**There are no rollback scripts.** The upgrade folder holds 9 forward-only, idempotent scripts. Rolling
back means restoring a backup.

**Not everything in the folders runs.** Of 16 seed scripts on disk, 10 are included by the
post-deployment script; of 9 upgrade scripts, 5 are included. The six unwired seeds include
`08_AdditionalPermissions.sql`, the one that would close the permission gap in section 5.

---

## 7. Subsystems and Background Work

### Notifications

**All notification content lives in the database, not in resource files or code.** An administrator edits
templates in the console; nothing needs a redeploy to change an email.

| Fact | Value |
|---|---|
| Notification types seeded | **16** |
| Templates seeded | **15** — `welcome-email` is the one seeded type with **no** template |
| Translation rows | **105** (15 templates × 7 languages) |
| Layouts seeded | **1** |
| Template language | Liquid, rendered through the Fluid sandbox |
| Delivery | SMTP (Simple Mail Transfer Protocol) through MailKit |

**Sending is queued through an outbox by default.** `Notifications:UseOutbox` is `true`, so a handler
writes a row and returns; a background dispatcher claims a batch and sends it. The dispatcher polls every
30 seconds (`Notifications:PollIntervalSeconds`), claims 20 rows at a time
(`Notifications:BatchSize`), retries up to 5 times (`Notifications:MaxAttempts`) before dead-lettering,
and reclaims rows stuck for more than 5 minutes (`Notifications:StaleClaimMinutes`).

### Localization

**Seven languages, on both sides, and they are the same seven.** English, Arabic, Turkish, French,
Chinese, Urdu and Persian — codes `en`, `ar`, `tr`, `fr`, `zh`, `ur`, `fa`. Arabic, Urdu and Persian are
right-to-left.

The backend has four resource families — application messages, domain errors, middleware messages and
validation messages — each with one neutral English file plus six culture files. The language for a
response is chosen in this priority order: **query string → cookie → `Accept-Language` header → the
custom `X-Language` header.** A value naming a language the system does not support is ignored — the
provider declines it, the next source is tried, and the answer comes back in English if none of the four
names a supported language. No request is ever rejected for asking for a language that does not exist.

### Account deletion

A user-initiated deletion is a scheduled, recoverable process rather than an immediate purge: a grace
window (`AccountDeletion:GraceDays`, 30 days) during which the user can recover, a one-time-password
confirmation step, and then an irreversible execution by a background worker. Destroyed identifiers are
recorded as hashes in a reservation registry so a deleted address cannot immediately be re-registered by
someone else; the reservation window is derived from the longest-lived record keyed to that address.

**The key that hashes those identifiers is permanent.** Rotating it breaks the registry. Section 15 lists
it explicitly.

### System settings

**Configuration is layered, and part of it is live.** A database table of sparse overrides sits on top of
the configuration files, is loaded by a custom configuration provider, and is edited through the admin
console. Settings are consumed through `IOptionsSnapshot`/`IOptionsMonitor`, so most take effect without
a restart. A registry marks which fields require a restart and the console shows a pending-restart state
for them. Section 17 gives the full precedence chain and the restart-required list.

### Background services

Seven hosted services run inside the API process, and one inside the gateway.

| Service | What it does |
|---|---|
| `SystemSettingsRefreshService` | Re-reads the database settings layer on a 5-minute safety-net timer |
| `TokenRevocationBackgroundService` | Drains the revocation channel into the `RevokedTokens` table |
| `NotificationTemplateStartupCheck` | Verifies the seeded templates on startup |
| `NotificationOutboxDispatcher` | Claims and sends queued notifications |
| `EmailLogoRenditionStartupTask` | Prepares email-sized logo renditions |
| `EncryptionMigrationService` | One-shot re-encryption pass, only when explicitly enabled |
| `AccountDeletionWorker` | Executes deletions whose grace period has expired |
| `GatewayRuntimeSettingsPoller` *(gateway process)* | Pulls limits and CORS origins from the API |

---

## 8. Performance Architecture

**No benchmark, load test or profiling artifact exists in this repository.** The design decisions below
are real; any number attached to them would not be. This section states the decisions and their
mechanisms, not measured figures.

### 1. Dapper: direct SQL execution

Instead of a full ORM that generates SQL, AuthSystem uses **Dapper** — a micro-ORM that maps result rows
onto objects and leaves the SQL to you.

| Aspect | Full ORM (e.g. Entity Framework) | Dapper (micro-ORM) |
|--------|-----------------------------------|---------------------|
| Query execution | Auto-generated SQL, may be inefficient | Hand-written, explicit SQL |
| Memory overhead | Higher (object tracking, change detection) | Minimal (no tracking) |
| SQL control | Abstracted away | Full control |
| Complexity | Higher abstraction | Direct and transparent |

That table is the design rationale for the choice. **Entity Framework is not a dependency of this system
and appears nowhere in it** — it is named here only as the comparison baseline.

The cost of the choice is stated honestly in section 6: every query is SQL Server-specific, and there is
no engine portability.

### 2. Async/await: non-blocking operations

Every database call is asynchronous and accepts a `CancellationToken`. A request that is abandoned by the
client is cancelled rather than run to completion; the exception middleware recognizes that case and
writes no response.

### 3. Connection pooling

ADO.NET pools connections per connection string, so a repository call reuses an open physical connection
instead of paying a new handshake.

**Note the local convention:** `SqlConnectionFactory.CreateConnectionAsync` returns a connection that is
**already open**. Callers must not call `Open()` again.

### 4. In-memory caching

Caching is entirely in-process. There is no cache server and no distributed cache abstraction.

| Cache | What it holds | Expiry |
|---|---|---|
| Token blacklist | Revoked token, session and user entries | User-level entries retained 1 hour past revocation; cleanup pass every 5 minutes |
| `TemplateCache` | Notification template and layout source, including "no such template" | 15-minute absolute ceiling, plus immediate eviction when a template is published |
| `FluidTemplateRenderer` parsed cache | Liquid templates already parsed, keyed by their exact source text | Bounded at 1,024 entries; no time expiry |
| `PolicyArtifactCache` | Rendered privacy-policy documents | No expiry at all — entries are replaced when a revision is published |
| Per-user key cache | Unwrapped data encryption keys | 15-minute sliding expiry |

**Permission checks are not cached, because they need no cache.** An endpoint permission check reads the
`permissions` claim off the already-validated token with no input/output at all. The one exception is the
organization fallback described in section 5, which performs a live database query per request when the
token carries no organization claims for that organization.

### Configuration is not simply "loaded at startup"

A database override layer sits on top of the files and is refreshed both on demand and on a 5-minute
timer, and most settings are read through snapshot interfaces. Section 17 gives the precedence chain and
names the settings that genuinely require a restart.

---

## 9. API Design & Patterns

### RESTful design

| Principle | Implementation |
|-----------|---------------|
| **Resources as nouns** | `/users`, `/roles` (not `/getUsers`) |
| **HTTP verbs for actions** | GET (read), POST (create), PUT (update), DELETE (remove) |
| **Stateless** | Each request carries all needed information — the bearer token |
| **Main URL shape** | `/api/v1/{resource}/{id}/{sub-resource}` |

**Three route families deliberately break that shape**, and a client author needs to know all three:

1. **Administration endpoints** sit under an extra segment: `/api/v1/admin/platform-settings`,
   `/api/v1/admin/system-settings`, `/api/v1/admin/Secrets`.
2. **The public privacy policy** is unversioned and outside `/api`: `/privacy`.
3. **Discovery endpoints** are unversioned by specification: `/.well-known/*`.

URL matching is case-insensitive, so `/api/v1/users` and `/api/v1/Users` reach the same action.

### Single responsibility per endpoint

```text
GET    /api/v1/users         -> List users
GET    /api/v1/users/{id}    -> Get one user
POST   /api/v1/users         -> Create user
PUT    /api/v1/users/{id}    -> Update user
DELETE /api/v1/users/{id}    -> Delete user
```

### API versioning

**One version exists: `v1`.** There is no v2 controller anywhere in the tree.

- Routes carry the version as a path segment. Every versioned controller's route template requires the
  literal segment, so **the URL is what actually selects the version**.
- Three version readers are registered — the URL segment, an `X-Api-Version` header, and an
  `api-version` query parameter — but the header and query readers cannot select a version on their own,
  because the route template demands the segment.
- The default version is `1.0`, assumed when unspecified, and the response reports supported versions.
- **Two controllers sit outside versioning entirely:** the discovery controller (declared version-neutral,
  because OIDC requires fixed paths) and the public policy controller (routed at `/privacy`).

### Database access isolation

Only repositories talk to the database, and only `Auth.Infrastructure` contains repositories.

```text
CORRECT:
Controller -> ISender -> MediatR handler -> Repository interface -> Repository -> Database

WRONG:
Controller -> Database (bypassing every layer)
Handler    -> Dapper directly (bypassing the repository)
```

---

## 10. Design Principles

### DRY (Don't Repeat Yourself)

Shared logic is centralized in reusable services. Password hashing exists in one place
(`Argon2PasswordHasher`) and is used by login, registration, API-key verification, recovery-code
verification and the step-up challenge. A bug fixed there is fixed everywhere.

### SOLID Principles

| Principle | Application in AuthSystem |
|-----------|--------------------------|
| **Single Responsibility** | One handler per use case — 190 of them, each in its own folder with its command and validator |
| **Open/Closed** | New external identity providers and notification channels are added by registering a new strategy in a keyed factory, with no change to the resolving code |
| **Liskov Substitution** | Repository implementations are substitutable behind their interfaces, which is what makes the handlers unit-testable with mocks. **It does not make the database engine substitutable** — every repository contains SQL Server-specific SQL |
| **Interface Segregation** | There are 38 repository interfaces, one for each entity type that is persisted on its own, so a handler depends on one narrow contract rather than a data-access god interface. **There is no separate read/write split** |
| **Dependency Inversion** | Interfaces are declared in `Auth.Domain` and `Auth.Application`; implementations live in `Auth.Infrastructure` and are wired by dependency injection at startup |

---

## 11. Deployment & Operations

> For the step-by-step production procedure — storage modes, IIS and shared hosting, the gateway, and
> bring-your-own-key migration — see
> [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md).

### Technology requirements

Two rows below are verifiable from the repository. The rest are **suggested starting points, not measured
requirements** — nothing in this repository sizes the system.

| Component | Verified from source | Note |
|---|---|---|
| **.NET runtime** | .NET 10.0 | Every C# project targets `net10.0` |
| **SQL Server** | SQL Server 2019 or later | The database project targets the SQL Server 2019 schema provider |
| **Operating system** | Windows | The only hosting configuration present is IIS with `AspNetCoreModuleV2`. The `Dpapi` secret mode is Windows-only by construction; `PlainText` or `Certificate` would be required elsewhere |

| Component | Suggested starting point | Basis |
|---|---|---|
| RAM | 2 GB minimum, 4 GB+ comfortable | **Not measured.** No sizing exercise exists in this repository |
| CPU | 2 cores minimum, 4 cores+ comfortable | **Not measured** |
| Disk | 10 GB minimum, more with audit-log volume | **Not measured** |

### Deployment method

| Method | Status |
|---|---|
| **IIS on Windows, in-process hosting under `AspNetCoreModuleV2`** | The only method this repository configures |

**There is no container image, no compose file, no orchestration manifest and no managed-cloud publish
profile in this repository.**

Be careful with `web.config`, because the two halves of the product treat it differently. Neither .NET
application's `web.config` is in git — it is produced by `dotnet publish` and then edited on the server
to set the IIS hosting model to `inprocess` and the environment name to `Production`, and it is
deliberately ignored because the deployed copy carries secrets. The two SPAs are the opposite case:
their `web.config` files **are** tracked, under `apps/<app>/public/`, and the build copies each one into
that application's output folder.

**There is no continuous integration and no continuous delivery.** The workflows folder exists and is
empty. Deployment is Visual Studio "Publish" for the two .NET applications, a manual database publish,
and a manual file copy for the two SPAs.

### What is and is not in source control

This matters more than it sounds, because an operator who assumes "configuration is in git" loses
everything below.

| File | Tracked? |
|---|---|
| `Auth_API/appsettings.json`, `appsettings.Development.json` | **yes** |
| `API_Gateway/appsettings.json`, `appsettings.Development.json` | **yes** |
| Both SPAs' `public/web.config` and `.env.production` | **yes** |
| `appsettings.Production.json` (either process) | **no** — ignored |
| `web.config` (either .NET process) | **no** — ignored |
| `appsettings.*.local.json` | **no** — ignored |
| Publish profiles (`*.pubxml`), database publish profiles | **no** — ignored |

**A clean clone has no production configuration at all.** The ignored files are ignored because they hold
secrets: a deployed `Auth_API/web.config` carries the database connection string including its password,
the SMTP password and the Data Protection certificate password as literal environment-variable values.
Back those up out of band, encrypted.

### Horizontal scaling

The API can run as multiple instances behind a load balancer, but **three pieces of state are per-process
and must be handled explicitly.** These are prerequisites, not optimizations.

1. **The Data Protection key ring must be a shared folder.** `DataProtection:KeyPath` must point every
   instance — API and gateway — at the same directory. Instances that do not share it cannot read each
   other's protected values, including the encrypted secrets file and every per-user encryption key.
2. **Token revocation does not propagate between instances.** The blacklist is in-process and is
   rehydrated from the database only at startup. A logout handled by instance A is invisible to instance
   B until B restarts. Plan for it, or terminate sessions at the edge.
3. **Rate limits are per instance.** Both limiters are in-process fixed windows, so N instances multiply
   every configured limit by N. Divide the configured numbers by the instance count, or enforce limits at
   a shared edge.

Stateless in the ordinary sense still holds: any instance can validate any access token, because
validation needs only the public key.

### Database deployment

The procedure is manual, and every step is listed here so that nothing is hidden behind the word
"deploy". Run these from the repository root unless stated otherwise.

1. **Build the database project.** Open `Auth/Auth_DB/Auth_DB.sqlproj` in Visual Studio and build it, or
   build it with the SSDT tooling. Success looks like a `.dacpac` file appearing in the project's output
   folder.
2. **Publish the DACPAC** against the target database, from Visual Studio's Publish dialog or with the
   `SqlPackage` command-line tool. Success looks like a completed publish report naming the objects
   created or altered.
3. **The post-deployment script then runs automatically**, executing 5 upgrade scripts and 10 seed
   scripts by include. You do not run these yourself.

Three things to know before you rely on this:

- **Nothing runs it for you.** There is no pipeline, no migration runner, and no startup-time schema
  check.
- **There are no rollback scripts.** Recovery from a bad publish is a database restore.
- **Four publish profiles exist and none of them is in git**, so the canonical target for a given
  environment is not recorded in this repository.

---

## 12. Monitoring & Observability

### Structured logging (Serilog)

Both processes use Serilog. Serilog captures structured properties internally, which is what makes the
logs queryable once a structured sink is attached.

**Both processes write plain text, not JSON.** Each sink uses an output template. No JSON formatter is
registered and no JSON formatting package is referenced by either project. A sink that renders JSON can
be swapped in, but nothing in this repository does so.

| Configuration | Auth API | API Gateway |
|---|---|---|
| **Output format** | Plain text via output template | Plain text via output template |
| **Sinks** | Console + rolling file | Console + rolling file |
| **Rolling interval** | Daily | Daily |
| **Retention** | `retainedFileCountLimit: 30` — the last **30 files** | `retainedFileCountLimit: 30` — the last **30 files** |
| **Enrichment** | `FromLogContext`, `WithMachineName`, `WithThreadId` | `FromLogContext`, `WithMachineName`, `WithThreadId` |
| **Configured log path** | `Logs/auth-api-.log` | `logs/gateway-.log` |

Serilog appends the date to the stem, so the files on disk are named `auth-api-20260815.log` and so on.

**Both paths are relative.** The base is the process content root, which under IIS in-process hosting is
the deployed application folder. This repository cannot state a physical server path; to find it, look at
the application's physical path in IIS and append the configured folder.

Only the four Serilog minimum-level keys are editable from the console and take effect immediately. Sinks
and enrichers are built once at startup.

### Health checks

**There are four health endpoints across the two processes, and the database check is not on `/health`.**

| Host | Path | Checks run | Status on failure |
|---|---|---|---|
| Auth API | `/health` | `self` — the process is running | — (always healthy) |
| Auth API | `/ready` | `database` (SQL Server, 5-second timeout) and `signing-key` | `Degraded` / **`Unhealthy`** |
| API Gateway | `/health` | `self` — the process is running | — |
| API Gateway | `/ready` | `auth-api` — an HTTP probe against the API's `/ready` | Unhealthy |

Use `/health` for a load-balancer liveness probe and `/ready` for readiness. The response body is the
same shape on both hosts:

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.3,
  "checks": [
    { "name": "database", "status": "Healthy", "durationMs": 4.1, "tags": ["ready"] }
  ]
}
```

An `error` field appears only when error details are enabled — in Development, or when
`HealthChecks:ExposeErrorDetails` is `true`. That flag is read per request, so it can be toggled live.

**All four endpoints are reachable only by addressing their process directly.** They are exempt from
gateway-token validation but are not in the gateway's route allowlist.

### Correlation IDs

**The gateway stamps `X-Correlation-ID` on every proxied request** — note the capitalized `ID` — reusing
the caller's value if one was supplied, otherwise minting a new GUID. It includes that value in its own
request log line.

**The Auth API does not attach it to anything.** There is no correlation middleware and no Serilog
enricher for it in the API. The only place the API uses the value is when an unhandled exception occurs:
it is echoed back in the error response body. It does not reach the API's own log lines, and
`AuditLogs.CorrelationId` **is not a column** — the repository hardcodes null for it.

**End-to-end tracing across the two processes is therefore not wired up today.** Treat this as a gap, not
a feature.

### Application performance monitoring

Serilog's structured properties are compatible with the usual application performance monitoring (APM)
destinations, once a suitable sink is added:

- **Application Insights** (Azure)
- **Elastic Stack** (ELK)
- **Seq** (structured log server)
- **Grafana + Loki** (open source)

None of these is configured in this repository.

---

## 13. Disaster Recovery & Business Continuity

### Backup strategy

| Component | Backup method | Frequency | Retention |
|-----------|--------------|-----------|-----------|
| **Database** | SQL Server full + differential backup | Full: daily; differential: every 6 hours | 30 days |
| **Configuration** | **Manual, out of band.** Only the base and Development `appsettings` files are in git. `appsettings.Production.json`, both `web.config` files, `appsettings.*.local.json` and every publish profile are deliberately git-ignored because they hold secrets — nothing in the repository backs them up for you | On every change | Indefinite, encrypted |
| **Secret store** | The encrypted `secrets.dpapi` file plus the Data Protection key ring, plus the `.pfx` in Certificate mode; or `appsettings.Production.json` in PlainText mode | On rotation | See the permanence note in section 15 |
| **Audit logs** | Database backup, plus optional CSV export from the console | Daily | Per compliance requirement (typically 1–7 years) |

### Recovery objectives (recommended targets)

These are targets to agree with the business, not measured capabilities of this system.

| Metric | Target | Description |
|--------|--------|-------------|
| **RPO** (Recovery Point Objective) | 6 hours | Maximum acceptable data loss |
| **RTO** (Recovery Time Objective) | 1 hour | Maximum acceptable downtime |

### Incident response

1. **Detection** — alerts on failed-authentication spikes, rate-limit rejections, or health-check
   failures. Nothing in this repository raises these alerts; they must be built on the log stream.
2. **Containment** — account lockout, token blacklisting, session revocation, tightening rate limits from
   the console.
3. **Investigation** — the gateway's request log carries `X-Correlation-ID`; the API's does not, so
   correlate by timestamp, user id and path when tracing into the API.
4. **Recovery** — database restore, key rotation (section 15), forced password reset.
5. **Post-incident** — audit-log review, root-cause analysis, policy updates.

---

## 14. Integration Capabilities

### Current integration points

| Integration | Method | Status |
|-------------|--------|--------|
| **The two web applications** | The typed API client, bearer tokens, and the identity-provider session cookie | Shipped |
| **REST API consumers** | Bearer JWT | Shipped |
| **Third-party applications (single sign-on)** | Authorization code with PKCE, discovery and JWKS — see section 4 | Shipped |
| **Google sign-in** | Google ID-token validation; unverified provider emails are rejected | Shipped, enabled by default (`ExternalAuth:Google:Enabled`) |
| **Apple sign-in** | Apple ID-token validation, issuer pinned, key set cached 24 hours | Implemented, **disabled by default** (`ExternalAuth:Apple:Enabled`) |
| **Service-to-service** | API keys, sent in the `X-Api-Key` header | Shipped |
| **Webhook callers** | Webhook keys, sent as a `?whk=` query parameter | Shipped |
| **.NET client library** | `Auth.Sdk` — see the honest note below | Ships in the solution, referenced by nothing |

### The SDK, stated honestly

`Auth.Sdk` builds a package named `AuthSystem.Sdk` version 1.0.0 that gives a third-party .NET
application three authentication schemes and a permission attribute. It is in the solution and **no
project references it**; there is no package manifest, no pack target, and no publishing configuration.

It also carries a defect worth knowing before adopting it: **it adds the `X-Gateway-Token` header
twice** — once when the named HTTP client is registered and again on the resolved client. A two-value
header can never match the middleware's single-value comparison, so every SDK call through a
token-validating gateway is rejected with **403**. Separately, the SDK attaches only the gateway token
and never an `Authorization` header, while the API-key and webhook-key `validate` endpoints require an
authenticated caller with a permission.

### Future integration roadmap

These are **not built**. Nothing below exists in the repository today.

| Integration | Description | Priority |
|-------------|-------------|----------|
| **LDAP / Active Directory** | Sync users from corporate directories | High |
| **SAML 2.0** | Enterprise SSO federation | Medium |
| **Outbound webhook notifications** | Push events to external systems | Medium |
| **Azure AD / Entra ID** | External identity provider integration | Medium |

On outbound events specifically: an integration-event abstraction exists, but its only implementation is
a no-op. **Nothing leaves the process.** See section 19.

### API keys and webhook keys

Both are long random values with an environment-tagged prefix, created and revoked from the console, and
both can be revoked at any time. **They are hashed differently, and the difference is deliberate.**

| | API key | Webhook key |
|---|---|---|
| Prefixes | `ak_prod_`, `ak_stag_`, `ak_dev_`, `ak_` | `wk_prod_`, `wk_stag_`, `wk_dev_`, `wk_` |
| Hash | **Argon2id** — same treatment as a password | **HMAC-SHA256**, deterministic |
| Why | Verification tries candidates fetched by prefix, so a slow hash is affordable | A deterministic hash allows lookup by value in one query |
| Scopes | Yes — each key joins to a set of permissions | **None** — webhook keys have no scope concept |
| Extra field | — | `TargetUrl` |
| Transport | `X-Api-Key` header | `?whk=` query parameter |

**Audit coverage is partial, and the gap is worth stating.** Creating and revoking an API key each write
an audit entry. **Using** one does not — validation updates the key's last-used timestamp and writes no
audit row. Creating or revoking a **webhook** key writes no audit entry at all, because its events have
no handler.

**Per-key rate limits are stored but never enforced.** See section 19.

---

## 15. Secret Management & Key Rotation

This section is the single home for storage modes, key material, provisioning and rotation. Nothing about
keys is described anywhere else in this document.

### The three storage modes

**There are exactly three modes**, selected by `SecretManagement:StorageMode`:

| Mode | Where secrets live | When to use it |
|---|---|---|
| **`PlainText`** | In the configuration file named by `SecretManagement:PlainTextTargetFile` | Development; and the only option on a non-Windows host |
| **`Certificate`** | Encrypted in `secrets.dpapi`, protected by an X.509 certificate you own | Shared hosting, and any server you may need to migrate — the certificate travels with you |
| **`Dpapi`** | Encrypted by the Windows Data Protection API, which ties the ciphertext to the machine or account | A single fixed Windows server |

DPAPI is Windows' built-in Data Protection API: it encrypts data so that only the same machine, or the
same user account, can decrypt it.

**The shipped defaults differ by file, and the difference trips people up.** The settings class default is
`PlainText`. The base `appsettings.json` sets **`Certificate`** for both processes. The Development file
explicitly sets **`PlainText`**, with a comment calling that the actual contract for Development.

**In Certificate mode, a missing certificate is fatal outside Development.** The process refuses to start.
Inside Development it falls back to PlainText with a warning.

### Where key material lives

| What | Configuration key | Default when unset |
|---|---|---|
| Encrypted secrets file | `SecretManagement:SecretFilePath` | `%LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi` |
| Data Protection key ring | `DataProtection:KeyPath` | `%ProgramData%\AuthSystem\Keys` |
| Protecting certificate | `DataProtection:Certificate:PfxPath`, or `:Thumbprint` to load from the Windows store | none — Certificate mode fails without one |
| Certificate password | The environment variable named by `DataProtection:Certificate:PasswordEnvironmentVariable`, default `AUTH_DP_CERT_PASSWORD` | — |
| Superseded certificates | `DataProtection:Certificate:AdditionalPfxPaths` — kept so old key-ring entries stay readable after rotation | `[]` |

**Point the API and the gateway at the same key ring folder.** Both use the Data Protection application
name `AuthSystem`, and both must be able to read the same protected values.

### The secrets that must exist or the process will not start

| Secret | Configuration key | Rotatable? |
|---|---|---|
| JWT signing key (RSA private key, PEM) | `Jwt:PrivateKeyPem` | Rotatable |
| Refresh-token HMAC key | `Jwt:RefreshTokenHmacKeyPlain` | Rotatable |
| Gateway token (API side) | `Gateway:ExpectedToken` | Rotatable |
| Account-deletion identifier HMAC key | `AccountDeletion:IdentifierHmacKeyPlain` | **PERMANENT — never rotate** |
| Gateway token (gateway side) | `Gateway:Token` | Rotatable |

**The account-deletion identifier key is permanent.** Every reservation in the deletion registry is a hash
computed under it. Rotate it and the registry can no longer recognize a destroyed identifier, which means
a deleted email address becomes re-registerable before its reservation window has run out. Back it up;
never regenerate it.

Two further keys are secret-managed but not startup-required: `ExternalAuth:Apple:PrivateKeyPem` (the
Apple `.p8` signing key) and the SMTP password `Email:Password`.

**A startup guard refuses to boot if any crown-jewel secret is present in plaintext Production
configuration.** It checks `Jwt:PrivateKeyPem`, `Jwt:RefreshTokenHmacKeyPlain`,
`AccountDeletion:IdentifierHmacKeyPlain` and `ExternalAuth:Apple:PrivateKeyPem`, and it runs *before* the
secret provider is layered.

### Provisioning

- **`SecretManagement:AutoGenerateKeys` is `true` by default**, and generation happens only when the
  secrets file does not exist. Generated sizes: RSA 2048; HMAC key, gateway token and pepper 32 bytes
  each.
- **Set it to `false` after the first successful startup**, so that a missing secret fails loudly instead
  of being silently regenerated — a silent regeneration invalidates every outstanding token.
- **Bring your own keys** by importing them through the administration endpoints below (Certificate and
  Dpapi modes) or by writing them into `appsettings.Production.json` (PlainText mode). This is what makes
  a server migration possible without logging every user out. See
  [PRODUCTION_DEPLOYMENT_GUIDE.md §F — Provision your own keys (BYOK)](PRODUCTION_DEPLOYMENT_GUIDE.md#f-provision-your-own-keys-byok--painless-migration).

### The step-up confirmation that guards destructive operations

**Every generate and import operation requires a second confirmation from the same administrator.** It
is easy to miss when reading the endpoint list, because the confirmation is a separate pair of calls
rather than a field on the operation itself.

| Property | Value |
|---|---|
| The code | 6 numeric digits, Argon2id-hashed, emailed to the requesting administrator |
| Code entry window | `Email:OtpExpirationMinutes` (class default 15, shipped 5) |
| Maximum attempts | **5**, hardcoded. The attempt is claimed by a conditional database update *before* the hash is verified, so slow hashing cannot be used to buy extra guesses |
| Approval window after verifying | **5 minutes**, hardcoded and deliberately not configurable |
| What the approval is bound to | The requesting administrator, the exact operation, and a SHA-256 hash of the payload. All three are re-checked when it is spent |
| Reuse | Single-use, consumed by a conditional update |
| Error messages | Unknown id, wrong owner, wrong code, expired and exhausted all return the *same* error |
| Issuance throttle | 3 challenges per 60 seconds per administrator |

Every secrets endpoint also carries `[RequirePermission("secrets.manage")]`, and the whole controller is
gated by `SecretManagement:EnableAdminApi`.

### What rotation actually does

**There is exactly one active RSA signing key at a time.** There is no key list, no dual-key window, and
the key set publishes a single entry. Validation pins that one key.

**Rotating the RSA key invalidates every access token already issued.** Holders must refresh. Because
access tokens live 15 minutes, the blast radius is one 15-minute window, not seven days.

**Refresh tokens are unaffected by RSA rotation.** They are random values hashed with HMAC-SHA256, not
signed with the RSA key. Rotating the *HMAC* key is the operation that invalidates them.

### How to rotate, step by step

Every step below is an HTTP call to the administration API. Run them against your deployed API host, as
an administrator holding `secrets.manage`, with `SecretManagement:EnableAdminApi` set to `true`.

1. **Request a challenge.** `POST /api/v1/admin/Secrets/challenges` — name the operation you intend.
   Success: a challenge id in the response, and a 6-digit code arrives in that administrator's inbox.
2. **Verify the challenge.** `POST /api/v1/admin/Secrets/challenges/{id}/verify` with the code. Success:
   a confirmation that the challenge is approved. **You now have 5 minutes.**
3. **Perform the operation.** One of `POST .../generate/rsa`, `.../generate/hmac`,
   `.../generate/gateway-token`, `.../import/rsa`, `.../import/hmac`, `.../import/gateway-token`.
   Success: the response confirms the new material was stored.
4. **Verify the result.** Fetch `GET /.well-known/jwks.json` and confirm the published key changed. Then
   sign in once and confirm you receive a working token.

**Two command-line escape hatches exist** for the case where the API cannot start. Run these from the
`Auth/Auth_API` directory; each prints an encrypted value and exits without starting the server:

```bash
dotnet run -- --generate-rsa-key
```

```bash
dotnet run -- --generate-hmac-key
```

### External secret managers are not supported

**No external secret manager is integrated, and none can be configured.** The three modes above are the
complete list. Integrating a vault would require writing a new configuration provider; none exists today.

---

## 16. Testing

### What is tested today

**There is one backend test project and one frontend test setup.** Both are unit-level.

| Backend | Value |
|---|---|
| Project | `Auth/Auth_API.Tests` — the only test project in the solution |
| Framework | xunit 2.9.3, with Moq 4.20.72 and FluentAssertions 8.10.0 |
| Source files | **176** `.cs` files, of which **171** contain test cases |
| Test attributes | **1,412** `[Fact]` and **68** `[Theory]`, fed by **241** `[InlineData]` rows and **2** `[MemberData]` sources |

| Frontend | Value |
|---|---|
| Unit runner | Vitest with jsdom and Testing Library |
| Unit test files | **40** |
| Unit test cases | **276** `it()` / `test()` calls counted in source |
| End-to-end runner | Playwright |
| End-to-end spec files | **5** |

### What is not tested

Stating this plainly matters more than the counts.

- **There is no database in the backend test project.** No test opens a SQL connection.
- **There is no HTTP host harness.** No test spins up the API and issues a request.
- **There are therefore no integration tests** in the usual sense. Repository SQL is exercised by parsing
  the repository source text and the database project's scripts, not by executing queries.
- **There are no load tests and no performance tests.** No load-testing tool or script exists anywhere in
  the repository.

### Guard tests worth knowing about

Several tests exist to fail the build on a *class* of drift rather than to test one handler. An architect
evaluating maintainability should know these exist:

| Guard | What it locks down |
|---|---|
| Gateway route coverage | Every controller prefix must have a gateway route, or the whole feature 404s |
| Gateway rate-limit parity | The same limit lives in three files and they must agree |
| System-settings apply coverage | A value saved in the console must materialize on its exact configuration key |
| Required-secrets coverage | Both secret generators must provision every required secret |
| Localization baseline parity | Every culture carries the same keys *and* the same format placeholders |
| Domain-error resource coverage | Every domain error code has a translation entry |
| User hard-delete SQL | Parses the schema for foreign keys, so a new table referencing users fails the build unless the purge covers it |
| Post-deployment script composition | Every included seed script must end with its own batch separator |

### Coverage

**No coverage percentage is enforced anywhere, and none can be quoted.** A coverage collector is
referenced by the test project, but there is no threshold, no report task, and no pipeline to check its
output. The workflows folder is empty. Any coverage figure in older documentation was an aspiration, not
a measurement.

### Running the tests

Backend, from the `Auth/` directory. Success looks like a summary line reporting zero failures:

```bash
dotnet test
```

Frontend unit tests, from the `Auth_UI/` directory. Success looks like every test file reported as
passed:

```bash
pnpm test
```

End-to-end tests, from the `Auth_UI/` directory. These need the `DEV_HTTPS_CERT` and `DEV_HTTPS_KEY`
environment variables set, Playwright browsers installed with `pnpm exec playwright install`, and — for
two of the five specs — the API running with seeded credentials:

```bash
pnpm e2e
```

### Manual security checklist

The following is a **manual** review checklist, not automated coverage. Nothing in the repository runs it.

- [ ] SQL injection attempts on all input fields
- [ ] XSS payload testing on all text inputs
- [ ] Rate-limit enforcement verification, on both the API and the gateway
- [ ] Token expiration and blacklisting
- [ ] Permission escalation attempts, including wildcard prefix edge cases
- [ ] Brute-force lockout verification
- [ ] Session fixation testing
- [ ] Gateway-token bypass attempts against the API directly

---

## 17. Configuration Reference

### Where a value can come from

.NET configuration is "last provider wins". This system adds several providers, and the resulting order
matters when a value appears in two places.

```text
newly minted pepper material, in memory (only on the boot that mints it)
  > database settings overrides (edited in the admin console)
    > encrypted secrets file (or generated secrets, in PlainText mode)
      > command-line arguments
        > environment variables
          > appsettings.{Environment}.local.json   (git-ignored)
            > appsettings.{Environment}.json
              > appsettings.json
```

Two qualifications keep that ordering honest:

- **The database layer never touches secret keys.** The override provider skips any key marked
  secret-owned and any field the registry does not mark editable, and the write path rejects the same
  keys. So "database beats secrets" is true on paper, but the two key sets are disjoint by construction.
- **The secrets file does beat environment variables**, including a connection string supplied through
  `ConnectionStrings__AuthDb`. That is why the escape hatch `AUTH_IGNORE_SECRET_CONNECTIONSTRING` exists.

### Environment variables the code reads

| Variable | Effect |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Selects the environment configuration files and gates every Development-only branch |
| `ConnectionStrings__AuthDb` | Supplies the database connection string (double underscore maps to `ConnectionStrings:AuthDb`) |
| `Email__Password` | Supplies the SMTP password |
| `AUTH_DP_CERT_PASSWORD` | Default name of the variable holding the Data Protection certificate password |
| `AUTH_DISABLE_DB_SETTINGS` | `true` skips the whole database settings layer |
| `AUTH_IGNORE_SECRET_CONNECTIONSTRING` | `true` stops the secrets file from overriding the connection string |

### Key reference

Values shown are what the shipped `appsettings.json` sets. Where the settings-class default differs, both
are given, because the class default is what runs if the key is deleted.

#### Connection

| Key | Controls | Shipped value |
|---|---|---|
| `ConnectionStrings:AuthDb` | SQL Server connection for everything | A placeholder naming the environment variable, not a real connection string. **Startup refuses to boot without a real value.** |

#### `Jwt`

| Key | Controls | Shipped | Hot or restart |
|---|---|---|---|
| `Jwt:Issuer` | The `iss` claim and the accepted issuer | placeholder | **restart** |
| `Jwt:Audience` | The `aud` claim and the accepted audience | placeholder | **restart** |
| `Jwt:AccessTokenLifetimeMinutes` | Access-token lifetime | `15` | hot |
| `Jwt:RefreshTokenLifetimeDays` | Refresh-token lifetime — **and the real session length** | `7` | hot |
| `Jwt:KeyId` | The `kid` published in the key set | `auth-key-1` | **restart** |
| `Jwt:RotateRefreshTokens` | Issue a new refresh token on each refresh | `true` | hot |
| `Jwt:ClockSkewSeconds` | Validation clock tolerance | `60` | **restart** |
| `Jwt:PrivateKeyPath` | RSA PEM file — signing-key source, priority 1 | empty | restart |
| `Jwt:PrivateKeyEncrypted` | Protected RSA PEM — priority 2 | empty | restart |
| `Jwt:PrivateKeyPem` | Inline RSA PEM — priority 3. **Secret-owned** | absent | restart |
| `Jwt:RefreshTokenHmacKeyPlain` | HMAC key for hashing refresh tokens. **Secret-owned, required** | absent | restart |
| `Jwt:RefreshTokenEncryptedKey` | Legacy protected HMAC key — priority 2 | empty | restart |
| `Jwt:PublicKeyPem` | **No reader — has no effect.** The public key is derived from the private key at runtime | absent | — |

If no key source resolves, the service **generates an ephemeral in-memory RSA key**. That is a valid
startup, and every token it signs dies with the process.

#### `Password`

Covered in full in section 3. The keys are `Password:MinimumLength`, `:RequireUppercase`,
`:RequireLowercase`, `:RequireDigit`, `:RequireSpecialCharacter`, `:HistoryCount`, `:MaxFailedAttempts`,
`:LockoutDurationMinutes`, `:Argon2MemorySize`, `:Argon2Iterations`, `:Argon2Parallelism`, `:SaltSize`,
`:HashSize`, plus the nested `Password:Pepper:*` and `Password:BreachedPasswordCheck:*` groups.

#### `Session`

| Key | Controls | Shipped | Read by code? |
|---|---|---|---|
| `Session:MaxConcurrentSessions` | Cap on live sessions; `0` = unlimited | `0` | yes |
| `Session:TerminateOldestOnMax` | Evict the oldest versus refuse the sign-in | `true` | yes |
| `Session:TerminateSessionsOnPasswordChange` | End sessions on password change | absent from every file; class default `true` | yes |
| `Session:TerminateSessionsOnPasswordReset` | End sessions on password reset | absent from every file; class default `true` | yes |
| `Session:LifetimeHours` | — | `24` | **no reader — has no effect** |
| `Session:ExtendOnActivity` | — | `true` | **no reader — has no effect** |
| `Session:ExtensionHours` | — | `1` | **no reader — has no effect** |
| `Session:IdleTimeoutMinutes` | — | `30` | **no reader — has no effect** |

#### `Gateway`

| Key | Controls | Shipped |
|---|---|---|
| `Gateway:TokenHeaderName` | Header the API expects the shared token in | `X-Gateway-Token` |
| `Gateway:ValidationEnabled` | Reject requests that did not come through the gateway | `true` (`false` in Development) |
| `Gateway:ExemptPaths` | Path prefixes that skip validation | `/.well-known/`, `/health`, `/ready`, `/swagger`, `/openapi`, `/uploads/` |
| `Gateway:ExpectedToken` | The value compared, constant-time. **Secret-owned, required** | absent |

#### `SecretManagement` and `DataProtection`

Covered in full in section 15. Note one dead key: **`SecretManagement:RequiredPermission` has no
reader** — every secrets endpoint hardcodes `[RequirePermission("secrets.manage")]` as a compile-time
attribute.

#### `RateLimiting` and `GatewayRateLimiting`

Covered in full in section 3. The API section holds only four keys —
`RateLimiting:LoginPermitLimit`, `:LoginWindowSeconds`, `:PasswordResetPermitLimit`,
`:PasswordResetWindowSeconds`. **The keys `RateLimiting:PermitLimit`, `:WindowSeconds` and `:QueueLimit`
no longer exist**; there is no general API rate limit, by design.

#### Remaining sections

| Section | Keys | What it governs |
|---|---|---|
| `Cors` | `Cors:AllowedOrigins`, `Cors:AllowCredentials` | Browser origins permitted to call the API |
| `HealthChecks` | `HealthChecks:ExposeErrorDetails` | Whether failed-check details appear in the health body |
| `Email` | 13 keys under `Email:*` | SMTP host, port, credentials, sender, link base URL, one-time-password lifetimes and throttles |
| `Notifications` | 7 keys under `Notifications:*` | Outbox behaviour and the new-device alert |
| `Registration` | `Registration:AllowSelfRegistration`, `Registration:AllowExternalProvisioning` | Whether strangers may create accounts — through the public sign-up endpoint, and through a first sign-in with Google or Apple. Both ship open; both are read per request |
| `IdentityProvider` | 5 keys under `IdentityProvider:*` | Accounts application origin, this API's public origin, code lifetime, session cookie name and lifetime |
| `ExternalAuth` | 10 keys under `ExternalAuth:*` | Google and Apple configuration, plus avatar import |
| `AccountDeletion` | 13 keys under `AccountDeletion:*` | Grace period, worker cadence, retention horizons, policy version, the permanent identifier key |
| `DataController` | 9 keys under `DataController:*` | Legal identity of the data controller. All ship empty, and **the privacy policy cannot be published until they are filled** |
| `ImageStorage` | 9 keys under `ImageStorage:*` | Upload folder, public URL prefix, size and dimension limits, accepted content types |
| `PrivacyPolicyPublication` | `PrivacyPolicyPublication:PhysicalPath` | Where rendered policy documents are written. **Must be non-blank or startup fails** |
| `GeoIp` | `GeoIp:Enabled`, `GeoIp:DatabasePath` | Optional sign-in location lookup. Ships disabled, and no database file ships |
| `Serilog`, `Logging`, `AllowedHosts` | See section 12 | Only the four minimum-level keys are console-editable and hot |

### Settings that require a restart

Changing any of these in the console has no effect until the process restarts, and the console shows a
pending-restart state for them:

`Jwt:Issuer`, `Jwt:Audience`, `Jwt:KeyId`, `Jwt:ClockSkewSeconds`, `Password:Argon2MemorySize`,
`Password:Argon2Iterations`, `Password:Argon2Parallelism`, `Password:Pepper:Enabled`,
`Password:BreachedPasswordCheck:Enabled`, `Password:BreachedPasswordCheck:TimeoutMs`,
`IdentityProvider:IdpSessionCookieName`, `GeoIp:Enabled`, `GeoIp:DatabasePath`,
`ImageStorage:RequestPath`, `AccountDeletion:RunEncryptionMigration`.

Everything else in the registry is hot.

---

## 18. API Endpoints Reference

### The surface, counted

**26 files match `*Controller.cs`. One of them is the shared base class, so there are 25 routable
controllers, carrying 199 actions between them.**

By verb: **74 GET, 82 POST, 21 PUT, 22 DELETE.** **There is no `PATCH`, `HEAD` or `OPTIONS` action
anywhere in the system.**

The **Permission-gated** column counts how many of that controller's actions carry a
`[RequirePermission]` attribute. Where it is lower than the action count, the remaining actions are
authenticated but self-service — a user acting on their own data, or on an organization where membership
decides the answer.

| Controller | Base path | Actions | Authentication | Permission-gated | Gateway policy |
|---|---|---:|---|---:|---|
| `AuthController` | `/api/v1/auth` | 27 | 16 anonymous, 11 authenticated | 0 | `auth` |
| `TwoFactorController` | `/api/v1/auth/2fa` | 4 | 1 anonymous, 3 authenticated | 0 | `auth` |
| `UsersController` | `/api/v1/users` | 29 | Authenticated | 20 | `api` |
| `OrganizationsController` | `/api/v1/organizations` | 23 | Authenticated | 17 | `api` |
| `ApplicationsController` | `/api/v1/applications` | 15 | 14 authenticated, 1 anonymous (public branding by `client_id`) | 14 | `api` |
| `PermissionsController` | `/api/v1/permissions` | 9 | Authenticated | 9 | `api` |
| `RolesController` | `/api/v1/roles` | 7 | Authenticated | 7 | `api` |
| `ApiKeysController` | `/api/v1/apikeys` | 5 | Authenticated | 5 | `api` |
| `WebhookKeysController` | `/api/v1/webhookkeys` | 5 | Authenticated | 5 | `api` |
| `AuditLogsController` | `/api/v1/audit-logs` | 5 | Authenticated | 5 | `api` |
| `InvitationsController` | `/api/v1/invitations` | 3 | 2 anonymous, 1 authenticated | 0 | `api` |
| `DashboardController` | `/api/v1/dashboard` | 6 | Authenticated | 5 | `api` |
| `ImagesController` | `/api/v1/images` | 1 | Authenticated | 0 | `api` |
| `NotificationTemplatesController` | `/api/v1/notification-templates` | 14 | Authenticated | 14 | `api` |
| `NotificationLayoutsController` | `/api/v1/notification-layouts` | 6 | Authenticated | 6 | `api` |
| `NotificationTypesController` | `/api/v1/notification-types` | 2 | Authenticated | 2 | `api` |
| `NotificationOutboxController` | `/api/v1/notification-outbox` | 3 | Authenticated | 3 | `api` |
| `PrivacyPolicyController` | `/api/v1/privacy-policy` | 8 | 7 authenticated, 1 anonymous (`GET published`) | 7 | `api` |
| `PublicPolicyController` | `/privacy` (unversioned) | 3 | Anonymous | 0 | `api` |
| `PlatformController` | `/api/v1/platform` | 1 | Anonymous (public branding) | 0 | `api` |
| `PlatformSettingsController` | `/api/v1/admin/platform-settings` | 2 | Authenticated | 2 | `admin` |
| `SystemSettingsController` | `/api/v1/admin/system-settings` | 4 | Authenticated | 4 | `admin` |
| `SecretsController` | `/api/v1/admin/Secrets` | 13 | Authenticated, behind the admin-API feature flag | 13, all `secrets.manage` | `admin` |
| `DiscoveryController` | `/.well-known/*` (unversioned) | 3 | Anonymous | 0 | none — global limiter only |
| `GatewayRuntimeSettingsController` | `/api/v1/internal/gateway-settings` | 1 | Anonymous | 0 | **not routed** — gateway calls it directly |
| **Total** | | **199** | | | |

### The response contract

**There is no envelope.** A successful response body is the data transfer object itself. There is no
`success`/`data`/`errors` wrapper anywhere in the success path.

- Property names are **camelCase**.
- **Null properties are omitted** from responses entirely.
- **Enums serialize as their string names**, not numbers.
- `DateTime` values are always emitted with a trailing `Z`.

List endpoints return a per-entity paged class — there is no generic `PagedResult<T>` — with this shape:

```json
{
  "users": [],
  "totalCount": 0,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 0,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

The collection property is named after the entity, so it is `users` here and `logs` on the audit-log
endpoint. **Page size is capped between 1 and 100; anything larger returns 400.**

### The error contract

Handler and domain errors return `ProblemDetails`, and the field meanings are not the conventional ones:

```json
{
  "status": 404,
  "title": "User.NotFound",
  "detail": "The requested user was not found.",
  "instance": "/api/v1/users/3f2a..."
}
```

- **`title` is the error code**, not a human-readable title. Program against it.
- **`detail` is the localized human message**, translated according to the request's language.
- **`extensions.errors`** appears **only when there is more than one error**, as an array of
  `{ code, description }`.

Status-code mapping: validation → 400, not found → 404, conflict → 409, forbidden → 403, unauthorized →
401, anything else → 500.

Four responses deliberately do **not** follow this shape, and a client author needs all four:

| Case | Shape |
|---|---|
| Rate-limit rejection (429) | `{ "error": "...", "retryAfter": 60.0 }` — see section 3 |
| Gateway-token rejection (403) | `application/problem+json` with `type`, `title`, `status`, `detail`, `instance` |
| Blacklisted token (401) | `application/problem+json`, title `Unauthorized` |
| Image upload failure | `{ "error": "..." }` — the images controller does not derive from the shared base |

### The OpenAPI document

**The OpenAPI document is served only in Development, at `/openapi/v1.json`.** It is produced by .NET 10's
native OpenAPI support.

**The API serves no interactive documentation page in any environment** — there is no browsable API
explorer of any kind. In Production it exposes no API description at all.

---

## 19. Configured but Inert

An architect deserves to know what is present in configuration, schema or code but does nothing. Every
row below was verified in source. **None of these is a feature.**

### Settings with no reader

| Setting | Reality |
|---|---|
| `Session:LifetimeHours` | Nothing reads it. The real session lifetime is `Jwt:RefreshTokenLifetimeDays` |
| `Session:ExtendOnActivity` | Nothing reads it |
| `Session:ExtensionHours` | Nothing reads it |
| `Session:IdleTimeoutMinutes` | Nothing reads it. **Sessions do not idle out** |
| `SecretManagement:RequiredPermission` | Nothing reads it; the permission is a compile-time attribute |
| `Jwt:PublicKeyPem` | Nothing maps it into configuration; the public key is derived from the private key |
| `Services:AuthApi:HealthUrl` (gateway) | Zero readers in code |

### Columns that are written but never acted on

| Column | Reality |
|---|---|
| `Users.PasswordExpiresUtc` | Round-tripped by the repository; no code ever sets a new value. There is no password expiry |
| `Users.IsSystemUser` | **The column does not exist.** The flag is always `false` at runtime; guards that work check a well-known user id instead |
| `Applications.MaxConcurrentSessions` | Stored, validated, returned and sortable — **never enforced.** Only the global `Session:MaxConcurrentSessions` is applied |
| `Applications.SessionTimeoutMinutes` | Same shape; no sign-in or session path consumes it |
| `Applications.RequireEmailVerification` | Same shape; no authentication path consumes it |
| `AuditLogs.ActionType`, `.IsSuccess`, `.ErrorMessage`, `.CorrelationId` | **These columns do not exist.** The repository hardcodes `"System"`, `true` and `null` when hydrating, so **every audit row reports success** |

### Filters accepted but never applied

| Filter | Reality |
|---|---|
| Audit-log `actionType` | The parameter is accepted and never added to the query |
| Audit-log `isSuccess` | Same — and meaningless anyway, since the column does not exist |

### Rate limits that limit nothing

| Item | Reality |
|---|---|
| `ApiKeys.RateLimitPerMinute`, `.RateLimitPerDay` | Stored, validated, returned by the validation endpoint, sortable — **no limiter in this repository reads them.** Whether a downstream consumer enforces them is outside this tree |

### Dead SQL and dead schema flags

| Item | Reality |
|---|---|
| Seeds `02`, `03`, `04`, `05`, `06`, `08` | On disk, **never included** by the post-deployment script. `08_AdditionalPermissions.sql` is the one that would close the permission gap in section 5 |
| Four of the nine upgrade scripts | On disk, **never included** |
| Five of the nine stored procedures | Defined, called by nothing |
| `Column Encryption Setting=Enabled` in a deployed connection string | **Inert.** No column is declared `ENCRYPTED WITH`; protection is application-side |

### Events published to nobody

| Item | Reality |
|---|---|
| `WebhookKeyCreatedEvent`, `WebhookKeyRevokedEvent` | Published, **zero handlers.** Creating or revoking a webhook key writes no audit entry |
| Integration events generally | The only implementation of the publisher is a no-op. **Nothing leaves the process.** No message broker is involved anywhere |

### Known-broken integration surface

| Item | Reality |
|---|---|
| `Auth.Sdk` gateway header | Sent **twice**, so every SDK call through a token-validating gateway is rejected 403 |
| SDK calls to the `validate` endpoints | The SDK never attaches an `Authorization` header, but those endpoints require one |
| `webhookkeys:*` and `apikeys:validate` permissions | Cannot be granted — no SQL file creates them. Only the global `*` reaches them |
| Console `/organizations` route | Has no route-level permission guard, unlike `/users`, `/roles` and `/api-keys`. The page itself decides what to show |
| `pnpm gen:api` target | Points at the HTTP port `5100` while both applications default to the HTTPS port `5101`. Both work; the mismatch is real and confusing |
| GeoIP lookup | The dependency is present, `GeoIp:Enabled` is `false`, the database path is empty, and no database file ships |

---

*Document Version: 2.0*
*Last Updated: August 2026*
