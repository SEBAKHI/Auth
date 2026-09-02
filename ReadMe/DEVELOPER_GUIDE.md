# AuthSystem Developer Guide

A comprehensive guide for developers to set up, configure, and use the AuthSystem — a multi-tenant, enterprise-grade authentication and authorization platform.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Prerequisites](#2-prerequisites)
3. [Getting Started](#3-getting-started)
4. [Architecture Deep Dive](#4-architecture-deep-dive)
5. [API Reference](#5-api-reference)
6. [Common Workflows](#6-common-workflows)
7. [Database Schema Overview](#7-database-schema-overview)
8. [Security Best Practices](#8-security-best-practices)
9. [Testing](#9-testing)
10. [Troubleshooting](#10-troubleshooting)
11. [Permission Matrix](#11-permission-matrix)

[Appendix A. Configured but Not Working](#appendix-a-configured-but-not-working)

---

## 1. System Overview

### 1.1 What is AuthSystem

AuthSystem is a production-grade authentication and authorization platform built on .NET 10. It provides multi-application, multi-organization identity management with hierarchical permissions, role-based access control (RBAC), two-factor authentication, external provider login (Google), application programming interface (API) key management, session tracking, and comprehensive audit logging. It is designed as a centralized identity service that other applications integrate with over HTTP.

**The product is three running programs, not one.** There is a back-end API, and there are two web applications that people actually click on. Everything a human does in this system — an administrator creating a user, an end user turning on two-factor authentication — happens in one of those two web applications. They are single-page applications (SPAs: a web page that loads once and then updates itself in the browser instead of reloading), written in React and served by their own web server.

| | Console | Accounts |
|---|---|---|
| Who uses it | An administrator running the platform | An end user managing their own account |
| What it is for | Users, roles, permissions, applications, organizations, audit logs, notification templates, system settings | Sign-in, profile, password, two-factor, sessions, account deletion |
| Folder | `Auth_UI/apps/console` | `Auth_UI/apps/accounts` |
| Workspace name | `@authsystem/console` | `@authsystem/accounts` |
| Development address | `https://localhost:5173` | `https://localhost:5174` |

Both applications talk to the same back-end API, and neither can do anything the API does not already allow. Section [3.6b](#36b-install-and-run-the-two-web-applications) gets them running on your machine.

### 1.2 API Capabilities at a Glance

The back-end API exposes **199 endpoints across 25 route-bearing controllers**. An "endpoint" here means one HTTP action — one method in a controller marked with a verb attribute such as `[HttpGet]` or `[HttpPost]`. A "controller" is one C# class that groups related endpoints. There are 26 controller files in total; one of them, `Auth_API/Common/ApiController.cs`, is a shared base class with no endpoints of its own, which is why 25 rather than 26 carry routes.

| Feature area | Endpoints | What it covers |
|---|---|---|
| **Discovery** | 3 | OpenID Connect (OIDC) discovery document, JSON Web Key Set (JWKS), public signing key |
| **Authentication** | 27 | Login, register, external (Google/Apple) login, token refresh and revoke, password reset and change, email verification, sessions, and the authorization-code + PKCE endpoints |
| **Two-Factor Auth** | 4 | Time-based one-time password (TOTP) setup, enable, verify, disable |
| **Users** | 29 | Create, read, update, delete, role and permission assignment, lock/unlock, activate/deactivate, self-service profile, hard delete |
| **Roles** | 7 | Create, read, update, delete, plus the users and applications attached to a role |
| **Permissions** | 9 | Create, read, update, delete, plus the users holding a permission |
| **Applications** | 15 | Registering the applications that use this identity service, with their own roles, permissions and redirect URIs |
| **Organizations** | 23 | Multi-tenant create/read/update/delete, member management, invitations, application subscriptions, member roles and permissions, ownership transfer |
| **Invitations** | 3 | Read an invitation by its token, register through it, accept it |
| **API Keys** | 5 | Create, list, revoke, rotate with a grace period, validate |
| **Webhook Keys** | 5 | Create, list, revoke, rotate, validate |
| **Audit Logs** | 5 | Query, filter, user-scoped and entity-scoped reads, export |
| **Dashboard** | 6 | Aggregated counts and charts for the console home screen |
| **Notification Templates** | 14 | The email templates, their versions and their seven translations |
| **Notification Layouts** | 6 | The shared HTML shell that wraps every email |
| **Notification Outbox** | 3 | The queue of emails waiting to be sent, and its failures |
| **Notification Types** | 2 | The catalogue of events that can produce a notification |
| **Privacy Policy** | 8 | Policy versions, translations and publication |
| **Public Policy** | 3 | The publicly readable policy pages, no sign-in required |
| **Secrets (Admin)** | 13 | Secret status, key generation, key import (bring-your-own-key), step-up challenges, SMTP password and connection string updates |
| **System Settings** | 4 | Runtime settings stored in the database as overrides on the configuration files |
| **Platform Settings** | 2 | Platform-wide branding values |
| **Platform** | 1 | The public branding endpoint the sign-in screens read before anyone is signed in |
| **Images** | 1 | Upload for avatars and logos |
| **Internal — gateway settings** | 1 | The API Gateway pulls its rate limits from here; not routed through the gateway itself |

> See [Section 5 — API Reference](#5-api-reference) for full endpoint details with request/response examples.

### 1.3 Architecture Diagram

Read this top to bottom. A person opens one of the two web applications in a browser; that application calls the back-end API; the API is the only thing that talks to the database.

```text
         Administrator                       End user
               │                                 │
               ▼                                 ▼
┌────────────────────────────┐    ┌────────────────────────────┐
│ Console SPA                │    │ Accounts SPA               │
│ @authsystem/console        │    │ @authsystem/accounts       │
│ dev: https://localhost:5173│    │ dev: https://localhost:5174│
└──────────────┬─────────────┘    └──────────────┬─────────────┘
               └───────────────┬─────────────────┘
                               │  HTTPS + JSON, bearer token
                               ▼
                 ┌─────────────┴──────────────┐
                 │ API_Gateway (YARP proxy)   │
                 │ dev: https://localhost:7159│
                 │ 24 allow-listed routes     │
                 │ adds X-Gateway-Token       │
                 │ edge rate limiting         │
                 └─────────────┬──────────────┘
                               ▼
                 ┌─────────────┴──────────────┐
                 │ Auth_API (the REST API)    │
                 │ dev: https://localhost:5101│
                 │ 199 actions, 25 controllers│
                 │ JWT + permission checks    │
                 │ audit logging, email outbox│
                 └─────────────┬──────────────┘
                               ▼
                 ┌─────────────┴──────────────┐
                 │ SQL Server - 52 tables     │
                 │ dev catalog: Astoom_Auth   │
                 └────────────────────────────┘
```

Four things about that picture are worth stating plainly, because they change what you do on your own machine.

**YARP is the reverse proxy, and it is optional in development.** YARP stands for "Yet Another Reverse Proxy"; it is Microsoft's .NET reverse-proxy library. `API_Gateway` uses it to sit in front of the API, stamp an `X-Gateway-Token` header on every forwarded request, and apply rate limits at the edge. In development the API is configured to accept requests without that header, so you can skip the gateway entirely and point the web applications straight at the API — which is exactly what their committed development settings do.
*In code:* `Auth/Auth_API/appsettings.Development.json` sets `Gateway:ValidationEnabled` to `false`.

**The two web applications call the API directly in development.** Both are built against `https://localhost:5101`, the API's own HTTPS address, not the gateway's.
*In code:* `Auth_UI/apps/console/.env.development` and `Auth_UI/apps/accounts/.env.development`, key `VITE_API_BASE_URL`.

**Other applications integrate over the same HTTP API.** A third-party application sends its users to the accounts application to sign in, using the authorization-code flow with Proof Key for Code Exchange (PKCE), and receives a token back. It does not post passwords to the API itself.

**There is no message broker, container platform or external key vault in this picture.** The API writes to SQL Server and to the local filesystem, and sends email over SMTP. That is the whole of its outbound world.

### 1.4 Solution Structure

This is the real folder layout on disk, from the root of the cloned repository.

```text
AuthSystem/
├── Auth/                        the .NET back end
│   ├── Auth.Domain              entities, interfaces, enums, error definitions
│   ├── Auth.Application         CQRS commands/queries, DTOs, validators, configuration
│   ├── Auth.Infrastructure      Dapper repositories, JWT, Argon2id, secret storage,
│   │                            Google auth, TOTP, SMTP, image storage
│   ├── Auth_API                 the ASP.NET Core 10 REST API — 25 route-bearing
│   │                            controllers, 199 actions
│   ├── Auth.Shared              configuration contracts and secret-storage primitives
│   ├── Auth_Localization        resource files for 7 languages (en, ar, tr, fr, zh, ur, fa)
│   ├── API_Gateway              YARP reverse proxy: rate limiting, security headers
│   ├── Auth.Sdk                 an unfinished .NET client library — see the note below
│   ├── Auth_Setup               a 23-line console utility that prints an Argon2id
│   │                            password hash; it touches no database and no config
│   ├── Auth_API.Tests           xUnit, Moq, FluentAssertions
│   ├── Auth_DB                  SQL Server database project: 52 tables in 6 groups,
│   │                            plus 9 stored procedures (only 4 of which are called)
│   ├── Auth.sln                 the Visual Studio solution file
│   └── Directory.Build.props    turns nullable-reference warnings into build errors
├── Auth_UI/                     the two web applications (a pnpm workspace)
│   ├── apps/
│   │   ├── console              @authsystem/console — the administrator application
│   │   └── accounts             @authsystem/accounts — the end-user application
│   └── packages/
│       ├── account              screens shared by both applications
│       ├── api                  the typed client generated from the API's OpenAPI document
│       ├── auth                 sign-in pages, session handling, route guards
│       ├── i18n                 the 7 display languages
│       └── ui                   the shadcn/ui component library
├── ReadMe/                      the documentation you are reading
├── Plans/                       design notes
└── Tools/                       one standalone script, verify-system-settings.mjs
```

Ten of those `Auth/` entries are C# projects; `Auth_DB` is a SQL Server database project built by a different toolchain, which matters in [§3.1](#31-clone-and-build).

**If Visual Studio shows you a different tree, you are looking at solution folders.** `Auth/Auth.sln` groups the projects under a top-level `src` folder holding `Services`, `Database`, `Shared`, `Gateway`, `Setup` and `Tests`. (`Auth.Sdk` is grouped nowhere; it sits at the solution root.) Those names exist only inside the solution file. There is no `src` folder on disk, and `cd Auth/src/Services/Auth_API` will fail.

**A warning about `Auth.Sdk`.** It is a client-library skeleton, not a shipped package. Nothing in this repository references it, there is no NuGet packaging target for it, and it currently sends the `X-Gateway-Token` header twice — once when the HTTP client is registered and again when a request is built. A gateway that validates that header compares a single value, so a two-value header can never match and every such call is rejected with HTTP 403. Treat it as unfinished.
*In code:* `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs` and `Auth/Auth.Sdk/AuthSystemClient.cs`.

### 1.5 Technology Stack and Rationale

**Back end (`Auth/`).** Versions are exact: every `.csproj` in this repository pins a single version, so what you see here is what restores.

| Technology | Purpose | Why This Over Alternatives |
|---|---|---|
| **.NET 10** (`net10.0`) | Runtime & framework | Latest version, with OpenAPI document generation built into the framework rather than added by a third-party package |
| **Dapper 2.1.79** | Database access (micro-ORM) | Full SQL control, stored procedure support, superior performance vs Entity Framework for read-heavy auth workloads. It is the **only** data-access library here; every query is hand-written SQL |
| **Microsoft.Data.SqlClient 7.0.2** | SQL Server driver | The connection layer Dapper runs on |
| **MediatR 14.2.0** | CQRS pattern | Decoupled command/query handlers, pipeline behaviors for cross-cutting concerns, excellent testability |
| **ErrorOr 2.1.1** | Error handling | Discriminated union pattern avoids exception-driven flow control; cleaner than Result pattern libraries |
| **FluentValidation 12.1.1** | Input validation | Declarative validation rules separated from domain logic; richer than Data Annotations |
| **RS256 JWT** (`Microsoft.IdentityModel.Tokens` / `System.IdentityModel.Tokens.Jwt` 8.22.0) | Token signing | Asymmetric keys allow external services to validate tokens using the public key without sharing the private key (unlike HS256) |
| **Konscious.Security.Cryptography.Argon2 1.3.1** | Password hashing (Argon2id) | OWASP 2024 recommended; memory-hard algorithm resistant to GPU/ASIC attacks (superior to bcrypt/PBKDF2) |
| **Microsoft.AspNetCore.DataProtection 10.0.10** | Encrypting the secret store at rest | Ships with the framework; the key ring protects the secrets file and stored two-factor secrets |
| **Secret storage (PlainText / Certificate / Dpapi)** | Secret encryption at rest | Pluggable `StorageMode`: PlainText for a quick cross-platform start, Certificate for portable encryption that survives server moves (recommended for shared hosting), Dpapi for Windows machine-bound encryption — no external key vault required |
| **Yarp.ReverseProxy 2.3.0** | API Gateway | .NET-native reverse proxy; configured in `appsettings.json`; superior .NET integration vs NGINX/Ocelot |
| **Serilog.AspNetCore 10.0.0** | Structured logging | Console and rolling-file sinks, enrichers, structured output; industry standard for .NET |
| **SQL Server + SSDT** | Database | Enterprise-grade relational database; SSDT (SQL Server Data Tools) provides a version-controlled schema. The schema model targets SQL Server 2019 or later |
| **xUnit 2.9.3 + Moq 4.20.72 + FluentAssertions 8.10.0** | Testing | Most popular .NET test stack; FluentAssertions for readable assertions; Moq for lightweight mocking |
| **Otp.NET 1.4.1** | TOTP two-factor | Lightweight RFC 6238 implementation for time-based one-time passwords |
| **Google.Apis.Auth 1.75.0** | External authentication | Official Google library for ID token validation |
| **MailKit 4.17.0** | Sending email | SMTP delivery for verification, password-reset and notification email |
| **Fluid.Core 2.31.0** | Email templating | Renders the Liquid templates that live in the database, inside a sandbox so a template cannot reach application state |
| **SkiaSharp 4.151.0** | Image processing | Resizes and re-encodes uploaded avatars and logos |
| **MaxMind.GeoIP2 6.1.0** | Approximate sign-in location | Reads a local GeoLite2 city database. **Off by default and no database file ships** — see [§3.4](#34-configuration-reference) |
| **Asp.Versioning.Mvc 10.0.1** | Version management | URL-based versioning (`/api/v1/`) for clear API evolution without breaking existing consumers |
| **Microsoft.AspNetCore.OpenApi 10.0.10** | API description document | Produces `/openapi/v1.json`, **in the Development environment only** |

**Front end (`Auth_UI/`).** These are the versions declared in `package.json`. They are ranges, not pins: the repository fixes exact resolutions in `pnpm-lock.yaml` (lockfile version `9.0`) rather than in `package.json`.

| Technology | Declared | Purpose |
|---|---|---|
| **React** + **React DOM** | `^19.2.6` | The user-interface runtime for both applications |
| **Vite** | `^8` | Development server and production bundler |
| **TypeScript** | `~6` | Type checking across every app and package |
| **Tailwind CSS** + `@tailwindcss/vite` | `^4` | Styling |
| **shadcn/ui on the `radix-ui` base** | `radix-ui ^1.6.0` | The component library in `packages/ui`, using the `radix-luma` style |
| **lucide-react** | `^1.21.0` | The only icon library |
| **react-router-dom** | `^7.18.0` | Routing in both applications |
| **@tanstack/react-query** | `^5.101.0` | Caching and refetching of server data |
| **@tanstack/react-table** | `^8.21.3` | The shared sortable, filterable data table |
| **react-hook-form** + **zod** | `^7.80.0` / `^4.4.3` | Forms and their validation rules |
| **i18next** + **react-i18next** | `^26.3.1` / `^17.0.8` | The 7 display languages |
| **openapi-fetch** + **openapi-typescript** | `^0.17.0` / `^7.13.0` | The typed API client, generated from the API's own OpenAPI document |
| **sonner** | `^2.0.7` | Toast notifications |
| **recharts** | `3.8.0` (exact) | Dashboard charts in the console |
| **vitest** + Testing Library | `^4.1.9` | Unit tests |
| **@playwright/test** | `^1.61.0` | End-to-end browser tests |

**Not used here.** Each of the following is a reasonable guess about a system of this shape, and each is absent from this repository. If you read otherwise anywhere, that text is out of date.

- **Blazor** — the administrator interface is a React application. There is no `.razor` file in the repository.
- **Entity Framework Core** — not referenced by any project. Persistence is Dapper over hand-written SQL. (Entity Framework appears in this guide only as the comparison that explains why Dapper was chosen.)
- **Swagger / Swashbuckle** — no Swagger user interface exists in any environment. The OpenAPI document comes from .NET 10 itself and is served only in Development.
- **Redis** or any distributed cache — caching is in-process only.
- **Docker** and **Kubernetes** — there is no Dockerfile, compose file or manifest. Deployment is to IIS.
- **RabbitMQ** or any message broker — the integration-event publisher that exists is a no-op that sends nothing anywhere.
- **SignalR** — there is no real-time channel.
- **Azure Key Vault** — there are exactly three secret storage modes: `PlainText`, `Certificate`, `Dpapi`.
- **MongoDB** — SQL Server is the only datastore.
- **A continuous-integration pipeline** — `.github/workflows/` exists and is empty. Nothing builds, tests or deploys automatically.

---

## 2. Prerequisites

Install the five tools in §2.1 before you start, then the certificate in §2.2. Each row gives the command that tells you whether you already have it; run those from any directory.

### 2.1 Required

| Requirement | How to check you have it | Why you need it |
|---|---|---|
| **.NET 10 SDK** | `dotnet --version` prints a version starting with `10.` | Every C# project in `Auth/` targets `net10.0`. The repository has no `global.json`, so the highest installed SDK is used — having .NET 8 or 9 alongside it is fine. [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **SQL Server** | The `SQL Server (SQLEXPRESS01)` service appears in the Windows Services list. If you also have the `sqlcmd` tool, `sqlcmd -S localhost\SQLEXPRESS01 -Q "SELECT @@VERSION"` prints a version banner | The only datastore. Express or Developer edition is enough. The schema targets SQL Server 2019 or later. The instance name matters — see [§3.2](#32-database-setup) |
| **Visual Studio with SQL Server Data Tools (SSDT)**, or **MSBuild.exe with SSDT** | Open `Auth/Auth_DB/Auth_DB.sqlproj` in Visual Studio without an error dialog | The **only** way to build the database package. The `dotnet` command-line tool cannot build this project — see [§3.1](#31-clone-and-build). This is not optional; without it you cannot create the database |
| **Node.js** | `node --version` | Runs the build tooling for the two web applications |
| **pnpm** | `pnpm --version` | The package manager the `Auth_UI/` workspace is built around. Install it with `npm install -g pnpm` if `npm` is available, or follow [pnpm's installation page](https://pnpm.io/installation) |

**About the Node.js and pnpm versions.** This repository does not declare them. There is no `engines` field, no `packageManager` field, no `.nvmrc` file and no continuous-integration configuration to copy a version from. What does constrain you:

- **The floor its dependencies accept is Node `^20.19.0 || ^22.13.0 || >=24`.** In plain terms: Node 20.19 or later within the 20 line, 22.13 or later within the 22 line, or any version 24 and above. That range is declared by the installed packages themselves, not by this repository.
- **The lockfile is `lockfileVersion: '9.0'`.** Any pnpm release that reads a version 9.0 lockfile will install this workspace. `pnpm install` tells you if yours cannot.
*In code:* `Auth_UI/pnpm-lock.yaml`, first line.

### 2.2 Required before sign-in works in a browser

| Requirement | How to check you have it | Why you need it |
|---|---|---|
| **A development HTTPS certificate, exported as PEM** | The files named by `DEV_HTTPS_CERT` and `DEV_HTTPS_KEY` exist on disk | Both web applications must serve HTTPS, matching the API. If they serve plain HTTP, Chrome silently discards the identity-provider session cookie and sign-in loops forever with no error message anywhere. [§3.6b](#36b-install-and-run-the-two-web-applications) shows the one command that creates it |

You can install everything else and still be unable to sign in without this. It is listed separately for that reason, not because it is less important.

### 2.3 Required only for a specific secret storage mode

The system stores its cryptographic keys in one of three ways, chosen by `SecretManagement:StorageMode`. Which extra prerequisites you need depends entirely on which mode you run. [§3.3](#33-first-startup-and-secret-generation) explains the modes themselves.

| Mode | Extra prerequisite | Notes |
|---|---|---|
| **`PlainText`** | None | This is what the committed development configuration uses, and it runs on Windows, Linux and macOS alike. If you are following this guide on your own machine, this is your mode and you need nothing extra |
| **`Certificate`** | An X.509 certificate as a `.pfx` file, and its password | **Runs anywhere .NET 10 runs.** It is not Windows-only. This is the shipped default for real deployments |
| **`Dpapi`** | **Windows** | Data Protection API (DPAPI) is a Windows feature. Keys are bound to that machine and that account, so this mode does not survive a server move |

### 2.4 Optional

| Tool | How to check you have it | What it gives you |
|---|---|---|
| **SqlPackage** | `sqlpackage /version` | A command-line alternative to publishing the database from inside Visual Studio. It is a separate download and is frequently **not** already on your `PATH` |
| **Postman** | The application opens | A request collection ships at `Auth/Auth_API/Postman/AuthSystem.postman_collection.json` (path is from the repository root). It covers 100 of the 199 endpoints and its `baseUrl` variable is set to a port nothing in this repository listens on — change `baseUrl` to `https://localhost:5101` before your first request |

---

## 3. Getting Started

### 3.1 Clone and Build

**Step 1 — clone the repository.** Run this from whichever folder you keep your code in. Replace `<repository-url>` with the actual clone URL.

```bash
git clone <repository-url>
```

**You should see:** git print `Resolving deltas: 100%`, then return to the prompt. It creates a new folder whose name comes from the repository URL — this guide calls it `AuthSystem/`, but yours may differ.

**Step 2 — move into the back-end folder.** Run this from the folder git just created.

```bash
cd Auth
```

The rest of §3.1 runs from this `Auth/` folder. Every later step names its own directory, and they are not all the same one — §3.6 runs from `Auth/Auth_API/` and §3.6b runs from `Auth_UI/`.

**Step 3 — build the two programs you will run.**

```bash
dotnet build Auth_API/Auth_API.csproj
```

```bash
dotnet build API_Gateway/API_Gateway.csproj
```

**You should see:** for each, a line ending `-> ...\bin\Debug\net10.0\Auth_API.dll` (or `API_Gateway.dll`), then `Build succeeded.` with `0 Error(s)`.

**Do not run `dotnet build Auth.sln`. It fails, and it is not your fault.** The solution file includes the database project `Auth_DB`, which is an older-style SQL Server Data Tools (SSDT) project that only Visual Studio's MSBuild can build. The `dotnet` command-line tool cannot load its build targets, so the whole solution build is reported as failed even though every C# project inside it compiled successfully. This is the exact output:

```text
Auth_DB.sqlproj : warning NU1503: Skipping restore for project '...\Auth_DB.sqlproj'.
  The project file may be invalid or missing targets required for restore.
Auth_DB.sqlproj(56,3): error MSB4278: The imported file
  "$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets"
  does not exist and appears to be part of a Visual Studio component. This file may require
  MSBuild.exe in order to be imported successfully, and so may fail to build in the dotnet CLI.
Build FAILED.   1 Warning(s)   1 Error(s)
```

If you see that, nothing is broken. Build the two `.csproj` files above instead, and build the database project from Visual Studio as described in §3.2.
*In code:* the failing import is at `Auth/Auth_DB/Auth_DB.sqlproj:56`; `Auth/Auth.sln` puts that project in the default build set.

### 3.2 Database Setup

**There is exactly one supported path: build the database package and publish it.** The package is a `.dacpac` file — a Data-tier Application Package, which is a single file containing the whole schema. Publishing it creates the tables *and* runs the post-deployment script, and that script is what creates the roles, the permissions and the administrator account. A database with tables but no post-deployment run is a database nobody can sign in to.

There is no Entity Framework migration path here. `dotnet ef database update` does not apply to this repository.

**Step 1 — decide the instance and database name, or match the committed one.** The development configuration in git points at a specific SQL Server instance and a specific database:

```text
Data Source=localhost\SQLEXPRESS01;Initial Catalog=Astoom_Auth;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

That means a **named** instance called `SQLEXPRESS01` — not the default instance, and not `.\SQLEXPRESS` — holding a database literally named `Astoom_Auth`, reached with your Windows login. You have two choices, and you must make one of them now:

- **Match it.** Publish to an instance named `SQLEXPRESS01` and a database named `Astoom_Auth`. Nothing else to configure.
- **Override it.** Publish wherever you like, then create the file `Auth/Auth_API/appsettings.Development.local.json` and put your own connection string in it. That file is ignored by git, it layers on top of the committed development settings, and §3.5 explains the layering.

*In code:* `Auth/Auth_API/appsettings.Development.json`, key `ConnectionStrings:AuthDb`.

**Step 2 — open the solution in Visual Studio.** Double-click `Auth/Auth.sln`. You need the SQL Server Data Tools component installed; §2.1 covers that.

**Step 3 — build the database project.** In Solution Explorer, right-click the `Auth_DB` project and choose **Build**.

**You should see:** the Output window end with `Build succeeded`, and a new file at `Auth/Auth_DB/bin/Debug/Auth_DB.dacpac`. That `bin` folder is ignored by git, which is why a fresh clone has no `.dacpac` until you do this.

**Step 4 — create a publish profile.** Right-click `Auth_DB` and choose **Publish**. A dialog opens with an empty target.

**There is no publish profile in the repository to open.** All publish profiles (`*.publish.xml`, `*.pubxml`) are git-ignored, so they exist only on the machine that created them. Yours is a new one you are about to create, and it will not be committed either.

**Step 5 — set the target and publish.** In the publish dialog:

1. Click **Edit…** next to the target database connection.
2. Set the server name to `localhost\SQLEXPRESS01` (or your own instance from Step 1).
3. Choose **Windows Authentication**.
4. Set the database name to `Astoom_Auth` (or your own from Step 1). If it does not exist yet, type the name anyway — publishing creates it.
5. Click **Save Profile As…** so you do not have to retype this. Save it anywhere; git will ignore it.
6. Click **Publish**.

**You should see:** the Data Tools Operations window report `Publish` succeeded, with `Created admin user (password must be set via application)` among the messages printed by the post-deployment script.

**If you prefer the command line**, the equivalent is `SqlPackage /Action:Publish` pointed at the `.dacpac` from Step 3. SqlPackage is a separate download and is often not on your `PATH` — see §2.4.

#### What publishing actually creates

Knowing this saves you from hunting for seed data that was never meant to run.

**The schema: 52 tables in 6 groups.** Authentication 7, Core 11, Notifications 6, Organizations 7, Security 16, System 5. Also 9 stored procedures, of which only 4 are ever called by the application — the rest of the data access is inline SQL.

**The seed data that runs:**

| What | How many |
|---|---|
| Roles | 8 — `super-admin`, `admin`, `user-manager`, `auditor`, `user`, `org-owner`, `org-admin`, `org-member` |
| Permission rows | 45 |
| Users | 2 — the administrator you will sign in as, and an inactive `system` account that cannot sign in |
| Notification types | 16 |
| Notification templates | 15, each with 7 language translations (105 translation rows) |
| Notification layouts | 1 |
| External auth providers | the Google and Apple rows, with Apple disabled |
| Privacy-policy versions, content and permissions | seeded |

**A trap in the seed folder, which will confuse you if you assume otherwise.**

The `Auth/Auth_DB/dbo/Scripts/SeedData/` folder holds 16 files, but only 10 of them are pulled into the post-deployment script. Six are not, and they split into two very different cases. Files `02` through `06` are harmless: their content was copied directly into the body of the post-deployment script instead, so those roles, permissions and the admin user do get created, just not from those files. File **`08_AdditionalPermissions.sql` is the one that matters: its content exists nowhere else, and it never runs.** It holds 47 permission rows. Twenty-eight of them are permissions the API actively checks for and that no other file creates, so on a freshly published database those 28 permissions do not exist and no role can be granted them.

The practical consequence: **50 distinct permission codes are enforced by the API, and 34 of them have no row in a freshly published database.** Six of those 34 (`apikeys:validate` and the five `webhookkeys:*` codes) appear in no SQL file anywhere in the repository. The only account that reaches any of them is the seeded administrator, because `super-admin` holds the global `*` grant. Section [11](#11-permission-matrix) lists which is which.
*In code:* `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql` — its `:r` lines are the complete list of files that run.

### 3.3 First Startup and Secret Generation

You never run a key-generation command. The first time the API starts it mints the cryptographic material it needs and writes it somewhere it can read back on the next start. This section says what it mints, where that lands, and what the log looks like when it worked.

**Four secrets are generated, not three.**

| Secret | Purpose | Can it be rotated? |
|---|---|---|
| **RSA key pair** | Signs JSON Web Tokens (JWTs) with the RS256 algorithm | Yes. Rotating invalidates every access token already issued |
| **Refresh-token HMAC key** | Hashes refresh tokens before they are stored, so a database dump alone cannot replay them | Yes. Rotating logs everyone out |
| **Gateway token** | Proves to the API that a request came through the API Gateway. Written under two configuration keys — `Gateway:ExpectedToken` for the API and `Gateway:Token` for the gateway — holding the same value | Yes, but the API and the gateway must be updated together |
| **Account-deletion identifier HMAC key** | Hashes the email addresses of deleted accounts so they stay reserved without being stored in the clear | **No. This key is permanent.** Every existing reservation is tied to it; replacing it silently orphans all of them |

*In code:* `Auth/Auth.Shared/Configuration/PlainTextSecretInitializer.cs`, method `EnsureSecrets`.

#### The three storage modes

| Mode | Where keys live | Protected by | Use when |
|---|---|---|---|
| **`PlainText`** | Readable JSON in an `appsettings` file — see below | File permissions only | Local development, and any quick cross-platform start |
| **`Certificate`** | An encrypted `secrets.dpapi` file | An X.509 certificate you own, so the file survives a server move | Shared hosting; servers that may be rebuilt or migrated |
| **`Dpapi`** | An encrypted `secrets.dpapi` file | The Windows Data Protection API, bound to this machine and this account | A Windows box you fully control and will not move |

**Which mode is active is not obvious, so here are all three facts.** The C# class's own fallback, if the key is missing everywhere, is `PlainText`. The committed base file `Auth/Auth_API/appsettings.json` sets `Certificate` — that is the shipped default for a real deployment. The committed development file `Auth/Auth_API/appsettings.Development.json` explicitly sets `PlainText`, because no developer has the certificate and `Certificate` mode would abort startup. So on your own machine you are in `PlainText` mode, deliberately.

#### Where PlainText keys are actually written — read this before you change anything

**They go to `appsettings.{Environment}.local.json`, next to the running application.** In Development that is `Auth/Auth_API/appsettings.Development.local.json`. That file is git-ignored, and it layers on top of the committed development settings so the next start reads the keys back instead of generating new ones.

**Do not point `SecretManagement:PlainTextTargetFile` at `appsettings.Production.json`.** That was the original fixed default and it was removed because it broke twice. Two separate failures came from it: in Development, `appsettings.Production.json` is not part of the loaded configuration, so every run generated fresh keys and invalidated every token issued before the restart; and it quietly seeded the Production configuration file with plaintext development keys — which is the exact state the production start-up guard refuses to boot on. If plaintext key material is found in Production configuration, the API stops with:

```text
Refusing to start: plaintext secret(s) [Jwt:PrivateKeyPem, ...] were found in the Production
configuration. The JWT signing key and the refresh-token HMAC key must never be stored in
plaintext in appsettings.
```

*In code:* `Auth/Auth.Shared/Configuration/PlainTextSecretInitializer.cs`, method `ResolveTargetFile`; the guard is `Auth/Auth_API/Common/ProductionSecretGuard.cs`.

#### Where Certificate and Dpapi keys are written

- **The secrets file:** the path in `SecretManagement:SecretFilePath`. When that key is empty, the default is `%LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi`.
- **The Data Protection key ring** that encrypts that file: the path in `DataProtection:KeyPath`. When empty, the default is `%ProgramData%\AuthSystem\Keys`.

#### When generation fires — it differs by mode

**In `PlainText` mode, any missing secret is generated on any start** while `AutoGenerateKeys` is `true`. If all four are already present, nothing happens.

**In `Certificate` and `Dpapi` mode, generation happens on the first start only.** The condition is that the secrets file does not yet exist. Once it exists the keys are loaded and never regenerated — even if one of them reads empty. That restriction is deliberate, and the reasoning is worth understanding: a blanket top-up would re-mint whichever secret happened to read empty after a restored backup, a renamed property or a changed file path, invalidating every issued token or leaving the gateway holding a token the API no longer accepts. The code calls that "a total outage that repeats on every restart".

There is exactly one exception. The permanent account-deletion identifier key was added after that file format existed, so an existing secrets file missing only that key gets it topped up — logged at Warning level, and then checked against the live deletion registry as soon as the database is reachable, because minting a new one over existing reservations would orphan them.
*In code:* `Auth/Auth_API/Program.cs`, the block that begins `Auto-generate keys on FIRST startup ONLY`.

#### What a good first run prints

Three lines, in this order:

```text
[HH:mm:ss INF] Generated plain-text secrets: Jwt:PrivateKeyPem, Jwt:RefreshTokenHmacKeyPlain, Gateway:ExpectedToken, AccountDeletion:IdentifierHmacKeyPlain
[HH:mm:ss INF] JWT Public Key (for external validation):
-----BEGIN PUBLIC KEY----- ...
[HH:mm:ss INF] Starting Auth API...
```

**And what a bad one prints.** If the keys were generated but could not be saved to disk, you get this warning. The API will run, but every restart mints new keys and logs everyone out, so fix the file permissions before going further:

```text
[HH:mm:ss WRN] Generated secrets are active for this run but were NOT saved to disk. They will be
regenerated on restart (invalidating existing tokens) until this is fixed.
```

#### After the first run

Set `SecretManagement:AutoGenerateKeys` to `false` once the keys exist. From then on, a missing secret makes the application fail loudly at startup instead of silently minting a replacement — which would invalidate every issued token and log everyone out.

To rotate a key or supply your own, use the [Secrets Admin API](#512-secrets-admin). **The import endpoints work only in `Certificate` and `Dpapi` mode.** In `PlainText` mode they refuse with HTTP 409 and the error code `Secret.ImportNotSupportedInPlainText`.

> **Deploying to production?** Storage-mode setup, the API Gateway, BYOK / server migration, and the password pepper & breached-password check are covered end-to-end in **[PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md)**.

### 3.4 Configuration Reference

The base configuration file is `Auth/Auth_API/appsettings.json`. It is 291 lines and holds about two dozen top-level sections. **Every JSON block below is copied from that file as committed**, so what you see is what a fresh clone has — placeholders and all. The sections you will not touch on a first run are listed at the end of this section rather than expanded.

Two conventions in that file are worth knowing before you read it:

- **Keys beginning with an underscore are comments.** JSON has no comment syntax, so `"_comment"`, `"_jwt_comment"` and similar keys carry the explanation. Nothing reads them.
- **`{{DOUBLE_BRACE}}` values are placeholders you must replace.** They are not defaults. A real value for `{{JWT_ISSUER_URL}}` looks like `https://auth.example.com`.

Throughout this guide a setting is written with colons between its levels: `Jwt:Issuer` means the `Issuer` key inside the `Jwt` object.

#### ConnectionStrings

```json
{
  "ConnectionStrings": {
    "AuthDb": "ConnectionStrings__AuthDb"
  }
}
```

**That value is not a mistake and it is not a connection string.** It is the *name of an environment variable*, left there as a deliberate placeholder so that a server which forgot to supply a real connection string fails immediately instead of failing later. If you start the API with that value still in place, it refuses to boot:

```text
Refusing to start: the AuthDb connection string is still the placeholder
'ConnectionStrings__AuthDb' from appsettings.json
```

For development you do not need to touch this: `appsettings.Development.json` already overrides it with the local string shown in [§3.2](#32-database-setup).
*In code:* `Auth/Auth_API/Common/ConnectionStringGuard.cs`.

#### JWT Settings

```json
{
  "Jwt": {
    "Issuer": "{{JWT_ISSUER_URL}}",
    "Audience": "{{JWT_AUDIENCE_URL}}",
    "AccessTokenLifetimeMinutes": 15,
    "RefreshTokenLifetimeDays": 7,
    "KeyId": "auth-key-1",
    "RotateRefreshTokens": true,
    "ClockSkewSeconds": 60,
    "PrivateKeyPath": "",
    "PrivateKeyPem": "",
    "PrivateKeyEncrypted": "",
    "RefreshTokenEncryptedKey": ""
  }
}
```

| Field | Description |
|---|---|
| `Issuer` | The token's `iss` claim, identifying who issued it. Replace the placeholder with your own origin, for example `https://auth.example.com` |
| `Audience` | The token's `aud` claim, identifying who the token is for. Replace the placeholder the same way |
| `AccessTokenLifetimeMinutes` | How long an access token stays valid. The file sets 15 |
| `RefreshTokenLifetimeDays` | How long a refresh token stays valid. The file sets 7. **This is the value that actually determines how long a session lives** — see the `Session` block below |
| `KeyId` | The key identifier published in the JSON Web Key Set (JWKS) document |
| `RotateRefreshTokens` | When `true`, using a refresh token issues a new one and retires the old one |
| `ClockSkewSeconds` | How far apart two servers' clocks may be before a token is judged expired |
| `PrivateKeyPath` | A file to read the signing key from. Empty means the key comes from the secret store instead |
| `PrivateKeyPem` | The signing key inline, in plain text. Only ever populated by `PlainText` mode, and only in the git-ignored local file |
| `PrivateKeyEncrypted`, `RefreshTokenEncryptedKey` | Encrypted forms of the same material, used by `Certificate` and `Dpapi` mode |

**`Jwt:PublicKeyPem` is inert — do not set it.** Of the JWT keys, the secret layer maps only `Jwt:PrivateKeyPem` and `Jwt:RefreshTokenHmacKeyPlain` into configuration; `Jwt:PublicKeyPem` is not on its list, and no code anywhere reads that key. The public key is derived from the private key at runtime, so a value you place there is never read. ([4.6](#46-secret-management) lists every key the secret layer does fill.)
*In code:* `Auth/Auth.Shared/Configuration/DpapiSecretConfigurationProvider.cs`; derivation at `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs`.

#### Password Policy

```json
{
  "Password": {
    "MinimumLength": 8,
    "RequireUppercase": true,
    "RequireLowercase": true,
    "RequireDigit": true,
    "RequireSpecialCharacter": true,
    "HistoryCount": 3,
    "MaxFailedAttempts": 5,
    "LockoutDurationMinutes": 15,
    "Argon2MemorySize": 19456,
    "Argon2Iterations": 2,
    "Argon2Parallelism": 1,
    "SaltSize": 16,
    "HashSize": 32,
    "Pepper": { "Enabled": false },
    "BreachedPasswordCheck": {
      "Enabled": false,
      "Mode": "Enforce",
      "FailOpen": true,
      "RejectThreshold": 1,
      "TimeoutMs": 2000
    }
  }
}
```

| Field | Description |
|---|---|
| `MinimumLength` | Minimum password length. The file sets **8**, and so does the code's fallback if the key is missing entirely. The console accepts **6 to 128** — 6 is the floor the whole stack honours, not a recommendation. OWASP recommends 12 or more |
| `HistoryCount` | Number of previous passwords to prevent reuse |
| `MaxFailedAttempts` | Failed login attempts before lockout |
| `LockoutDurationMinutes` | Account lockout duration after max failed attempts |
| `Argon2MemorySize` | Memory cost in KB (19456 = ~19 MB, OWASP 2024 recommended) |
| `Argon2Iterations` | Time cost (number of iterations) |
| `Argon2Parallelism` | Thread count for hashing |
| `Pepper.Enabled` | Mix a server-side secret into every Argon2id hash (defense-in-depth against a DB-only breach). Key material is auto-provisioned into the active secret store; **back it up like the JWT keys — losing it permanently locks out all peppered users.** |
| `BreachedPasswordCheck` | Reject or warn on known-breached passwords via the keyless HIBP Pwned Passwords range API (k-anonymity). `Mode`: `Enforce` rejects, `Warn` allows but returns an `X-Password-Warning` header. `FailOpen` allows the change if HIBP is unreachable. |

> Both `Pepper` and `BreachedPasswordCheck` are **opt-in** (default `false`); Argon2id hashing itself is always on. See [PRODUCTION_DEPLOYMENT_GUIDE.md §F](PRODUCTION_DEPLOYMENT_GUIDE.md) for migration and rotation details.

**There is no password-expiry setting.** A `Password:ExpirationDays` key used to exist and was removed, because nothing ever computed a password's age. The `Users.PasswordExpiresUtc` database column still exists but is never given a value, so no password ever expires. If you need forced rotation, it is a feature to build, not a setting to turn on.
*In code:* the removal is recorded in a comment in `Auth/Auth.Application/Configuration/PasswordSettings.cs`.

#### Gateway Settings

```json
{
  "Gateway": {
    "TokenHeaderName": "X-Gateway-Token",
    "ValidationEnabled": true,
    "ExemptPaths": [
      "/.well-known/",
      "/health",
      "/ready",
      "/swagger",
      "/openapi",
      "/uploads/"
    ]
  }
}
```

| Field | Description |
|---|---|
| `ValidationEnabled` | When `true`, every request must arrive with the gateway header. The committed development file sets this to `false` so you can call the API without running the gateway |
| `TokenHeaderName` | The header name carrying the gateway token |
| `ExemptPaths` | Path prefixes that skip the check. Six entries, not five |

**Why `/uploads/` is exempt.** Uploaded avatars and logos are served as static files, and static-file serving is wired into the pipeline before the authorization stage. Without the exemption every image would be rejected.

**`/swagger` is vestigial — leave it, but do not go looking for it.** There is no Swagger user interface in this system, in any environment. The only API description document is at `/openapi/v1.json`, and that is served in the Development environment only.
*In code:* `Auth/Auth_API/Program.cs` wraps `app.MapOpenApi()` in a Development-only check.

#### Session Settings

```json
{
  "Session": {
    "LifetimeHours": 24,
    "ExtendOnActivity": true,
    "ExtensionHours": 1,
    "MaxConcurrentSessions": 0,
    "TerminateOldestOnMax": true,
    "IdleTimeoutMinutes": 30
  }
}
```

**Four of these six keys are read by nothing. Setting them changes nothing at all.** They are `LifetimeHours`, `ExtendOnActivity`, `ExtensionHours` and `IdleTimeoutMinutes`. The only places those names appear in the code are the property declarations themselves. A session's real lifetime is `Jwt:RefreshTokenLifetimeDays`, described above.

| Field | Does it do anything? | Description |
|---|---|---|
| `LifetimeHours` | **No** | Inert. Nothing reads it |
| `ExtendOnActivity` | **No** | Inert |
| `ExtensionHours` | **No** | Inert |
| `IdleTimeoutMinutes` | **No** | Inert |
| `MaxConcurrentSessions` | Yes | The maximum number of simultaneous sessions per user, counted across every application. **`0` means unlimited, and `0` is the shipped value** |
| `TerminateOldestOnMax` | Yes, when the cap is above 0 | `true` ends the user's least recently used sessions to make room. `false` **refuses the new sign-in instead**, returning the error `Session.MaxSessionsReached` |

*In code:* the enforcement is in `Auth/Auth.Application/Features/Authentication/Common/LoginResponseBuilder.cs`; the inert keys are declared in `Auth/Auth.Application/Configuration/SessionSettings.cs`.

#### Email Settings

```json
{
  "Email": {
    "SmtpHost": "{{SMTP_HOST}}",
    "SmtpPort": 587,
    "UseSsl": true,
    "Username": "{{SMTP_USERNAME}}",
    "Password": "Email__Password",
    "SenderEmail": "{{SENDER_EMAIL}}",
    "SenderName": "Auth System",
    "FrontendBaseUrl": "{{FRONTEND_BASE_URL}}",
    "OtpExpirationMinutes": 5,
    "ResetTokenExpirationMinutes": 30,
    "RateLimitWindowSeconds": 60,
    "MaxOtpRequestsPerWindow": 3,
    "Enabled": false
  }
}
```

| Field | Description |
|---|---|
| `SenderEmail`, `SenderName` | The From address and display name on every outgoing message |
| `Password` | Like the connection string, the committed value `Email__Password` is the *name of an environment variable*, not a password |
| `FrontendBaseUrl` | The origin that password-reset and verification links point at. **Required as an absolute URL whenever `Enabled` is `true`** |
| `OtpExpirationMinutes` | How long a one-time code stays valid. The file sets 5 |
| `ResetTokenExpirationMinutes` | How long a password-reset link stays valid. The file sets 30 |
| `MaxOtpRequestsPerWindow`, `RateLimitWindowSeconds` | How many one-time codes a single recipient may request, and over what period |
| `Enabled` | Master switch. `false` means no mail is sent at all |

**`FrontendBaseUrl` will stop your startup if you get it wrong, and that is intentional.** Turning email on with an empty or relative value fails configuration validation immediately:

```text
Email:FrontendBaseUrl must be an absolute URL when Email:Enabled is true.
```

The check exists because without it, every reset and verification link in every email would be silently relative — that is, dead — and nobody would find out until a user complained.

> **Note:** Keep the real SMTP password out of `appsettings.json`. Supply it through the `Email__Password` environment variable, or store it in the encrypted secret store (`Certificate` and `Dpapi` modes).

#### External Authentication

```json
{
  "ExternalAuth": {
    "Google": {
      "Enabled": true,
      "ClientId": "{{GOOGLE_CLIENT_ID}}"
    },
    "Apple": {
      "Enabled": false,
      "ServicesId": "{{APPLE_SERVICES_ID}}",
      "TeamId": "{{APPLE_TEAM_ID}}",
      "KeyId": "{{APPLE_KEY_ID}}"
    },
    "AvatarImport": {
      "Enabled": true,
      "TimeoutMs": 3000,
      "MaxBytes": 2097152
    }
  }
}
```

**Google.** The client ID is a public value, not a secret. The Google client *secret* is not needed here, because this system validates the ID token Google issues rather than performing its own token exchange.

**Apple.** Disabled in the committed file. Turning it on takes more than flipping `Enabled`: you need the Apple Developer prerequisites (a Services ID, a verified domain with return URLs, and a `.p8` signing key), the private key must be provisioned into the secret store as `AppleSigningKeyPem` rather than written here, **and** the seeded `apple` row in the `ExternalAuthProviders` database table must be switched to `IsEnabled = 1`. The `google` row is seeded enabled; the `apple` row is seeded disabled.

**AvatarImport.** On a user's first external sign-in, if they have no picture yet, their provider profile picture is copied into this system's own image storage. It is copied rather than linked because the content-security policy names only this origin for images, so a remote provider URL would fail to load and fall back to initials. Set `Enabled` to `false` on a server with no outbound HTTP access.

#### CORS

Cross-Origin Resource Sharing (CORS) is the browser rule that decides which web origins may call this API. Get it wrong and the two web applications cannot talk to the API at all.

```json
{
  "Cors": {
    "AllowedOrigins": [
      "{{FRONTEND_ORIGIN_1}}",
      "{{FRONTEND_ORIGIN_2}}"
    ],
    "AllowCredentials": true
  }
}
```

**Never set `AllowedOrigins` to `["*"]`, in any environment, including your own machine.** It looks like a convenient shortcut and it silently breaks sign-in. The reason is specific: when the list contains `*`, the code takes a different branch that calls `AllowAnyOrigin()` and **never calls `AllowCredentials()`**. Without credentials allowed, the browser refuses to store the identity-provider session cookie. Sign-in appears to succeed, nothing is kept to prove it, and the authorize endpoint bounces straight back to the login page — an endless loop with no error message anywhere. Outside Development the wildcard is worse still: that branch is Development-only, so in any other environment a wildcard list produces a deny-all policy and every browser call fails.

**What to do instead: list every origin explicitly, with its scheme and port.** The committed development file lists all four combinations of `http`/`https` and ports `5173`/`5174` for exactly this reason — see [§3.5](#35-development-overrides). For production, list your real front-end origins.

**And in production, the list may not be empty.** Starting outside Development with no origins configured aborts the boot:

```text
CORS AllowedOrigins must be explicitly configured in production.
Set Cors:AllowedOrigins in appsettings.json
```

*In code:* `Auth/Auth_API/Common/DynamicCorsPolicyProvider.cs` holds the wildcard branch; the startup check is in `Auth/Auth_API/Program.cs`.

#### Rate Limiting

```json
{
  "RateLimiting": {
    "LoginPermitLimit": 20,
    "LoginWindowSeconds": 60,
    "PasswordResetPermitLimit": 10,
    "PasswordResetWindowSeconds": 60
  }
}
```

**This section is flat — there is no `General` bucket and no nesting.** It holds one entry per *named* policy that endpoints deliberately opt into. There are exactly two such policies: `login` (20 requests per 60 seconds) and `password-reset` (10 per 60 seconds).

**The API has no global rate limit, by design.** No default bucket applies to the other 197 endpoints. A general policy that read `RateLimiting:PermitLimit`, `WindowSeconds` and `QueueLimit` used to exist and was deleted, because no endpoint ever opted into it. Those three keys no longer exist; if you find them referenced anywhere, that text is out of date. Broad throttling is the API Gateway's job, which is the next section.
*In code:* `Auth/Auth_API/Program.cs`, the rate-limiter registration block.

#### Gateway Rate Limiting

```json
{
  "GatewayRateLimiting": {
    "GlobalPermitLimit": 1000,
    "GlobalWindowSeconds": 60,
    "GlobalQueueLimit": 100,
    "AuthPermitLimit": 20,
    "AuthWindowSeconds": 60,
    "ApiPermitLimit": 100,
    "ApiWindowSeconds": 60,
    "AdminPermitLimit": 120,
    "AdminWindowSeconds": 60
  }
}
```

**These are the API Gateway's limits, living in the API's configuration file. That is not a mistake.** The gateway is a separate process and it cannot read the database, so it cannot pick up settings an administrator changed in the console. Instead it *pulls* these values from the API over an internal endpoint, `/api/v1/internal/gateway-settings`, which lets the console own them like any other setting.

The gateway applies four buckets: a global one across everything, then tighter ones for authentication routes, general API routes and administration routes.

**These values must stay identical to the `RateLimiting` section of `Auth/API_Gateway/appsettings.json`.** A test enforces it, so a change to one and not the other fails the build.
*In code:* `Auth/Auth_API.Tests/Configuration/GatewayRateLimitingParityTests.cs`.

#### Secret Management

```json
{
  "SecretManagement": {
    "StorageMode": "Certificate",
    "SecretFilePath": "",
    "PlainTextTargetFile": "",
    "AutoGenerateKeys": true,
    "EnableAdminApi": false,
    "RequiredPermission": "secrets.manage"
  }
}
```

**The shipped `StorageMode` is `Certificate`, not `PlainText`.** That is the value a real deployment needs. On your own machine `appsettings.Development.json` overrides it to `PlainText`, which is why a fresh clone starts without a certificate — see [§3.3](#33-first-startup-and-secret-generation).

| Field | Description |
|---|---|
| `StorageMode` | `PlainText`, `Certificate` or `Dpapi`. The file sets `Certificate`; the development file overrides it to `PlainText` — see [§3.3](#33-first-startup-and-secret-generation) |
| `SecretFilePath` | Where the encrypted secrets file lives, in `Certificate` and `Dpapi` mode. Empty means the default `%LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi` |
| `PlainTextTargetFile` | Which file `PlainText` mode writes generated keys into. **Empty means `appsettings.{Environment}.local.json`, which is what you want.** Read the warning in [§3.3](#33-first-startup-and-secret-generation) before you set this to anything |
| `AutoGenerateKeys` | Generate missing keys at startup. Turn this off once the keys exist, so a missing secret fails loudly instead of being replaced silently |
| `EnableAdminApi` | Enables the `/api/v1/admin/secrets` endpoints |
| `RequiredPermission` | **Inert. Changing it does nothing.** The permission `secrets.manage` is compiled into every secrets endpoint as an attribute, so it cannot be changed by configuration. The settings registry deliberately leaves this key out for that reason |

#### Data Protection

```json
{
  "DataProtection": {
    "KeyPath": "",
    "Certificate": {
      "PfxPath": "",
      "Password": "",
      "PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD",
      "Thumbprint": "",
      "AdditionalPfxPaths": []
    }
  }
}
```

| Field | Description |
|---|---|
| `KeyPath` | Where the Data Protection key ring is stored. Empty defaults to `%ProgramData%\AuthSystem\Keys`. On IIS or shared hosting the application-pool identity often cannot write there, so set it to a writable folder **outside the public web root**, and point the Auth API and the API Gateway at the **same** folder so both share one ring |
| `Certificate:PfxPath` | The certificate file, used only in `Certificate` storage mode |
| `Certificate:Password` | The `.pfx` password inline. Prefer the environment variable below |
| `Certificate:PasswordEnvironmentVariable` | The name of an environment variable holding the `.pfx` password. Defaults to `AUTH_DP_CERT_PASSWORD` |
| `Certificate:Thumbprint` | Load the certificate from the machine's certificate store by thumbprint instead of from a file |
| `Certificate:AdditionalPfxPaths` | Older certificates kept only for decrypting existing data during a rotation |

#### Serilog

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "Logs/auth-api-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
  }
}
```

**`path` is relative, and this guide cannot tell you where it lands on a server.** Log files are written relative to wherever the process is running from. On your own machine that is the project folder, so look in `Auth/Auth_API/Logs/`. On a server hosted by IIS it is the content root of the deployed application — to find it, look next to the deployed `Auth_API.dll`.

#### Sections not expanded here

The base file holds more than the blocks above. These do not need attention on a first run, and each is described where it is actually used:

`AllowedHosts`, `Logging`, `HealthChecks`, `ImageStorage`, `PrivacyPolicyPublication`, `Notifications`, `GeoIp`, `AccountDeletion`, `DataController`, `IdentityProvider`.

Two of them deserve a warning even so:

- **`GeoIp` does nothing as shipped.** `Enabled` is `false`, `DatabasePath` is empty, and no MaxMind `.mmdb` database file ships with this repository. The lookup fails open, so turning `Enabled` on without supplying a database file shows no locations at all rather than an error.
- **`IdentityProvider:PublicBaseUrl` must exactly match the origin the web applications were built against.** The authorize endpoint builds its return address from this value, and the web applications reject any other origin as a possible open-redirect attack. A mismatch drops the post-sign-in resume silently, with no error shown.

### 3.5 Development Overrides

Configuration is layered. Each layer overrides the one before it, and only for the keys it actually mentions:

**base `appsettings.json` → `appsettings.{Environment}.json` → `appsettings.{Environment}.local.json` → environment variables → database system settings.**

In Development that middle pair means `appsettings.Development.json`, which is committed to git and must stay free of secrets, then `appsettings.Development.local.json`, which is git-ignored and is where the API writes the keys it generates on first run. **If a setting is not behaving the way the committed file says it should, check the `.local.json` file first** — it beats every committed file.
*In code:* the local file's position is computed rather than appended, in `Auth/Auth.Shared/Configuration/LocalConfigurationExtensions.cs`, so an environment variable still beats a stale `.local.json` value.

**The database layer is last, so it beats an environment variable too.** The overrides an administrator saves in the console are added to the configuration after every file and after the environment-variable provider, which means a value edited in the console wins over the same key set in `web.config` or the shell. Only the keys the settings registry marks editable are loaded that way, and secret-owned keys are excluded from it entirely. If you need to get a bad database override out of the way, start the process with the environment variable `AUTH_DISABLE_DB_SETTINGS=true`, which skips the layer altogether.
*In code:* `Auth/Auth.Infrastructure/Configuration/DbSettingsConfigurationProvider.cs`; the layer is added in `Auth/Auth_API/Program.cs`.

Everything the committed `Auth/Auth_API/appsettings.Development.json` overrides, in the order it appears:

| Setting | Development value | Why |
|---|---|---|
| `Logging:LogLevel:Default` | `Debug` | More detail while you work |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Information` | — |
| `SecretManagement:StorageMode` | `PlainText` | **The single most important override.** No developer has the Data Protection certificate, so `Certificate` mode would abort startup |
| `ConnectionStrings:AuthDb` | `Data Source=localhost\SQLEXPRESS01;Initial Catalog=Astoom_Auth;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true` | A named instance and a specific catalog — see [§3.2](#32-database-setup) |
| `Jwt:Issuer` | `https://localhost:5101` | The API's own HTTPS address |
| `Jwt:Audience` | `https://localhost:5101` | The same address |
| `Gateway:ValidationEnabled` | `false` | So the API is reachable without running the gateway |
| `Email:SmtpHost` / `SmtpPort` | `localhost` / `1025` | Point these at a local mail catcher such as Papercut or MailHog |
| `Email:UseSsl` | `false` | A local catcher has no certificate |
| `Email:Username` | `""` | — |
| `Email:SenderEmail` | `dev@localhost` | — |
| `Email:FrontendBaseUrl` | `https://localhost:5174` | The accounts application, where reset links must land |
| `Email:Enabled` | `false` | No mail is sent until you turn this on |
| `ExternalAuth:Google:Enabled` | `true` | — |
| `ExternalAuth:Google:ClientId` | `your-google-client-id.apps.googleusercontent.com` | A placeholder. Put a real one in the `.local.json` file — it is not a secret, but it is machine-specific |
| `IdentityProvider:AccountsBaseUrl` | `https://localhost:5174` | Where the authorize endpoint sends people who are not signed in |
| `IdentityProvider:PublicBaseUrl` | `https://localhost:5101` | Must equal the origin the web applications are built against |
| `Cors:AllowedOrigins` | `["http://localhost:5173", "https://localhost:5173", "http://localhost:5174", "https://localhost:5174"]` | Four explicit origins, never a wildcard. Both schemes are listed on purpose, because the development servers fall back to plain HTTP when the certificate variables are unset |
| `Cors:AllowCredentials` | `true` | Required, or the browser discards the session cookie |
| `ImageStorage:PublicBaseUrl` | `https://localhost:5101/uploads/images` | Absolute and HTTPS on purpose: an HTTP image on an HTTPS page is mixed content, which Chrome upgrades to `https://localhost:5100`, where nothing listens — breaking every avatar and logo |

**There is no production configuration in this repository.** No `appsettings.Production.json` is committed, nor is any `web.config` for the .NET applications, nor any publish profile. A clean clone has none of them, and building a server's configuration from scratch is covered in **[PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md)**.

### 3.6 Running the API and Gateway

**Start the Auth API. Run this from `Auth/Auth_API/`.**

```bash
dotnet run --launch-profile https
```

**You should see:** the secret-generation lines from [§3.3](#33-first-startup-and-secret-generation), then

```text
Now listening on: https://localhost:5101
Now listening on: http://localhost:5100
Application started. Press Ctrl+C to shut down.
```

**The `--launch-profile https` part is not optional.** Without it the .NET command-line tool picks the first profile in the launch settings file, which is `http`, and the API listens on `http://localhost:5100` **only**. Both web applications are built against `https://localhost:5101`, and on plain HTTP the browser discards the identity-provider session cookie, so sign-in loops with no error. The committed development configuration says the same thing in a comment: run the API on the `https` launch profile.
*In code:* `Auth/Auth_API/Properties/launchSettings.json`.

Leave this terminal running. Open a new one for the next step.

**Starting the API Gateway is optional in development.** The committed development configuration turns gateway-token validation off, so the API answers requests that did not come through the gateway, and both web applications point straight at the API. Skip this unless you are specifically testing gateway behaviour.

If you do want to run it, there is a step you cannot skip. **The gateway needs the token the API generated, and nothing copies it for you.**

1. Open `Auth/Auth_API/appsettings.Development.local.json`, the file the API wrote on its first run.
2. Copy the value of `Gateway:ExpectedToken`.
3. Create `Auth/API_Gateway/appsettings.Development.local.json` and paste that value in as `Gateway:Token`:

   ```json
   {
     "Gateway": {
       "Token": "paste-the-value-from-the-API-here"
     }
   }
   ```

4. Start the gateway. **Run this from `Auth/API_Gateway/`.**

   ```bash
   dotnet run --launch-profile https
   ```

**You should see:** `Now listening on: https://localhost:7159` and `Now listening on: http://localhost:5034`.

**If you skip the copy step in Development, nothing stops you, and that is the trap.** The missing-secret guard is deliberately skipped when the environment is Development, so the gateway starts normally with an empty `Gateway:Token`. It then forwards every request **without** the `X-Gateway-Token` header, because the code only adds the header when the token is a non-empty string. Nothing in the log says the header is missing.

**That is harmless only while the API is not checking.** The moment you turn the API's own check on, every request the gateway forwards is rejected with HTTP 403 and the body `Direct API access is not allowed. Please use the API Gateway.` — from a gateway that looks perfectly healthy.
*In code:* the guard and its `!builder.Environment.IsDevelopment()` condition are in `Auth/API_Gateway/Program.cs`; the rejection is in `Auth/Auth_API/Common/Middleware/GatewayTokenValidationMiddleware.cs`.

**Outside Development the same missing token stops the gateway before it serves anything**, with a message beginning:

```text
Refusing to start: missing required secret(s) [...]. Ensure the encrypted secrets file
contains the GatewayToken.
```

To actually route traffic through the gateway you must also set `Gateway:ValidationEnabled` back to `true` on the API side, in `Auth/Auth_API/appsettings.Development.local.json`. Do that only after the token is in place, for the reason just given.

### 3.6b Install and Run the Two Web Applications

Everything so far produced a back-end API answering on a port. **Nobody signs in to a port.** This step gets the two web applications running, and it is where a first-time reader most often stops too early.

#### Step 1 — Install the workspace dependencies

`Auth_UI/` is a pnpm *workspace*: one dependency tree shared by both applications and the five packages they import. You install once, at the top, not per application.

**Run this from `Auth_UI/`.**

```bash
pnpm install
```

**You should see:** a progress summary ending with `Done in ...s`, and a new `node_modules` folder in `Auth_UI/` and in each app and package. Nothing is committed; `node_modules` is git-ignored.

#### Step 2 — Create the development HTTPS certificate

**Do this before you start either application.** Both must serve HTTPS, matching the API's `https://localhost:5101`. If they serve plain HTTP instead, Chrome treats `http://localhost` and `https://localhost` as different sites, refuses to store the identity-provider session cookie, and sign-in loops between the login page and the authorize endpoint **forever, with no error message in the browser, in the console or in the API log**. It is the single most confusing failure in this whole setup, which is why it gets its own step.

Export the certificate your .NET installation already trusts. **Run this from any directory**, in PowerShell:

```bash
dotnet dev-certs https --export-path "$env:USERPROFILE\.aspnet\https\localhost.pem" --format PEM --no-password
```

**You should see:** `The HTTPS developer certificate was generated successfully.` and two files created — `localhost.pem` and `localhost.key`, both under `%USERPROFILE%\.aspnet\https\`.

Now point each application at those two files. Create `.env.development.local` in **both** app folders — `Auth_UI/apps/console/` and `Auth_UI/apps/accounts/` — each containing the two absolute paths. Replace `<you>` with your Windows user name:

```ini
DEV_HTTPS_CERT=C:\Users\<you>\.aspnet\https\localhost.pem
DEV_HTTPS_KEY=C:\Users\<you>\.aspnet\https\localhost.key
```

Those files are git-ignored, so they stay on your machine.

**What happens if you skip this.** The development server still starts, but over plain HTTP, after printing this warning — which is easy to scroll past:

```text
[dev-https] DEV_HTTPS_CERT/DEV_HTTPS_KEY not set - serving http.
OAuth sign-in will loop between /login and /auth/authorize: Chrome
drops the IdP session cookie when the SPA and the API differ in scheme.
```

**And if you point them at a file that is not there**, you get an opaque Node.js error rather than a helpful "file not found". That is deliberate: the code reads the files itself so a wrong path fails honestly, instead of being swallowed and passed through as if it were a certificate.
*In code:* `Auth_UI/dev-https.ts`.

#### Step 3 — Start the console (the administrator application)

**Run this from `Auth_UI/`.**

```bash
pnpm dev
```

**You should see:** Vite print `Local: https://localhost:5173/`. Leave it running.

#### Step 4 — Start the accounts application (the end-user application)

**Run this from `Auth_UI/`, in another terminal.**

```bash
pnpm dev:accounts
```

**You should see:** Vite print `Local: https://localhost:5174/`. Leave it running too.

**Both ports are pinned, and a busy port is a hard failure, not a fallback.** Each application sets `strictPort`, so if 5173 or 5174 is already taken, Vite exits instead of quietly choosing another port. That is on purpose: the API's allowed-origins list names exactly those two ports, so an application that silently moved to 5175 would be rejected by the browser's cross-origin rules with a much more confusing error. If a start fails this way, free the port rather than changing it.

At this point four things are running: the API, the console, the accounts application, and SQL Server. The gateway is not, and does not need to be.

### 3.6c Sign In for the First Time

**Open `https://localhost:5173` in a browser.** That is the console. Your browser may warn about the certificate the first time; accept it, because it is the development certificate you exported in §3.6b.

**Give the seeded administrator a password first — it has none.** The seed creates `admin@company.com` with `PasswordHash` set to `NULL`, so no deployment of this system ships a password anyone could look up. Pick one and run:

```bash
dotnet run --project Auth/Auth_Setup -- "<the password you chose>"
```

Run the `UPDATE` it prints against your database. With no argument it prompts instead, which keeps the password out of your shell history.

**Then sign in:**

- **Email:** `admin@company.com`
- **Password:** the one you just set

Until you do this, sign-in is refused: `LoginCommandHandler` rejects a null hash before it reaches the password verifier, so the account exists and holds `super-admin` but cannot authenticate.

**Do not rely on the "must change password" flag to protect a shared password.** The seed still sets `MustChangePassword = 1`, and the console still redirects to `/force-password-change` on it, but that decision is made in the browser — the flag is carried in the sign-in response and no server path reads it. A password that is known to more than one person is live until someone changes it, which is why the seed no longer sets one at all.
*In code:* the seed is in `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql`; the null-hash refusal is `Auth/Auth.Application/Features/Authentication/Login/LoginCommandHandler.cs`; the redirect is `Auth_UI/packages/auth/src/pages/login.tsx`.

**You should see, after the password change:** the console dashboard, with navigation to the administration areas — `/users`, `/roles`, `/permissions`, `/applications`, `/organizations`, `/api-keys`, `/webhook-keys`, `/audit-logs`, `/notifications` and the `/admin/*` settings screens.
*In code:* the full route list is `Auth_UI/apps/console/src/routes.tsx`.

**This account is a super administrator, and on a freshly published database it is the only account that can do anything.** It holds the global `*` permission grant, which is the only grant that reaches the 34 enforced permission codes that have no seed row (see [§3.2](#32-database-setup)). A role you create and grant `users:read` to will not work, because there is no `users:read` row to grant.

**There is a second seeded account, and it cannot sign in.** The user `system` / `system@localhost` exists with an inactive status and a placeholder password hash that matches nothing. It exists so that database rows created by the system itself have an author to point at. Do not try to use it, and do not delete it.

**To see the end-user application, open `https://localhost:5174`** and sign in with the same account. It shows the same person a different set of screens: their own profile at `/profile` (which is where password, two-factor setup and active sessions live), the organizations they belong to at `/organizations`, and account deletion at `/delete-account`. It also owns the screens a person reaches without being signed in — `/register`, `/forgot-password`, `/reset-password`, `/verify-email`, `/accept-invitation` and `/account-recovery`. Nothing administrative appears there.
*In code:* `Auth_UI/apps/accounts/src/routes.tsx`.

### 3.7 Verifying the Setup

Run each of these from any directory, with the API running. `curl` ships with Windows 10 and later; if `curl` is not available, opening the same URL in a browser works just as well.

**If `curl` reports a certificate problem**, the development certificate is not trusted on your machine. Fix it once with `dotnet dev-certs https --trust` (run from any directory, and accept the Windows prompt), or add `-k` to each command below to skip the check for a one-off probe.

**Check 1 — is the process alive?**

```bash
curl https://localhost:5101/health
```

**A healthy response** is HTTP 200 with a body of this shape. `/health` deliberately checks nothing external, so it stays green during a database outage — that is what makes it safe as a restart probe:

```json
{"status":"Healthy","totalDurationMs":0.4,"checks":[{"name":"self","status":"Healthy","durationMs":0.1,"description":"Auth API process is running.","tags":["live"]}]}
```

**Check 2 — can it actually serve a sign-in?**

```bash
curl https://localhost:5101/ready
```

**A healthy response** is HTTP 200 listing two checks, `database` and `signing-key`. This is the check that proves your work in §3.2 and §3.3 landed: `database` opens a real connection with a 5-second limit, and `signing-key` asks the token service for its public key.

```json
{"status":"Healthy","totalDurationMs":38.2,"checks":[{"name":"database","status":"Healthy","durationMs":37.9,"tags":["ready"]},{"name":"signing-key","status":"Healthy","durationMs":0.3,"tags":["ready"]}]}
```

If `status` is `Degraded`, the database check failed — go back to §3.2. If it is `Unhealthy`, the signing key is missing or empty — go back to §3.3. Failure messages are hidden from this response by default, because `/health` and `/ready` are publicly reachable; the full error is always in the log file. Set `HealthChecks:ExposeErrorDetails` to `true` briefly if you need to see it here.

**Check 3 — is the OpenID Connect discovery document correct?**

```bash
curl https://localhost:5101/.well-known/openid-configuration
```

**A healthy response** is HTTP 200 with a JSON document whose `issuer` is `https://localhost:5101` and whose `authorization_endpoint` and `token_endpoint` are absolute URLs on that same origin, ending `/api/v1/auth/authorize` and `/api/v1/auth/token`.

Read those origins carefully, because they do not come from the address you called. `issuer` comes from `Jwt:Issuer`, and every endpoint URL is built from `IdentityProvider:PublicBaseUrl`. A probe sent to `http://localhost:5100` therefore comes back full of `https://localhost:5101` URLs and looks inconsistent when nothing is actually wrong.

**None of these three checks proves that a human can sign in.** They are server-side probes. The cookie problem described in §3.6b is invisible to `curl`, because `curl` does not enforce the browser's same-site cookie rules. **The only real verification is opening `https://localhost:5173` and signing in**, which is §3.6c.

---

## 4. Architecture Deep Dive

### 4.1 Clean Architecture Layers

```text
┌─────────────────────────────────────────┐
│              Auth_API (Outer)           │
│  Controllers, Middleware, Authorization  │
├─────────────────────────────────────────┤
│         Auth.Infrastructure             │
│  Repositories, JWT, Argon2, DPAPI, SMTP │
├─────────────────────────────────────────┤
│          Auth.Application               │
│  Commands, Queries, DTOs, Validators    │
├─────────────────────────────────────────┤
│            Auth.Domain (Core)           │
│  Entities, Interfaces, Errors, Enums    │
└─────────────────────────────────────────┘
```

**The dependency rule: arrows point inward only.** The innermost project knows nothing about the ones around it, and each outer ring may only reach in. Concretely, `Auth.Domain` references no other project in this repository — its only two packages are `ErrorOr` and `MediatR.Contracts`. `Auth.Application` references `Auth.Domain` and nothing else. `Auth.Infrastructure` references `Auth.Domain`, `Auth.Application`, `Auth.Shared` and `Auth_Localization`. Nothing at all references `Auth_API` except the test project.

*In code:* the `<ProjectReference>` lines in `Auth/Auth.Domain/Auth.Domain.csproj`, `Auth/Auth.Application/Auth.Application.csproj`, `Auth/Auth.Infrastructure/Auth.Infrastructure.csproj` and `Auth/Auth_API/Auth_API.csproj`.

**`Auth_API` names only two of those projects in its own project file: `Auth.Infrastructure` and `Auth_Localization`.** It still uses Domain and Application types everywhere, because a project reference is transitive — referencing Infrastructure pulls in everything Infrastructure references. That is why you will see `using Auth.Domain...;` in a file whose project never mentions `Auth.Domain`.

**Two more projects sit beside the four layers rather than inside them.** `Auth.Shared` holds the startup, secret-loading, data-protection and security-header code that both the API and the API Gateway need; it references no other project in the solution. `Auth_Localization` holds the embedded translation resources and the localization middleware, described in [4.11](#411-localization).

**There are no per-layer registration helpers.** If you have worked on a solution like this before, you will look for an `AddApplication()`, `AddInfrastructure()` or `AddPersistence()` extension method to add your new service to. **They do not exist here** — a search of the whole repository returns none. Every service, repository, options binding and hosted service is registered directly, inline, in one file.

*In code:* `Auth/Auth_API/Program.cs` — 1,075 lines of top-level statements.

### 4.2 CQRS with MediatR

Every API endpoint dispatches one message and waits for one answer. The pattern is called **CQRS**, short for Command Query Responsibility Segregation: a **command** changes something and a **query** reads something, and the two never share a handler. The library that carries the message from the controller to its handler is MediatR.

**Naming convention:**
- Command: `LoginCommand` → `LoginCommandHandler`
- Query: `GetUserByIdQuery` → `GetUserByIdQueryHandler`

**File organization — there is no `Commands/` or `Queries/` folder level.** The shape is `Features/{Area}/{UseCase}/` and the file name says which kind it is:

```text
Auth/Auth.Application/Features/
├── Authentication/
│   ├── Register/
│   │   ├── RegisterCommand.cs
│   │   ├── RegisterCommandHandler.cs
│   │   └── RegisterCommandValidator.cs
│   └── Login/
├── Users/
│   ├── CreateUser/
│   └── GetUsers/
│       ├── GetUsersQuery.cs
│       ├── GetUsersQueryHandler.cs
│       └── GetUsersQueryValidator.cs
└── ...
```

**There are 17 feature areas**, and these are their exact folder names: `AccountDeletion`, `ApiKeys`, `Applications`, `AuditLogs`, `Authentication`, `Dashboard`, `Discovery`, `Notifications`, `Organizations`, `Permissions`, `Platform`, `PrivacyPolicy`, `Roles`, `Secrets`, `SystemSettings`, `Users`, `WebhookKeys`.

**The controller side uses different names on purpose.** A controller for the `Users` feature area lives at `Auth/Auth_API/Modules/UserManagement/Controllers/UsersController.cs`. The `Management` suffix belongs to the API module, not to the application feature; do not go looking for a `UserManagement` folder under `Features/`.

**Counts, so you know the scale of what you are joining.** There are 190 handlers: 120 command handlers and 70 query handlers. Each one implements `IRequestHandler<TRequest, ErrorOr<TResponse>>` and receives its dependencies through its constructor.

**Controllers send messages through `ISender`, never `IMediator`.** 23 of the 25 route-bearing controllers inject `ISender`; the two that do not are `GatewayRuntimeSettingsController` and `ImagesController`, which do their work without a handler.

**Cross-cutting concerns go in a pipeline behavior, and there are exactly two.** A behavior wraps every message on its way to the handler, in registration order:

| Order | Behavior | What it does |
|---|---|---|
| 1 | `LoggingBehavior` | Logs the request at Information level, logs a Warning when the handler returns an error, and logs a second Warning when the handler took longer than 500 milliseconds |
| 2 | `ValidationBehavior` | Runs every `IValidator<TRequest>` registered for the message, in parallel. If any fails, the handler is never called and the failures come back as validation errors |

*In code:* `Auth/Auth.Application/Behaviors/LoggingBehavior.cs` and `Auth/Auth.Application/Behaviors/ValidationBehavior.cs`, registered at the `AddMediatR` call in `Auth/Auth_API/Program.cs`.

**There is no transaction, caching, authorization or performance behavior.** If you add a new cross-cutting concern, this is where it goes, and you must register it in `Program.cs` yourself. MediatR scans two assemblies for handlers — `Auth_API` and `Auth.Application` — so a handler placed anywhere else is never found.

### 4.3 Error Handling (ErrorOr Pattern)

Handlers return `ErrorOr<T>` instead of throwing exceptions. Controllers map results to HTTP responses:

```text
ErrorOr<T> Success  → 200/201 with response body
ErrorOr<T> Error    → Mapped to ProblemDetails (RFC 7807)
```

**Error-to-HTTP mapping:**

| Error Type | HTTP Status |
|---|---|
| `Error.Validation` | 400 Bad Request |
| `Error.NotFound` | 404 Not Found |
| `Error.Conflict` | 409 Conflict |
| `Error.Forbidden` | 403 Forbidden |
| `Error.Unauthorized` | 401 Unauthorized |
| Default | 500 Internal Server Error |

**ProblemDetails response format.** The body carries exactly four fields. There is no `type` field and no `correlationId` field — do not write a client that looks for them:

```json
{
  "status": 400,
  "title": "User.InvalidCredentials",
  "detail": "The provided credentials are invalid.",
  "instance": "/api/v1/auth/login"
}
```

**When a handler returns more than one error, a fifth field appears.** Only then. The first error still decides the status code, the title and the detail; the rest are listed under `errors`:

```json
{
  "status": 400,
  "title": "Email",
  "detail": "Email must be a valid email address.",
  "instance": "/api/v1/users",
  "errors": [
    { "code": "Email", "description": "Email must be a valid email address." },
    { "code": "Password", "description": "Password is required." }
  ]
}
```

**For a field-validation failure the code is the name of the field**, because the validation pipeline builds each error with the property name as its code. For a business-rule failure the code is the domain error's own identifier, such as `User.InvalidCredentials`.

**`title` is never translated; `detail` is.** The title is the raw error code, which is a stable identifier your client can branch on. The detail is resolved for the caller's language in three steps, stopping at the first hit:

1. Look up the **error code** in `DomainErrors.resx` — for example the key `User.InvalidCredentials`.
2. Failing that, look up the **error description** in `ValidationMessages.resx`. This is why validators are written to emit a resource key such as `Validation.Email.InvalidFormat` as their message rather than English prose: the key is the lookup.
3. Failing that, use the raw English description the handler produced.

*In code:* `Auth/Auth_API/Common/ApiController.cs`, methods `Problem` and `LocalizeError`. How the caller's language is chosen is [4.11](#411-localization).

### 4.4 Permission-Based Authorization

Authorization here is not ASP.NET Identity roles. Every protected action names one **permission code**, and the caller's access token must carry a claim that satisfies it.

**How a single check runs, end to end:**

1. A controller action carries `[RequirePermission("users:read")]`.
2. `PermissionPolicyProvider` turns that string into an authorization policy on demand — there is no policy list to register.
3. `PermissionRequirementHandler` reads the caller's `permissions` claims out of the validated token and compares each one against the required code.
4. If none matches, the handler logs a warning naming the required permission and the claims the caller actually held, and ASP.NET Core returns **403 Forbidden**.

*In code:* `Auth/Auth_API/Authorization/RequirePermissionAttribute.cs`, `PermissionPolicyProvider.cs`, `PermissionRequirementHandler.cs`.

**Permission code format:** `{resource}:{action}`, or `{app}:{resource}:{action}` for a code scoped to one application. Real examples from this system: `users:read`, `roles:create`, `org:members:manage`.

#### The matching rule, stated exactly

Matching is a **string prefix** test, not a walk over a tree. Three arms are tried against each claim the caller holds, in this order: the claim is the single character `*`; the claim equals the required code, ignoring case; or the claim ends in the literal `:*` and the required code starts with everything before that `:*` plus a colon.

*In code:* `Auth/Auth_API/Authorization/PermissionRequirementHandler.cs`, method `PermissionMatches`.

**What it matches:**

- `*` satisfies every requirement in the system, with no exceptions.
- `users:*` satisfies `users:read`, `users:manage-roles`, `users:manage-permissions` — and any deeper code such as `users:sessions:revoke`. **One `:*` covers the whole subtree, at every depth**, not one level.
- `users:*` also satisfies the bare code `users`, if such a code ever existed.
- Case does not matter for the second and third arms: a claim spelled `Users:Read` satisfies `users:read`.

**What it does not match. Each of these is a real and expensive misunderstanding:**

- **A grant of `auth:users:*` does *not* satisfy a requirement of `users:read`.** The required string `users:read` does not start with `auth:`, so the test fails. This is not theoretical — the seeded `admin`, `user-manager` and `auditor` roles hold exactly these `auth:`-prefixed codes, and consequently authorize nothing at all. See [Section 11](#11-permission-matrix).
- **A parent code without `:*` grants nothing below it.** Holding `users` does not satisfy `users:read`. Holding `notification-templates:manage` does not satisfy `notification-templates:read`; the read endpoints will return 403 to a manage-only holder.
- **An asterisk inside a code is not a pattern.** `user*` matches nothing. `users:*:read` matches nothing. Only a trailing `:*`, as the final two characters, is special.
- **The global wildcard must stand alone.** It is compared with an exact equality test, so `**`, `" *"` and `"* "` grant nothing.
- **A partial segment is not a prefix.** A claim of `user:*` (prefix `user`) does not satisfy `users:read`, because `users:read` does not start with `user:`.

**`secrets.manage` uses a dot, not a colon — so no wildcard can ever reach it except the global `*`.** Not `secrets:*`, not `auth:*`. The prefix arm needs a colon to work with, and this code has none. All 13 endpoints under `/api/v1/admin/Secrets` are therefore reachable only by a holder of the literal code `secrets.manage` or of `*`, permanently, unless the code itself is changed.

#### Permission implications do not grant anything

The database has a `PermissionImplications` table, and the seed scripts fill it with 19 rows. The `Permissions` table also has `ParentId`, `Level` and `IsWildcard` columns, and they are populated too.

**None of it affects authorization.** The permission list baked into a token is a flat union of the codes granted through the user's roles and the codes granted to the user directly — no recursive walk, no join to `PermissionImplications`. **A role holding `users:manage` therefore does not thereby satisfy `users:read`**, no matter what implication row says otherwise. Those rows and columns are display metadata for the admin console.

*In code:* `Auth/Auth.Infrastructure/Persistence/PermissionRepository.cs`, the query that builds the permission list.

#### Organization-scoped permissions

A second kind of claim exists for permissions that only apply inside one organization. It is called `org_perm`, and there is one claim per organization-and-permission pair. The value is the organization's identifier, a colon, then the code — for example `3f2a...:org:members:manage`.

*In code:* the constant is `JwtClaimNames.OrgPermissions`; the claims are emitted in `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs`.

**The org-scoped branch runs only when all three of these are true:** the required permission starts with `org:`, the request is an HTTP request, and an organization identifier can be read from the route. The route value `orgId` is tried first and the route value `id` second, which is why both `/organizations/{id}` and `/organizations/{orgId}/members` work.

**Platform claims are checked first**, so a holder of `*` passes every organization endpoint without being a member of anything.

**There is a live-membership fallback, and its trigger is narrower than it looks.** If the token carries **no** `org_perm` claim for that organization at all, the handler reads the user's membership straight from the database and matches against that instead. It exists so that a user who creates an organization during the current session is not locked out of it until their next token refresh.

Two consequences follow, and both matter in production:

- **It costs one database query per request** from any token with no claim for the organization in the route — including every request from a stranger probing an organization identifier. There is no caching of the negative answer.
- **A role downgrade is not enforced until the token expires.** The fallback fires only on *zero* claims, so a member whose token still carries `org:members:manage` keeps that power until the token is refreshed, even after an administrator takes the role away. The same is true in reverse for an upgrade inside an organization the user was already a member of.

*In code:* `Auth/Auth_API/Authorization/PermissionRequirementHandler.cs`, method `HandleRequirementAsync`.

> **Before you plan a role model, read [Section 11](#11-permission-matrix).** On a freshly published database, 34 of the 50 permission codes this API enforces have no row in the `Permissions` table and cannot be granted to anyone. The rule above is correct; the catalogue you have to work with is much smaller than the list of codes.

### 4.5 Middleware Pipeline

Every HTTP request passes through a fixed chain of steps before it reaches a controller. The order is not cosmetic — several of these stages only work because of what runs before them.

**This is the real order, as registered:**

```text
Request
  │
  ▼
 1. UseHsts                        — outside Development only. Sends the HTTP Strict Transport
  │                                  Security (HSTS) header telling browsers to use https for
  │                                  365 days, including sub-domains
  ▼
 2. UseForwardedHeaders            — reads X-Forwarded-For and X-Forwarded-Proto so the request
  │                                  looks like it came from the real client, not the proxy
  ▼
 3. SecurityHeadersMiddleware      — adds the OWASP response headers, removes the Server header
  │
  ▼
 4. UseSerilogRequestLogging       — one structured log line per request
  │
  ▼
 5. UseAuthLocalization            — picks the response language for this request
  │
  ▼
 6. ExceptionHandlingMiddleware    — catches anything unhandled, returns ProblemDetails
  │
  ▼
 7. GatewayTokenValidationMiddleware — checks X-Gateway-Token; disabled in Development
  │
  ▼
 8. MapOpenApi                     — Development only. Serves /openapi/v1.json
  │
  ▼
 9. UseHttpsRedirection            — sends plain http requests to https
  │
  ▼
10. UseStaticFiles                 — serves uploaded images from ImageStorage:PhysicalPath
  │
  ▼
11. UseCors                        — applies the Cross-Origin Resource Sharing policy
  │
  ▼
12. UseRateLimiter                 — applies a rate-limit policy, but only to endpoints that ask
  │
  ▼
13. UseAuthentication              — validates the Bearer token, builds the ClaimsPrincipal
  │
  ▼
14. JwtBlacklistValidationMiddleware — rejects a token whose identifier was revoked at logout
  │
  ▼
15. UseAuthorization               — runs the permission checks from 4.4
  │
  ▼
/health, /ready, then the controller action
```

*In code:* `Auth/Auth_API/Program.cs`, everything after the line `var app = builder.Build();`.

**Three placements are load-bearing, and changing them breaks things quietly:**

- **Localization (5) runs before exception handling (6)**, so that an unhandled exception is reported in the caller's language. The code carries a comment saying exactly this.
- **The blacklist check (14) runs after authentication (13)**, not before. It needs the token to have been parsed and validated first; putting it earlier would leave it inspecting an unverified string.
- **Rate limiting (12) has no global bucket.** `UseRateLimiter` is in the chain, but the API defines only two named policies, `login` and `password-reset`, and sets no global limiter. An endpoint is limited only if it carries `[EnableRateLimiting(...)]`. This is deliberate — a general policy existed once, was read by no endpoint, and was removed. The gateway is where a blanket limit lives; see [4.8](#48-api-gateway-yarp).

### 4.6 Secret Management

The full explanation of what is generated, when, and where it lands is in [3.3 First Startup and Secret Generation](#33-first-startup-and-secret-generation). This section covers only what the running application does with those secrets.

**Secrets are loaded at startup and layered into `IConfiguration`.** After that, the rest of the code reads them exactly like any other setting — nothing calls a "get secret" service at request time. `SecretManagement:StorageMode` decides how they were protected at rest, and that is the only difference between the three modes:

| Mode | Where the values are written | Protected by | Survives a server move? |
|---|---|---|---|
| **`PlainText`** | `appsettings.{Environment}.local.json`, next to the running application, unless `SecretManagement:PlainTextTargetFile` names another file | File permissions only | Yes — it is a readable file you can copy. It is also readable by anyone who can read the folder |
| **`Certificate`** | The encrypted file at `SecretManagement:SecretFilePath`, default `%LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi` | An X.509 certificate you supply as a `.pfx` file | Yes — carry the certificate, the Data Protection key ring and the secrets file together |
| **`Dpapi`** | The same encrypted file | The Windows Data Protection API, bound to this machine and this account | No. The file is unreadable on any other machine |

**The class default is `PlainText`, the shipped `appsettings.json` sets `Certificate`, and `appsettings.Development.json` sets `PlainText` back.** All three statements are true at once; on your own machine you are in `PlainText` mode deliberately, because no developer has the certificate.

**Which configuration keys the secret file fills.** In `Certificate` and `Dpapi` mode the encrypted file is deserialized and each stored value is written to a configuration key:

| Stored secret | Configuration key it becomes |
|---|---|
| RSA private key | `Jwt:PrivateKeyPem` |
| Refresh-token HMAC key | `Jwt:RefreshTokenHmacKeyPlain` |
| Account-deletion identifier HMAC key | `AccountDeletion:IdentifierHmacKeyPlain` |
| Apple sign-in signing key | `ExternalAuth:Apple:PrivateKeyPem` |
| SMTP password | `Email:Password` |
| Gateway token | `Gateway:ExpectedToken` **and** `Gateway:Token` — one value, written under both names, because the API expects it and the gateway sends it |
| Database connection string | `ConnectionStrings:AuthDb` |
| Password pepper material | `Password:Pepper:Keys:{id}` and `Password:Pepper:CurrentKeyId` |
| Any custom secret | `Secrets:Custom:{key}` |

*In code:* `Auth/Auth.Shared/Configuration/DpapiSecretConfigurationProvider.cs`.

**`Jwt:PublicKeyPem` is not on that list, and setting it does nothing.** The public key is never stored. It is derived from the private key at startup, which is what the JSON Web Key Set (JWKS) endpoint publishes.

**The secret file outranks environment variables.** This surprises people deploying under Internet Information Services (IIS), where `ConnectionStrings__AuthDb` and `Email__Password` are usually set as environment variables in `web.config`: if the secrets file holds a connection string, the file wins. The escape hatch is the environment variable `AUTH_IGNORE_SECRET_CONNECTIONSTRING=true`, which makes the provider skip the stored connection string and let the environment value through.

### 4.7 JWT Token Lifecycle

A successful sign-in returns two very different things. The **access token** is a signed statement about who you are that any service can verify on its own; it is short-lived. The **refresh token** is an opaque random string that is worth nothing except at this API, and its only job is to obtain a new access token.

```text
Login
  │
  ├──▶ Access token — signed with RS256, lifetime from Jwt:AccessTokenLifetimeMinutes
  │                   (the shipped value is 15 minutes)
  │
  └──▶ Refresh token — 64 random bytes, base64. Lifetime from Jwt:RefreshTokenLifetimeDays
                       (the shipped value is 7 days). Only its HMAC-SHA256 hash is stored,
                       so a stolen database dump cannot be replayed

Refresh
  │
  ├──▶ A new access token
  └──▶ A new refresh token; the old one is revoked  (Jwt:RotateRefreshTokens, shipped true)

Logout
  │
  ├──▶ The access token's jti is added to the in-memory revocation list
  └──▶ The refresh token is revoked in the database
```

**`Jwt:RefreshTokenLifetimeDays` is what actually governs how long a session survives.** If you went looking for `Session:LifetimeHours`, stop: nothing in this system reads it. The same is true of `Session:ExtendOnActivity`, `Session:ExtensionHours` and `Session:IdleTimeoutMinutes`.

**Every claim the access token carries.** The first seven rows and the last row are always present. The rows between them appear only when the underlying value exists:

| Claim | Meaning |
|---|---|
| `sub` | The user's identifier |
| `email` | The user's email address |
| `name`, `given_name`, `family_name` | Display name, first name, last name |
| `jti` | A unique identifier for this token — this is what logout blacklists |
| `iat` | When the token was issued |
| `nbf` | The moment the token becomes usable. Set to the issue time, so it never delays anything on a clock that agrees |
| `exp` | When it expires |
| `sid` | The session identifier. **It stays the same across refreshes**, so it identifies the sign-in rather than the token |
| `locale`, `timezone`, `theme` | The user's stored display preferences, when set |
| `roles` | One claim per role code |
| `permissions` | One claim per platform permission code |
| `org_perm` | One claim per organization-and-permission pair — see [4.4](#44-permission-based-authorization) |
| `iss`, `aud` | Issuer, and audience. The audience is `Jwt:Audience` for a direct sign-in to the console or accounts application, and the requesting application's own audience for the authorization-code flow |

*In code:* `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs`, method `GenerateAccessToken`; the claim names are constants in `Auth/Auth.Domain/Constants/JwtClaimNames.cs`.

**The access-token lifetime is read fresh for every token issued**, so changing it in system settings applies to the next sign-in without a restart. The issuer, the audience and the signing key are captured once at startup, because issuing tokens with values the validator did not capture would reject every new token.

**External validation.** Any service can verify an access token without calling this API, using the public key:

- `GET /.well-known/openid-configuration` — the discovery document, which points at the two below
- `GET /.well-known/jwks.json` — the JSON Web Key Set (JWKS)
- `GET /.well-known/public-key.pem` — the same public key in PEM form

### 4.8 API Gateway (YARP)

The gateway is a separate program that sits in front of the API and forwards requests to it. It is a reverse proxy built on YARP, and it is optional in development.

| Feature | What it does |
|---|---|
| **Routing** | 24 routes, all forwarding to one cluster named `auth-cluster` |
| **Rate limiting** | Four policies — see below |
| **Header injection** | Adds `X-Gateway-Token`, `X-Forwarded-For`, `X-Forwarded-Host`, `X-Forwarded-Proto`, and an `X-Correlation-ID` it generates when the caller did not send one |
| **Health monitoring** | Probes the API every 30 seconds and takes a destination out of rotation after consecutive failures |
| **Security headers** | The same OWASP response headers as the API |

*In configuration:* `Auth/API_Gateway/appsettings.json`, section `ReverseProxy`.

#### Routing is an allowlist, and that is the usual cause of a 404

**A path that has no route entry is not forwarded — it is refused.** The gateway does not proxy everything under `/api`; each feature has its own route. If you add a controller with a new path prefix and call it through the gateway, you will get a 404 until you add a route for it.

**Route paths are written version-agnostically**, as `/api/v{version:int}/users/{**catch-all}`. A new API *version* therefore needs no gateway change. A new *feature prefix* does.

**A test enforces this so you cannot forget.** `Auth/Auth_API.Tests/Gateway/GatewayRouteCoverageTests.cs` compares the controllers against the gateway's route table and fails the build when a controller prefix has no route. One prefix is excluded on purpose: `api/v{version}/internal/gateway-settings`, because the gateway calls the API directly for it rather than proxying it.

#### The four rate-limit policies

| Policy | Applied to | Shipped seed value |
|---|---|---|
| Global | Every request through the gateway | 1000 per 60 seconds, with a queue of 100 |
| `auth` | The authentication routes | 20 per 60 seconds |
| `api` | The 19 general feature routes | 100 per 60 seconds |
| `admin` | The admin routes | 120 per 60 seconds |

Three routes carry no policy at all: the discovery document, the OpenAPI document, and the uploaded-image path.

**Those numbers are only a startup seed.** The gateway cannot read the database, so it pulls the live values from the API at `/api/v1/internal/gateway-settings` and replaces its own. The values it serves come from the `GatewayRateLimiting` section of the API's configuration, which an administrator edits in the console. The seeded numbers stay as the fallback for whenever the API is unreachable, and a test named `GatewayRateLimitingParityTests` fails if the two files disagree.

#### One setting here does nothing

**`Services:AuthApi:HealthUrl` has no reader.** It is set in `Auth/API_Gateway/appsettings.json` and nothing in the code looks at it. The probe target is `Services:AuthApi:ReadyUrl`; when that is empty the gateway derives it as `Services:AuthApi:BaseUrl` plus `/ready`, and failing that falls back to `http://localhost:5100/ready`.

### 4.9 Domain Events and Side Effects

When something happens that other parts of the system should react to — a user was created, a password changed, a session was force-ended — the code publishes an **event**. Anything that wants to react subscribes to it. This is how audit rows get written and how emails get sent, without the command handler knowing about either.

**There are 34 event types and 38 subscribers.**

*In code:* the events are records in `Auth/Auth.Domain/Events/`; every subscriber is a class implementing `INotificationHandler<TEvent>` under `Auth/Auth_API/Modules/`.

**Two ways an event gets published, and the less obvious one is the common one:**

- **The entity raises it.** A method on an aggregate such as `User` records the event on itself, and `MediatRDomainEventDispatcher` publishes the collected events afterwards. Only 13 places in the whole Domain layer do this, all inside `User` and `NotificationTemplate`.
- **The handler publishes it directly.** A command handler injects `IPublisher` and publishes the event itself. **27 handlers do this**, so it is the majority path. Three shared collaborators publish that way too — `LoginResponseBuilder`, `AccountDeletionRequestor` and `AccountDeletionRecoverer`. If you are looking for where an event comes from, search the feature folder before searching the entity.

**Where a subscriber lives tells you what it is for.** Every subscriber's class name ends in `EventHandler`. The ones that write audit rows all live together in `Auth/Auth_API/Modules/AuditLog/EventHandlers/`, and most of them carry an `Audit` infix — `UserCreatedAuditEventHandler`, for example — though a few do not, so use the folder rather than the name to decide. Subscribers that cause a side effect such as an email live in the owning module's own `EventHandlers/` folder instead.

**Two events have no subscriber at all.** `WebhookKeyCreatedEvent` and `WebhookKeyRevokedEvent` are published and nothing listens, so creating or revoking a webhook key writes **no audit row** — unlike an API key, which has one. If you need that trail, you have to write the handler.

**Integration events go nowhere.** There is an `IIntegrationEventPublisher` interface with three contracts defined against it, and its only implementation does nothing at all. Nothing leaves this process. Do not build on the assumption that another service will receive these.

*In code:* `Auth/Auth.Infrastructure/IntegrationEvents/NoOpIntegrationEventPublisher.cs`.

### 4.10 How a Notification Becomes an Email

This is the path a message takes from the moment something decides to notify someone, to the moment mail leaves the server. Follow it in order; each step hands off to the next.

**Step 1 — something asks for a notification.** Either a command handler sends it as part of its own work (registration asking for a verification email), or an `INotificationHandler<TEvent>` sends it as a side effect of an event (a new-device sign-in). Both build a `NotificationRequest` and call `INotificationService.SendAsync`.

**The request names a type code, a recipient and a bag of variables. It never names a template, a language or a layout** — those are resolved for it. Type codes are constants, such as `email-verification` and `password-reset`.

*In code:* `Auth/Auth.Application/Notifications/NotificationRequest.cs`; the codes are in `Auth/Auth.Domain/Constants/NotificationTypeCodes.cs`.

**Step 2 — the message is rendered first, always, before anything is queued.** If rendering fails, nothing is queued and nothing is sent; the error goes back to whoever asked. Rendering resolves four things in this order:

1. **The template.** If the request named an application, an application-specific template is looked for first; otherwise, or on a miss, the global template is used. The template must have a published version — a draft is never sent.
2. **The language.** The first of these that yields a supported language wins: an explicit language on the request; the recipient's stored `PreferredLanguage`, looked up by user identifier or, failing that, by email address; the language hint carried from the current request; the template's own default language; and finally English.
3. **The variables.** The renderer supplies platform and application values — sender name, current year, platform name, the email logo renditions, the application's name and base URL — and then merges the caller's own variables **last**, so a caller's value wins.
4. **The layout.** One shared wrapper supplies the visual chrome. Application-specific first, then global. It must be published too.

**The template language is Liquid, rendered by Fluid in a sandbox.** It cannot reach into .NET objects — every variable resolves from the supplied dictionary and nothing else — and it is capped at 5,000 execution steps, so a runaway loop is stopped rather than hanging the request. HTML bodies, the layout and the layout's text strings are HTML-encoded; the subject line and the plain-text body are not. A variable that does not exist renders as an empty string rather than raising an error.

*In code:* `Auth/Auth.Infrastructure/Notifications/NotificationRenderingService.cs` and `FluidTemplateRenderer.cs`.

**Step 3 — the rendered message becomes one row in the outbox.** With `Notifications:UseOutbox` at its shipped default of `true`, the finished subject and both body versions are written to the `NotificationOutbox` table with status Pending, a signal is raised to wake the dispatcher, and the caller is told the send succeeded. **Because the row stores the already-rendered text, editing a template later never changes mail that is already queued.**

With `Notifications:UseOutbox` set to `false` the message is instead sent inline, inside the original request, and a delivery failure becomes the caller's failure.

**Step 4 — the background dispatcher picks the row up.** `NotificationOutboxDispatcher` is a hosted background service. It wakes either on the enqueue signal or after `Notifications:PollIntervalSeconds` (30). On waking it first returns any row that has been stuck in Processing for longer than `Notifications:StaleClaimMinutes` (5) back to Pending, then claims up to `Notifications:BatchSize` (20) rows in a single atomic database statement — so two dispatchers can never deliver the same message twice — and keeps draining batches until one comes back empty.

**Step 5 — the email channel sends it.** The dispatcher looks up a channel by type. **Only the email channel is registered**; a message for any other channel fails with "No delivery channel registered". The email channel opens one SMTP connection per message with a 30-second timeout, authenticates when a username is configured, sends, and disconnects. Port 465 uses implicit TLS; any other port uses STARTTLS.

**Step 6 — the row is closed out.** On success the row is marked Sent. For the six notification types that carry a live secret — email verification, password reset, organization invitation, ownership-transfer code, account-deletion verification and secret-operation challenge — **both bodies are overwritten with `[redacted]` at that moment**, so the delivery log can never be used to re-read someone's one-time code.

#### What happens when SMTP is down

**The user's operation already succeeded.** With the outbox on, success was reported back at Step 3, before any connection was attempted. Nothing the user did fails.

**The row is retried on a widening schedule.** Each failure increments the attempt count, records the error text on the row, and sets the next attempt at 1, 4, 16, 64 and then 256 minutes. After `Notifications:MaxAttempts` (5) the row is marked **Dead** and is never claimed again. Each attempt logs a Warning; the dead-letter logs an Error.

**Where to look:** the row itself, with its `AttemptCount` and `LastError`, at `GET /api/v1/notification-outbox` — the console shows it under **Notifications → Delivery log**, where a Retry or Dead row can be requeued by hand.

**Four failure modes are worth knowing because they do not look like failures:**

- **`Email:Enabled` is `false`** — the shipped default. The channel logs what it would have sent and **reports success**, so the outbox row reads Sent and its body is redacted, even though no mail left the building. Whenever email is disabled, the one-time codes and password-reset links are additionally written to the log at Warning level — which is how you read them on a machine with no SMTP server.
- **A malformed recipient address** is caught by the same handler as a network failure, so it consumes the whole retry budget before being dead-lettered, rather than failing immediately.
- **A missing or unpublished template** fails at Step 2, before anything is queued, and the failed resolution is cached for up to 15 minutes. Publishing from the console clears that cache immediately; editing the database by hand does not.
- **A username with no password** is treated as a configuration fault, not a transient one. It is logged as an Error naming `Email:Password` and the send is not attempted.

**Two startup tasks run alongside the dispatcher, and neither can block startup.** One checks that every system notification type has a published global email template and logs an Error listing any that do not — this is your first warning that the seed is incomplete. The other rebuilds the email-safe logo images, because some mail clients cannot display the format the console stores.

> **`welcome-email` is a seeded notification type with no template at all** — the only one of the 16 without one. It can never send. It is not a system type, so the startup check will not flag it.

### 4.11 Localization

The API answers in the caller's language. Seven languages are supported, and the same seven are supported by the two web applications:

| Code | Language | Text direction |
|---|---|---|
| `en` | English (the default) | left to right |
| `ar` | Arabic | right to left |
| `tr` | Turkish | left to right |
| `fr` | French | left to right |
| `zh` | Chinese | left to right |
| `ur` | Urdu | right to left |
| `fa` | Persian | right to left |

*In code:* the list is `SupportedCultures` in `Auth/Auth_Localization/Extensions/LocalizationServiceExtensions.cs`.

**Four families of translated text exist, and each has all seven languages.** English is the neutral file with no language suffix; the other six ship as satellite resources beside it.

| Family | What it holds | How a key is named |
|---|---|---|
| `DomainErrors` | Every business-rule error message | The key **is** the error code, for example `User.InvalidCredentials` |
| `ValidationMessages` | Field-validation messages | `Validation.{Field}.{Rule}` |
| `MiddlewareMessages` | The messages the exception and gateway-token middleware produce | `Middleware.{Case}.{Title\|Detail}` |
| `AuthMessages` | Success messages that opt in to translation | Either a plain name or a dotted message code |

*In code:* `Auth/Auth_Localization/Resources/`.

**There is no fifth family for email content.** Email and notification bodies are not resource files at all — they live in the database and are edited in the console. That is [4.10](#410-how-a-notification-becomes-an-email).

**Success messages are translated only when the handler opts in** by returning a message code alongside its English text. **Exactly three exist in the entire codebase**: `ApiKey.Rotated`, `Invitation.AlreadyMember` and `Invitation.Joined`. Every other success message comes back in English.

#### How a client asks for a language

Four sources are consulted **in this order**, and the first one that yields a supported language wins:

1. The query string, `?culture=ar`
2. A culture cookie
3. The standard **`Accept-Language`** request header
4. The custom `X-Language` request header

**Use `Accept-Language`.** It is what this system's own two web applications send on every request.

**`X-Language` is last, which makes it a trap.** A client that sends `X-Language: ar` while its HTTP library also sends `Accept-Language: en` gets **English**, because the third source answered before the fourth was reached. If you use `X-Language`, you must make sure no `Accept-Language` header is present.

**An unsupported or malformed language never produces an error.** There is no 400 and no warning header. The value is ignored and the response comes back in English. A regional code falls back to its parent, so `ar-EG` resolves to `ar`.

*In code:* the provider list is at the end of `Auth/Auth_Localization/Extensions/LocalizationServiceExtensions.cs`.

**The user's stored `preferredLanguage` does not select the response language.** It becomes the `locale` claim in their token, and it decides which language their *notifications* are rendered in. The language of an API response is decided per request, by the four sources above, and by nothing else.

**Two tests fail the build if a translation file drifts.** `BaselineCoverageTests` compares all four families across all seven languages in both directions — a key present in one file and missing from another fails, as does a `{0}` placeholder that appears in the English text but not the translation. `DomainErrorResourceCoverageTests` fails when any error code has no entry in the neutral `DomainErrors` file; errors built inline inside a handler cannot be found by reflection, so those must be added by hand to that test's `HandlerInlineCodes` list.

*In code:* both are in `Auth/Auth_API.Tests/Localization/`.

### 4.12 The Two Web Applications

Everything above describes the back end. The interface a human being actually uses is two React applications in `Auth_UI/`, and neither of them is part of the .NET solution.

| | Console | Accounts |
|---|---|---|
| Workspace name | `@authsystem/console` | `@authsystem/accounts` |
| Folder | `Auth_UI/apps/console` | `Auth_UI/apps/accounts` |
| Who it is for | An administrator running the platform | An end user managing their own account |
| Development address | `https://localhost:5173` | `https://localhost:5174` |
| Talks to | The API at `https://localhost:5101` by default | The same API |

Setting them up and running them is [3.6b](#36b-install-and-run-the-two-web-applications).

**Both are single-page applications built by Vite, and both are https-only in development, deliberately.** The port is fixed with `strictPort: true`, so if the port is already taken the server **exits** rather than choosing another one — a fallback port would not be in the API's allowed-origins list.

#### The five shared packages

Both applications are workspaces in one pnpm workspace, and they share five packages. Nothing has a single barrel file; you import the exact module, for example `@authsystem/ui/button`.

| Package | What it holds |
|---|---|
| `@authsystem/api` | The typed API client, the token store, cross-tab session sync, error helpers, the device identifier header |
| `@authsystem/auth` | The session context (`AuthProvider`, `useAuth`), the route guards, and the shared sign-in pages: login, forgot password, reset password, forced password change, two-factor verification, email verification, invitation acceptance |
| `@authsystem/i18n` | The seven languages, the loader, and the text-direction provider |
| `@authsystem/ui` | The shadcn/ui component library, the shared data table, and the layout shell |
| `@authsystem/account` | The screens both applications mount: profile, sessions, security, organizations |

#### How a session is held in the browser

**The API returns tokens in the response body, not in a browser cookie, so the applications have to hold them.** They are held in two different places on purpose:

- **The access token lives in memory only.** It is never written to disk. It is broadcast to the other tabs of the same origin so that they adopt a refresh instead of each racing their own.
- **The refresh token is stored in `localStorage`**, so that reloading the page can silently re-establish the session.

**The refresh token is single-use.** The server rotates it on every use and treats a second presentation of the same value as theft, revoking every token the account holds. Because `localStorage` is shared by every tab, that makes it a shared single-use resource — which is why the client takes a cross-tab lock before refreshing and records the token it is about to spend, so a tab that dies mid-refresh can tell on the next load that it consumed a token without learning the outcome.

*In code:* `Auth_UI/packages/api/src/token-store.ts` and `tab-sync.ts`.

**One separate cookie does exist, and it is not the session.** Signing in also mints an HttpOnly identity-provider session cookie, which is what lets the authorization-code flow recognise an already-signed-in user. That cookie is why both development servers must run on https: a browser treats `http://localhost` and `https://localhost` as different sites and silently drops it, and the symptom is a sign-in that loops back to the login page with no error at all.

#### How the typed API client is generated

**The client is generated from the API's own OpenAPI document, not written by hand.** Run this from `Auth_UI/`, with the API already running:

```bash
pnpm gen:api
```

It overwrites `Auth_UI/packages/api/src/schema.d.ts`. Success looks like a changed file and no output; failure is a connection error, which means the API is not running.

**That script targets `http://localhost:5100/openapi/v1.json` — the plain-http port — while everything else in the applications defaults to `https://localhost:5101`.** Both work, because the API's `https` launch profile binds both ports. The mismatch is real, and it is confusing the first time you meet it.

**The OpenAPI document is served in Development only.** In any other environment there is no API document to generate from.

#### How route guards work

Three components do all the gating, and all three are render-time only:

| Component | Behaviour |
|---|---|
| `RequireAuth` | Shows a spinner while the session is being established; sends an unauthenticated visitor to `/login`, remembering where they were going |
| `RequireAnonymous` | The inverse — keeps an already-signed-in user off the login and registration pages, returning them to where they were headed |
| `PermissionRoute` | Redirects to `/403` when the user's token does not carry the required permission. Used only in the console |

There is also `RequirePermission`, which hides a button or menu item rather than redirecting.

**These guards are convenience, not security.** They run in the user's browser, on claims decoded from the user's own token. The API re-checks every permission on every request, as described in [4.4](#44-permission-based-authorization). A guard that is missing is a user-experience bug, not an open door.

**One console route has no permission guard: `/organizations`.** Unlike `/users`, `/roles` and `/api-keys`, it is reachable by any signed-in administrator. The page itself then chooses between the platform-wide list and the user's own memberships based on whether they hold `organizations:read`.

**The accounts application has no permission guards at all, and no `/403` route.** Every screen in it is about the signed-in user's own account, so authentication is the only gate it needs.

---

## 5. API Reference

Every endpoint below obeys the same handful of rules: which address you call, what a success body looks like, what an error body looks like, how enumerated values are spelled, and how lists are paged and sorted. Those rules are written out once here and are not repeated on each endpoint.

**Where the API listens in development.** There are four addresses. Use the first one unless you have a specific reason not to.

| Address | What it is | When to use it |
|---|---|---|
| `https://localhost:5101` | The API itself, over HTTPS | **The default choice.** Both web applications are built against this address, and a browser only keeps the sign-in cookie over HTTPS |
| `http://localhost:5100` | The API itself, over plain HTTP | A quick command-line probe only. The browser sign-in flow does not complete on it |
| `https://localhost:7159` | The API Gateway, over HTTPS | When you want the gateway's routing and rate limits in the path, the way production has them |
| `http://localhost:5034` | The API Gateway, over plain HTTP | The same, without transport security |

The API listens on `https://localhost:5101` **only** when you start it with the `https` launch profile. Started with no profile it listens on `http://localhost:5100` and nothing else. [Section 3.6](#36-running-the-api-and-gateway) gives the exact command and the line you should see in the console.
*In code:* `Auth/Auth_API/Properties/launchSettings.json`, `Auth/API_Gateway/Properties/launchSettings.json`.

**Production addresses are not in this repository.** Every committed configuration value for a public origin is a placeholder such as `{{JWT_ISSUER_URL}}`. Do not copy a hostname out of this guide into a production client.

**The version segment is part of the path, and `v1` is its only value today.** A client writes it literally: `/api/v1/auth/login`. The route template behind it is `api/v{version:apiVersion}`, and exactly one version — `1.0` — is declared anywhere in the code, so no other value matches. Two alternative ways of naming a version are also registered, the request header `X-Api-Version` and the query-string parameter `api-version`, but neither can replace the path segment: without the segment the route does not match at all, so the header and the query string are only ever redundant. Three groups of endpoints carry no version segment because they are fixed public addresses: the three discovery endpoints under `/.well-known/`, the three public policy pages under `/privacy`, and the two health endpoints `/health` and `/ready`.
*In code:* `Auth/Auth_API/Program.cs:699-714`.

**Route matching ignores letter case.** The route templates spell some segments with a capital letter, because they are generated from the C# class name — `UsersController` produces `/api/v1/Users`. ASP.NET Core matches routes case-insensitively, so `/api/v1/users` reaches the same action. The index in 5.0 prints the literal template casing so you can see what the code actually declares; the per-endpoint sections that follow use lowercase. Both work.

**Most endpoints need a bearer token.** Send it as the request header `Authorization: Bearer <access token>`, where the access token is the `token.accessToken` value that signing in returned. Endpoints marked *Anonymous* in the tables take no token. Endpoints marked with a permission code need a token whose permission claims satisfy that code — the matching rule is in [4.4](#44-permission-based-authorization), and the catalogue of codes is in [Section 11](#11-permission-matrix).
*One surprise worth knowing:* an access token is also accepted in the `access_token` query-string parameter, not only in the header (`Auth/Auth_API/Program.cs:745-754`).

**There is no response envelope.** A successful body is the object itself. There is no `success` flag, no `data` wrapper and no `message` field around it — if a description below says the response is a `UserDto`, then the whole body is that user object. Three serialization rules apply to every response: property names are camelCase; **any property whose value is null is omitted from the body entirely**, so a client must treat "absent" and "null" as the same thing; and every date-time value is written in Coordinated Universal Time (UTC) with a trailing `Z`, for example `2026-03-12T10:00:00Z`.
*In code:* `Auth/Auth_API/Program.cs:687-696`.

**A paged list is a small object whose item array is named after the entity, not `items`.** The user list calls its array `users`, the audit-log list calls its array `logs`, and so on. The remaining fields are the same everywhere:

```json
{
  "users": [],
  "totalCount": 42,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 3,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

`totalPages`, `hasPreviousPage` and `hasNextPage` are computed from the other three; the server sends them so the client does not have to.
*In code:* `Auth/Auth.Application/DTOs/UserDto.cs:65-74`.

**Errors come back as a ProblemDetails object with four fields**: `status`, `title`, `detail` and `instance`. `title` is the machine-readable error code, such as `User.InvalidCredentials`, not a sentence — branch your client on it. `detail` is the human sentence, translated into the caller's language. A fifth field, `errors`, appears only when a single request produced more than one error. There is no `type` field and no `correlationId` field on this path. [Section 4.3](#43-error-handling-erroror-pattern) explains the shape, the error-type-to-status mapping and how `detail` is translated, with examples.

**Two different bodies come back for HTTP 429 Too Many Requests, and which one you get depends on whether you went through the gateway.** They are not interchangeable, so a client that only handles one will mis-read the other. Calling the API directly returns a two-field body, where `retryAfter` is a number of seconds that may have a fractional part, and **no `Retry-After` header is set**:

```json
{
  "error": "Too many requests. Please try again later.",
  "retryAfter": 42.5
}
```

Calling through the API Gateway returns a five-field body, where `retryAfter` is a whole number of seconds, **and** the standard `Retry-After` header is set to the same value:

```json
{
  "type": "https://httpstatuses.com/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please try again later.",
  "retryAfter": 42
}
```

*In code:* `Auth/Auth_API/Program.cs:822-838` and `Auth/API_Gateway/Program.cs:256-273`.

**The API itself rate-limits only two things, and there is no general limit.** Two named policies exist, both counted per client IP address over a rolling window: `login` allows **20 requests per 60 seconds**, and `password-reset` allows **10 requests per 60 seconds**. Only the endpoints marked with a policy in the tables below are limited; every other endpoint on the API has no limit at all, deliberately. The gateway is where broad limits live, and it applies its own four policies to whole path prefixes — see [4.8](#48-api-gateway-yarp).
*In code:* `Auth/Auth_API/Program.cs:769-839`; the numbers come from `RateLimiting:LoginPermitLimit`, `RateLimiting:LoginWindowSeconds`, `RateLimiting:PasswordResetPermitLimit` and `RateLimiting:PasswordResetWindowSeconds` in `Auth/Auth_API/appsettings.json:241-247`.

**Enumerated values travel as their names, spelled exactly as the code declares them.** A user's status is the string `"Active"`, never the number `1`; a sort direction is `"Asc"` or `"Desc"`. Two enumerations override this and use lower-case names fixed by the specification they implement: a token type hint is `"access_token"` or `"refresh_token"`, and a device type is `"unknown"`, `"desktop"`, `"mobile"` or `"tablet"`.
*In code:* `Auth/Auth_API/Program.cs:691`; the two exceptions are `Auth/Auth.Domain/Enums/TokenTypeHint.cs` and `Auth/Auth.Domain/Enums/DeviceType.cs`.

**Every list endpoint takes the same paging and sorting parameters, and going outside their limits is an error, not a silent correction.** All four are optional.

| Parameter | Type | Default | Limit, and what happens outside it |
|---|---|---|---|
| `pageNumber` | integer | `1` | Must be 1 or more. `0` or a negative number returns **400 Bad Request** |
| `pageSize` | integer | `20` — **except the audit-log endpoints, where it is `50`** | Must be between 1 and 100 inclusive. `0`, `101` or `1000` returns **400 Bad Request**; the server does **not** quietly clamp it to 100 |
| `sortBy` | string | `null`, meaning the endpoint's own default order | Must be a field name on that endpoint's allow-list, compared case-insensitively. Anything else returns **400 Bad Request**; it is **not** ignored |
| `sortDirection` | string | `"Asc"` — **except the notification-outbox list, where it is `"Desc"`** | `"Asc"` or `"Desc"` |

**Each endpoint has its own allow-list of sortable fields**, kept in one file so the API and the console cannot drift apart. The names are camelCase, exactly as a client sends them. For the user list, for example, the allowed values are `name`, `displayName`, `firstName`, `lastName`, `email`, `status`, `emailConfirmed`, `phoneConfirmed`, `twoFactorEnabled`, `preferredLanguage`, `timeZone`, `createdAt`, `modifiedAt` and `lastLoginAt`. Some columns are deliberately absent: `phoneNumber` cannot be sorted on because the stored value is per-user encrypted text, and identifiers, JSON blobs and secret hashes are excluded on purpose.
*In code:* `Auth/Auth.Domain/Constants/SortFields.cs`; the bounds above are enforced in `Auth/Auth.Application/Validators/Rules/SharedValidationRules.cs:74-90` and `:160-168`.

**Two list endpoints use their own bounds instead of the shared ones.** Sign-in history (`GET /api/v1/Auth/login-history`) takes `take` rather than a page, between 1 and 100, default 20. The dashboard's credential-expiry endpoint takes `horizonDays` between 1 and 365, default 14, while every other dashboard endpoint takes `days` between 1 and 90, default 30.

**Status codes used across the API:**

| Code | Meaning |
|---|---|
| 200 | Success, with a body |
| 201 | Created, with a body |
| 202 | Accepted — the work was queued, not finished |
| 204 | Success, deliberately with no body |
| 302 | Redirect (only the browser-facing sign-in and policy endpoints) |
| 304 | Not Modified (only the public policy pages, in answer to `If-None-Match`) |
| 400 | The request was rejected: validation failure or a broken business rule |
| 401 | No token, an expired token, or a token that has been revoked |
| 403 | Authenticated, but the token lacks the required permission — also what a missing or wrong gateway token returns |
| 404 | No such record, or no such route |
| 409 | Conflict, for example a duplicate email or a stale `rowVersion` |
| 429 | Rate limited — see the two body shapes above |
| 500 | Unhandled server-side failure |

### 5.0 Endpoint Index

**This is the complete list: all 199 endpoints, in the 25 route-bearing controllers.** Nothing is left out, including the areas that do not get their own worked example further down. Paths are printed with the literal casing the route templates declare; route matching ignores case, so a lowercase path reaches the same action.

How to read the last column. **Anonymous** means no token is required. **Authenticated** means any valid access token will do and no permission is checked. A code such as `users:read` means the token's permission claims must satisfy that code. `login` and `password-reset` name the rate-limit policy that applies — 20 and 10 requests per 60 seconds respectively, counted per client IP address.

#### Discovery — 3 endpoints

These three carry no `/api/v1/` segment. They are the fixed addresses another system reads to learn how to verify this one's tokens.

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/.well-known/openid-configuration` | The OpenID Connect discovery document — the addresses of every endpoint a standards-based client needs | Anonymous |
| GET | `/.well-known/jwks.json` | The public signing keys as a JSON Web Key Set, so another service can verify a token offline | Anonymous |
| GET | `/.well-known/public-key.pem` | The same signing key as plain PEM text, for tools that cannot read a key set | Anonymous |

#### Authentication — 27 endpoints

| Method | Path | What it does | Auth |
|---|---|---|---|
| POST | `/api/v1/Auth/login` | Sign in with email and password; also mints the browser sign-in cookie | Anonymous · `login` |
| POST | `/api/v1/Auth/register` | Create an account for oneself and send the verification code | Anonymous · `login` |
| GET | `/api/v1/Auth/external-providers` | List the external sign-in buttons a login screen should draw | Anonymous |
| POST | `/api/v1/Auth/external-login` | Sign in with a Google or Apple identity token instead of a password | Anonymous · `login` |
| POST | `/api/v1/Auth/refresh` | Trade a refresh token for a new pair of tokens | Anonymous |
| GET | `/api/v1/Auth/authorize` | The OAuth 2.0 authorization endpoint. Redirects the browser back to the application with a one-time code, or to the accounts application's sign-in page | Anonymous |
| POST | `/api/v1/Auth/token` | The OAuth 2.0 token endpoint. Trades that one-time code for tokens. Takes form-encoded fields, not JSON | Anonymous · `login` |
| POST | `/api/v1/Auth/logout` | End this session, revoke its tokens and clear the sign-in cookie | Authenticated |
| POST | `/api/v1/Auth/change-password` | Change one's own password, knowing the current one | Authenticated |
| POST | `/api/v1/Auth/forgot-password` | Start a password reset; emails a reset link | Anonymous · `login` |
| POST | `/api/v1/Auth/reset-password` | Finish a password reset using the token from that link | Anonymous · `password-reset` |
| GET | `/api/v1/Auth/sessions` | List one's own signed-in sessions | Authenticated |
| DELETE | `/api/v1/Auth/sessions/{sessionId}` | End one of one's own sessions | Authenticated |
| DELETE | `/api/v1/Auth/sessions` | End all of one's own sessions except the current one | Authenticated |
| GET | `/api/v1/Auth/devices` | List the browsers this account has signed in from | Authenticated |
| DELETE | `/api/v1/Auth/devices/{deviceId}` | Forget a browser and end every session it still holds | Authenticated |
| GET | `/api/v1/Auth/login-history` | One's own recent sign-in attempts, successful and failed | Authenticated |
| GET | `/api/v1/Auth/me` | Echo the caller's own token claims. Reads no database row | Authenticated |
| POST | `/api/v1/Auth/revoke` | Revoke a token (RFC 7009). Anonymous by design: the token is the credential | Anonymous |
| POST | `/api/v1/Auth/introspect` | Ask whether a token is still valid and what it carries (RFC 7662) | Authenticated |
| POST | `/api/v1/Auth/send-verification-email` | Email a fresh verification code to the signed-in caller | Authenticated · `login` |
| POST | `/api/v1/Auth/verify-email` | Confirm an email address with the code. Two behaviours — see [5.2](#52-authentication) | Anonymous · `login` |
| POST | `/api/v1/Auth/resend-verification-email` | Re-send the verification code to an address, no sign-in needed | Anonymous · `login` |
| POST | `/api/v1/Auth/deletion/request` | Step 1 of deleting an account without signing in: email a code | Anonymous · `login` |
| POST | `/api/v1/Auth/deletion/confirm` | Step 2: prove the mailbox with that code and schedule the deletion | Anonymous · `login` |
| POST | `/api/v1/Auth/deletion/recover` | Cancel a scheduled deletion using the password, and sign in | Anonymous · `login` |
| POST | `/api/v1/Auth/deletion/recover-external` | The same, for an account that has no password and uses Google or Apple | Anonymous · `login` |

#### Two-Factor Authentication — 4 endpoints

| Method | Path | What it does | Auth |
|---|---|---|---|
| POST | `/api/v1/auth/2fa/setup` | Produce a secret and a QR-code address for an authenticator app | Authenticated |
| POST | `/api/v1/auth/2fa/enable` | Turn two-factor on after checking one code; returns the recovery codes | Authenticated |
| POST | `/api/v1/auth/2fa/verify` | Finish a sign-in that stopped for two-factor. **Anonymous**, because the sign-in has not happened yet | Anonymous · `login` |
| POST | `/api/v1/auth/2fa/disable` | Turn two-factor off after checking one code | Authenticated |

#### Users — 29 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/Users` | Paged list of users | `users:read`; adding `includeDeleted=true` also needs `users:manage` |
| GET | `/api/v1/Users/{id}` | One user | `users:read` |
| POST | `/api/v1/Users` | Create a user as an administrator | `users:create` |
| PUT | `/api/v1/Users/{id}` | Update a user's profile | `users:update` |
| DELETE | `/api/v1/Users/{id}` | Soft-delete: the row stays, marked deleted | `users:delete` |
| DELETE | `/api/v1/Users/{id}/permanent` | **Irreversible.** Purge an already soft-deleted account and everything attached to it | `users:manage` |
| POST | `/api/v1/Users/{id}/roles` | Give a user a role | `users:manage-roles` |
| GET | `/api/v1/Users/{id}/roles` | The roles a user holds | `users:read` |
| DELETE | `/api/v1/Users/{id}/roles/{roleId}` | Take a role away | `users:manage-roles` |
| GET | `/api/v1/Users/{id}/organizations` | The organizations a user belongs to | `users:read` |
| GET | `/api/v1/Users/{id}/applications` | The applications a user may reach | `users:read` |
| GET | `/api/v1/Users/{id}/permissions` | Permissions granted to the user directly | `users:read` |
| POST | `/api/v1/Users/{id}/permissions` | Grant one permission directly | `users:manage-permissions` |
| DELETE | `/api/v1/Users/{id}/permissions/{permissionId}` | Take that direct grant away | `users:manage-permissions` |
| POST | `/api/v1/Users/{id}/lock` | Lock an account, with a reason and an optional duration | `users:manage` |
| POST | `/api/v1/Users/{id}/unlock` | Unlock it | `users:manage` |
| POST | `/api/v1/Users/{id}/activate` | Move the account to Active | `users:manage` |
| POST | `/api/v1/Users/{id}/deactivate` | Move the account to Inactive | `users:manage` |
| GET | `/api/v1/Users/me` | The caller's own full profile, read from the database | Authenticated |
| PUT | `/api/v1/Users/me` | Update the caller's own profile | Authenticated |
| PUT | `/api/v1/Users/me/profile-image` | Set one's own picture from an already uploaded image key | Authenticated |
| DELETE | `/api/v1/Users/me/profile-image` | Remove one's own picture | Authenticated |
| GET | `/api/v1/Users/me/ui-preferences` | One's own display settings, as a key-to-value map | Authenticated |
| PUT | `/api/v1/Users/me/ui-preferences/{key}` | Store one display setting | Authenticated |
| DELETE | `/api/v1/Users/me/ui-preferences/{key}` | Remove one display setting | Authenticated |
| PUT | `/api/v1/Users/{id}/profile-image` | Set another user's picture | `users:update` |
| DELETE | `/api/v1/Users/{id}/profile-image` | Remove another user's picture | `users:update` |
| POST | `/api/v1/Users/me/deletion` | Ask to delete one's own account, proving the mailbox with an emailed code | Authenticated · `login` |
| POST | `/api/v1/Users/me/deletion/send-code` | Email that code | Authenticated · `login` |

#### Roles — 7 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/Roles` | List roles, optionally for one application | `roles:read` |
| GET | `/api/v1/Roles/{id}` | One role | `roles:read` |
| GET | `/api/v1/Roles/{id}/users` | Paged list of the users holding the role | `roles:read` |
| GET | `/api/v1/Roles/{id}/applications` | The applications the role relates to | `roles:read` |
| POST | `/api/v1/Roles` | Create a role | `roles:create` |
| PUT | `/api/v1/Roles/{id}` | Rename a role or change its description | `roles:update` |
| DELETE | `/api/v1/Roles/{id}` | Delete a role | `roles:delete` |

#### Permissions — 9 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/Permissions` | List permissions, optionally for one application | `permissions:read` |
| GET | `/api/v1/Permissions/{id}` | One permission | `permissions:read` |
| GET | `/api/v1/Permissions/{id}/users` | Paged list of the users granted it | `permissions:read` |
| POST | `/api/v1/Permissions` | Create a permission | `permissions:create` |
| PUT | `/api/v1/Permissions/{id}` | Rename it or change its description | `permissions:update` |
| DELETE | `/api/v1/Permissions/{id}` | Delete it | `permissions:delete` |
| GET | `/api/v1/Permissions/{id}/implications` | The permissions recorded as implied by this one | `permissions:read` |
| POST | `/api/v1/Permissions/{id}/implications` | Record an implication | `permissions:manage` |
| DELETE | `/api/v1/Permissions/{id}/implications/{impliedId}` | Remove one | `permissions:manage` |

Implications are descriptive only. They are stored and shown in the console, and they grant nothing at runtime — see [4.4](#44-permission-based-authorization).

#### Applications — 15 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/Applications` | Paged list of registered applications | `applications:read` |
| GET | `/api/v1/Applications/{clientId}/public-branding` | Name and logo for a sign-in screen, before anyone is signed in. **Anonymous** — the accounts application calls it | Anonymous |
| GET | `/api/v1/Applications/{id}` | One application | `applications:read` |
| GET | `/api/v1/Applications/{id}/roles` | Its roles | `applications:read` |
| GET | `/api/v1/Applications/{id}/permissions` | Its permissions | `applications:read` |
| GET | `/api/v1/Applications/{id}/users` | Paged list of its users | `applications:read` |
| GET | `/api/v1/Applications/{id}/organizations` | Paged list of organizations that have it enabled | `applications:read` |
| POST | `/api/v1/Applications` | Register an application, with its redirect URIs and access mode | `applications:create` |
| PUT | `/api/v1/Applications/{id}` | Update it | `applications:update` |
| DELETE | `/api/v1/Applications/{id}` | Delete it | `applications:delete` |
| POST | `/api/v1/Applications/{id}/activate` | Switch it on | `applications:update` |
| POST | `/api/v1/Applications/{id}/deactivate` | Switch it off and revoke its sessions and tokens | `applications:update` |
| GET | `/api/v1/Applications/{id}/access` | Its individual access list, used when access mode is Restricted | `applications:read` |
| POST | `/api/v1/Applications/{id}/access` | Add a user to that list | `applications:update` |
| DELETE | `/api/v1/Applications/{id}/access/{userId}` | Remove a user and revoke that application's tokens for them | `applications:update` |

#### Organizations — 23 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/Organizations` | The organizations the **caller** belongs to | Authenticated |
| GET | `/api/v1/Organizations/all` | Paged list of every organization on the platform | `organizations:read` |
| GET | `/api/v1/Organizations/{id}` | One organization. A caller holding `organizations:read` may read any; otherwise membership decides | Authenticated |
| POST | `/api/v1/Organizations` | Create one. The creator becomes its owner | Authenticated |
| PUT | `/api/v1/Organizations/{id}` | Rename it or change its details | `org:update` |
| DELETE | `/api/v1/Organizations/{id}` | Delete it. Ownership is checked inside the handler; a caller holding `organizations:manage` may delete any | Authenticated |
| POST | `/api/v1/Organizations/{orgId}/ownership/initiate` | The owner emails a transfer code to the next owner | Authenticated |
| POST | `/api/v1/Organizations/{orgId}/ownership` | Complete the transfer with that code | Authenticated |
| GET | `/api/v1/Organizations/{id}/members` | Paged member list | `org:members:read` |
| PUT | `/api/v1/Organizations/{orgId}/members/{userId}/role` | Change a member's organization role | `org:members:manage` |
| DELETE | `/api/v1/Organizations/{orgId}/members/{userId}` | Remove a member | `org:members:manage` |
| GET | `/api/v1/Organizations/{id}/invitations` | Invitations still pending | `org:members:read` |
| POST | `/api/v1/Organizations/{id}/invitations` | Invite someone by email | `org:members:invite` |
| POST | `/api/v1/Organizations/{orgId}/invitations/{invitationId}/resend` | Issue a fresh token for an existing invitation | `org:members:invite` |
| GET | `/api/v1/Organizations/{id}/applications` | Applications enabled for the organization | `org:apps:read` |
| GET | `/api/v1/Organizations/{id}/applications/available` | Applications it could still enable | `org:apps:manage` |
| POST | `/api/v1/Organizations/{id}/applications` | Enable one | `org:apps:manage` |
| PUT | `/api/v1/Organizations/{id}/applications/{applicationId}` | Change that subscription | `org:apps:manage` |
| DELETE | `/api/v1/Organizations/{id}/applications/{applicationId}` | Disable it | `org:apps:manage` |
| GET | `/api/v1/Organizations/{orgId}/members/{userId}/roles` | A member's application-level roles | `org:permissions:read` |
| POST | `/api/v1/Organizations/{orgId}/members/{userId}/roles` | Give a member an application role | `org:permissions:manage` |
| DELETE | `/api/v1/Organizations/{orgId}/members/{userId}/roles/{roleId}` | Take it away | `org:permissions:manage` |
| POST | `/api/v1/Organizations/{orgId}/members/{userId}/permissions` | Grant a member one permission directly | `org:permissions:manage` |

#### Invitations — 3 endpoints

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/api/v1/Invitations/{token}` | Show what an invitation is for, before accepting it | Anonymous · `login` |
| POST | `/api/v1/Invitations/{token}/register` | Create an account through the invitation. The email arrives already confirmed | Anonymous · `login` |
| POST | `/api/v1/Invitations/{token}/accept` | Accept it as someone who already has an account | Authenticated |

#### API Keys — 5 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/ApiKeys` | List keys. Omitting `applicationId` returns every application's keys | `apikeys:read` |
| POST | `/api/v1/ApiKeys` | Create a key. The secret is shown once, in this response | `apikeys:create` |
| POST | `/api/v1/ApiKeys/{id}/revoke` | Revoke a key | `apikeys:revoke` |
| POST | `/api/v1/ApiKeys/validate` | Check a key supplied in the body and return its metadata | `apikeys:validate` |
| POST | `/api/v1/ApiKeys/{id}/rotate` | Issue a replacement, with a grace period for the old one | `apikeys:rotate` |

`apikeys:validate` is not created by any database script in this repository, so only a holder of the global `*` permission can call that endpoint. See [Section 11](#11-permission-matrix).

#### Webhook Keys — 5 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/WebhookKeys` | List webhook signing keys | `webhookkeys:read` |
| POST | `/api/v1/WebhookKeys` | Create one for a target URL | `webhookkeys:create` |
| POST | `/api/v1/WebhookKeys/validate` | Check a key supplied in the body | `webhookkeys:validate` |
| POST | `/api/v1/WebhookKeys/{id}/revoke` | Revoke one | `webhookkeys:revoke` |
| POST | `/api/v1/WebhookKeys/{id}/rotate` | Rotate one, with a grace period | `webhookkeys:rotate` |

**None of the five `webhookkeys:` codes is created by any database script anywhere in this repository.** Only the global `*` permission reaches them, which in practice means only the `super-admin` role.

#### Audit Logs — 5 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/audit-logs` | Paged audit trail. Default page size is **50**, not 20 | `auditlogs:read` |
| GET | `/api/v1/audit-logs/{id}` | One entry | `auditlogs:read` |
| GET | `/api/v1/audit-logs/users/{userId}` | Paged entries for one user | `auditlogs:read` |
| GET | `/api/v1/audit-logs/entities/{entityType}/{entityId}` | Entries for one record, unpaged | `auditlogs:read` |
| POST | `/api/v1/audit-logs/export` | Export a filtered range as a file | `auditlogs:export` |

#### Dashboard — 6 endpoints

Aggregated figures for the console's home screen. Each takes `days` between 1 and 90, default 30, unless noted.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/dashboard/user-stats` | Totals, sign-ups over time, activation funnel, dormant accounts | `users:read` |
| GET | `/api/v1/dashboard/auth-stats` | Sign-in outcomes, daily active users, failure reasons, lockouts | `auditlogs:read` |
| GET | `/api/v1/dashboard/audit-stats` | Audit totals with a daily series and breakdowns | `auditlogs:read` |
| GET | `/api/v1/dashboard/session-stats` | Session and refresh-token hygiene | `auditlogs:read` |
| GET | `/api/v1/dashboard/app-activity` | Activity per application, and organization enablements | `applications:read` |
| GET | `/api/v1/dashboard/credential-stats` | Which API and webhook keys expire soon. Takes `horizonDays`, 1–365, default 14 | Authenticated. Deliberately has no permission attribute: the handler blanks out whichever family the caller cannot read |

#### Notification Templates — 14 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-templates` | Paged template list | `notification-templates:read` |
| GET | `/api/v1/notification-templates/summary` | Overview of what exists and what is published | `notification-templates:read` |
| GET | `/api/v1/notification-templates/{id}` | One template with its versions and translations | `notification-templates:read` |
| GET | `/api/v1/notification-templates/{id}/versions/{versionId}` | One version's translations | `notification-templates:read` |
| POST | `/api/v1/notification-templates` | Create an empty draft | `notification-templates:manage` |
| PUT | `/api/v1/notification-templates/{id}/draft` | Save draft edits | `notification-templates:manage` |
| DELETE | `/api/v1/notification-templates/{id}/draft` | Discard the draft. Returns 200 with the template, not 204 | `notification-templates:manage` |
| POST | `/api/v1/notification-templates/{id}/publish` | Publish the draft | `notification-templates:publish` |
| POST | `/api/v1/notification-templates/{id}/unpublish` | Unpublish it | `notification-templates:publish` |
| POST | `/api/v1/notification-templates/{id}/rollback` | Point the published version back at an earlier one | `notification-templates:publish` |
| POST | `/api/v1/notification-templates/{id}/versions/{versionId}/restore-draft` | Copy an old version into a new draft | `notification-templates:manage` |
| DELETE | `/api/v1/notification-templates/{id}` | Delete the template and its history | `notification-templates:manage` |
| POST | `/api/v1/notification-templates/preview` | Render an editor buffer on the server | `notification-templates:read` |
| POST | `/api/v1/notification-templates/{id}/test-send` | Send one rendered test message | `notification-templates:manage` |

#### Notification Layouts — 6 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-layouts` | Every layout, the global one first | `notification-templates:read` |
| GET | `/api/v1/notification-layouts/{id}` | One layout | `notification-templates:read` |
| POST | `/api/v1/notification-layouts` | Create a layout for one application | `notification-layouts:manage` |
| PUT | `/api/v1/notification-layouts/{id}/draft` | Save draft edits | `notification-layouts:manage` |
| POST | `/api/v1/notification-layouts/{id}/publish` | Publish the draft | `notification-layouts:manage` |
| POST | `/api/v1/notification-layouts/preview` | Preview a layout buffer | `notification-templates:read` |

Note the asymmetry: reading uses `notification-templates:read`, writing uses `notification-layouts:manage`. There is no `notification-layouts:read` code.

#### Notification Outbox — 3 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-outbox` | Paged delivery log. `sortDirection` defaults to `Desc` here | `notification-templates:read` |
| GET | `/api/v1/notification-outbox/{id}` | One entry with the message as it was rendered | `notification-templates:read` |
| POST | `/api/v1/notification-outbox/{id}/retry` | Put a failed or dead message back in the queue | `notification-templates:manage` |

#### Notification Types — 2 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-types` | Every seeded type with its variable catalogue and sample data | `notification-templates:read` |
| PUT | `/api/v1/notification-types/{id}` | Edit a type's name, description, variable list or sample data | `notification-templates:manage` |

#### Privacy Policy — 8 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/privacy-policy/published` | The published policy for a language, plus the live configuration disclosures | Anonymous |
| GET | `/api/v1/privacy-policy/versions` | Every revision, newest first | `privacy-policy:read` |
| POST | `/api/v1/privacy-policy/versions` | Record a new revision, named `YYYY.MM` | `privacy-policy:manage` |
| PUT | `/api/v1/privacy-policy/versions` | Change a revision's effective date or note. The revision is named in the body, not the path | `privacy-policy:manage` |
| GET | `/api/v1/privacy-policy/versions/content` | One language's document for one revision. `version` and `language` are required | `privacy-policy:read` |
| PUT | `/api/v1/privacy-policy/versions/content` | Create or replace one language's document | `privacy-policy:manage` |
| POST | `/api/v1/privacy-policy/versions/publish` | Make a revision the published one | `privacy-policy:manage` |
| POST | `/api/v1/privacy-policy/versions/notify` | Email the change notice to every active confirmed user | `privacy-policy:manage` |

#### Public Policy — 3 endpoints

Outside `/api/`, unversioned, and meant to be linked to from an app store listing or a website footer.

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/privacy` | Redirects to the reader's best language, chosen from the `Accept-Language` header | Anonymous |
| GET | `/privacy/{language}` | The published notice as a complete HTML page | Anonymous |
| GET | `/privacy/v{version}/{language}` | A superseded revision, at a permanent address | Anonymous |

#### Platform — 1 endpoint

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/api/v1/Platform/branding` | The platform name and logo addresses that sign-in screens draw before anyone is signed in | Anonymous |

#### Platform Settings — 2 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/admin/platform-settings` | The branding values with their audit fields | `platform-settings:manage` |
| PUT | `/api/v1/admin/platform-settings` | Update `platformName`, `logoUrl`, `logoUrlDark`, `faviconUrl` | `platform-settings:manage` |

#### System Settings — 4 endpoints

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/admin/system-settings` | Every section, showing each field's effective value, its database override, its file baseline, and whether a restart is pending | `system-settings:manage` |
| PUT | `/api/v1/admin/system-settings/{sectionKey}` | Replace one section's overrides. The payload is the **complete** new set — any field left out reverts to the file value. `rowVersion` guards against a concurrent edit, which returns 409 | `system-settings:manage` |
| POST | `/api/v1/admin/system-settings/{sectionKey}/reset` | Drop every override in a section | `system-settings:manage` |
| POST | `/api/v1/admin/system-settings/email/test` | Send a diagnostic email using the settings currently in effect | `system-settings:manage` |

#### Secrets (Admin) — 13 endpoints

All thirteen require the permission `secrets.manage` — **note the dot, which is unique in this system; every other code uses colons** — and all thirteen return 403 when `SecretManagement:EnableAdminApi` is `false`.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/admin/Secrets/status` | Which secrets are configured. Never their values | `secrets.manage` |
| POST | `/api/v1/admin/Secrets/challenges` | Start a step-up confirmation, emailing a code | `secrets.manage` · `login` |
| POST | `/api/v1/admin/Secrets/challenges/{challengeId}/verify` | Answer with that code; returns what the change would affect | `secrets.manage` · `login` |
| POST | `/api/v1/admin/Secrets/generate/rsa` | Rotate the token-signing key pair | `secrets.manage` |
| POST | `/api/v1/admin/Secrets/generate/hmac` | Rotate the shared-secret key behind refresh tokens, reset links and challenges | `secrets.manage` |
| POST | `/api/v1/admin/Secrets/generate/gateway-token` | Rotate the gateway token | `secrets.manage` |
| POST | `/api/v1/admin/Secrets/import/rsa` | Supply your own signing key. Returns 409 in PlainText storage mode | `secrets.manage` |
| POST | `/api/v1/admin/Secrets/import/hmac` | Supply your own shared-secret key. 409 in PlainText mode | `secrets.manage` |
| POST | `/api/v1/admin/Secrets/import/gateway-token` | Supply your own gateway token. 409 in PlainText mode | `secrets.manage` |
| PUT | `/api/v1/admin/Secrets/smtp-password` | Move the mail-server password into the encrypted store | `secrets.manage` |
| PUT | `/api/v1/admin/Secrets/connection-string` | Move the database connection string into the encrypted store | `secrets.manage` |
| PUT | `/api/v1/admin/Secrets/custom/{key}` | Set a custom secret | `secrets.manage` |
| DELETE | `/api/v1/admin/Secrets/custom/{key}` | Delete a custom secret | `secrets.manage` |

#### Images — 1 endpoint

| Method | Path | What it does | Auth |
|---|---|---|---|
| POST | `/api/v1/Images` | Upload and process an image, returning `{ key, url }`. Send it as `multipart/form-data` with the form field named `file` | Authenticated, no permission code. Failures return `{ "error": "…" }`, not a ProblemDetails body |

#### Internal — gateway settings — 1 endpoint

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/api/v1/internal/gateway-settings` | Serves the API Gateway its live allowed origins and rate limits | Framework-anonymous, but the action itself demands a valid gateway token header and returns 401 without one |

This one path is deliberately **not** on the gateway's own route list: the gateway calls the API directly for it.

#### Four more HTTP addresses that are not controller endpoints

These are not part of the 199 and have no permission gate. They are listed so you are not surprised by them.

| Method | Path | What it does |
|---|---|---|
| GET | `/health` | Liveness. Answers as long as the process is running |
| GET | `/ready` | Readiness. Runs the SQL Server probe and the signing-key check |
| GET | `/uploads/images/**` | Serves uploaded images as static files |
| GET | the OpenAPI document | A JSON description of the API, **registered only in Development** — production serves no API document at all. It is a JSON file, not a browsable page: this system ships no interactive API explorer in any environment |

---

### 5.1 Discovery (OIDC)

These endpoints follow the OpenID Connect Discovery specification. They are **version-neutral** (no `/api/v1/` prefix) and **anonymous**.

#### GET `/.well-known/openid-configuration`

Returns the OpenID Connect discovery document — the single address another system reads to learn how to talk to this one.

**Auth:** Anonymous

**Response (200), as a development install answers it:**

```json
{
  "issuer": "https://localhost:5101",
  "jwks_uri": "https://localhost:5101/.well-known/jwks.json",
  "authorization_endpoint": "https://localhost:5101/api/v1/auth/authorize",
  "token_endpoint": "https://localhost:5101/api/v1/auth/token",
  "userinfo_endpoint": "https://localhost:5101/api/v1/auth/me",
  "end_session_endpoint": "https://localhost:5101/api/v1/auth/logout",
  "revocation_endpoint": "https://localhost:5101/api/v1/auth/revoke",
  "introspection_endpoint": "https://localhost:5101/api/v1/auth/introspect",
  "response_types_supported": ["code"],
  "subject_types_supported": ["public"],
  "token_endpoint_auth_methods_supported": ["none"],
  "claims_supported": ["sub", "email", "name", "roles", "permissions", "iat", "exp", "aud", "iss"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"]
}
```

**The property names here are snake_case, and that is deliberate.** Every other response in this API uses camelCase; this one contract is pinned to the exact names RFC 8414 and OpenID Connect Discovery define, because standards-based clients recognise nothing else.
*In code:* `Auth/Auth.Application/Features/Discovery/GetDiscoveryDocument/GetDiscoveryDocumentQuery.cs:19-68`.

**The document advertises implemented capabilities and nothing else, so what is missing from it is information too.** Two fields a reader may expect are absent, and their absence is a statement of fact rather than an oversight: there is no `scopes_supported`, because this system has no scopes, and no `id_token_signing_alg_values_supported`, because it issues no OpenID Connect identity token. They are declared in the contract as nullable and left unset, and null properties are omitted from every response in this API, so they simply do not appear.
*In code:* `Auth/Auth.Application/Features/Discovery/GetDiscoveryDocument/GetDiscoveryDocumentQueryHandler.cs:28-30`.

**`token_endpoint_auth_methods_supported` is `["none"]` on purpose.** Clients here are public and PKCE is mandatory, so nothing authenticates itself at the token endpoint with a secret. Leaving the field out would have been worse than saying `none`: RFC 8414 says an omitted value implies `client_secret_basic`, which would tell every client to send credentials this system does not accept.

**Every address in the body except `issuer` is built from `IdentityProvider:PublicBaseUrl`, falling back to the host of the request.** That is what keeps the API Gateway's internal destination out of a document the whole world reads. `issuer` comes from `Jwt:Issuer`. In development both are `https://localhost:5101`; in production both must be the public origin, and neither is committed to this repository.
*In code:* `Auth/Auth_API/Controllers/DiscoveryController.cs:39-44`.

#### GET `/.well-known/jwks.json`

Returns the JSON Web Key Set — the public half of the signing key, in the format another service's token library reads automatically.

**Auth:** Anonymous

**Response (200):**

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "alg": "RS256",
      "kid": "auth-key-1",
      "n": "<modulus, base64url>",
      "e": "AQAB"
    }
  ]
}
```

**`kid` is not a fixed string.** It is whatever `Jwt:KeyId` is set to; `auth-key-1` is only the value shipped in `Auth/Auth_API/appsettings.json:23`. There is exactly one key in the set — this system does not publish a second key alongside a rotation.
*In code:* `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs:225-248`.

#### GET `/.well-known/public-key.pem`

Returns the RSA public key in PEM format.

**Auth:** Anonymous

**Response:** `text/plain`

```text
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...
-----END PUBLIC KEY-----
```

---

### 5.2 Authentication

**Base route:** `/api/v1/auth`

#### POST `/api/v1/auth/login`

Authenticate a user with email and password.

**Auth:** Anonymous | **Rate limited:** `login` policy — 20 requests per 60 seconds, per client IP address

**Request:**

```json
{
  "email": "user@example.com",
  "password": "SecureP@ssw0rd!",
  "deviceId": "optional-device-identifier"
}
```

**Response (200) — the ordinary case, where the sign-in completed:**

```json
{
  "token": {
    "accessToken": "eyJhbGciOiJSUzI1NiIs...",
    "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
    "tokenType": "Bearer",
    "expiresIn": 900,
    "refreshExpiresIn": 604800
  },
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "displayName": "John Doe",
    "preferredLanguage": "en",
    "timeZone": "UTC",
    "theme": "dark",
    "roles": ["admin", "user"],
    "permissions": ["users:read", "users:create", "roles:read"]
  },
  "requiresPasswordChange": false,
  "requiresTwoFactor": false
}
```

**The `user` object already contains the caller's roles and permissions, so do not call another endpoint to fetch them.** `roles` and `permissions` are always present, as arrays that may be empty. `displayName`, `preferredLanguage`, `timeZone` and `theme` appear only when the account has a value for them, because null properties are omitted from every response. That is the complete list of fields on this object — it is a `UserInfo`, not the full profile, so `phoneNumber`, `emailConfirmed`, `twoFactorEnabled` and `status` are not here; read those from `GET /api/v1/users/me`.
*In code:* `Auth/Auth.Application/DTOs/UserInfo.cs`; it is filled in at `Auth/Auth.Application/Features/Authentication/Common/LoginResponseBuilder.cs:313-325`.

**Response (200) — the second case, where the account has two-factor authentication turned on.** This is still a 200, and it is easy to mistake for success. No tokens are issued: `token` and `user` are absent from the body altogether, because null properties are omitted. `requiresTwoFactor` is `true` and `twoFactorChallengeToken` carries the ticket you hand to `POST /api/v1/auth/2fa/verify` to finish. The sign-in is not complete until that call succeeds.

```json
{
  "requiresPasswordChange": false,
  "requiresTwoFactor": true,
  "twoFactorChallengeToken": "opaque-challenge-string"
}
```

**A successful sign-in also sets a cookie that never appears in the body.** The response carries `Set-Cookie: auth_idp=…`, marked HttpOnly, Secure, SameSite=Lax, with a seven-day lifetime. That cookie is the browser's identity-provider session — it is what lets `GET /api/v1/auth/authorize` recognise the user later without asking for the password again. It is deliberately kept out of the JSON so that no client can copy it into storage a script can read. Because it is marked Secure, a browser will not store it if you are running the API over plain HTTP; that is the root of the sign-in loop described in [Section 10](#10-troubleshooting).
*In code:* `Auth/Auth_API/Common/IdpSessionCookie.cs:51-65`; the cookie name comes from `IdentityProvider:IdpSessionCookieName`.

**Error codes:** `User.InvalidCredentials`, `User.AccountLocked`, `User.AccountInactive`, `User.AccountPending`, `User.EmailNotConfirmed`, and one that surprises people: `Session.MaxSessionsReached` (or `Session.MaxSessionsReachedUntil`).

**About that last one.** When `Session:MaxConcurrentSessions` is set above zero and `Session:TerminateOldestOnMax` is `false`, a sign-in that would exceed the cap is **refused** rather than silently ending an older session. The refusal is a 400 whose `detail` names how many sessions are open, what the limit is, and — when it is known — the moment the earliest of them expires, so the user has a way forward: sign out on another device, or wait until that time. Shipped configuration sets `MaxConcurrentSessions` to `0`, which means no limit, so this error cannot occur until an operator changes it.
*In code:* `Auth/Auth.Domain/Errors/SessionErrors.cs:41-53`; the check is at `Auth/Auth.Application/Features/Authentication/Common/LoginResponseBuilder.cs:102-131`.

#### POST `/api/v1/auth/register`

Self-registration for new users.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "email": "newuser@example.com",
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe",
  "displayName": "Jane Doe",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "createOrganization": false
}
```

| Field | Required | Description |
|---|---|---|
| `email` | Yes | Must be unique across all users |
| `password` | Yes | Must meet password policy requirements |
| `firstName` | Yes | User's first name |
| `lastName` | Yes | User's last name |
| `createOrganization` | No | If `true`, creates a personal organization for the user (default: `false`) |

**Response (201):**

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "maskedEmail": "new***@example.com",
  "message": "Registration successful. Please verify your email.",
  "organizationCreated": false,
  "verificationCodeExpiresAt": "2026-03-12T10:15:00Z"
}
```

**`verificationCodeExpiresAt` is how a client shows a countdown without asking for a second code.** If it is missing from the body, the verification email failed to send, and the person needs `POST /api/v1/auth/resend-verification-email` before they can do anything.

**Registering does not sign anyone in.** There are no tokens in this response. The account exists with an unconfirmed email; the next step is verification.
*In code:* `Auth/Auth.Application/DTOs/RegisterResponse.cs`.

#### GET `/api/v1/auth/external-providers`

List all enabled external authentication providers.

**Auth:** Anonymous

**Response (200):**

```json
[
  {
    "code": "google",
    "name": "Google",
    "iconUrl": "https://...",
    "isEnabled": true,
    "displayOrder": 1
  }
]
```

#### POST `/api/v1/auth/external-login`

Authenticate via external provider (e.g., Google).

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "provider": "google",
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "nonce": "random-nonce-value",
  "createOrganization": false
}
```

**Response (200):** Same as login response.

> The system validates the Google ID token server-side, creates/links the user account, and returns JWT tokens.

#### POST `/api/v1/auth/refresh`

Exchange a refresh token for new access and refresh tokens.

**Auth:** Anonymous

**Request:**

```json
{
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4..."
}
```

**Response (200):**

```json
{
  "accessToken": "eyJhbGciOiJSUzI1NiIs...",
  "refreshToken": "bmV3IHJlZnJlc2ggdG9rZW4...",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "refreshExpiresIn": 604800
}
```

> When `RotateRefreshTokens` is `true` (default), the old refresh token is revoked and a new one is issued.

#### How a person actually signs in: the authorization-code flow with PKCE

**This is the flow both shipped web applications use, and the one a third-party application should use.** It exists so that an application never handles anyone's password. Instead the application sends the browser to this API, the person types their password on a page this API controls, and the application gets back a short-lived code that it trades for tokens. PKCE — Proof Key for Code Exchange, pronounced "pixy" — is the part that stops a stolen code from being useful to anyone but the application that started the flow.

**There are no client secrets here.** Every registered application is a *public client*: it runs in a browser or on a phone, where a secret could be read out of it, so the system does not issue one. PKCE takes the secret's place, and it is mandatory — a token request without a valid verifier is rejected. The discovery document says as much, listing `"none"` as the only supported client authentication method.

The eight steps, in order. Nothing is skipped.

1. **The application invents a random string and keeps it.** This is the `code_verifier`. It never leaves the application.
2. **The application hashes that string with SHA-256 and base64url-encodes the result.** This is the `code_challenge`. It is safe to send in a URL, because a hash cannot be turned back into the verifier.
3. **The application sends the browser to the authorize endpoint**, with the challenge and the address it wants the person returned to:

   ```text
   GET https://localhost:5101/api/v1/Auth/authorize
       ?response_type=code
       &client_id=my-app
       &redirect_uri=https://localhost:5173/callback
       &code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
       &code_challenge_method=S256
       &state=opaque-value-the-app-checks-later
   ```

   `client_id` is the application's code as registered. `redirect_uri` must be one of the addresses registered for that application; `state` is any opaque value the application will recognise when it comes back, and checking it is how the application detects a forged return. Two optional parameters force a fresh sign-in even when the person already has a session: `prompt=login`, and `max_age=<seconds>`, which rejects a session older than that many seconds.
4. **The API checks `client_id` and `redirect_uri` before doing anything else.** If the application is unknown, or the redirect address is not one of its registered ones, the answer is **400 Bad Request and no redirect at all** — deliberately, because redirecting to an unverified address is how open-redirect attacks work.
5. **Now the API looks for the browser's `auth_idp` sign-in cookie.** If a valid one is present, skip to step 7.
6. **If there is no valid cookie, the API sends the browser to the accounts application's sign-in page**, at `{IdentityProvider:AccountsBaseUrl}/login?returnTo=<the whole original authorize URL>`. The person signs in there, which sets the `auth_idp` cookie, and the accounts application sends the browser back to `returnTo` — which is this same authorize request, now with a cookie. This is the only place a password is typed.
7. **The API mints a one-time code and redirects the browser to the application's `redirect_uri`**, with `code` and the original `state` attached. **That code is valid for 60 seconds and can be used once.**
8. **The application exchanges the code for tokens** by calling the token endpoint from its own code, sending the verifier it kept in step 1. The API hashes that verifier, compares it with the challenge from step 3, and issues tokens only if they match.

*In code:* the authorize endpoint is `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:219`, its logic `Auth/Auth.Application/Features/Authentication/Authorize/AuthorizeCommandHandler.cs`; the code lifetime is `IdentityProvider:AuthorizationCodeLifetimeSeconds`, default 60 (`Auth/Auth.Application/Configuration/IdentityProviderSettings.cs:43`).

#### GET `/api/v1/auth/authorize`

The OAuth 2.0 authorization endpoint — step 3 above. It always answers with a redirect (HTTP 302), never with a JSON body, except when it refuses.

**Auth:** Anonymous

**Query parameters.** All are spelled in snake_case, as the OAuth specification requires — not camelCase like the rest of this API.

| Parameter | Required | Description |
|---|---|---|
| `response_type` | Yes | Must be `code`. Nothing else is supported |
| `client_id` | Yes | The registered application's code |
| `redirect_uri` | Yes | Must exactly match one of that application's registered redirect addresses |
| `code_challenge` | Yes | Base64url of the SHA-256 hash of the verifier |
| `code_challenge_method` | Yes | Must be `S256`. The weaker `plain` method is not supported |
| `state` | No, but use it | Opaque value echoed back untouched on the redirect |
| `prompt` | No | `login` forces a fresh sign-in even if a session exists |
| `max_age` | No | Seconds. A session older than this must sign in again |

**Response (302), when the browser already had a valid sign-in cookie:**

```text
Location: https://localhost:5173/callback?code=<one-time code>&state=opaque-value-the-app-checks-later
```

**Response (302), when it did not:**

```text
Location: https://accounts.example.com/login?returnTo=<url-encoded original authorize request>
```

**Response (400):** a ProblemDetails body, with no redirect, when `client_id` is unknown or `redirect_uri` is not registered for it.

#### POST `/api/v1/auth/token`

The OAuth 2.0 token endpoint — step 8 above. Two things about it differ from every other endpoint in this API, and both trip people up.

**It takes a form, not JSON.** The `Content-Type` must be `application/x-www-form-urlencoded`. Posting JSON to it returns 415.

**Its response uses snake_case field names**, because standard OAuth client libraries expect exactly those names. Everything else in this API is camelCase.

**Auth:** Anonymous | **Rate limited:** `login` policy — 20 requests per 60 seconds

**Request — the `authorization_code` grant:**

```text
grant_type=authorization_code
&code=<the one-time code from the redirect>
&redirect_uri=https://localhost:5173/callback
&client_id=my-app
&code_verifier=<the random string kept in step 1>
```

**Request — the `refresh_token` grant.** The same endpoint also renews tokens, which is convenient for a standard OAuth client that only knows this one address:

```text
grant_type=refresh_token
&refresh_token=<a valid refresh token>
```

Any other `grant_type` is rejected with the error code `Auth.UnsupportedGrantType`.

**Response (200):**

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIs...",
  "token_type": "Bearer",
  "expires_in": 900,
  "refresh_token": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "refresh_expires_in": 604800
}
```

*In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:267`; the response record is `OAuthTokenResponse` in `Auth/Auth.Application/Features/Authentication/TokenExchange/ExchangeAuthorizationCodeCommand.cs:36-51`.

#### POST `/api/v1/auth/logout`

Terminate the current session and revoke tokens.

**Auth:** Authenticated

**Request:**

```json
{
  "refreshToken": "dGhpcyBpcyBhIHJlZnJlc2ggdG9rZW4...",
  "logoutAllDevices": false
}
```

| Field | Description |
|---|---|
| `refreshToken` | Optional; if provided, the specific refresh token is revoked |
| `logoutAllDevices` | If `true`, revokes all sessions and tokens for the user |

**Response:** 204 No Content

#### POST `/api/v1/auth/change-password`

Change the authenticated user's password.

**Auth:** Authenticated

**Request:**

```json
{
  "currentPassword": "OldP@ssw0rd!",
  "newPassword": "NewSecureP@ss1!",
  "confirmNewPassword": "NewSecureP@ss1!",
  "terminateSessions": false
}
```

| Field | Description |
|---|---|
| `terminateSessions` | If `true`, terminates all other sessions after password change |

**Response:** 204 No Content

**Validations:** Password policy enforcement, password history check (last 3 passwords).

#### POST `/api/v1/auth/forgot-password`

Initiate password reset flow (sends email with reset token/OTP).

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "email": "user@example.com"
}
```

**Response (200):**

```json
{
  "message": "If the email exists, a password reset link has been sent.",
  "maskedEmail": "us***@example.com"
}
```

> The response is intentionally vague to prevent email enumeration.

#### POST `/api/v1/auth/reset-password`

Complete a password reset using the token from the emailed link.

**Auth:** Anonymous | **Rate limited:** `password-reset` policy — 10 requests per 60 seconds, per client IP address. This is the stricter of the two policies; every other anonymous endpoint in this section uses `login`, which allows 20.

**There is no email field, and that is deliberate.** The token identifies the account on its own — the server looks the reset up by a hash of the token alone. Sending an address alongside it would add nothing and would let an attacker test whether an address exists.

**Request:**

```json
{
  "token": "reset-token-from-email",
  "newPassword": "NewSecureP@ss1!",
  "confirmNewPassword": "NewSecureP@ss1!",
  "terminateSessions": true
}
```

| Field | Required | Description |
|---|---|---|
| `token` | Yes | The token from the reset link. Identifies the account by itself |
| `newPassword` | Yes | Checked against the configured password policy at the moment of use, not against a hardcoded length |
| `confirmNewPassword` | Yes | Must equal `newPassword` |
| `terminateSessions` | No | End every other session after the change. When omitted, the server's configured default applies |

**Response:** 204 No Content
*In code:* `Auth/Auth_API/Modules/Authentication/Contracts/ResetPasswordRequest.cs`.

#### GET `/api/v1/auth/sessions`

List all active sessions for the authenticated user.

**Auth:** Authenticated

**Response (200):**

```json
[
  {
    "id": "3fa85f64-...",
    "ipAddress": "192.168.1.1",
    "userAgent": "Mozilla/5.0...",
    "deviceId": "device-123",
    "createdAt": "2026-03-12T10:00:00Z",
    "lastActivityAt": "2026-03-12T14:30:00Z",
    "expiresAt": "2026-03-13T10:00:00Z",
    "isCurrent": true
  }
]
```

#### DELETE `/api/v1/auth/sessions/{sessionId}`

Terminate a specific session.

**Auth:** Authenticated

**Response:** 204 No Content

#### DELETE `/api/v1/auth/sessions`

Terminate all sessions except the current one.

**Auth:** Authenticated

**Response (200):**

```json
{
  "terminatedCount": 3
}
```

#### GET `/api/v1/auth/devices`

List the browsers this account has signed in from. A *device* here is one browser on one machine, not one session — a person who has three tabs open in Chrome has one device and possibly several sessions.

**Auth:** Authenticated

**Response (200):**

```json
[
  {
    "id": "3fa85f64-...",
    "deviceName": "Chrome on Windows",
    "deviceType": "desktop",
    "firstSeenAt": "2026-01-04T09:12:00Z",
    "lastSeenAt": "2026-03-12T14:30:00Z",
    "activeSessionCount": 2,
    "isCurrent": true
  }
]
```

`deviceType` is one of `unknown`, `desktop`, `mobile`, `tablet` — lower-case, unlike most enumerated values in this API. The browser's own signature is deliberately not returned; the `id` is all you need to act on it.
*In code:* `Auth/Auth.Application/DTOs/KnownDeviceDto.cs`.

#### DELETE `/api/v1/auth/devices/{deviceId}`

Forget a browser: remove its recognition record and end every session it still holds.

**Auth:** Authenticated

**Response (200):**

```json
{
  "terminatedCount": 2
}
```

**You cannot forget the browser you are calling from.** That request is refused. Signing out is what ends the current session; forgetting is for the other machines.

#### GET `/api/v1/auth/login-history`

The caller's own recent sign-in attempts, successful and failed. This is a record of what happened, so nothing here can be revoked.

**Auth:** Authenticated

**Query parameters:**

| Parameter | Type | Default | Limit |
|---|---|---|---|
| `take` | integer | 20 | Between 1 and 100. Outside that range returns 400 |

**Response (200):**

```json
[
  {
    "id": "3fa85f64-...",
    "attemptedAt": "2026-03-12T14:30:00Z",
    "isSuccess": false,
    "secondFactorIncomplete": false,
    "secondFactorAttempts": 0,
    "failureReason": "Invalid password",
    "ipAddress": "203.0.113.7",
    "location": "Cairo, Egypt",
    "deviceName": "Chrome on Windows",
    "deviceType": "desktop"
  }
]
```

Three fields need explaining. **`secondFactorIncomplete`** is `true` when the password was accepted but the verification code was never supplied — the sign-in simply stopped, so it is neither a success nor a rejection, and `failureReason` is absent. **`secondFactorAttempts`** counts the codes that were rejected during one sign-in, so the history can show how hard somebody tried without one row per guess. **`location`** is approximate and derived from the IP address; it is absent unless the optional geographic-lookup database is configured, which by default it is not.
*In code:* `Auth/Auth.Application/DTOs/LoginAttemptDto.cs`.

#### GET `/api/v1/auth/me`

Echo back what the caller's own access token says about them, including the roles and permissions it carries.

**Auth:** Authenticated

**Response (200):**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "displayName": "John Doe",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "theme": "dark",
  "roles": ["admin", "user"],
  "permissions": ["users:read", "users:create", "roles:read"]
}
```

**Those ten fields are the whole body — there are no others.** The action builds the answer entirely from the claims in the bearer token and never reads a database row, so anything the token does not carry cannot appear here. In particular **`phoneNumber`, `emailConfirmed`, `twoFactorEnabled` and `status` are not on this endpoint at all**; asking for them here returns nothing, and a client that expects them will read `undefined`. `displayName`, `preferredLanguage`, `timeZone` and `theme` come back only when the token carries them, because null properties are omitted from every response; `roles` and `permissions` are always present, as arrays that may be empty.

**Use `GET /api/v1/users/me` ([5.4](#54-users)) when you need the real profile.** That one reads the database and returns a full `UserDto`, which does carry `phoneNumber`, `emailConfirmed`, `twoFactorEnabled`, `status` and the rest. The trade-off is the point of having both: `/auth/me` is a cheap claims echo that costs no query, `/users/me` is the authoritative record.
*In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:604-620`; the shape is `Auth/Auth.Application/DTOs/UserInfo.cs`.

#### POST `/api/v1/auth/revoke`

Revoke a token (RFC 7009 compliant).

**Auth:** Anonymous. This is not an oversight: RFC 7009 makes the token itself the credential, so a client that holds a token can retire it without also holding a valid session. The companion endpoint `introspect`, immediately below, is the opposite — it **requires** a bearer token, because asking questions about someone else's token is a privileged act.

**Request:**

```json
{
  "token": "token-to-revoke",
  "tokenTypeHint": "access_token"
}
```

| `tokenTypeHint` values | Description |
|---|---|
| `access_token` | Revokes an access token (adds JTI to blacklist) |
| `refresh_token` | Revokes a refresh token |

**Response:** 200 OK

#### POST `/api/v1/auth/introspect`

Inspect a token's validity and claims (RFC 7662 compliant).

**Auth:** Authenticated

**Request:**

```json
{
  "token": "token-to-inspect",
  "tokenTypeHint": "access_token"
}
```

**Response (200):**

```json
{
  "active": true,
  "sub": "3fa85f64-...",
  "username": "user@example.com",
  "email": "user@example.com",
  "exp": 1710244200,
  "iat": 1710243300,
  "iss": "https://localhost:5101",
  "aud": "https://localhost:5101",
  "token_type": "bearer",
  "roles": ["admin"],
  "permissions": ["users:read"]
}
```

`token_type` is `bearer` when the token was an access token and `refresh_token` when it was a refresh token. When a token is unknown, expired or revoked the body is simply `{ "active": false }` — every other field is omitted.

**This is the one response in the whole API that uses snake_case.** RFC 7662 fixes the field names, so `token_type` and `client_id` are spelled that way here while every other endpoint would call them `tokenType` and `clientId`. Do not assume camelCase when parsing this one.
*In code:* `Auth/Auth.Application/DTOs/IntrospectTokenResponse.cs`.

`iss` and `aud` are whatever `Jwt:Issuer` and `Jwt:Audience` are set to. In development both are `https://localhost:5101`.

#### POST `/api/v1/auth/send-verification-email`

Send a verification email to the authenticated user.

**Auth:** Authenticated | **Rate Limited:** `login` policy

**Response (200):**

```json
{
  "expiresAt": "2026-03-12T10:15:00Z",
  "maskedEmail": "us***@example.com"
}
```

#### POST `/api/v1/auth/verify-email`

Confirm an email address with the six-digit one-time password (OTP) that was emailed to it.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**This endpoint has two behaviours, and which one you get depends on which field you send.** Send exactly one of `userId` or `email`; `otp` is always required.

**Path one — the self-service path, keyed by `email`.** This is what the accounts application uses after somebody registers. Confirming the address also **signs the person in**: the answer is a **200** carrying a full login response, and the identity-provider sign-in cookie is set on the same response. Nobody has to type their password again just after proving they own the mailbox.

```json
{
  "email": "user@example.com",
  "otp": "123456",
  "deviceId": "optional-device-identifier"
}
```

Response: **200**, with the same body shape as `POST /api/v1/auth/login`.

**Path two — the administrative path, keyed by `userId`.** This is for an administrator marking somebody's address confirmed. It signs nobody in and returns nothing.

```json
{
  "userId": "3fa85f64-...",
  "otp": "123456"
}
```

Response: **204 No Content.**

A client that assumes 204 will break on the first path, and a client that assumes a body will break on the second. Branch on the status code.
*In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:721-751`; the request contract is `Auth/Auth_API/Modules/Authentication/Contracts/VerifyEmailRequest.cs`.

#### POST `/api/v1/auth/resend-verification-email`

Resend email verification to a specific email address.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "email": "user@example.com"
}
```

**Response (200):**

```json
{
  "expiresAt": "2026-03-12T10:15:00Z",
  "maskedEmail": "us***@example.com"
}
```

#### Deleting an account without signing in

**These four endpoints exist because an app-store listing must offer a way to delete an account to somebody who has forgotten their password.** They form one flow, so they are explained together and then listed. Deletion is two-phase: confirming it schedules the account for destruction after a **30-day grace window**, during which the person can change their mind; only after that window does the irreversible work run. The grace period is `AccountDeletion:GraceDays`, which the shipped configuration sets to `30`.

The first two endpoints schedule a deletion. The second two cancel one that is still inside its grace window.

#### POST `/api/v1/auth/deletion/request`

Step 1: ask for a deletion code to be emailed to an address.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "email": "user@example.com"
}
```

**Response:** 202 Accepted, with no body. **The answer is the same whether or not that address has an account** — the endpoint never reveals which addresses exist.

#### POST `/api/v1/auth/deletion/confirm`

Step 2: prove the mailbox with the code, and schedule the deletion.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "email": "user@example.com",
  "otpCode": "123456"
}
```

**Response:** 202 Accepted. Confirming a deletion that is already pending succeeds again rather than erroring, so a double submission is harmless.

#### POST `/api/v1/auth/deletion/recover`

Cancel a pending deletion during the grace window, for an account that has a password. Success cancels the deletion, restores the account **and signs the person in**.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "email": "user@example.com",
  "password": "SecureP@ssw0rd!",
  "twoFactorCode": "123456"
}
```

`twoFactorCode` is required only for accounts that have two-factor authentication turned on.

**Response (200):** the same body shape as `POST /api/v1/auth/login`.

#### POST `/api/v1/auth/deletion/recover-external`

The same recovery, for an account that has no password because it signs in through Google or Apple.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "provider": "google",
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "nonce": "random-nonce-value",
  "twoFactorCode": "123456"
}
```

**Response (200):** the same body shape as `POST /api/v1/auth/login`.

**One caveat about these two recovery endpoints.** They read the caller's IP address straight from the network connection rather than from the `X-Forwarded-For` header, unlike every other action in this controller. Behind a reverse proxy that address is the proxy's, not the user's, so the audit entry and the location shown for these two recoveries will be wrong. It is a known defect, recorded here rather than hidden.
*In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:839` and `:871`.

---

### 5.3 Two-Factor Authentication

**Base route:** `/api/v1/auth/2fa`

Four endpoints. Three of them — `setup`, `enable` and `disable` — manage the feature for somebody who is already signed in, and require a bearer token. The fourth, `verify`, is **anonymous**, and the reason is worth stating plainly: it completes a sign-in that has not happened yet, so there is no token to present.

**How a two-factor sign-in works, end to end.** It is two calls, not one.

1. The client calls `POST /api/v1/auth/login` with email and password as usual. Because the account has two-factor turned on, the answer is a **200** with `requiresTwoFactor: true` and a `twoFactorChallengeToken`, and with **no tokens and no user object**. The person is not signed in.
2. The client shows a code box, then calls `POST /api/v1/auth/2fa/verify` with that challenge token and the six-digit code from the authenticator app. This call returns the real login response — tokens, user, and the sign-in cookie.

A client that treats the first 200 as success will appear to sign people in and then fail on every subsequent request, because it never received a token. Check `requiresTwoFactor` before reading `token`.

#### POST `/api/v1/auth/2fa/setup`

Generate a TOTP secret and QR code URI for 2FA setup.

**Auth:** Authenticated

**Response (200):**

```json
{
  "secret": "JBSWY3DPEHPK3PXP",
  "qrCodeUri": "otpauth://totp/AuthSystem:user%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=AuthSystem&algorithm=SHA1&digits=6&period=30",
  "manualEntryKey": "JBSW Y3DP EHPK 3PXP"
}
```

**Three fields, and the third is the one people forget.** `qrCodeUri` is what you render as a QR code. `manualEntryKey` is the same secret broken into four-character groups separated by spaces, for somebody typing it in by hand on a device that cannot scan — show both, because scanning is not always possible. `secret` is the raw value behind the other two.

**Note the two details in the URI that are easy to get wrong when hand-building one.** The account label is percent-encoded, so an email address carries `%40` rather than `@`, and the algorithm is stated explicitly as `SHA1` with `digits=6` and `period=30`.

**The name before the colon is the platform name, not a fixed string.** It is read from Platform Settings ([5.21](#521-platform-settings)), which is what an authenticator app displays as the account's provider. If no platform name is set, the system falls back to the host part of `Jwt:Issuer` — `localhost` in development — and only then to the literal `AuthSystem`.
*In code:* `Auth/Auth.Infrastructure/Authentication/TotpService.cs:36-45`; the issuer is resolved in `Auth/Auth.Application/Features/Authentication/SetupTwoFactor/SetupTwoFactorCommandHandler.cs:93-119`.

**Nothing is switched on by this call.** The secret is stored against the account but two-factor stays off until `enable` succeeds. Calling `setup` again on an account that is already enrolled returns **409**; calling it again mid-enrolment replaces the stored secret, which invalidates any QR code already on screen.

#### POST `/api/v1/auth/2fa/enable`

Enable 2FA after verifying a TOTP code.

**Auth:** Authenticated

**Request:**

```json
{
  "code": "123456"
}
```

**Response (200):**

```json
{
  "recoveryCodes": [
    "ABCD-2345",
    "EFGH-6789",
    "JKLM-NPQR",
    "STUV-WXYZ",
    "2345-ABCD",
    "6789-EFGH",
    "NPQR-JKLM",
    "WXYZ-STUV",
    "A2B3-C4D5",
    "E6F7-G8H9"
  ]
}
```

**There are always exactly ten codes, and each is eight characters printed as two groups of four.** The alphabet deliberately leaves out `I`, `O`, `0` and `1`, because those are the characters people mistype when reading a code off a printout.

**A recovery code is accepted with or without its dash, in any letter case** — the server strips dashes and spaces and upper-cases before checking. Send it to `POST /api/v1/auth/2fa/verify` with `useRecoveryCode` set to `true`.

**This is the only time the codes exist in readable form.** Only Argon2id hashes of them are stored, so nobody — including a platform administrator — can show them again. If they are lost, the only way back is to disable two-factor and enrol afresh.
*In code:* `Auth/Auth.Infrastructure/Authentication/TotpService.cs:70-118`; the count is fixed at `Auth/Auth.Application/Features/Authentication/EnableTwoFactor/EnableTwoFactorCommandHandler.cs:62`.

#### POST `/api/v1/auth/2fa/verify`

Finish a sign-in that stopped for two-factor verification, using the challenge token that `POST /api/v1/auth/login` returned.

**Auth:** Anonymous — the caller has no token yet, which is the whole point of this endpoint | **Rate Limited:** `login` policy

**Request:**

```json
{
  "challengeToken": "opaque-challenge-string",
  "code": "123456",
  "useRecoveryCode": false,
  "deviceId": "optional-device-identifier"
}
```

| Field | Required | Description |
|---|---|---|
| `challengeToken` | Yes | The `twoFactorChallengeToken` from the login response |
| `code` | Yes | The six-digit code from the authenticator app, or one of the recovery codes |
| `useRecoveryCode` | No | Set to `true` when `code` is a recovery code rather than an app code. Default `false` |
| `deviceId` | No | A stable client identifier, used only to decide whether a sign-in from an unfamiliar device is worth emailing the owner about. It is never trusted for authorization |

**Response (200):** the full login response — `token`, `user`, `requiresPasswordChange` — with the same shape as `POST /api/v1/auth/login`. This response also sets the identity-provider sign-in cookie.

#### POST `/api/v1/auth/2fa/disable`

Disable 2FA (requires a valid TOTP code to confirm).

**Auth:** Authenticated

**Request:**

```json
{
  "code": "123456"
}
```

**Response:** 204 No Content

---

### 5.4 Users

**Base route:** `/api/v1/users`

Twenty-nine endpoints, and they fall into two groups that are easy to confuse.

**The `/{id}` endpoints are administrative**: they act on somebody else's account and each one needs a permission code. **The `/me` endpoints are self-service**: they act on the caller's own account and need only a valid token, no permission at all. That is deliberate — a person must be able to change their own display name without an administrator granting them `users:update`.

#### GET `/api/v1/users`

List users with paging, search and sorting.

**Permission:** `users:read`. Adding `includeDeleted=true` additionally requires `users:manage`.

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | integer | 1 | Which page. Must be 1 or more |
| `pageSize` | integer | **20** | How many per page. Must be between 1 and 100; anything outside returns 400 |
| `searchTerm` | string | null | Matches against name and email |
| `sortBy` | string | null | One of the allowed field names listed at the top of [Section 5](#5-api-reference). Anything else returns 400 |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |
| `includeDeleted` | boolean | `false` | Include soft-deleted accounts. See below |

**`includeDeleted` is guarded separately, and the refusal does not look like a normal permission failure.** The endpoint itself is gated by `users:read`, but asking for deleted accounts is a second, stricter act: the check for `users:manage` happens inside the action, so a caller who has `users:read` but not `users:manage` receives a **403 whose `title` is `User.DeletedUsersViewNotAllowed`**, rather than the framework's blank 403. Treat that code as "you may list users, but not deleted ones".
*In code:* `Auth/Auth_API/Modules/UserManagement/Controllers/UsersController.cs:59-83`.

**Response (200).** Note the array is called `users`, not `items`:

```json
{
  "users": [
    {
      "id": "3fa85f64-...",
      "email": "user@example.com",
      "firstName": "John",
      "lastName": "Doe",
      "displayName": "John Doe",
      "status": "Active",
      "emailConfirmed": true,
      "twoFactorEnabled": false,
      "createdAt": "2026-01-01T00:00:00Z"
    }
  ],
  "totalCount": 42,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 3,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

**That item is trimmed to the fields a list screen draws; a real row carries the whole `UserDto`.** The full object also holds `phoneNumber`, `profileImageUrl`, `phoneConfirmed`, `preferredLanguage`, `timeZone`, `theme`, `lastLoginAt`, `failedLoginAttempts`, `lockoutEnd`, `lastLoginIp`, `passwordChangedAt`, `passwordExpiresUtc`, `mustChangePassword`, `hasPassword`, `createdBy`, `createdByName`, `modifiedAt`, `modifiedBy`, `modifiedByName`, `isDeleted`, `deletedAt`, `roles` and `permissions`. Fields whose value is null are omitted, so a given row will show fewer than all of them. Two are worth knowing about: **`hasPassword` is `false` on an account created through Google or Apple**, which is what makes the system ask that person for an emailed code instead of a password when it needs re-confirmation; and **`passwordExpiresUtc` is never set by anything in this system**, so treat it as always absent rather than as a rotation date.
*In code:* `Auth/Auth.Application/DTOs/UserDto.cs`.

#### GET `/api/v1/users/{id}`

Get a user by ID.

**Permission:** `users:read`

**Response (200):** one `UserDto` — **the same object as one item of the list above, not a richer one.** The list is not a reduced projection: both paths build the whole `UserDto` from the same fields.

**A soft-deleted account is not reachable here at all.** The query behind this endpoint filters deleted rows out, so asking for one by identifier returns **404**, never a body with `"isDeleted": true`. There is no `includeDeleted` switch on this endpoint. To see a deleted account, use the list endpoint with `includeDeleted=true` and the `users:manage` permission.
*In code:* `Auth/Auth.Application/Features/Users/GetUserById/GetUserByIdQueryHandler.cs:46-80`; the filter is `AND [IsDeleted] = 0` in `Auth/Auth_DB/dbo/StoredProcedures/Users/sp_GetUserById.sql:46`.

#### POST `/api/v1/users`

Create a new user (admin-initiated).

**Permission:** `users:create`

**Request:**

```json
{
  "email": "newuser@example.com",
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe",
  "displayName": "Jane Doe",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "roleIds": ["role-guid-1", "role-guid-2"]
}
```

**Response (201):** `UserDto`

#### PUT `/api/v1/users/{id}`

Update a user's profile information.

**Permission:** `users:update`

**Request:**

```json
{
  "firstName": "Jane",
  "lastName": "Smith",
  "displayName": "Jane Smith",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "ar",
  "timeZone": "Asia/Riyadh"
}
```

**Response (200):** `UserDto`

#### DELETE `/api/v1/users/{id}`

Soft-delete a user. **Nothing is destroyed.** The row stays, marked deleted with a timestamp, and disappears from ordinary listings; only a caller holding `users:manage` can see it again, with `includeDeleted=true`. This is the delete the console offers.

**Permission:** `users:delete`

**Response:** 204 No Content

**Two rules can refuse it.** The built-in system account cannot be deleted, and it is protected by its well-known identifier rather than by a flag — there is no `IsSystemUser` column in the database, so that property is always `false` at runtime and would let the system account straight through on its own. The second rule is about organizations: an account that owns an organization with other members in it cannot be deleted until the organization is transferred or emptied. An owned organization with no other members is deleted along with the account.
*In code:* `Auth/Auth.Application/Features/Users/DeleteUser/DeleteUserCommandHandler.cs:46-60`.

#### DELETE `/api/v1/users/{id}/permanent`

**Irreversible.** This destroys the account. There is no undo inside the system.

**Permission:** `users:manage` — deliberately a stronger code than the `users:delete` that the soft delete needs.

**The account must already be soft-deleted.** Calling this on a live account returns an error whose code is `User.NotSoftDeleted`; use the ordinary delete first. The system account is refused here too, and the same owned-organization rule applies.

**Response:** 204 No Content

**What "permanent" means here, precisely.** Three different things happen to three kinds of data, and the difference matters if you have a retention obligation.

1. **A tombstone is written first**, before anything is destroyed, so that a failure part-way through cannot lose the record that a deletion happened.
2. **Credentials and personal data are deleted outright** — the encryption key that protects the person's phone number and authenticator secret, then refresh tokens, sessions, sign-in cookies, external logins, password history, two-factor records, role and permission assignments, organization memberships and invitations, queued email, display preferences and recognised devices.
3. **The audit trail and the sign-in history are anonymized, never deleted.** The rows survive with the identity and the personal fields stripped: the user reference becomes null, before-and-after values, details, IP address and user agent are cleared, and anything the deleted account *performed* is re-attributed to the built-in system account. The security record therefore stays intact while the person does not.

**The email address can never be registered again.** The tombstone holds a keyed hash of it, and every path that creates a user checks that registry. An attempt to re-register the address returns the ordinary "email already taken" conflict — byte for byte the same answer as any duplicate — so nothing about the deletion leaks. The tombstone itself is retained for `AccountDeletion:IdentifierReservationDays`, which the shipped configuration sets to 1095 days.
*In code:* `Auth/Auth_API/Modules/UserManagement/Controllers/UsersController.cs:195`; the rules are in `Auth/Auth.Application/Features/Users/HardDeleteUser/HardDeleteUserCommandHandler.cs:43-86`, the purge itself in `Auth/Auth.Infrastructure/Persistence/UserRepository.cs`, and the address check in `Auth/Auth.Application/Features/Users/Common/IdentifierReservationGuard.cs`.

#### POST `/api/v1/users/{id}/roles`

Assign a role to a user.

**Permission:** `users:manage-roles`

**Request:**

```json
{
  "roleId": "role-guid",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

| Field | Description |
|---|---|
| `expiresAt` | Optional; if set, the role assignment expires at this time |

**Response:** 204 No Content

#### GET `/api/v1/users/{id}/roles`

Get all roles assigned to a user.

**Permission:** `users:read`

**Response (200):**

```json
[
  {
    "roleId": "role-guid",
    "roleName": "Admin",
    "roleCode": "admin",
    "applicationId": null,
    "assignedAt": "2026-01-01T00:00:00Z",
    "expiresAt": null
  }
]
```

#### GET `/api/v1/users/{id}/organizations`

The organizations this user is a member of.

**Permission:** `users:read`

**Query parameters:** `sortBy`, `sortDirection`. Both optional; the allow-list rule from the top of [Section 5](#5-api-reference) applies.

**Response (200):** an array of organization summaries. Not paged.

#### GET `/api/v1/users/{id}/applications`

The applications this user can reach, whether through an organization or through an individual invitation.

**Permission:** `users:read`

**Query parameters:** `sortBy`, `sortDirection`.

**Response (200):** an array. Not paged.

#### DELETE `/api/v1/users/{id}/roles/{roleId}`

Remove a role from a user.

**Permission:** `users:manage-roles`

**Response:** 204 No Content

#### GET `/api/v1/users/{id}/permissions`

Get all permissions for a user (direct + inherited from roles).

**Permission:** `users:read`

**Response (200):**

```json
[
  {
    "permissionId": "perm-guid",
    "permissionCode": "users:read",
    "permissionName": "Read Users",
    "source": "direct",
    "applicationId": null,
    "expiresAt": null
  }
]
```

#### POST `/api/v1/users/{id}/permissions`

Grant a permission directly to a user.

**Permission:** `users:manage-permissions`

**Request:**

```json
{
  "permissionId": "perm-guid",
  "applicationId": "app-guid",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

**Response:** 204 No Content

#### DELETE `/api/v1/users/{id}/permissions/{permissionId}`

Revoke a directly granted permission from a user.

**Permission:** `users:manage-permissions`

**Response:** 204 No Content

#### POST `/api/v1/users/{id}/lock`

Lock a user account.

**Permission:** `users:manage`

**Request:**

```json
{
  "reason": "Suspicious activity detected",
  "lockDurationMinutes": 60
}
```

| Field | Description |
|---|---|
| `lockDurationMinutes` | Optional; if omitted, the account is locked indefinitely until manually unlocked |

**Response:** 204 No Content

#### POST `/api/v1/users/{id}/unlock`

Unlock a locked user account.

**Permission:** `users:manage`

**Response:** 204 No Content

#### POST `/api/v1/users/{id}/activate`

Activate a deactivated user account.

**Permission:** `users:manage`

**Response:** 204 No Content

#### POST `/api/v1/users/{id}/deactivate`

Deactivate a user account.

**Permission:** `users:manage`

**Response:** 204 No Content

#### GET `/api/v1/users/me`

Get the authenticated user's own profile.

**Permission:** None (authentication only)

**Response (200):** `UserDto`

#### PUT `/api/v1/users/me`

Update the authenticated user's own profile.

**Permission:** None (authentication only)

**Request:**

```json
{
  "firstName": "John",
  "lastName": "Doe",
  "displayName": "Johnny",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "America/New_York"
}
```

All fields are optional.

**Response (200):** `UserDto`

#### PUT `/api/v1/users/me/profile-image`

Set the caller's own profile picture.

**Permission:** None (authentication only)

**This is a two-step operation, and this endpoint is the second step.** First upload the file to `POST /api/v1/Images`, which stores it and answers with `{ key, url }`. Then send that `key` here. This endpoint does not accept a file.

**Request:**

```json
{
  "imageKey": "the key returned by the image upload"
}
```

**Response:** 204 No Content

#### DELETE `/api/v1/users/me/profile-image`

Remove the caller's own profile picture.

**Permission:** None (authentication only)

**Response:** 204 No Content

#### GET `/api/v1/users/me/ui-preferences`

The caller's own display settings — today, the saved column layouts of the console's tables. These are the caller's own view settings and nobody else's, which is why they need no permission.

**Permission:** None (authentication only)

**Response (200):** a flat map of key to value.

```json
{
  "table:users": "{\"columnOrder\":[\"email\",\"status\"]}"
}
```

#### PUT `/api/v1/users/me/ui-preferences/{key}`

Store or replace one display setting. The key travels in the path; the value is an opaque string the server never interprets.

**Permission:** None (authentication only)

**This endpoint is deliberately fenced in, because any signed-in caller can write to it.** Without limits it would be a general-purpose storage service behind a login. Three rules apply, and breaking any of them returns 400:

- The key must match `table:` followed by 1 to 60 lowercase letters, digits or hyphens — for example `table:users`. No other namespace is accepted.
- The key is at most 100 characters and the value at most 4000.
- One account may hold at most 100 keys. The 101st new key is refused.

**Request:**

```json
{
  "value": "{\"columnOrder\":[\"email\",\"status\"]}"
}
```

**Response:** 204 No Content

#### DELETE `/api/v1/users/me/ui-preferences/{key}`

Remove one display setting.

**Permission:** None (authentication only)

**Response:** 204 No Content

#### PUT `/api/v1/users/{id}/profile-image`

Set another user's profile picture, as an administrator. Same two-step upload as the self-service version above.

**Permission:** `users:update`

**Request:**

```json
{
  "imageKey": "the key returned by the image upload"
}
```

**Response:** 204 No Content

#### DELETE `/api/v1/users/{id}/profile-image`

Remove another user's profile picture.

**Permission:** `users:update`

**Response:** 204 No Content

#### POST `/api/v1/users/me/deletion/send-code`

Email the caller a six-digit code, which the next endpoint requires. Call this one first.

**Permission:** None (authentication only) | **Rate Limited:** `login` policy

**Response:** 202 Accepted

#### POST `/api/v1/users/me/deletion`

Ask to delete one's own account, from inside the application.

**Permission:** None (authentication only) | **Rate Limited:** `login` policy

**Being signed in is not enough.** The caller must also supply the code emailed by `send-code` above. That is a fresh proof of mailbox possession, demanded of every account — including accounts that sign in through Google or Apple and have no password to re-enter.

**Request:**

```json
{
  "otpCode": "123456"
}
```

**Response (202):**

```json
{
  "graceEndsAtUtc": "2026-04-11T10:00:00Z"
}
```

**What happens immediately, and what happens later.** On success the account is deactivated at once and every session is revoked — the caller is signed out everywhere. The account is then destroyed only after the grace window closes, at the moment given in `graceEndsAtUtc`, which is `AccountDeletion:GraceDays` (shipped value: 30) after the request. Until then the person can undo it with `POST /api/v1/auth/deletion/recover`.

---

### 5.5 Roles

**Base route:** `/api/v1/roles`

A role is a named bundle of permissions. Assigning a role to a person is how they get permissions in bulk, instead of one at a time.

**A role either belongs to one application or belongs to none.** A role with no application is global and applies everywhere; a role with an application is scoped to it. The database column is nullable for exactly this reason, and the unique constraint is on the pair (code, application), so two different applications may each have a role coded `ADMIN`.
*In code:* `Auth/Auth_DB/dbo/Tables/Core/Roles.sql`.

**A role created through this API has its code stored upper-cased, whatever you send.** Post `editor` and the stored code is `EDITOR`. The upper-casing happens in the domain object, so the value comes back upper-cased in the same response. The eight roles the database seed creates are inserted by SQL directly and therefore keep their lower-case codes — `super-admin`, `admin`, `user-manager`, `auditor`, `user`, `org-owner`, `org-admin`, `org-member`. Both spellings coexist; nothing normalizes the old rows.
*In code:* `Auth/Auth.Domain/Entities/Role.cs:81`.

**All four permission codes this area enforces — `roles:read`, `roles:create`, `roles:update`, `roles:delete` — have no row in a freshly published database**, so on a clean install only a holder of the global `*` permission can call any endpoint here. [Section 11](#11-permission-matrix) explains why and what to do about it.

#### GET `/api/v1/roles`

List roles. This endpoint is **not paged** — the response is a plain JSON array.

**Permission:** `roles:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `applicationId` | Guid | not set | Return only the roles belonging to this application. **Leaving it out returns every role on the platform**, global and application-scoped alike — it does not mean "global roles only" |
| `sortBy` | string | null | One of `name`, `code`, `description`, `isSystem`, `isActive`, `createdAt`, `modifiedAt`. Anything else returns 400 |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**Response (200).** Each item carries its permissions as an array of permission **codes**, not identifiers, and not a count:

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "applicationName": "Customer Relationship Manager",
    "code": "EDITOR",
    "name": "Content Editor",
    "description": "Can edit and publish content",
    "isSystem": false,
    "isActive": true,
    "level": 0,
    "createdAt": "2026-01-01T00:00:00Z",
    "createdBy": "00000000-0000-0000-0000-000000000001",
    "createdByName": "System",
    "permissions": ["crm:leads:read", "crm:leads:update"]
  }
]
```

Two fields need a warning. **`level` is always `0` on a role** — the property exists on the object and is never assigned, so it is meaningless here; do not build anything on it. `applicationName`, `modifiedAt`, `modifiedBy` and `modifiedByName` are present only when they have values, because null properties are omitted from every response body in this API.
*In code:* `Auth/Auth.Application/DTOs/RoleDto.cs`; the projection that leaves `level` unset is `Auth/Auth.Application/Features/Roles/GetRoles/GetRolesQueryHandler.cs:59-83`.

#### GET `/api/v1/roles/{id}`

Get one role.

**Permission:** `roles:read`

**Response (200):** a single `RoleDto`, the same shape as one item of the list above.

#### GET `/api/v1/roles/{id}/users`

The people who currently hold this role, paged. Use it before deleting a role, to see who is about to lose it.

**Permission:** `roles:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (default 20), `searchTerm`, `sortBy` — one of `email`, `firstName`, `lastName`, `displayName`, `status`, `lastLoginAt`, `createdAt` — and `sortDirection`.

**Response (200).** The array is called `users`. Each row says **how** the person got the role, which matters because removing a direct assignment does not remove one that arrives through an organization:

```json
{
  "users": [
    {
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "user@example.com",
      "firstName": "John",
      "lastName": "Doe",
      "fullName": "John Doe",
      "status": "Active",
      "lastLoginAt": "2026-03-12T14:30:00Z",
      "createdAt": "2026-01-01T00:00:00Z",
      "assignmentSource": "direct",
      "organizationNames": null
    }
  ],
  "totalCount": 12,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

`assignmentSource` is one of `"direct"`, `"organization"` or `"both"`. When an organization is involved, `organizationNames` lists them, comma-separated.
*In code:* `Auth/Auth.Application/DTOs/RoleUserDto.cs`.

#### GET `/api/v1/roles/{id}/applications`

The applications this role relates to — the one that owns it, plus any application the role has actually been assigned in. Not paged.

**Permission:** `roles:read`

**Response (200):**

```json
[
  {
    "applicationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "code": "CRM",
    "name": "Customer Relationship Manager",
    "logoUrl": null,
    "isActive": true,
    "relationship": "owner"
  }
]
```

`relationship` is `"owner"` (the application the role belongs to), `"assigned"` (the role is used there) or `"both"`.

#### POST `/api/v1/roles`

Create a role.

**Permission:** `roles:create`

**Request:**

```json
{
  "applicationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "code": "editor",
  "name": "Content Editor",
  "description": "Can edit and publish content",
  "permissionIds": ["a1b2c3d4-0000-0000-0000-000000000001"]
}
```

| Field | Required | Description |
|---|---|---|
| `applicationId` | yes | The application the role belongs to. The field is a plain identifier, not a nullable one: **there is no request body that creates a global role.** Global roles exist in the database and are created by the seed scripts, not through this endpoint |
| `code` | yes | Unique within that application. Stored upper-cased |
| `name` | yes | Display name |
| `description` | no | Free text |
| `permissionIds` | no | Permissions to attach immediately. **An identifier that matches no permission is silently skipped** — the role is still created, just without that permission |

**Response (201):** a `RoleDto`. Its `permissions` array lists only the permissions that were actually attached, so compare it against what you sent.

**A duplicate code is refused** with the error code `Role.DuplicateCode`.
*In code:* `Auth/Auth.Application/Features/Roles/CreateRole/CreateRoleCommandHandler.cs:31-61`.

#### PUT `/api/v1/roles/{id}`

Change a role's display name or description. **This endpoint cannot change the code, the application or the permissions** — those are fixed at creation, and permissions are managed through the user and organization endpoints.

**Permission:** `roles:update`

**Request:**

```json
{
  "name": "Senior Editor",
  "description": "Can edit, publish, and approve content"
}
```

**Response (200):** `RoleDto`

#### DELETE `/api/v1/roles/{id}`

Delete a role.

**Permission:** `roles:delete`

**Response:** 204 No Content

**A role marked `isSystem` cannot be deleted** — the request comes back 403 with the error code `Role.CannotDeleteSystemRole`. All eight seeded roles are system roles. Check `GET /api/v1/roles/{id}/users` first: deleting a role removes it from everyone who held it.
*In code:* `Auth/Auth.Application/Features/Roles/DeleteRole/DeleteRoleCommandHandler.cs:32-38`.

---

### 5.6 Permissions

**Base route:** `/api/v1/permissions`

A permission is a single named right, such as `users:read`. Endpoints check permission codes; roles are just a convenient way to hand out several at once.

**All five codes this area enforces — `permissions:read`, `permissions:create`, `permissions:update`, `permissions:delete`, `permissions:manage` — have no row in a freshly published database.** That produces a deadlock worth knowing about before you start: the endpoint that would create the missing rows is itself guarded by `permissions:create`, which is one of the missing rows. On a clean install the only caller who can break the deadlock is a holder of the global `*` permission — in practice the seeded `super-admin` role. [Section 11](#11-permission-matrix) sets out the whole picture.

#### GET `/api/v1/permissions`

List permissions. **Not paged** — the response is a plain JSON array.

**Permission:** `permissions:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `applicationId` | Guid | not set | Return only the permissions belonging to this application. **Leaving it out returns every permission on the platform**, not just the global ones |
| `sortBy` | string | null | One of `name`, `code`, `description`, `level`, `isWildcard`, `isActive`, `createdAt`, `modifiedAt`. Anything else returns 400 |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**Response (200):**

```json
[
  {
    "id": "a1b2c3d4-0000-0000-0000-000000000001",
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "applicationName": "Customer Relationship Manager",
    "code": "crm:leads:read",
    "name": "Read Leads",
    "description": "View CRM leads",
    "level": 3,
    "isWildcard": false,
    "isActive": true,
    "createdAt": "2026-01-01T00:00:00Z",
    "createdBy": "00000000-0000-0000-0000-000000000001",
    "createdByName": "System"
  }
]
```

`parentId`, `modifiedAt`, `modifiedBy` and `modifiedByName` appear when they have values.
*In code:* `Auth/Auth.Application/DTOs/PermissionDto.cs`.

**Do not trust `level` on a seeded row.** For a permission created through this API the value is computed from the code, but the value on an existing row is simply whatever was written when the row was inserted, and the seed scripts do not follow the computed rule — `org:read` has one colon yet is seeded with `level` 3. The field is display metadata either way; nothing in the authorization path reads it.
*In code:* `Auth/Auth_DB/dbo/Scripts/SeedData/07_OrganizationRolesPermissions.sql:56`.

#### GET `/api/v1/permissions/{id}`

Get one permission.

**Permission:** `permissions:read`

**Response (200):** a single `PermissionDto`.

#### GET `/api/v1/permissions/{id}/users`

The people who currently hold this permission, paged — and, importantly, **how** each of them holds it.

**Permission:** `permissions:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (default 20), `searchTerm`, `sortBy` — one of `email`, `firstName`, `lastName`, `displayName`, `status`, `lastLoginAt`, `createdAt` — and `sortDirection`.

**Response (200).** The array is called `users`. The three `via` flags are independent and more than one can be true at once:

```json
{
  "users": [
    {
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "user@example.com",
      "fullName": "John Doe",
      "status": "Active",
      "createdAt": "2026-01-01T00:00:00Z",
      "viaDirect": false,
      "viaOrganization": false,
      "viaRole": true,
      "roleNames": "Content Editor"
    }
  ],
  "totalCount": 7,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

Revoking the permission from one source does not revoke it from the others. `roleNames` lists the roles that carry it, comma-separated, and is present only when `viaRole` is true.
*In code:* `Auth/Auth.Application/DTOs/PermissionUserDto.cs`.

#### POST `/api/v1/permissions`

Create a permission.

**Permission:** `permissions:create`

**Request:**

```json
{
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "code": "crm:leads:read",
  "name": "Read Leads",
  "description": "View CRM leads",
  "parentId": null
}
```

| Field | Required | Description |
|---|---|---|
| `applicationId` | yes | The application the permission belongs to |
| `code` | yes | **Stored lower-cased and trimmed.** Only lower-case letters, digits, colons and asterisks are accepted; anything else returns 400 with `PermissionCode.InvalidFormat`. Maximum 200 characters |
| `name` | yes | Display name |
| `description` | no | Free text |
| `parentId` | no | Another permission's identifier, recorded for display in the console. It has no effect on who can call what |

**Response (201):** `PermissionDto`. A duplicate code in the same application is refused with `Permission.DuplicateCode`.

**Two fields on the response are computed for you and cannot be sent.** `level` is the number of colon-separated segments in the code — `*` is level 0, `users` is 1, `users:read` is 2, `crm:leads:read` is 3 — and `isWildcard` is true when the code is exactly `*` or ends in `:*`.
*In code:* `Auth/Auth.Domain/ValueObjects/PermissionCode.cs:28-31,99-103`.

**The naming convention is real; the hierarchy is not enforced by it.** Writing codes as `area:resource:action` keeps them readable and makes the console's grouping useful, but `level` and `parentId` are descriptive columns. What actually decides access is the matching rule in [4.4](#44-permission-based-authorization): an exact match, or a held code ending in `:*` whose prefix matches. **One `:*` covers the whole subtree at every depth** — a token holding `crm:*` satisfies `crm:leads:read` and `crm:leads:approve:bulk` alike — and a code without `:*` grants nothing below it, so holding `crm:leads` does not satisfy `crm:leads:read`.

#### PUT `/api/v1/permissions/{id}`

Change a permission's display name or description. **The code cannot be changed** — create a new permission instead, because every token and every seed row refers to the code.

**Permission:** `permissions:update`

**Request:**

```json
{
  "name": "View Leads",
  "description": "View CRM leads and their details"
}
```

**Response (200):** `PermissionDto`

#### DELETE `/api/v1/permissions/{id}`

Delete a permission.

**Permission:** `permissions:delete`

**Response:** 204 No Content

**One permission is protected: the global wildcard `*`.** Deleting it returns 403 with the error code `Permission.CannotDeleteSystemPermission`. Every other permission, including seeded ones, can be deleted — check `GET /api/v1/permissions/{id}/users` first.
*In code:* `Auth/Auth.Application/Features/Permissions/DeletePermission/DeletePermissionCommandHandler.cs:34-38`.

#### The three implication endpoints, and what they do not do

**An implication is a note that one permission is meant to cover another. It is recorded, it is displayed, and it grants nothing.** The list of permissions baked into an access token is a flat union of the codes granted by the holder's roles and the codes granted to them directly; no query walks the implication table, at sign-in or at any other time. Holding `users:manage` therefore does **not** let you call an endpoint that requires `users:read` — that call returns 403. If you want someone to have both, grant both, or grant a code ending in `:*`.
*In code:* the token's permission list is built by `Auth/Auth.Infrastructure/Persistence/PermissionRepository.cs:133-158` — a single `UNION`, with no implication join.

The nineteen implication rows the database seeds are in the same position: recorded, shown in the console's permission screen, and inert.

#### GET `/api/v1/permissions/{id}/implications`

The permissions recorded as implied by this one. Not paged.

**Permission:** `permissions:read`

**Response (200):** an array of `PermissionDto`, sorted by `code` unless you pass `sortBy` (`name`, `code`, `description`, `level`, `isWildcard`, `isActive`, `createdAt`, `modifiedAt`) and `sortDirection`.

#### POST `/api/v1/permissions/{id}/implications`

Record that this permission implies another.

**Permission:** `permissions:manage`

**Request:**

```json
{
  "impliedPermissionId": "a1b2c3d4-0000-0000-0000-000000000002"
}
```

**Response:** 201 Created, with no body.

Three refusals are possible: either identifier matching no permission gives `Permission.NotFound`; a pair that already exists gives 409 `Permission.AlreadyGranted`; and a pair that would close a loop gives 400 `Permission.CircularImplication`.

#### DELETE `/api/v1/permissions/{id}/implications/{impliedId}`

Remove a recorded implication.

**Permission:** `permissions:manage`

**Response:** 204 No Content

---

### 5.7 Applications

**Base route:** `/api/v1/applications`

An application is a system that uses this platform for identity — a website, a mobile app, an internal service. Registering one gives it a `code` (which doubles as its public client identifier in the sign-in flow), a redirect-URI allowlist, and its own roles and permissions.

**Two independent switches decide who may sign in, and they are not the same thing.** `isActive` answers "is this application switched on at all?" and beats everything: an inactive application admits nobody. `accessMode` is consulted only for an application that is already active, and has two values — `"Everyone"`, meaning any authenticated platform user may sign in, and `"Restricted"`, meaning only users on the application's own access list may. **`Restricted` is the default for a newly created application**, so an application you create and forget to populate admits nobody but leaves no error to explain why.
*In code:* `Auth/Auth.Domain/Enums/ApplicationAccessMode.cs`.

**Five fields on this object are stored and returned but change nothing.** They round-trip through create, update, the response and the sort allow-list, and no sign-in path reads them: `allowSelfRegistration`, `requireTwoFactor`, `requireEmailVerification`, `sessionTimeoutMinutes` and `maxConcurrentSessions`. The only concurrent-session cap that is applied is the platform-wide `Session:MaxConcurrentSessions` setting. Do not build a security expectation on any of the five.
*In code:* the enforced cap is read in `Auth/Auth.Application/Features/Authentication/Common/LoginResponseBuilder.cs:102`; the entity's own comment on `MaxConcurrentSessions` says "Stored, never enforced" (`Auth/Auth.Domain/Entities/Application.cs:79-95`).

All four codes this area enforces — `applications:read`, `applications:create`, `applications:update`, `applications:delete` — have no row in a freshly published database. See [Section 11](#11-permission-matrix).

#### GET `/api/v1/applications`

List applications, paged.

**Permission:** `applications:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | integer | 1 | Which page |
| `pageSize` | integer | **20** | Between 1 and 100; anything outside returns 400 |
| `searchTerm` | string | null | Matches name and code. The parameter is `searchTerm`, not `search` |
| `isActive` | boolean | not set | Leave it out to get both active and inactive |
| `sortBy` | string | null | One of `name`, `code`, `description`, `baseUrl`, `contactEmail`, `status`, `isActive`, `allowSelfRegistration`, `requireTwoFactor`, `requireEmailVerification`, `sessionTimeoutMinutes`, `maxConcurrentSessions`, `createdAt`, `modifiedAt` |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**Response (200).** The array is called `applications`:

```json
{
  "applications": [
    {
      "id": "8b1d9f20-1111-2222-3333-444455556666",
      "code": "CRM",
      "name": "Customer Relationship Manager",
      "description": "Lead and contact management",
      "baseUrl": "https://crm.example.com",
      "contactEmail": "crm@example.com",
      "isActive": true,
      "accessMode": "Restricted",
      "allowSelfRegistration": false,
      "requireTwoFactor": false,
      "requireEmailVerification": true,
      "sessionTimeoutMinutes": 60,
      "maxConcurrentSessions": 5,
      "redirectUris": [],
      "createdAt": "2026-01-01T00:00:00Z",
      "createdBy": "00000000-0000-0000-0000-000000000001"
    }
  ],
  "totalCount": 3,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

**`redirectUris` is deliberately empty in this list.** It is filled in only when you read a single application, because the list query does not join the redirect-URI table. Do not conclude from an empty array here that an application has no redirect URIs.
*In code:* `Auth/Auth.Application/DTOs/ApplicationDto.cs:45-49`.

#### GET `/api/v1/applications/{clientId}/public-branding`

The name and logo to show on a sign-in screen, for a person who is not signed in yet. **This is the one anonymous endpoint in this area** — the accounts application calls it while handling an authorization request, before any token exists.

**Auth:** Anonymous. No token, no permission.

**Path parameter:** `clientId` is the application's `code`, not its identifier.

**Response (200).** Two fields only, on purpose — nothing else about the application is exposed to an anonymous caller:

```json
{
  "name": "Customer Relationship Manager",
  "logoUrl": "https://auth.example.com/uploads/app-logos/crm.png"
}
```

**An unknown code and a switched-off application both return 404**, with the same body, so an anonymous caller cannot use this endpoint to discover which applications exist.
*In code:* `Auth/Auth_API/Modules/ApplicationManagement/Controllers/ApplicationsController.cs:68-85`.

#### GET `/api/v1/applications/{id}`

Get one application. This is the read that returns a populated `redirectUris` array.

**Permission:** `applications:read`

**Response (200):** a single `ApplicationDto`.

#### GET `/api/v1/applications/{id}/roles`

The roles that belong to this application. Not paged.

**Permission:** `applications:read`

**Response (200):** an array of `RoleDto`, the shape shown in [5.5](#55-roles). `sortBy` and `sortDirection` use the role allow-list.

#### GET `/api/v1/applications/{id}/permissions`

The permissions that belong to this application. Not paged.

**Permission:** `applications:read`

**Response (200):** an array of `PermissionDto`, the shape shown in [5.6](#56-permissions).

#### GET `/api/v1/applications/{id}/users`

The people attached to this application, paged.

**Permission:** `applications:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (default 20), `searchTerm`, `sortBy` — one of `email`, `firstName`, `lastName`, `displayName`, `status`, `lastLoginAt`, `createdAt` — and `sortDirection`.

**Response (200).** The array is called `users`:

```json
{
  "users": [
    {
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "user@example.com",
      "fullName": "John Doe",
      "status": "Active",
      "createdAt": "2026-01-01T00:00:00Z",
      "roleNames": "Content Editor",
      "accessSource": "grant"
    }
  ],
  "totalCount": 12,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

**This is a roster, not an admission list, and the difference bites in both directions.** `accessSource` says why the person appears — `"grant"` (on the access list), `"direct"` (holds an application-scoped role), `"organization"` (holds one through an organization) or `"multiple"`. For a `Restricted` application only a `grant` actually lets someone in, so a row showing `direct` is not proof of access; for an `Everyone` application people sign in who never appear on this list at all.
*In code:* `Auth/Auth.Application/DTOs/ApplicationUserDto.cs:9-12`.

#### GET `/api/v1/applications/{id}/organizations`

The organizations that have enabled this application, paged. Organizations whose link is inactive are included, so an administrator can see disabled tenants.

**Permission:** `applications:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (default 20), `searchTerm`, `sortBy` — one of `name`, `code`, `enabledAt`, `expiresAt`, `isActive`, `organizationIsActive`, `memberCount` — and `sortDirection`.

**Response (200).** The array is called `organizations`. Note the two separate active flags: `isActive` is the enablement link, `organizationIsActive` is the organization itself:

```json
{
  "organizations": [
    {
      "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "code": "acme-corp",
      "name": "Acme Corporation",
      "organizationIsActive": true,
      "isActive": true,
      "enabledAt": "2026-01-01T00:00:00Z",
      "memberCount": 25
    }
  ],
  "totalCount": 4,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

#### POST `/api/v1/applications`

Register an application.

**Permission:** `applications:create`

**Request:**

```json
{
  "code": "CRM",
  "name": "Customer Relationship Manager",
  "description": "Lead and contact management",
  "baseUrl": "https://crm.example.com",
  "logoUrl": "https://crm.example.com/logo.png",
  "contactEmail": "crm@example.com",
  "allowSelfRegistration": false,
  "requireTwoFactor": false,
  "requireEmailVerification": false,
  "sessionTimeoutMinutes": 60,
  "maxConcurrentSessions": 5,
  "redirectUris": ["https://crm.example.com/signin-callback"],
  "reauthenticationMaxAgeMinutes": null,
  "accessMode": "Restricted"
}
```

| Field | Required | Default if omitted | Description |
|---|---|---|---|
| `code` | yes | — | Unique. Also the public client identifier used by `/api/v1/auth/authorize` and by the public-branding endpoint |
| `name` | yes | — | Display name |
| `description`, `baseUrl`, `logoUrl`, `contactEmail` | no | null | Descriptive |
| `allowSelfRegistration`, `requireTwoFactor`, `requireEmailVerification` | no | `false` | Stored, returned — **and read by nothing** |
| `sessionTimeoutMinutes` | no | `60` | Stored, returned — **read by nothing** |
| `maxConcurrentSessions` | no | `5` | Stored, returned — **read by nothing**; the platform-wide setting is the one that applies |
| `redirectUris` | no | empty | Exact-match allowlist for the authorization-code flow. A redirect URI that is not on this list is rejected |
| `reauthenticationMaxAgeMinutes` | no | null | **This one is enforced.** When set, an authorization request for this application is honoured only if the person signed in within that many minutes; an older single-sign-on session is made to sign in again. Null disables the check |
| `accessMode` | no | `"Restricted"` | `"Everyone"` or `"Restricted"` |

**There is no `isActive` field here, and its absence is deliberate.** Switching an application on or off has its own two endpoints, so that a full-object update assembled from stale client state — say, while uploading a logo — can never switch a deactivated application back on as a side effect.
*In code:* `Auth/Auth_API/Modules/ApplicationManagement/Contracts/CreateApplicationRequest.cs:5-10`.

**Response (201):** `ApplicationDto`. A duplicate code returns 409.

#### PUT `/api/v1/applications/{id}`

Update an application. Same fields as create **except `code`, which cannot be changed**, and still no `isActive`.

**Permission:** `applications:update`

**Response (200):** `ApplicationDto`

#### DELETE `/api/v1/applications/{id}`

Delete an application.

**Permission:** `applications:delete`

**Response:** 204 No Content

**Two things refuse the delete, both with 409.** An application that still has active user assignments returns `Application.HasActiveUsers`; one that is still enabled for at least one organization returns `Application.HasActiveOrganizations`. Clear both first, or deactivate the application instead of deleting it.
*In code:* `Auth/Auth.Application/Features/Applications/DeleteApplication/DeleteApplicationCommandHandler.cs:38-43`.

#### POST `/api/v1/applications/{id}/activate`

Switch the application on. Its access mode is left exactly as it was, so an application that was `Restricted` before is `Restricted` after.

**Permission:** `applications:update`

**Response:** 204 No Content

#### POST `/api/v1/applications/{id}/deactivate`

Switch the application off. **This is not only a flag change: every refresh token and every session for this application is revoked immediately**, so people using it are signed out rather than continuing until their tokens expire. Nobody signs in or refreshes while it is off, whatever the access mode says.

**Permission:** `applications:update`

**Response:** 204 No Content

#### GET `/api/v1/applications/{id}/access`

The application's access list — the users individually invited to it. This is the list that decides admission when `accessMode` is `"Restricted"`. Not paged.

**Permission:** `applications:read`

**Response (200):**

```json
[
  {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "fullName": "John Doe",
    "status": "Active",
    "grantedAt": "2026-02-01T09:00:00Z",
    "grantedBy": "00000000-0000-0000-0000-000000000001",
    "grantedByName": "Platform Admin",
    "expiresAt": null,
    "note": "Pilot group"
  }
]
```

`expiresAt` null means the invitation stands until it is withdrawn.

#### POST `/api/v1/applications/{id}/access`

Invite one user to the application.

**Permission:** `applications:update`

**Request:**

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "roleId": "a1b2c3d4-0000-0000-0000-000000000009",
  "expiresAt": "2027-01-01T00:00:00Z",
  "note": "Pilot group"
}
```

| Field | Required | Description |
|---|---|---|
| `userId` | yes | Who to invite |
| `roleId` | no | A role scoped to this application, assigned at the same time. **The invitation only opens the door** — without a role the invitee signs in able to do nothing, so supplying one here saves a second call |
| `expiresAt` | no | When the invitation lapses on its own |
| `note` | no | Free text, e.g. which trial this is for |

**Response:** 204 No Content. Inviting someone who is already on the list returns 409.

#### DELETE `/api/v1/applications/{id}/access/{userId}`

Withdraw one user's invitation. **Their tokens and sessions for this application are revoked immediately**; their access to every other application is untouched.

**Permission:** `applications:update`

**Response:** 204 No Content

---

### 5.8 Organizations

**Base route:** `/api/v1/organizations`

An organization is a tenant: a group of people, with its own membership roles, that enables applications for itself. Twenty-three endpoints, and they divide into five groups — the organization itself, ownership, members, invitations, and enabled applications.

**Two different families of permission code appear in this section, and they behave differently.** The `org:` codes — `org:update`, `org:members:read`, `org:apps:manage`, and so on — are **organization-scoped**: they are checked against the caller's rights *inside the organization named in the route*, and they are what a member of one organization holds. The `organizations:` codes — `organizations:read` and `organizations:manage` — are **platform-wide**: they let a platform administrator act on any organization. [Section 4.4](#44-permission-based-authorization) explains the mechanism, including the live-membership fallback.

**A trap worth reading before you design roles.** The seeded `org-admin` role does **not** hold `org:update`. It holds `org:read`, `org:members:*`, `org:apps:*` and `org:permissions:*`. Only `org-owner`, whose grant is the single wildcard `org:*`, can rename an organization or change its details. If an administrator reports that saving the organization's name gives them 403, this is why.
*In code:* `Auth/Auth_DB/dbo/Scripts/SeedData/07_OrganizationRolesPermissions.sql:204-260`.

**Whoever creates an organization is made its owner**, and receives the `org-owner` role in it. That includes the organization created automatically when someone registers with `createOrganization: true`.
*In code:* `Auth/Auth.Application/Features/Organizations/CreateOrganization/CreateOrganizationCommandHandler.cs:45`; the registration path is `Auth/Auth.Application/Features/Authentication/Common/PersonalOrganizationCreator.cs`.

#### GET `/api/v1/organizations`

The organizations the **caller** belongs to. Not paged, and it is not a platform-wide list — use `/all` for that.

**Auth:** Authenticated. No permission code.

**Query parameters:** `sortBy` — one of `name`, `code`, `roleName`, `memberCount`, `isActive` — and `sortDirection`.

**Response (200).** A summary object, deliberately smaller than the full organization:

```json
[
  {
    "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "code": "acme-corp",
    "name": "Acme Corporation",
    "logoUrl": null,
    "isActive": true,
    "userRole": "Organization Owner",
    "memberCount": 25
  }
]
```

The caller's own role in each organization is the field `userRole`, not `role`.
*In code:* `Auth/Auth.Application/DTOs/OrganizationDto.cs:46-55`.

#### GET `/api/v1/organizations/all`

Every organization on the platform, paged. This is the administrative list.

**Permission:** `organizations:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (default 20), `searchTerm`, `sortBy` — one of `name`, `code`, `contactEmail`, `isActive`, `memberCount`, `createdAt`, `modifiedAt` — and `sortDirection`.

**Response (200).** The array is called `organizations`, and each item is the full `OrganizationDto` shown below.

#### GET `/api/v1/organizations/{id}`

One organization, with its members and its enabled applications included.

**Auth:** Authenticated. Membership normally decides access; a caller who holds the platform code `organizations:read` may read any organization, member or not.

**Response (200):**

```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "code": "acme-corp",
  "name": "Acme Corporation",
  "description": "A leading technology company",
  "website": "https://acme.example.com",
  "contactEmail": "admin@acme.example.com",
  "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "ownerName": "John Doe",
  "ownerEmail": "john@acme.example.com",
  "isActive": true,
  "memberCount": 25,
  "enabledAppCount": 3,
  "createdAt": "2026-01-01T00:00:00Z",
  "createdBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "members": [ /* one object per member */ ],
  "enabledApplications": [ /* one object per enabled application */ ]
}
```

**Those last two arrays are fully populated here, and they are not paged.** Each `members` entry has the shape shown under `GET /{id}/members` below, and each `enabledApplications` entry the shape shown under `GET /{id}/applications`. For an organization with thousands of members this single call returns every one of them, so use the paged member endpoint when you only need a page.

The count of enabled applications is `enabledAppCount`. There is no `applicationCount` field and no `isAutoCreated` field on this object.
*In code:* `Auth/Auth.Application/DTOs/OrganizationDto.cs:6-41`; the projection is `Auth/Auth.Application/Features/Organizations/GetOrganizationById/GetOrganizationByIdQueryHandler.cs:150-151`.

#### POST `/api/v1/organizations`

Create an organization. **Any signed-in person may do this** — there is no permission code on it, by design, because self-service organization creation is a product feature.

**Auth:** Authenticated.

**Request:**

```json
{
  "code": "acme-corp",
  "name": "Acme Corporation",
  "contactEmail": "admin@acme.example.com",
  "description": "A leading technology company",
  "logoUrl": "https://acme.example.com/logo.png",
  "website": "https://acme.example.com"
}
```

`code`, `name` and `contactEmail` are required; the other three are optional. A duplicate code returns 409.

**Response (201):** `OrganizationDto`. The caller is now the owner and holds `org-owner` in it.

#### PUT `/api/v1/organizations/{id}`

Change the organization's details.

**Permission:** `org:update` — which, as noted above, **the seeded `org-admin` role does not have.**

**Request:**

```json
{
  "name": "Acme Corp International",
  "contactEmail": "global@acme.example.com",
  "description": "Updated description",
  "logoUrl": "https://acme.example.com/logo.png",
  "website": "https://acme.example.com",
  "isActive": true
}
```

`name` and `contactEmail` are required; `isActive` is optional and omitting it leaves the current value alone. The `code` cannot be changed.

**Response (200):** `OrganizationDto`

#### DELETE `/api/v1/organizations/{id}`

Delete an organization. Members, enabled applications and invitations go with it, through the database's own cascade rules.

**Auth:** Authenticated — **there is no permission attribute on this endpoint.** Ownership is checked inside the handler instead: the caller must be the organization's owner, *or* hold the platform code `organizations:manage`, which lets them delete any organization. A caller who is neither gets the error code `Organization.NotOwner`.
*In code:* `Auth/Auth.Application/Features/Organizations/DeleteOrganization/DeleteOrganizationCommandHandler.cs:35-39`.

**Response:** 204 No Content

#### POST `/api/v1/organizations/{orgId}/ownership/initiate`

Start handing the organization to somebody else. This sends a six-digit confirmation code by email to the prospective new owner; nothing changes yet.

**Auth:** Authenticated. **Strictly the sitting owner** — the code flow exists to prove the current owner consents, so even a platform administrator is refused here and uses the direct transfer below instead.

**Request:**

```json
{
  "newOwnerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response (200).** The address is masked, so the response never reveals an email the caller might not already know:

```json
{
  "expiresAt": "2026-03-12T14:35:00Z",
  "targetEmailMasked": "j***@acme.example.com"
}
```

**Five rules can refuse it**, each with its own error code: the caller is not the owner (`Organization.NotOwner`); the organization is the personal one created with an account and never changes hands (`Organization.CannotTransferPersonalOrganization`); the target is the current owner (`Organization.CannotTransferToSelf`); the target is not an active, unexpired member, or their account is not active with a confirmed email (`Organization.CannotTransferOwnership` / `Organization.TransferTargetNotEligible`); or too many codes have been requested for this organization recently (`Organization.TooManyTransferRequests`). Issuing a new code invalidates any outstanding one. The code's lifetime is the shared `Email:OtpExpirationMinutes` setting, whose shipped value is 5 minutes.
*In code:* `Auth/Auth.Application/Features/Organizations/InitiateOwnershipTransfer/InitiateOwnershipTransferCommandHandler.cs:61-125`.

**When email is switched off in development, the code is written to the log** rather than lost — look for the warning line beginning `Email disabled - Ownership transfer code`.

#### POST `/api/v1/organizations/{orgId}/ownership`

Complete the transfer.

**Auth:** Authenticated. The owner supplies the code the new owner received. A caller holding the platform code `organizations:manage` may transfer without a code — that is the recovery path for an organization whose owner is gone.

**Request:**

```json
{
  "newOwnerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "code": "418203"
}
```

**Response:** 204 No Content

#### GET `/api/v1/organizations/{id}/members`

The organization's members, paged.

**Permission:** `org:members:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (default 20), `searchTerm`, `sortBy` — one of `name`, `fullName`, `firstName`, `lastName`, `email`, `roleName`, `roleCode`, `isActive`, `joinedAt`, `invitedByName`, `expiresAt` — and `sortDirection`.

**Response (200).** The array is called `members`. `id` is the membership row, not the person — the person is `userId`:

```json
{
  "members": [
    {
      "id": "c0ffee00-0000-0000-0000-000000000001",
      "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "email": "member@acme.example.com",
      "firstName": "John",
      "lastName": "Doe",
      "fullName": "John Doe",
      "roleId": "10000000-0000-0000-0001-000000000002",
      "roleCode": "org-admin",
      "roleName": "Organization Admin",
      "isActive": true,
      "joinedAt": "2026-01-15T00:00:00Z",
      "invitedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "invitedByName": "Jane Roe"
    }
  ],
  "totalCount": 25,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 2,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

#### PUT `/api/v1/organizations/{orgId}/members/{userId}/role`

Change a member's organization-level role — the membership role, one of `org-owner`, `org-admin` or `org-member`, not an application role.

**Permission:** `org:members:manage`

**Request:**

```json
{
  "roleId": "10000000-0000-0000-0001-000000000002"
}
```

**Response (200):** `OrganizationMemberDto`

#### DELETE `/api/v1/organizations/{orgId}/members/{userId}`

Remove a member.

**Permission:** `org:members:manage`

**Response:** 204 No Content

#### GET `/api/v1/organizations/{id}/invitations`

Invitations still outstanding for this organization. Not paged.

**Permission:** `org:members:read`

**Query parameters:** `sortBy` — one of `email`, `roleName`, `roleCode`, `status`, `isExpired`, `invitedByName`, `invitedByEmail`, `acceptedAt`, `createdAt`, `expiresAt` — and `sortDirection`.

**Response (200):**

```json
[
  {
    "id": "d1e2f3a4-0000-0000-0000-000000000001",
    "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "organizationName": "Acme Corporation",
    "email": "invitee@example.com",
    "roleId": "10000000-0000-0000-0001-000000000003",
    "roleCode": "org-member",
    "roleName": "Organization Member",
    "status": "Pending",
    "expiresAt": "2026-03-17T00:00:00Z",
    "isExpired": false,
    "invitedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "invitedByName": "Jane Roe",
    "invitedByEmail": "jane@acme.example.com",
    "createdAt": "2026-03-10T00:00:00Z"
  }
]
```

**The `token` field is absent from this listing on purpose.** The raw invitation token is returned exactly once, in the response to creating or resending the invitation. If it is lost, resend rather than trying to read it back.

#### POST `/api/v1/organizations/{id}/invitations`

Invite somebody by email address. The invitation email is sent from the database-held template; **if the email fails to send, the request still succeeds**, because the token is in the response and an administrator can pass it on by hand.

**Permission:** `org:members:invite`

**Request:**

```json
{
  "email": "invitee@example.com",
  "roleId": "10000000-0000-0000-0001-000000000003",
  "languageCode": "ar"
}
```

| Field | Required | Description |
|---|---|---|
| `email` | yes | Who to invite |
| `roleId` | yes | The organization membership role they will hold |
| `languageCode` | no | Which language to write the email in. Left out, the system uses the invitee's own profile language when they already have an account, and otherwise the language of this request |

**Response (201).** An `OrganizationInvitationDto` — the same shape as the listing above, **plus the one-time `token`**:

```json
{
  "id": "d1e2f3a4-0000-0000-0000-000000000001",
  "token": "y1Zq...redacted...",
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "organizationName": "Acme Corporation",
  "email": "invitee@example.com",
  "roleId": "10000000-0000-0000-0001-000000000003",
  "roleCode": "org-member",
  "roleName": "Organization Member",
  "status": "Pending",
  "expiresAt": "2026-03-17T00:00:00Z",
  "isExpired": false,
  "invitedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "createdAt": "2026-03-10T00:00:00Z"
}
```

**An invitation lasts 7 days**, a constant in the handler rather than a setting.
*In code:* `Auth/Auth.Application/Features/Organizations/InviteMember/InviteMemberCommandHandler.cs:21`.

What the invitee does next is in [5.9](#59-invitations).

#### POST `/api/v1/organizations/{orgId}/invitations/{invitationId}/resend`

Issue a fresh token for an invitation that already exists and email it again. The old token stops working.

**Permission:** `org:members:invite`

**Response (200):** `OrganizationInvitationDto`, again including the new one-time `token`.

#### GET `/api/v1/organizations/{id}/applications`

The applications this organization has enabled. Not paged.

**Permission:** `org:apps:read`

**Query parameters:** `sortBy` — one of `applicationName`, `applicationCode`, `applicationDescription`, `subscriptionTier`, `enabledAt`, `expiresAt`, `isActive`, `assignedUserCount` — and `sortDirection`.

**Response (200):**

```json
[
  {
    "id": "e5f6a7b8-0000-0000-0000-000000000001",
    "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "applicationCode": "CRM",
    "applicationName": "Customer Relationship Manager",
    "isActive": true,
    "enabledAt": "2026-01-01T00:00:00Z",
    "enabledBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "enabledByName": "John Doe",
    "expiresAt": "2027-01-01T00:00:00Z",
    "subscriptionTier": "Enterprise",
    "assignedUserCount": 12
  }
]
```

#### GET `/api/v1/organizations/{id}/applications/available`

The applications this organization could still enable: switched on, open to everyone, and not already enabled here. This feeds the picker in the console, so it returns display fields only.

**Permission:** `org:apps:manage`

**Why this is its own endpoint rather than a filter over `/api/v1/applications`:** that list is guarded by the platform code `applications:read`, which an organization administrator has no reason to hold — and without it the picker would simply come back empty with no explanation.

**Response (200):**

```json
[
  {
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "code": "CRM",
    "name": "Customer Relationship Manager",
    "logoUrl": null
  }
]
```

**An application whose access mode is `Restricted` never appears here**, and the enable call refuses one outright, because a restricted application admits only the users on its own access list.
*In code:* `Auth/Auth.Application/DTOs/OrganizationApplicationDto.cs:24-39`.

#### POST `/api/v1/organizations/{id}/applications`

Enable an application for the organization.

**Permission:** `org:apps:manage`

**Request:**

```json
{
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "subscriptionTier": "Enterprise",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

Only `applicationId` is required. `subscriptionTier` is free text this system stores and reports; it enforces no billing of any kind.

**Response (201):** `OrganizationApplicationDto`. Enabling an application that is already enabled returns 409.

#### PUT `/api/v1/organizations/{id}/applications/{applicationId}`

Change an existing enablement — its tier, its expiry, or whether it is active.

**Permission:** `org:apps:manage`

**Request:**

```json
{
  "subscriptionTier": "Premium",
  "expiresAt": "2027-06-01T00:00:00Z",
  "isActive": true
}
```

All three fields are optional; omitting one leaves it as it is.

**Response (200):** `OrganizationApplicationDto`

#### DELETE `/api/v1/organizations/{id}/applications/{applicationId}`

Disable an application for the organization.

**Permission:** `org:apps:manage`

**Response:** 204 No Content

#### GET `/api/v1/organizations/{orgId}/members/{userId}/roles`

The application-level roles this member holds inside this organization. These are different from the membership role: a membership role says what you may do *to the organization*, an application role says what you may do *in an application* the organization has enabled. Not paged.

**Permission:** `org:permissions:read`

**Response (200):**

```json
[
  {
    "id": "f1a2b3c4-0000-0000-0000-000000000001",
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "applicationCode": "CRM",
    "applicationName": "Customer Relationship Manager",
    "roleId": "a1b2c3d4-0000-0000-0000-000000000009",
    "roleCode": "EDITOR",
    "roleName": "Content Editor",
    "assignedAt": "2026-02-01T09:00:00Z",
    "assignedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "assignedByName": "John Doe",
    "expiresAt": null
  }
]
```

#### POST `/api/v1/organizations/{orgId}/members/{userId}/roles`

Give a member an application-level role inside this organization.

**Permission:** `org:permissions:manage`

**Request:**

```json
{
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "roleId": "a1b2c3d4-0000-0000-0000-000000000009",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

`expiresAt` is optional; leaving it out makes the assignment permanent until removed.

**Response (201):** `OrganizationMemberAppRoleDto`

#### DELETE `/api/v1/organizations/{orgId}/members/{userId}/roles/{roleId}`

Take an application-level role away from a member. **The application is not in the route because it does not need to be** — a role belongs to exactly one application, so the role identifier determines it.

**Permission:** `org:permissions:manage`

**Response:** 204 No Content

#### POST `/api/v1/organizations/{orgId}/members/{userId}/permissions`

Grant a member one permission directly, without a role. Use this for an exception; use roles for everything else.

**Permission:** `org:permissions:manage`

**Request:**

```json
{
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "permissionId": "a1b2c3d4-0000-0000-0000-000000000001",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**Response (201):**

```json
{
  "id": "9a8b7c6d-0000-0000-0000-000000000001",
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "applicationCode": "CRM",
  "applicationName": "Customer Relationship Manager",
  "permissionId": "a1b2c3d4-0000-0000-0000-000000000001",
  "permissionCode": "crm:leads:read",
  "permissionName": "Read Leads",
  "grantedAt": "2026-03-12T10:00:00Z",
  "grantedBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "grantedByName": "John Doe",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**There is no matching revoke endpoint for a directly granted organization permission.** Granting one is a one-way door through this API; removing it means removing the row another way. Prefer an `expiresAt`.

---

### 5.9 Invitations

**Base route:** `/api/v1/invitations`

This is the invitee's side of the flow that [5.8](#58-organizations) starts. Three endpoints, and which two you use depends on one thing: **whether the invited person already has an account.**

- **They have one:** they sign in, then call `POST /{token}/accept`.
- **They do not:** they call `POST /{token}/register`, which creates the account and accepts the invitation in a single step. They do not register through the ordinary registration endpoint first.

Either way, start with `GET /{token}` to show them what they are being invited to — and to find out which of the two paths applies, from the `userExists` field.

**The first two endpoints are anonymous, because possession of the emailed token is the credential.** Both carry the `login` rate-limit policy — 20 requests per 60 seconds per client IP address — so that the token space cannot be searched by brute force.

#### GET `/api/v1/invitations/{token}`

Show what an invitation is for, before anyone accepts it.

**Auth:** Anonymous. Rate limit: `login`.

**Response (200).** Deliberately limited — it names the organization and the role, and nothing else about either:

```json
{
  "id": "d1e2f3a4-0000-0000-0000-000000000001",
  "organizationName": "Acme Corporation",
  "organizationLogoUrl": null,
  "email": "invitee@example.com",
  "roleName": "Organization Member",
  "invitedByName": "Jane Roe",
  "status": "Pending",
  "expiresAt": "2026-03-17T00:00:00Z",
  "isExpired": false,
  "isAlreadyMember": false,
  "userExists": true
}
```

**Two fields decide what your screen should do next.** `userExists` says whether an account already exists for the invited address: true means send them to sign in and then accept, false means send them to the register-through-invitation call. `isExpired` says whether the seven-day window has closed.

**`isAlreadyMember` is always `false`.** The field exists on the object and no code path ever sets it, so it tells you nothing — do not branch on it. If the person is already a member, the accept call says so instead, with the message code `Invitation.AlreadyMember`.
*In code:* `Auth/Auth.Application/Features/Organizations/GetInvitationByToken/GetInvitationByTokenQueryHandler.cs:53-66`.

An unknown or already-used token returns the error code `Organization.InvitationNotFoundByToken`.

#### POST `/api/v1/invitations/{token}/register`

Create an account through the invitation and join the organization in one call. This is the path for somebody who has never used the platform.

**Auth:** Anonymous. Rate limit: `login`.

**Request.** There is no email field — **the address comes from the invitation**, so an invitee cannot redirect the invitation to a different mailbox:

```json
{
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe",
  "preferredLanguage": "en",
  "timeZone": "UTC"
}
```

`password`, `firstName` and `lastName` are required. `preferredLanguage` defaults to `en` and `timeZone` to `UTC`.

**Response (200).** Note this is 200, not 201:

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "invitee@example.com",
  "organizationName": "Acme Corporation",
  "roleName": "Organization Member",
  "message": "Account created and invitation accepted. Please sign in."
}
```

**The new account's email address is already confirmed, and no verification email is sent** — receiving the token at that address is what proves the mailbox belongs to them. **No tokens are returned here**, which is what the message means: the person must now sign in normally.
*In code:* `Auth/Auth_API/Modules/OrganizationManagement/Controllers/InvitationsController.cs:50-80`.

#### POST `/api/v1/invitations/{token}/accept`

Accept an invitation as somebody who is already signed in.

**Auth:** Authenticated. The token identifies the invitation; the bearer token identifies who is accepting it.

**Request:** no body.

**Response (200):**

```json
{
  "success": true,
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "organizationName": "Acme Corporation",
  "roleName": "Organization Member",
  "message": "Successfully joined the organization.",
  "messageCode": "Invitation.Joined"
}
```

There is no `role` field; the field is `roleName`.

**Accepting an invitation you have already accepted is a success, not an error.** The response comes back with `success: true` and the message code `Invitation.AlreadyMember` instead of `Invitation.Joined`, so a client that retries after a dropped connection does not have to treat the second attempt as a failure.

**`messageCode` is the field to branch on, and `message` is the field to display.** These two codes are two of only three places in the whole system where a success message is translated into the caller's language; everywhere else, success messages are plain English. [Section 4.11](#411-localization) explains the mechanism.
*In code:* `Auth/Auth.Application/Features/Organizations/AcceptInvitation/AcceptInvitationCommandHandler.cs:108-146`.

---

### 5.10 API Keys

**Base route:** `/api/v1/apikeys`

An API key is a long-lived secret string that a service uses to identify itself, instead of a person signing in. Each key belongs to one application.

**Read this before you design around the two rate-limit fields.** `rateLimitPerMinute` and `rateLimitPerDay` are accepted, validated, stored, returned in every response and available as sort keys — and **nothing in this system enforces them.** No limiter reads either value. A key you create with `rateLimitPerMinute: 100` is not throttled at 100 requests a minute, or at any other number. If you need per-key throttling, implement it in the service that consumes the key.
*In code:* every occurrence of the two names is a data-transfer object, a command, a validator, a projection or a sort-field constant; the API's only rate limits are the two named policies described at the top of [Section 5](#5-api-reference).

**A plain key looks like `ak_prod_` followed by 32 random characters.** The prefix depends on the `environment` you asked for: `production` gives `ak_prod_`, `staging` gives `ak_stag_`, `development` gives `ak_dev_`, and any other word gives the bare `ak_`. The random part is 32 bytes of cryptographic randomness rendered as base64 with `+`, `/` and `=` stripped, then cut to 32 characters. Only a hash of the whole key is stored — Argon2id, the same algorithm used for passwords — so the plain key exists in exactly one place after creation: wherever you put it.
*In code:* `Auth/Auth.Infrastructure/Security/ApiKeyGenerator.cs`.

All five codes this area enforces have no row in a freshly published database, and one of them — `apikeys:validate` — appears in **no** database script anywhere in the repository, not even in the unused one. See [Section 11](#11-permission-matrix).

#### GET `/api/v1/apikeys`

List API keys. **Not paged** — the response is a plain array.

**Permission:** `apikeys:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `applicationId` | Guid | not set | Narrow to one application. **Optional** — leaving it out returns every application's keys, which is what the console does so the page renders without waiting for a picker, and which also keeps the page usable for a caller who holds `apikeys:read` but not `applications:read` |
| `sortBy` | string | null | One of `name`, `description`, `keyPrefix`, `environment`, `rateLimitPerMinute`, `rateLimitPerDay`, `createdAt`, `expiresAt`, `lastUsedAt`, `revokedAt` |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**Response (200).** The key itself is not here and never will be — only its prefix:

```json
[
  {
    "id": "b2c3d4e5-0000-0000-0000-000000000001",
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "applicationName": "Customer Relationship Manager",
    "name": "Production API Key",
    "description": "Main production key",
    "keyPrefix": "ak_prod_",
    "environment": "production",
    "rateLimitPerMinute": 60,
    "rateLimitPerDay": 10000,
    "createdAt": "2026-01-01T00:00:00Z",
    "createdBy": "00000000-0000-0000-0000-000000000001",
    "createdByName": "Platform Admin",
    "expiresAt": "2027-01-01T00:00:00Z",
    "lastUsedAt": "2026-03-12T14:30:00Z",
    "isRevoked": false,
    "scopes": ["crm:leads:read"]
  }
]
```

`scopes` lists the permission codes attached to the key.
*In code:* `Auth/Auth.Application/DTOs/ApiKeyDto.cs`.

#### POST `/api/v1/apikeys`

Create a key.

**Permission:** `apikeys:create`

**Request:**

```json
{
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "name": "Production API Key",
  "description": "Main production key for the CRM integration",
  "environment": "production",
  "rateLimitPerMinute": 60,
  "rateLimitPerDay": 10000,
  "expiresAt": "2027-01-01T00:00:00Z",
  "permissionIds": ["a1b2c3d4-0000-0000-0000-000000000001"]
}
```

| Field | Required | Default if omitted | Notes |
|---|---|---|---|
| `applicationId` | yes | — | Must exist, or the call returns `Application.NotFound` |
| `name` | yes | — | Display name |
| `description` | no | null | Free text |
| `environment` | no | `"production"` | Any string up to 50 characters is accepted. Only `production`, `staging` and `development` produce a recognisable prefix; anything else gets `ak_` |
| `rateLimitPerMinute` | no | `60` | Must be greater than 0. **Enforced by nothing** |
| `rateLimitPerDay` | no | `10000` | Must be greater than 0. **Enforced by nothing** |
| `expiresAt` | no | null (never expires) | Must be in the future |
| `permissionIds` | no | none | Permission scopes for the key. An identifier matching no permission is silently skipped |

**Response (201).** This is the only moment the plain key exists in a response. There is no `message` field — the four fields below are the whole body:

```json
{
  "id": "b2c3d4e5-0000-0000-0000-000000000001",
  "apiKey": "ak_prod_9fK2mQx7Ld3PvR8sT1uW4yZ6aB0cE5gH",
  "keyPrefix": "ak_prod_",
  "createdAt": "2026-03-12T10:00:00Z",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**Store it now.** It cannot be recovered later, by anyone, including a platform administrator with the global `*` permission. If it is lost, rotate the key.

#### POST `/api/v1/apikeys/{id}/revoke`

Revoke a key immediately.

**Permission:** `apikeys:revoke`

**Request.** The body is optional; sending no body at all is valid:

```json
{
  "reason": "Compromised key detected"
}
```

**Response:** 204 No Content. Revoking a key that is already revoked returns 409 with the error code `ApiKey.AlreadyRevoked`.

#### POST `/api/v1/apikeys/validate`

Check a key and get its metadata back. This is the endpoint a downstream service calls to find out whether a key it was handed is real.

**Permission:** `apikeys:validate` — **and this code exists in no database script in the repository.** On any installation of this system it can only be satisfied by the global `*` permission, which in practice means the `super-admin` role. Plan for that before you design a service around this endpoint.

**Request:**

```json
{
  "apiKey": "ak_prod_9fK2mQx7Ld3PvR8sT1uW4yZ6aB0cE5gH"
}
```

**Response (200):**

```json
{
  "active": true,
  "apiKeyId": "b2c3d4e5-0000-0000-0000-000000000001",
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "name": "Production API Key",
  "environment": "production",
  "scopes": ["crm:leads:read"],
  "rateLimitPerMinute": 60,
  "rateLimitPerDay": 10000
}
```

**A key that does not validate is an error, not `active: false`.** An unknown, revoked or expired key returns **400** with the error code `ApiKey.Invalid`, so branch on the status code rather than on the `active` field, which is `true` in every successful response.

**A successful validation has a side effect:** the key's `lastUsedAt` is updated.

#### POST `/api/v1/apikeys/{id}/rotate`

Issue a replacement key and put the old one on a timer, so a running service can be switched over without an outage.

**Permission:** `apikeys:rotate`

**Request.** Optional; omitting the body gives a 60-minute grace period:

```json
{
  "gracePeriodMinutes": 60
}
```

**What rotation actually does.** A new key is created with the same application, environment, rate-limit values, scopes and expiry as the old one, named `<old name> (rotated)`. The old key is not revoked — it is given an expiry `gracePeriodMinutes` from now, after which it stops working on its own.

**Response (200):**

```json
{
  "newApiKey": "ak_prod_Zx4Nm8Qr2Tv6Yb1Dc9Fg3Hj5Kl7Pw0S",
  "newApiKeyId": "c3d4e5f6-0000-0000-0000-000000000002",
  "newKeyPrefix": "ak_prod_",
  "oldKeyExpiresAt": "2026-03-12T15:30:00Z",
  "oldApiKeyId": "b2c3d4e5-0000-0000-0000-000000000001",
  "message": "New API key generated successfully. Old key will remain valid until 2026-03-12 15:30:00 UTC. Please update your applications to use the new key before the grace period ends.",
  "messageCode": "ApiKey.Rotated"
}
```

**This response is one of only three places in the system where a success message is translated into the caller's language.** `message` arrives already translated, with the old key's expiry substituted into it; `messageCode` is the stable identifier `ApiKey.Rotated` to branch on. Display `message`, never build your own sentence from `messageCode`.
*In code:* `Auth/Auth_API/Modules/ApiKeyManagement/Controllers/ApiKeysController.cs:158-166`.

Rotating a revoked key returns 400 with the error code `ApiKey.AlreadyRevoked`.

---

### 5.10b Webhook Keys

**Base route:** `/api/v1/webhookkeys`

A webhook key is a secret shared with one destination URL, so that the receiver can tell that a call really came from this system. It is shaped almost exactly like an API key — list, create, validate, revoke, rotate — with three differences worth knowing.

1. **It carries a `targetUrl`.** The key is bound to the address it signs for; an API key is not bound to any address.
2. **There are no rate-limit fields.** An API key has two that nothing enforces; a webhook key simply does not have them.
3. **The hash is HMAC-SHA256, not Argon2id.** That is why validation can look the key up directly by hash in one query, while validating an API key has to fetch every candidate sharing a prefix and verify them one by one.
*In code:* `Auth/Auth.Infrastructure/Security/WebhookKeyGenerator.cs`.

**Before you plan anything around this area, two honest warnings.**

**None of the five permission codes these endpoints require — `webhookkeys:read`, `webhookkeys:create`, `webhookkeys:validate`, `webhookkeys:revoke`, `webhookkeys:rotate` — is created by any database script in this repository.** Searching every `.sql` file under `Auth/Auth_DB` for the word `webhookkeys` returns nothing, including the seed script that is on disk but never runs. There is therefore no way to grant these permissions to a role or to a person through ordinary means: the only claim that satisfies them is the global `*`, held by the seeded `super-admin` role. Creating the rows by hand needs `POST /api/v1/Permissions`, which is itself guarded by an unseeded code. In practice: **only `super-admin` can use this area at all.** [Section 11](#11-permission-matrix) has the full picture.

**Creating or revoking a webhook key writes no audit-log entry.** The system publishes a `WebhookKeyCreatedEvent` and a `WebhookKeyRevokedEvent` when those things happen, and **no handler anywhere subscribes to either**, so nothing records them. An API key is different — it has an audit handler. If you rely on the audit trail for key lifecycle, this is a gap you must cover elsewhere.
*In code:* the events are published at `Auth/Auth.Application/Features/WebhookKeys/CreateWebhookKey/CreateWebhookKeyCommandHandler.cs:69` and `.../RevokeWebhookKey/RevokeWebhookKeyCommandHandler.cs:51`; no `INotificationHandler` implementation for either type exists in `Auth/Auth_API`.

#### GET `/api/v1/webhookkeys`

List webhook keys. **Not paged** — a plain array.

**Permission:** `webhookkeys:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `applicationId` | Guid | not set | Narrow to one application. Optional; leaving it out returns every application's keys, mirroring the API-keys endpoint so the two console pages behave identically |
| `sortBy` | string | null | One of `name`, `description`, `keyPrefix`, `targetUrl`, `environment`, `createdAt`, `expiresAt`, `lastUsedAt`, `revokedAt` |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**Response (200):**

```json
[
  {
    "id": "a7b8c9d0-0000-0000-0000-000000000001",
    "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
    "applicationName": "Customer Relationship Manager",
    "name": "Order events",
    "description": "Signs order webhooks",
    "keyPrefix": "wk_prod_",
    "targetUrl": "https://crm.example.com/hooks/orders",
    "environment": "production",
    "createdAt": "2026-01-01T00:00:00Z",
    "createdBy": "00000000-0000-0000-0000-000000000001",
    "createdByName": "Platform Admin",
    "expiresAt": null,
    "lastUsedAt": "2026-03-12T14:30:00Z",
    "isRevoked": false
  }
]
```

There is no `scopes` array on a webhook key.
*In code:* `Auth/Auth.Application/DTOs/WebhookKeyDto.cs`.

#### POST `/api/v1/webhookkeys`

Create a webhook key.

**Permission:** `webhookkeys:create`

**Request:**

```json
{
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "name": "Order events",
  "targetUrl": "https://crm.example.com/hooks/orders",
  "description": "Signs order webhooks",
  "environment": "production",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

| Field | Required | Default if omitted | Notes |
|---|---|---|---|
| `applicationId` | yes | — | Must exist, or the call returns `Application.NotFound` |
| `name` | yes | — | Display name |
| `targetUrl` | yes | — | Must be a valid absolute URL |
| `description` | no | null | Free text |
| `environment` | no | `"production"` | Up to 50 characters. `production`, `staging` and `development` give the prefixes `wk_prod_`, `wk_stag_` and `wk_dev_`; anything else gives the bare `wk_` |
| `expiresAt` | no | null (never expires) | Must be in the future |

**Response (201).** The plain key appears here once and nowhere else. Note the field is `webhookKey`, not `apiKey`:

```json
{
  "id": "a7b8c9d0-0000-0000-0000-000000000001",
  "webhookKey": "wk_prod_4hT8nJ2kR6vY1bC5dF9gL3mP7qS0wX",
  "keyPrefix": "wk_prod_",
  "createdAt": "2026-03-12T10:00:00Z",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

#### POST `/api/v1/webhookkeys/validate`

Check a webhook key and get its metadata back.

**Permission:** `webhookkeys:validate`

**Request:**

```json
{
  "webhookKey": "wk_prod_4hT8nJ2kR6vY1bC5dF9gL3mP7qS0wX"
}
```

**Response (200):**

```json
{
  "active": true,
  "webhookKeyId": "a7b8c9d0-0000-0000-0000-000000000001",
  "applicationId": "8b1d9f20-1111-2222-3333-444455556666",
  "name": "Order events",
  "targetUrl": "https://crm.example.com/hooks/orders",
  "environment": "production"
}
```

**An unknown key returns 400 with the error code `WebhookKey.Invalid`**, not a body saying `active: false`. A successful validation updates the key's `lastUsedAt`.

#### POST `/api/v1/webhookkeys/{id}/revoke`

Revoke a webhook key immediately.

**Permission:** `webhookkeys:revoke`

**Request.** Optional; sending no body is valid:

```json
{
  "reason": "Endpoint decommissioned"
}
```

**Response:** 204 No Content. A key that is already revoked returns 409 with `WebhookKey.AlreadyRevoked`.

#### POST `/api/v1/webhookkeys/{id}/rotate`

Issue a replacement and give the old key a grace period.

**Permission:** `webhookkeys:rotate`

**Request.** Optional; omitting the body gives 60 minutes:

```json
{
  "gracePeriodMinutes": 60
}
```

The new key keeps the old one's application, target URL and environment, and is named `<old name> (rotated)`. **Passing `0` changes the behaviour**: no expiry is set on the old key at all, so it stays valid until you revoke it by hand — the response message says so.

**Response (200):**

```json
{
  "newWebhookKey": "wk_prod_8pQ3rV7yB2eH6kN0sU4xZ1cG5jM9tW",
  "newWebhookKeyId": "b8c9d0e1-0000-0000-0000-000000000002",
  "newKeyPrefix": "wk_prod_",
  "oldKeyExpiresAt": "2026-03-12T15:30:00Z",
  "oldWebhookKeyId": "a7b8c9d0-0000-0000-0000-000000000001",
  "message": "New webhook key created. Old key remains valid for 60 minutes."
}
```

Unlike the API-key rotation response, this `message` is **not** translated and there is no `messageCode`. Rotating a revoked key returns 403 with the error code `WebhookKey.Revoked`.

---

### 5.11 Audit Logs

**Base route:** `/api/v1/audit-logs`

A record of things that happened: who did what, to which record, from which address, and when.

**Three limits to understand before you rely on this, all of them consequences of the same thing — the database table has fourteen columns, and the object the API returns has more fields than that.**

1. **Every row reports success, because there is no success column.** The table has no `IsSuccess` column, so the code that reads a row hardcodes `true`. A failed operation and a successful one are indistinguishable in the audit log. If you need failed sign-ins specifically, they are recorded separately and are reachable through `GET /api/v1/auth/login-history`.
2. **`actionType` is always the literal string `"System"`.** There is no `ActionType` column either; the value is hardcoded when the row is read. It carries no information — group by `action` instead, which is a real column and holds values such as `user.login`, `password.changed` and `role.assigned`.
3. **`errorMessage` and `correlationId` are always null**, for the same reason, so they never appear in a response body at all — null properties are omitted from every response in this API.

*In code:* the table is `Auth/Auth_DB/dbo/Tables/Security/AuditLogs.sql`; the four hardcoded values are at `Auth/Auth.Infrastructure/Persistence/AuditLogRepository.cs:218-235`, each annotated "not in current DB schema".

**Coverage is good but not universal.** Some operations write no audit row at all — creating or revoking a webhook key is the clearest example, because the events it publishes have no subscriber ([5.10b](#510b-webhook-keys)). Do not describe this log to an auditor as a complete record of every operation without checking the specific operation first.

**Both permission codes here — `auditlogs:read` and `auditlogs:export` — have no row in a freshly published database.** The seeded `auditor` role does not help: it holds codes in the `auth:` family, which no endpoint checks. On a clean install the audit log is readable only by a holder of the global `*` permission. See [Section 11](#11-permission-matrix).

#### GET `/api/v1/audit-logs`

Query the audit trail, paged.

**Permission:** `auditlogs:read`

**Query parameters.** Note the page-size default: **50 here, not the 20 every other list uses.**

| Parameter | Type | Default | What it does |
|---|---|---|---|
| `pageNumber` | integer | 1 | Which page |
| `pageSize` | integer | **50** | Between 1 and 100; outside that returns 400 |
| `userId` | Guid | not set | Exact match on the acting user |
| `applicationId` | Guid | not set | Exact match on the application |
| `action` | string | not set | **Substring** match, not exact — passing `login` matches `user.login` and `user.login.failed` alike |
| `fromDate` | date-time | not set | Entries at or after this moment |
| `toDate` | date-time | not set | Entries at or before this moment. Must be later than `fromDate`, or the call returns 400 |
| `actionType` | string | not set | **Accepted and silently ignored.** See below |
| `isSuccess` | boolean | not set | **Accepted and silently ignored.** See below |
| `sortBy` | string | null | One of `action`, `entityType`, `timestamp`, `actor`, `userName`, `userEmail`, `applicationName`, `ipAddress`, `userAgent` |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**`actionType` and `isSuccess` do nothing, and the failure is silent.** Both are declared on the endpoint, accepted by the model binder, and passed all the way down to the repository — which never adds either one to the `WHERE` clause. The clause is built from `userId`, `applicationId`, `action`, `fromDate` and `toDate`, and nothing else. Sending `isSuccess=false` therefore returns the same rows as sending nothing: **not an empty result, not an error — the unfiltered page.** A report built on either parameter is wrong in a way that looks like it is working.
*In code:* `Auth/Auth.Infrastructure/Persistence/AuditLogRepository.cs:83-125`.

**Response (200).** The array is called `logs`:

```json
{
  "logs": [
    {
      "id": "f0e1d2c3-0000-0000-0000-000000000001",
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "userName": "John Doe",
      "userEmail": "john@example.com",
      "actionType": "System",
      "action": "user.login",
      "entityType": "User",
      "entityId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "ipAddress": "203.0.113.24",
      "userAgent": "Mozilla/5.0 ...",
      "isSuccess": true,
      "timestamp": "2026-03-12T14:30:00Z"
    }
  ],
  "totalCount": 1500,
  "pageNumber": 1,
  "pageSize": 50,
  "totalPages": 30,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

`actionType` is `"System"` on every row and `isSuccess` is `true` on every row, for the reasons given above. `userName`, `userEmail` and `applicationName` are looked up for you and are present when the referenced record still exists.

**Two real columns are not in this response.** The table stores `SessionId` — which sign-in session the action happened in — and `PerformedBy`, which can differ from `UserId`: an administrator changing somebody else's password is recorded with the subject in `UserId` and the administrator in `PerformedBy`. Neither field is on the object this API returns, so "who really did it" is only available by querying the database directly.
*In code:* `Auth/Auth_DB/dbo/Tables/Security/AuditLogs.sql:6,15`.

#### GET `/api/v1/audit-logs/{id}`

One entry, including the before-and-after values.

**Permission:** `auditlogs:read`

**Response (200):**

```json
{
  "id": "f0e1d2c3-0000-0000-0000-000000000001",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userName": "John Doe",
  "userEmail": "john@example.com",
  "actionType": "System",
  "action": "user.updated",
  "entityType": "User",
  "entityId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "oldValues": "{\"firstName\":\"John\"}",
  "newValues": "{\"firstName\":\"Jonathan\"}",
  "ipAddress": "203.0.113.24",
  "userAgent": "Mozilla/5.0 ...",
  "additionalData": "{\"note\":\"bulk import\"}",
  "isSuccess": true,
  "timestamp": "2026-03-12T14:30:00Z"
}
```

`oldValues`, `newValues` and `additionalData` are **strings containing JSON**, not nested objects — parse them a second time. `additionalData` is the table's `Details` column under a different name. There is no `correlationId` field in the body, because that value is always null and null properties are omitted.

#### GET `/api/v1/audit-logs/users/{userId}`

Everything recorded for one person, paged.

**Permission:** `auditlogs:read`

**Query parameters:** `pageNumber` (default 1), `pageSize` (**default 50**), `fromDate`, `toDate`, `sortBy` and `sortDirection` — the same sort allow-list as the main list.

**Response (200):** the same paged object, with its array called `logs`.

#### GET `/api/v1/audit-logs/entities/{entityType}/{entityId}`

Everything recorded against one record — for example every change ever made to one role. **Not paged**, so it returns the whole history in one array; be careful with a long-lived record.

**Permission:** `auditlogs:read`

**Path parameters:** `entityType` is the string stored in the column, such as `User`, `Role`, `Permission`, `Application`, `ApiKey` or `Session`. `entityId` is a GUID.

**Response (200):** an array of the same entry objects.

#### POST `/api/v1/audit-logs/export`

Download a filtered range as a file.

**Permission:** `auditlogs:export` — a separate, stronger code than reading.

**Request:**

```json
{
  "format": "csv",
  "userId": null,
  "applicationId": null,
  "action": "user.login",
  "fromDate": "2026-01-01T00:00:00Z",
  "toDate": "2026-03-12T23:59:59Z",
  "maxRecords": 10000,
  "sortBy": "timestamp",
  "sortDirection": "Desc"
}
```

| Field | Default | Notes |
|---|---|---|
| `format` | `"csv"` | **`csv` and `json` are the only values that produce a file.** The validator also lets the word `excel` through, but the handler then rejects it with the error code `AuditLog.InvalidExportFormat`. Do not send it |
| `maxRecords` | `10000` | Between 1 and 10000. If more rows match than this, the extra rows are dropped silently — the response is a complete file of truncated data, with only a server-side warning in the log |
| `userId`, `applicationId`, `action`, `fromDate`, `toDate` | not set | The same five filters that work on the list |
| `actionType`, `isSuccess` | not set | Accepted here too, and ignored here too |
| `sortBy`, `sortDirection` | null / `Asc` | Same allow-list as the list endpoint |

**Response:** the file itself, as a download. `text/csv` or `application/json`, named `audit_logs_<yyyyMMdd_HHmmss>.csv` or `.json`.

**The CSV is not the same data as the JSON.** The CSV has exactly ten fixed columns — `Id, Timestamp, UserId, UserEmail, ApplicationId, ApplicationName, Action, EntityType, EntityId, IpAddress` — and therefore **drops `oldValues`, `newValues` and `userAgent` entirely**. The JSON export keeps them. If the export is going to an auditor who needs to see what changed, choose `json`.
*In code:* `Auth/Auth.Application/Features/AuditLogs/ExportAuditLogs/ExportAuditLogsCommandHandler.cs:98-133`.

---

### 5.12 Secrets (Admin)

**Base route:** `/api/v1/admin/secrets`

**Prerequisites:** `SecretManagement:EnableAdminApi` must be `true` in configuration (keep it off except while provisioning), and requests must be made over HTTPS — `generate/*` and `import/*` carry private key material.

All endpoints require the `secrets.manage` permission. `generate/*` has the **system** mint a fresh key; `import/*` stores a value **you supply** (BYOK) and works only in Certificate/Dpapi mode.

#### GET `/api/v1/admin/secrets/status`

Get the status of all system secrets (no values exposed).

**Permission:** `secrets.manage`

**Response (200).** Every secret is reported inside one `secrets` map, keyed by name — there is no `…Configured` boolean per secret:

```json
{
  "secretFileExists": true,
  "secretFilePath": "C:\\Users\\you\\AppData\\Local\\AuthSystem\\Secrets\\secrets.dpapi",
  "lastModified": "2026-03-01T10:00:00Z",
  "machineName": "BUILD-01",
  "schemaVersion": 1,
  "secrets": {
    "JwtPrivateKeyPem": "Configured",
    "JwtPublicKeyPem": "NotConfigured",
    "RefreshTokenHmacKey": "Configured",
    "SmtpPassword": "NotConfigured",
    "GatewayToken": "Configured",
    "AccountDeletionIdentifierHmacKey": "Configured",
    "PasswordPepper": "NotConfigured",
    "ConnectionStrings.AuthDb": "NotConfigured",
    "Custom:ApiIntegrationKey": "Configured"
  }
}
```

**Each value in `secrets` is one of three words: `Configured`, `NotConfigured` or `Empty`.** `Empty` means the entry exists but holds only whitespace, which is a misconfiguration rather than an absence — it is worth telling apart from `NotConfigured` for exactly that reason. Any custom secret you have stored appears in the same map under the key `Custom:<name>`.

**When the secret file does not exist yet, the body is short and every secret reads `NotConfigured`.** `secretFileExists` is `false`, `lastModified`, `machineName` and `schemaVersion` are absent or zero, and the map lists the seven built-in names with no custom entries. That is the normal state of a fresh installation, not a fault.

**No value is ever returned, in any state.** The map reports only whether something is there.
*In code:* `Auth/Auth.Infrastructure/Security/DpapiSecretService.cs:370-416`; the shape is `SecretStatusResult` in `Auth/Auth.Application/Interfaces/IDpapiSecretService.cs:189-219`.

#### Six of these endpoints refuse to run until you have confirmed the operation by email

**Rotating or replacing a key is a ceremony in three steps, and the endpoint that does the work is the last of them.** The six destructive endpoints — `generate/rsa`, `generate/hmac`, `generate/gateway-token`, `import/rsa`, `import/hmac` and `import/gateway-token` — each require a body naming a confirmation you have already answered:

```json
{
  "challengeId": "6f2c1e40-0000-0000-0000-000000000001"
}
```

Send one of them without that field, or with an identifier that was never confirmed, and the call returns **403** with the error code `Secret.ChallengeNotApproved`. The three steps are:

1. **Raise the confirmation.** `POST /api/v1/admin/secrets/challenges`, naming the operation you intend to run. A six-digit code is emailed to the confirmed email address of the account making the request — not to a shared operations mailbox.
2. **Answer it.** `POST /api/v1/admin/secrets/challenges/{challengeId}/verify` with that code. The reply states what the operation is about to break, counted in people rather than in rows, and opens an approval window of **five minutes**.
3. **Run the operation** inside that window, sending the same `challengeId`. The approval is spent on use, so a second call carrying the same identifier is refused.

**One approval authorises exactly one operation, and for the three import calls it is bound to the exact key material as well.** That is why raising a confirmation for an import also takes the key you are about to import: the approval is tied to a digest of those bytes. An approval obtained for the operation with the smallest consequences — the gateway token, which invalidates nobody's credentials — therefore cannot be spent on the one with the largest, the refresh-token key that signs everybody out. Reusing an approval across operations, across administrators, or with different key material all fail the same way, with `Secret.ChallengeNotApproved`.
*In code:* `Auth/Auth.Application/Features/Secrets/Common/SecretOperationChallengeService.cs`; the operation names are the members of `Auth/Auth.Domain/Enums/SecretOperation.cs`.

**In development, where `Email:Enabled` is `false` by default, no email is sent and the code goes to the log instead** — at Warning level, in a line beginning `Email disabled - Secret operation confirmation code`. That fallback is gated on the environment as well as on the setting, so an operator who turns email off in production does not start writing this code into a production log file.
*In code:* `Auth/Auth.Application/Features/Secrets/Common/SecretOperationChallengeService.cs:117-127`.

#### POST `/api/v1/admin/secrets/challenges`

Ask for a confirmation code for one destructive operation. **This call rotates nothing.**

**Permission:** `secrets.manage`

**Rate limited:** the `login` policy — 20 requests per 60 seconds per client address. A second, per-administrator limit also applies: at most `Email:MaxOtpRequestsPerWindow` (shipped as **3**) codes within `Email:RateLimitWindowSeconds` (shipped as **60**), after which the call returns 403 `Secret.TooManyChallengeRequests`.

**Request:**

```json
{
  "operation": "GenerateRsaKey"
}
```

| Field | Required | Notes |
|---|---|---|
| `operation` | yes | One of `GenerateRsaKey`, `GenerateHmacKey`, `GenerateGatewayToken`, `ImportRsaKey`, `ImportHmacKey`, `ImportGatewayToken`, spelled exactly like that — enumerated values travel as their names |
| `value` | only for the three `Import…` operations | The key material you are going to import, in the same format the matching import endpoint takes. Omit it for the three `Generate…` operations, where binding a payload would make the digest a constant |

**Response (200).** The address is masked and the code itself is never in the body:

```json
{
  "challengeId": "6f2c1e40-0000-0000-0000-000000000001",
  "expiresAt": "2026-03-12T10:05:00Z",
  "maskedEmail": "a***n@company.com"
}
```

`expiresAt` is `Email:OtpExpirationMinutes` — shipped as **5 minutes** — after the request. Raising a new confirmation invalidates every outstanding one for the same administrator, so a guesser never has more than one live target.

**Failures worth planning for:**

| Error code | Status | When |
|---|---|---|
| `Secret.ChallengeRecipientUnavailable` | 409 | The requesting account has no confirmed email address, so there is nowhere to send a code |
| `Secret.TooManyChallengeRequests` | 403 | More codes were asked for than the per-administrator window allows |
| `Secret.ImportNotSupportedInPlainText` | 409 | An `Import…` operation was named while `SecretManagement:StorageMode` is `PlainText`. It is refused here, before a code is wasted on an operation that cannot run |
| `Secret.ChallengeEmailFailed` | 500 | The code could not be sent. Nothing was rotated |

#### POST `/api/v1/admin/secrets/challenges/{challengeId}/verify`

Answer a confirmation with the emailed code, and see what the operation would cost before you run it.

**Permission:** `secrets.manage` · **Rate limited:** the `login` policy

**Request:**

```json
{
  "code": "418302"
}
```

**Five attempts are allowed per confirmation, and every failure returns the same error** — `Secret.InvalidChallengeCode`, status 400 — whether the code was wrong, the confirmation had expired, it belonged to a different administrator, or the identifier does not exist at all. The response deliberately does not tell you which.

**Response (200).** The approval window opens now, and this is the last thing you see before the operation runs:

```json
{
  "operation": "GenerateHmacKey",
  "approvalExpiresAt": "2026-03-12T10:07:30Z",
  "affectedUsers": 412,
  "details": [
    { "code": "usersSignedOut", "count": 412 },
    { "code": "usersWithSsoSessions", "count": 96 },
    { "code": "pendingPasswordResets", "count": 3 },
    { "code": "pendingTwoFactorChallenges", "count": 1 },
    { "code": "activeWebhookKeys", "count": 7 }
  ],
  "requiresApiRestart": true,
  "requiresGatewayReconfiguration": false
}
```

**The `details` array holds only the consequences that are real for the operation you named, and the three keys break genuinely different things.** Switch on the `code` values; they are a fixed vocabulary.

| Operation | `affectedUsers` counts | `details` entries | Also true |
|---|---|---|---|
| `GenerateRsaKey`, `ImportRsaKey` | People holding a live session | `usersWithLiveAccessTokens`, `usersWithActiveSessions` | Forces a token refresh but signs **nobody** out — refresh tokens are opaque and unsigned |
| `GenerateHmacKey`, `ImportHmacKey` | People holding a live refresh token | `usersSignedOut`, `usersWithSsoSessions`, `pendingPasswordResets`, `pendingTwoFactorChallenges`, `activeWebhookKeys` | The one that signs everybody out. The same key hashes refresh tokens, password-reset links, two-factor challenges and webhook keys, so all four stop working |
| `GenerateGatewayToken`, `ImportGatewayToken` | People holding a live session | `usersWithActiveSessions` | Invalidates no user credential at all. `requiresGatewayReconfiguration` is `true`: until the gateway carries the same value, every proxied request is rejected — an outage, not a credential loss |

**`requiresApiRestart` is `true` for all six**, because the running process captured the old key when it started. Nothing changes for users until the API is recycled, and the counts above describe what happens then, not now.
*In code:* `Auth/Auth.Application/Features/Secrets/Common/SecretRotationImpact.cs`.

#### POST `/api/v1/admin/secrets/generate/rsa`

Generate a new RSA key pair (replaces existing).

**Permission:** `secrets.manage`

**Response (200):**

```json
{
  "publicKey": "-----BEGIN PUBLIC KEY-----\nMIIBIjAN...",
  "message": "RSA key pair generated. WARNING: All existing access tokens are now invalid."
}
```

> **Warning:** Regenerating RSA keys invalidates ALL active access tokens. All users will need to refresh their tokens.

#### POST `/api/v1/admin/secrets/generate/hmac`

Generate a new HMAC key (replaces existing).

**Permission:** `secrets.manage`

**Response (200):**

```json
{
  "message": "HMAC key generated. WARNING: All existing refresh tokens are now invalid."
}
```

> **Warning:** Regenerating the HMAC key invalidates ALL refresh tokens. All users will need to re-authenticate.

#### POST `/api/v1/admin/secrets/generate/gateway-token`

Generate a new gateway token.

**Permission:** `secrets.manage`

**Response (200):**

```json
{
  "message": "Gateway token generated. Update the API Gateway configuration to use the new token."
}
```

> **Important:** After regeneration, the API Gateway must be restarted to pick up the new token.

#### POST `/api/v1/admin/secrets/import/rsa`

Import a caller-supplied RSA **private** key for JWT signing (bring-your-own-keys). The matching public key is derived and stored automatically.

**Permission:** `secrets.manage`

**Request:**

```json
{
  "value": "-----BEGIN PRIVATE KEY-----\nMIIEvg...\n-----END PRIVATE KEY-----"
}
```

> Supply the PEM (PKCS#8 or PKCS#1, ≥ 2048-bit) with newlines JSON-escaped as `\n`.

**Response (200):**

```json
{
  "success": true,
  "message": "RSA signing key imported successfully. All existing access tokens are now invalid. Users must re-authenticate.",
  "publicKeyPem": "-----BEGIN PUBLIC KEY-----\n..."
}
```

> **Storage-mode requirement:** `import/*` works only in **Certificate** or **Dpapi** mode. In **PlainText** mode it returns `409 Secret.ImportNotSupportedInPlainText` — edit the keys directly in `appsettings.Production.json` instead. Importing **replaces** the current key; re-importing the *same* value is a safe no-op for live tokens.

#### POST `/api/v1/admin/secrets/import/hmac`

Import a caller-supplied HMAC key for refresh-token hashing (BYOK).

**Permission:** `secrets.manage`

**Request:**

```json
{
  "value": "<base64-encoded key, >= 32 bytes>"
}
```

**Response (200):**

```json
{
  "success": true,
  "message": "HMAC key imported successfully. All existing refresh tokens are now invalid. Users must re-authenticate."
}
```

> Certificate/Dpapi mode only (`409` in PlainText). Replaces the current key.

#### POST `/api/v1/admin/secrets/import/gateway-token`

Import a caller-supplied gateway token for inter-service authentication (BYOK).

**Permission:** `secrets.manage`

**Request:**

```json
{
  "value": "<gateway token, >= 16 chars>"
}
```

**Response (200):**

```json
{
  "success": true,
  "message": "Gateway token imported successfully. Update the API Gateway configuration with the same token."
}
```

> Certificate/Dpapi mode only (`409` in PlainText). The API Gateway must be configured with the same token.

#### PUT `/api/v1/admin/secrets/smtp-password`

Move the mail-server password into the encrypted secrets file, where it overrides `Email:Password` from configuration.

**Permission:** `secrets.manage`

**No confirmation code is required here, unlike the six rotations above.** The reason is circular dependency: the confirmation code is delivered by email, so demanding one before you may repair a broken mail password would mean the code can only arrive after the fix it was meant to authorise.

**Request:**

```json
{
  "value": "the-smtp-password"
}
```

`value` must not be empty and must be 512 characters or fewer. Nothing else about it is checked — a mail provider may allow any character set, so a stricter rule would reject valid passwords. You prove it works by restarting and then calling `POST /api/v1/admin/system-settings/email/test` ([5.22](#522-system-settings)).

**Response:** 204 No Content. **The new value takes effect on the next API restart**, not immediately.

**Failures:** 400 if the value is missing or too long; **409 `Secret.SetNotSupportedInPlainText`** when `SecretManagement:StorageMode` is `PlainText`, because in that mode there is no encrypted file to write to — supply the password through configuration or an environment variable instead.

#### PUT `/api/v1/admin/secrets/connection-string`

Move the database connection string into the encrypted secrets file, where it overrides `ConnectionStrings:AuthDb` — including the environment variables supplied by `web.config` on a deployed server.

**Permission:** `secrets.manage`

**Request:**

```json
{
  "value": "Data Source=db.internal;Initial Catalog=Astoom_Auth;User ID=authapi;Password=REPLACE_ME;TrustServerCertificate=True",
  "forceSave": false
}
```

| Field | Required | Notes |
|---|---|---|
| `value` | yes | The complete connection string. Maximum 2,048 characters |
| `forceSave` | no, defaults to `false` | Store the value even though no connection could be opened with it |

**The value is probed before it is stored, and the two failure shapes mean different things.**

- **It cannot be parsed** → 400 `Secret.ConnectionStringMalformed`. Nothing is stored, and `forceSave` will not help.
- **It parses but no connection opens** → 400 `Secret.ConnectionStringUnreachable`. Nothing is stored on this attempt. Resubmitting the same value with `"forceSave": true` stores it anyway.

**`forceSave` exists for one legitimate case: staging a database password that has not been switched over at the server yet.** Storing a string you cannot connect with is otherwise a mistake, so the endpoint makes you say so twice.

**Response:** 204 No Content. **Takes effect on the next API restart.** In `PlainText` storage mode it returns 409 `Secret.SetNotSupportedInPlainText`, exactly as the SMTP password does.
*In code:* `Auth/Auth.Application/Features/Secrets/SetConnectionString/SetConnectionStringCommandHandler.cs`.

#### PUT `/api/v1/admin/secrets/custom/{key}`

Set a custom secret value.

**Permission:** `secrets.manage`

**Path parameter:** `key` — Alphanumeric characters, underscores, and dots only (max 100 chars)

**Request:**

```json
{
  "value": "my-secret-value"
}
```

**Response:** 204 No Content

> Custom secrets are stored under the `Custom:` namespace (e.g., `Custom:my.api.key`).

#### DELETE `/api/v1/admin/secrets/custom/{key}`

Delete a custom secret.

**Permission:** `secrets.manage`

**Response:** 204 No Content

---

### 5.13 Dashboard

**Base route:** `/api/v1/dashboard`

These six endpoints answer the questions the console's home screen asks: how many people have accounts, how sign-in is going, what the audit trail looks like, how sessions are behaving, which applications are busy, and which keys are about to expire.

**Every figure is computed in the database, across the whole table.** You cannot reproduce them by reading a page of a list endpoint and counting in the client, because a page is a sample. That is the entire reason this area exists.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/dashboard/user-stats` | Totals, the status mix, sign-ups per day, the activation funnel, dormancy, and the ten largest organizations | `users:read` |
| GET | `/api/v1/dashboard/auth-stats` | Sign-in outcomes, daily active users, failure reasons, lockouts and the addresses failing most often | `auditlogs:read` |
| GET | `/api/v1/dashboard/audit-stats` | Audit totals with a daily series and breakdowns by action and entity type | `auditlogs:read` |
| GET | `/api/v1/dashboard/session-stats` | Session and refresh-token hygiene | `auditlogs:read` |
| GET | `/api/v1/dashboard/app-activity` | Activity per application, and which organizations have enabled which application | `applications:read` |
| GET | `/api/v1/dashboard/credential-stats` | Which API keys and webhook keys expire soon | Authenticated, no permission code — see below |

**Three query parameters shape the answer, and the two windows point in opposite directions.** IANA is the Internet Assigned Numbers Authority, whose time-zone identifiers look like `Europe/Istanbul`.

| Parameter | Which endpoints | Default | Range | Meaning |
|---|---|---|---|---|
| `days` | the first five | `30` | 1 to 90; outside it returns **400** | A window that runs **backwards** from now |
| `timeZone` | `user-stats`, `auth-stats`, `audit-stats` only | `"UTC"` | Any IANA identifier, such as `Europe/Istanbul`; an unrecognised or empty one returns **400** | Where the day boundaries are cut for the per-day series |
| `horizonDays` | `credential-stats` only | `14` | 1 to 365; outside it returns **400** | A window that runs **forwards**, because an expiry date is in the future |

**`credential-stats` deliberately carries no permission attribute, and that is not an oversight.** The two credential families it reports are gated by two different codes — `apikeys:read` and `webhookkeys:read` — and the attribute accepts only one. So the check moved inside: a family the caller may not read comes back **`null` rather than `0`**, because zero would assert that nothing is expiring, and this response is not entitled to make that claim when it was not allowed to look.
*In code:* `Auth/Auth_API/Modules/Dashboard/Controllers/DashboardController.cs:148-166`.

**All five permission codes used by this area — `users:read`, `auditlogs:read`, `applications:read`, `apikeys:read`, `webhookkeys:read` — have no row in a freshly published database.** On a clean install only a holder of the global `*` permission, which the seeded `super-admin` role has, can open the dashboard at all. [Section 11](#11-permission-matrix) explains why and what to do about it.

#### GET `/api/v1/dashboard/user-stats`

The first call most people make, because it is what the console's home page loads.

**Permission:** `users:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `days` | integer | `30` | Trailing window, 1 to 90 |
| `timeZone` | string | `"UTC"` | IANA identifier used to cut the day buckets in `signupsPerDay` |

**Response (200):**

```json
{
  "days": 30,
  "totalUsers": 1284,
  "byStatus": [
    { "status": 1, "count": 1180 },
    { "status": 2, "count": 61 },
    { "status": 3, "count": 12 },
    { "status": 4, "count": 31 }
  ],
  "activeUsers": 1180,
  "mfaEnabled": 214,
  "newInWindow": 96,
  "signupsPerDay": [
    { "date": "2026-03-10T00:00:00Z", "count": 4 },
    { "date": "2026-03-11T00:00:00Z", "count": 7 }
  ],
  "cohortCreated": 96,
  "cohortEmailConfirmed": 74,
  "cohortLoggedIn": 61,
  "dormantOver30Days": 302,
  "dormantOver60Days": 188,
  "dormantOver90Days": 121,
  "neverLoggedIn": 44,
  "usersByOrganization": [
    {
      "organizationId": "9f8e7d6c-0000-0000-0000-000000000001",
      "organizationName": "Acme Corporation",
      "isAutoCreated": false,
      "count": 42
    }
  ],
  "totalActiveMemberships": 1310
}
```

**Five things about this body will surprise you if nobody says them.**

1. **`status` is a number here, not a name.** Everywhere else in this API a user's status is the string `"Active"`. In this one array it is the raw stored byte: **1 Active, 2 Inactive, 3 Locked, 4 Pending**. The field is a byte rather than the enumerated type, so the name converter never sees it.
2. **`date` in `signupsPerDay` is a calendar day in the time zone you asked for, but it is still written with a trailing `Z`** like every other timestamp in this API. Do not convert it a second time — `2026-03-10T00:00:00Z` here means "the 10th of March, in `Europe/Istanbul`", not midnight UTC.
3. **`cohortCreated` and `newInWindow` are the same number**, read from the same expression. The three `cohort*` values are a funnel over the people created inside the window — created, then confirmed their email, then signed in at least once — so they can only decrease.
4. **`mfaEnabled`, the three `dormantOver*` counts and `neverLoggedIn` are counted among Active accounts only**, while `totalUsers` and `byStatus` count every status. A "dormant" account is one whose last sign-in, or its creation date if it never signed in, is older than the stated number of days.
5. **`usersByOrganization` is the ten largest organizations by active membership, never the whole list.** `totalActiveMemberships` is the denominator, so the console can render an "other" slice. Soft-deleted users are excluded from every count on this endpoint.

*In code:* `Auth/Auth.Infrastructure/Persistence/DashboardStatsRepository.cs:30-107`.

---

### 5.14 Notification Templates

**Base route:** `/api/v1/notification-templates`

**Every word this system emails lives in the database, not in the code**, and these endpoints are how it is edited, translated, previewed and published without a redeployment. [4.10](#410-how-a-notification-becomes-an-email) describes what happens after a message is sent; this section is the editing side of the same system. In the console it is **Notifications → Templates**.

**One template is one notification type, on one channel, for one application or for the whole platform**, and it holds a history of versions. Two pointers decide what happens: a published version, which is what actually gets sent, and a draft, which is what you are editing. A draft is never sent to anyone.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-templates` | Paged template list | `notification-templates:read` |
| GET | `/api/v1/notification-templates/summary` | Counts and what is currently published, for the section overview page | `notification-templates:read` |
| GET | `/api/v1/notification-templates/{id}` | One template with its versions and translations — the editor view | `notification-templates:read` |
| GET | `/api/v1/notification-templates/{id}/versions/{versionId}` | One historical version's translations | `notification-templates:read` |
| POST | `/api/v1/notification-templates` | Create a template as an empty draft, version 1. Returns **201** | `notification-templates:manage` |
| PUT | `/api/v1/notification-templates/{id}/draft` | Save draft edits | `notification-templates:manage` |
| DELETE | `/api/v1/notification-templates/{id}/draft` | Discard the draft. Returns **200 with the template**, not 204 | `notification-templates:manage` |
| POST | `/api/v1/notification-templates/{id}/publish` | Make the draft the published version | `notification-templates:publish` |
| POST | `/api/v1/notification-templates/{id}/unpublish` | Stop sending this template | `notification-templates:publish` |
| POST | `/api/v1/notification-templates/{id}/rollback` | Point the published version back at an earlier one | `notification-templates:publish` |
| POST | `/api/v1/notification-templates/{id}/versions/{versionId}/restore-draft` | Copy an old version into a new draft | `notification-templates:manage` |
| DELETE | `/api/v1/notification-templates/{id}` | Delete the template and its whole history. Returns 204 | `notification-templates:manage` |
| POST | `/api/v1/notification-templates/preview` | Render an editor buffer on the server, without saving anything | `notification-templates:read` |
| POST | `/api/v1/notification-templates/{id}/test-send` | Send one rendered test message. Returns 204 | `notification-templates:manage` |

**Four behaviours are easy to get wrong from the endpoint names alone.**

1. **Publishing moves a pointer; it copies nothing and deletes nothing.** The previously published version stays in the history, which is what makes rollback instant and lossless. Publishing also clears the draft pointer, so the draft you published is now the published version and there is no draft any more.
2. **All languages of a version publish together.** There is one pointer per template, not one per language, so you cannot publish the French text while holding back the Arabic.
3. **Saving a draft and publishing a draft both run the text through the renderer first.** Saving rejects a syntax error. Publishing goes further: it renders every language against the type's sample data with unknown variables treated as failures, and a single bad language blocks the publish for all of them, with the failing language named in the error.
4. **Publishing requires a translation in the template's own default language.** Without it the operation is refused.

*In code:* `Auth/Auth.Domain/Entities/NotificationTemplate.cs`; the publish gate is `Auth/Auth.Application/Features/Notifications/PublishNotificationTemplate/PublishNotificationTemplateCommandHandler.cs:67-94`.

**The four permission codes used across 5.14 to 5.17 do exist in a freshly published database, but no role holds any of them.** `notification-templates:read`, `notification-templates:manage`, `notification-templates:publish` and `notification-layouts:manage` are created by the seed, alongside a `notification-templates:*` wildcard row that covers the first three — but nothing grants them to `admin`, `user-manager` or any other seeded role, so out of the box only the global `*` of `super-admin` reaches this area. Unlike the webhook-key codes ([5.10b](#510b-webhook-keys)), these rows are real, so you can grant them to a role yourself. [Section 11](#11-permission-matrix) explains the whole situation.

#### GET `/api/v1/notification-templates`

List templates, paged. The screen you land on.

**Permission:** `notification-templates:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | integer | `1` | Which page |
| `pageSize` | integer | `20` | 1 to 100 |
| `notificationTypeId` | Guid | not set | Only templates for one notification type |
| `applicationId` | Guid | not set | Only templates belonging to one application |
| `channel` | string | not set | `Email`, `Sms` or `Push`, by name. **Only `Email` can actually be delivered** — see the warning below |
| `isPublished` | boolean | not set | `true` for live templates, `false` for those that are not |
| `searchTerm` | string | not set | Free-text search |
| `sortBy` | string | null | One of `typeName`, `typeCode`, `applicationName`, `channel`, `defaultLanguage`, `publishedVersionNumber`, `createdAt`, `modifiedAt` |
| `sortDirection` | string | `Asc` | `Asc` or `Desc` |

**Response (200).** The array is called `templates`:

```json
{
  "templates": [
    {
      "id": "5c4b3a29-0000-0000-0000-000000000001",
      "notificationTypeId": "40000000-0000-0000-0000-000000000001",
      "typeCode": "email-verification",
      "typeName": "Email Verification",
      "typeIsSystem": true,
      "applicationId": null,
      "applicationName": null,
      "channel": "Email",
      "defaultLanguage": "en",
      "isPublished": true,
      "publishedVersionNumber": 3,
      "hasDraft": true,
      "draftVersionNumber": 4,
      "translationCount": 7,
      "createdAt": "2026-01-01T00:00:00Z",
      "modifiedAt": "2026-03-10T09:12:00Z"
    }
  ],
  "totalCount": 15,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 1,
  "hasPreviousPage": false,
  "hasNextPage": false
}
```

**A null `applicationId` means a global template**, used by every application that has no template of its own. **`typeIsSystem: true` marks a type the platform itself depends on**; the global template of such a type can be neither unpublished nor deleted, and either attempt returns 403 — `Notification.CannotUnpublishSystemTemplate` or `Notification.CannotDeleteSystemGlobalTemplate`.

**Only the email channel can be delivered.** The channel values `Sms` and `Push` exist in the enumeration and can be stored on a template, but no delivery strategy is registered for either one, so a message queued on them fails with "No delivery channel registered" and is retried until it dead-letters. Do not build on them.
*In code:* the only registered channel is `Auth/Auth.Infrastructure/Notifications/Channels/EmailNotificationChannel.cs`.

**A clean database has 15 templates covering 16 seeded notification types.** The one without a template is `welcome-email`, which therefore can never send anything.

---

### 5.15 Notification Layouts

**Base route:** `/api/v1/notification-layouts`

A layout is the shared frame — header, footer, styling — wrapped around every message body, so that changing the look of all your email is one edit rather than fifteen. In the console it is **Notifications → Layouts**.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-layouts` | Every layout, the global one first. **Not paged** — a plain array | `notification-templates:read` |
| GET | `/api/v1/notification-layouts/{id}` | One layout, for editing | `notification-templates:read` |
| POST | `/api/v1/notification-layouts` | Create a layout for one application. Returns 201 | `notification-layouts:manage` |
| PUT | `/api/v1/notification-layouts/{id}/draft` | Save draft edits | `notification-layouts:manage` |
| POST | `/api/v1/notification-layouts/{id}/publish` | Publish the draft | `notification-layouts:manage` |
| POST | `/api/v1/notification-layouts/preview` | Render a layout buffer with placeholder body content | `notification-templates:read` |

**Reading uses `notification-templates:read` while writing uses `notification-layouts:manage`, and that asymmetry is real.** There is no `notification-layouts:read` code anywhere in this system, so do not try to grant one.

**Layouts do not have version history, unlike templates.** Each layout row carries a draft pair of columns and a published pair; publishing copies one into the other. There is nothing to roll back to, so keep your own copy before a large change.
*In code:* `Auth/Auth.Domain/Entities/NotificationLayout.cs:14-15,171-184`.

#### GET `/api/v1/notification-layouts`

**Permission:** `notification-templates:read`

**No query parameters.** The response is a plain array, global layout first.

**Response (200):**

```json
[
  {
    "id": "6d5c4b3a-0000-0000-0000-000000000001",
    "applicationId": null,
    "applicationName": null,
    "channel": "Email",
    "name": "Default email layout",
    "draftContent": "<table>…{{ Body }}…</table>",
    "draftStringsJson": "{\"footerNote\":\"You received this because you have an account.\"}",
    "isPublished": true,
    "hasUnpublishedChanges": false,
    "publishedAt": "2026-02-02T08:00:00Z",
    "createdAt": "2026-01-01T00:00:00Z",
    "modifiedAt": "2026-02-02T07:59:00Z"
  }
]
```

**`applicationId: null` is the global layout, and a clean database seeds exactly one of them.** An application-specific layout is used in preference to the global one when a message belongs to that application.

**`draftStringsJson` is a string containing JSON, not a nested object** — parse it a second time. It holds the layout's own text snippets, kept separate from the markup so they can be translated without touching the HTML. **`hasUnpublishedChanges` compares the draft against the published copy**, so it tells you whether pressing publish would change anything.

---

### 5.16 Notification Outbox

**Base route:** `/api/v1/notification-outbox`

The outbox is the delivery log: one row per message the system has queued, with what was sent, to whom, in which language, by which template version, and how the delivery went. In the console it is **Notifications → Delivery log**. **This is where you look first when someone says they did not receive an email.**

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-outbox` | Paged delivery log | `notification-templates:read` |
| GET | `/api/v1/notification-outbox/{id}` | One entry, including the message exactly as it was rendered | `notification-templates:read` |
| POST | `/api/v1/notification-outbox/{id}/retry` | Put a failed message back in the queue for immediate dispatch. Returns 204 | `notification-templates:manage` |

**Retry only applies to a message in `Retry` or `Dead` status.** Asking to retry anything else returns **409 `Notification.OutboxMessageNotRetryable`**.

#### GET `/api/v1/notification-outbox`

**Permission:** `notification-templates:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | integer | `1` | Which page |
| `pageSize` | integer | `20` | 1 to 100 |
| `status` | string | not set | `Pending`, `Processing`, `Sent`, `Retry` or `Dead`, by name |
| `channel` | string | not set | `Email`, `Sms` or `Push`, by name |
| `searchTerm` | string | not set | Free-text search |
| `sortBy` | string | null | One of `typeCode`, `recipient`, `languageCode`, `status`, `attemptCount`, `nextAttemptAt`, `sentAt`, `createdAt` |
| `sortDirection` | string | **`Desc`** | **The one list in this API that defaults to descending**, because the newest message is the one you came to look at |

**Response (200).** The array is called `messages`:

```json
{
  "messages": [
    {
      "id": "7e6d5c4b-0000-0000-0000-000000000001",
      "notificationTypeCode": "password-reset",
      "channel": "Email",
      "applicationId": null,
      "applicationName": null,
      "recipient": "john@example.com",
      "recipientName": "John Doe",
      "recipientUserId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "languageCode": "en",
      "templateId": "5c4b3a29-0000-0000-0000-000000000002",
      "templateVersionId": "5c4b3a29-0000-0000-0000-0000000000a1",
      "templateVersionNumber": 3,
      "subject": "Reset your password",
      "status": "Retry",
      "attemptCount": 2,
      "nextAttemptAt": "2026-03-12T14:46:00Z",
      "sentAt": null,
      "lastError": "Connection refused (localhost:1025)",
      "createdAt": "2026-03-12T14:30:00Z",
      "createdBy": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
    }
  ],
  "totalCount": 4021,
  "pageNumber": 1,
  "pageSize": 20,
  "totalPages": 202,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

**`status` and `channel` come back as names, not numbers**, and `lastError` carries the raw text of the most recent failure — the fastest diagnosis you will get. The retry schedule widens: 1, 4, 16, 64 and then 256 minutes, and after five attempts the row becomes `Dead` and is never picked up again on its own.

**`Sent` does not always mean an email left the building.** When `Email:Enabled` is `false` — the shipped default, and the default in development — the channel logs what it would have sent and reports success, so the row reads `Sent` anyway. Check that setting before you conclude that delivery worked.

**A message that carried a one-time code never shows its body here, whatever its status.** Six notification types are treated this way — email verification, password reset, organization invitation, ownership-transfer code, account-deletion verification and secret-operation challenge — and `GET /api/v1/notification-outbox/{id}` returns `[redacted]` in `bodyHtml` and `bodyText` for every one of them, including a message still sitting in `Pending`, `Processing`, `Retry` or `Dead`. The delivery log can never be used to read somebody's code back.

**Two separate mechanisms produce that, and it is worth knowing both.** The stored row keeps the real body only while dispatch still needs it: **the moment delivery succeeds, the database columns themselves are overwritten with `[redacted]`**, so the secret stops existing at rest. Independently of that, this read endpoint substitutes `[redacted]` for these six types before answering, which is what covers the window before delivery. An administrator with `notification-templates:read` therefore cannot read a live verification code out of the delivery log even by opening the entry while the message is still queued.
*In code:* the six codes are `NotificationTypeCodes.SensitiveContentCodes` in `Auth/Auth.Domain/Constants/NotificationTypeCodes.cs:121-129`; the read-path substitution is `Auth/Auth.Application/Features/Notifications/GetNotificationOutboxMessageById/GetNotificationOutboxMessageByIdQueryHandler.cs:74-75`, and the at-rest overwrite is `Auth/Auth.Infrastructure/Persistence/NotificationOutboxRepository.cs:98-107`.

---

### 5.17 Notification Types

**Base route:** `/api/v1/notification-types`

A notification type is a named occasion on which the system sends something — `password-reset`, `email-verification`, and fourteen others. **The catalogue is fixed: types are created by the database seed, and these two endpoints can read them and edit their descriptive metadata, but cannot add or remove one.** Adding a type is a code change, because something in the code has to decide to send it.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/notification-types` | Every seeded type, with its variable catalogue and preview sample data. **Not paged** — a plain array | `notification-templates:read` |
| PUT | `/api/v1/notification-types/{id}` | Edit the name, description, variable list or sample data of one type | `notification-templates:manage` |

#### GET `/api/v1/notification-types`

The call the template editor makes first, because the variable catalogue tells an author which placeholders they are allowed to use.

**Permission:** `notification-templates:read`

**Response (200):**

```json
[
  {
    "id": "40000000-0000-0000-0000-000000000002",
    "code": "password-reset",
    "name": "Password Reset",
    "description": "Sent when a user asks to reset their password",
    "isSystem": true,
    "variablesJson": "[{\"name\":\"UserName\"},{\"name\":\"ResetUrl\"},{\"name\":\"ExpirationMinutes\"}]",
    "sampleDataJson": "{\"UserName\":\"John Doe\",\"ResetUrl\":\"https://example.com/reset?token=…\",\"ExpirationMinutes\":30}",
    "isActive": true
  }
]
```

**`variablesJson` and `sampleDataJson` are strings containing JSON, not nested objects** — parse them a second time. `variablesJson` is the catalogue the editor offers an author; `sampleDataJson` is what a preview and a publish are rendered against, and a publish fails on any placeholder that sample data does not supply. Between them they are what makes a template with a broken placeholder impossible to publish.

**A clean database has 16 types, and `isSystem: true` marks the ones the platform depends on.** Editing a type here changes how it is described and previewed; it never changes when or whether the system sends it.

**`isActive` is presentational.** Template resolution does not consult it, so switching a type to inactive does not stop anything from being sent.
*In code:* `Auth/Auth.Infrastructure/Persistence/NotificationTemplateRepository.cs:307-312`.

---

### 5.18 Privacy Policy

**Base route:** `/api/v1/privacy-policy`

**This is the privacy notice your users are shown, kept as a set of dated revisions rather than as one page you overwrite.** Each revision is named `YYYY.MM`, holds one document per language, and keeps its own permanent address after it is superseded — so an acknowledgement record, a rights request or a regulator can be shown the exact text that applied on a given date. In the console it is **Notifications → Privacy policy versions**.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/privacy-policy/published` | The published notice for one language, plus the configuration values the notice quotes | **Anonymous** |
| GET | `/api/v1/privacy-policy/versions` | Every revision, newest first | `privacy-policy:read` |
| POST | `/api/v1/privacy-policy/versions` | Record a new revision. Returns 201 | `privacy-policy:manage` |
| PUT | `/api/v1/privacy-policy/versions` | Change a revision's effective date, note, or name | `privacy-policy:manage` |
| GET | `/api/v1/privacy-policy/versions/content` | One language's document for one revision. `version` and `language` are **required** query parameters | `privacy-policy:read` |
| PUT | `/api/v1/privacy-policy/versions/content` | Create or replace one language's document | `privacy-policy:manage` |
| POST | `/api/v1/privacy-policy/versions/publish` | Make a revision the published one. Returns 204 | `privacy-policy:manage` |
| POST | `/api/v1/privacy-policy/versions/notify` | Email the change notice to every active, email-confirmed user | `privacy-policy:manage` |

**The two `PUT` endpoints name the revision in the body, not in the path.** That is unusual for this API and it is easy to misread: `PUT /api/v1/privacy-policy/versions` has no `{version}` segment, because the identifying `version` string travels as a field.

**Publishing is not a flag being flipped — it is a rendering step, and it can be refused.** When you publish a revision, the system renders the finished HTML page for all seven languages then and there, and stores those bytes. Reading the notice afterwards touches no template, no settings and no substitution, which is what guarantees a reader can never be shown a half-filled placeholder. Publishing is refused, leaving the previous revision serving, when any of these is true:

1. **The English document is missing.** It is the fallback every other language falls back to.
2. **Any language's document is not readable JSON.**
3. **The data-controller identity is incomplete.** All six of `DataController:LegalName`, `Address`, `PrivacyEmail`, `EmailProvider`, `HostingProvider` and `HostingCountry` must be filled in, and none may still contain a bracketed placeholder. They ship empty, so **a fresh installation cannot publish a policy until somebody fills them in** — through System Settings ([5.22](#522-system-settings)) or the configuration files.
4. **`IdentityProvider:AccountsBaseUrl` is empty.** The notice carries an absolute link to the account-deletion page, and there is no origin to build it from.

*In code:* `Auth/Auth.Application/Features/PrivacyPolicy/PublishPrivacyPolicyVersion/PublishPrivacyPolicyVersionCommandHandler.cs`.

**A language with no written document is not left unreachable and is not silently served English.** It is served the English text carrying a notice, in the reader's own language, saying that this translation is not available yet.

**`privacy-policy:read` and `privacy-policy:manage` both exist in a freshly published database, and a `privacy-policy:*` wildcard row exists too — but no role holds any of them.** Only the global `*` of `super-admin` reaches these seven authenticated endpoints until you grant the codes to a role yourself. See [Section 11](#11-permission-matrix).

#### GET `/api/v1/privacy-policy/published`

The one endpoint in this area that anybody may call, and the one the accounts application calls on every visit to notice that the policy has changed.

**Auth:** Anonymous

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `language` | string | `en` | One of `en`, `ar`, `tr`, `fr`, `zh`, `ur`, `fa`. A regional form such as `tr-TR` is reduced to `tr`; anything unsupported falls back to `en` rather than failing |

**Response (200):**

```json
{
  "version": "2026.03",
  "effectiveDateUtc": "2026-03-01T00:00:00Z",
  "languageCode": "en",
  "contentJson": "{\"title\":\"Privacy Notice\",\"sections\":[…]}",
  "disclosure": {
    "graceDays": 30,
    "otpValidityMinutes": 15,
    "loginAttemptRetentionDays": 365,
    "outboxRetentionDays": 180,
    "identifierReservationDays": 1095,
    "policyVersion": "2026.07",
    "legalName": "Example Ltd",
    "address": "1 Example Street, Example City",
    "privacyEmail": "privacy@example.com",
    "emailProvider": "Example Mail",
    "hostingProvider": "Example Hosting",
    "hostingCountry": "Türkiye",
    "dpoContact": "",
    "verbisNo": "",
    "kepAddress": ""
  }
}
```

**`contentJson` is a string containing JSON, not a nested object** — parse it a second time. It is the structured document, not finished HTML; the ready-made HTML page lives at the public addresses in [5.19](#519-the-public-policy-pages).

**The last three disclosure fields are always present, and an empty string is their normal value.** `dpoContact`, `verbisNo` and `kepAddress` are optional legal details — a data protection officer's contact, a Turkish VERBİS registration number, and a KEP registered-email address. Unlike a null property, they are **not** omitted from the body: they are plain strings, so an installation that has not filled them in sends `""`. That is deliberate, because a JSON null rendered into the published notice would print the literal word "null" in a legal document. Treat an empty string as "omit this line", which is what the notice itself does.
*In code:* `Auth/Auth.Application/DTOs/PrivacyPolicyVersionDto.cs:102-159`; they are filled in at `Auth/Auth.Application/Features/PrivacyPolicy/GetPublishedPrivacyPolicy/GetPublishedPrivacyPolicyQueryHandler.cs:70-92`.

**`disclosure` is read live from configuration on every request, not frozen into the revision.** Those numbers are the retention and recovery windows the notice quotes, so changing a setting changes what the API reports here immediately. That is also why `GET /api/v1/privacy-policy/versions` returns a `disclosureOutOfDate` flag on each revision: it tells an administrator that a published revision's frozen page no longer matches the running configuration and should be published again, rather than letting the system quietly amend a notice people have already been shown.

**There are two version strings in that body and they are not the same thing.** The top-level `version` is the policy revision you are reading, named when somebody recorded it. `disclosure.policyVersion` is the configuration key `AccountDeletion:PolicyVersion`, which stamps the retention policy the deletion machinery is operating under. They are both `YYYY.MM` and they move independently.

**`languageCode` in the response tells you what you actually got**, which is not always what you asked for — compare it with your request before displaying a language label.

**404 `PrivacyPolicy.NoPublishedVersion` means no revision has been published yet.** On a fresh installation that is the normal state, not an error in your client.

---

### 5.19 The Public Policy Pages

**Base route:** `/privacy` — outside `/api/`, with no version segment

**These three addresses serve the privacy notice as a finished web page, for a human being to read.** They are what you put in an app-store listing or a website footer. They are anonymous, they are stable, and they return HTML rather than JSON.

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/privacy` | **302 redirect** to the reader's best language, chosen from the `Accept-Language` header | Anonymous |
| GET | `/privacy/{language}` | The currently published notice, as one complete HTML page | Anonymous |
| GET | `/privacy/v{version}/{language}` | A superseded revision, at its permanent address — for example `/privacy/v2026.01/tr` | Anonymous |

**The browser's language preference is consulted at `/privacy` and nowhere else.** Quality values are honoured and unsupported tags are skipped; an unrecognised preference lands on English. Once a reader is on `/privacy/tr`, that stated choice always wins — overriding it with a guess from the browser would itself be a dark pattern.

**Each page is standalone and inert.** It loads no script, no font, no image and makes no request of any kind. It carries its own Content Security Policy, stricter than the one the rest of the API sends, which denies everything and permits exactly one inline stylesheet named by its hash.

**Caching is deliberate and worth understanding before you put a content delivery network in front of it.** The response sets `Cache-Control: public, s-maxage=300, stale-while-revalidate=604800, stale-if-error=2592000`, a strong `ETag` built from the content, `Last-Modified`, and `Vary: Accept-Encoding`. Sending `If-None-Match` with that ETag returns **304**. There is deliberately no `must-revalidate` and no `no-cache`: those would oblige a disconnected cache to produce an error rather than serve what it holds, which turns a brief outage into a broken legal page.

**A missing document returns a bare 404 with no body** — not the ProblemDetails object the rest of the API returns. The caller here is a browser showing a page to a person, not a client parsing errors.

**In production these pages are not served by the API at all.** The accounts application's IIS configuration rewrites `/privacy/...` to static HTML files that publishing wrote to disk, so the notice stays readable even when the API is down. In development the accounts development server proxies `/privacy` to `https://localhost:5101` so the same links work.
*In code:* `Auth/Auth_API/Modules/NotificationManagement/Controllers/PublicPolicyController.cs`; the rewrite rules are in `Auth_UI/apps/accounts/public/web.config`; the on-disk location is `PrivacyPolicyPublication:PhysicalPath`.

#### GET `/privacy/{language}`

**Auth:** Anonymous

**Path parameter:** `language` — one of `en`, `ar`, `tr`, `fr`, `zh`, `ur`, `fa`.

**Response (200):** `text/html; charset=utf-8`, a complete page.

```text
HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8
Cache-Control: public, s-maxage=300, stale-while-revalidate=604800, stale-if-error=2592000
ETag: "9f2c…"
Last-Modified: Sun, 01 Mar 2026 00:00:00 GMT
Vary: Accept-Encoding
Content-Security-Policy: default-src 'none'; style-src 'sha256-…'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'

<!doctype html> …
```

**Send that `ETag` back as `If-None-Match` on the next request and you get 304 with no body.**

---

### 5.20 Platform Branding

**Base route:** `/api/v1/Platform`

One anonymous endpoint that answers a question every sign-in screen has to ask before anybody is signed in: what is this platform called, and what is its logo?

| Method | Path | What it does | Auth |
|---|---|---|---|
| GET | `/api/v1/Platform/branding` | The platform name and logo addresses | **Anonymous** |

#### GET `/api/v1/Platform/branding`

**Auth:** Anonymous. **It is the only endpoint on this controller, and it is anonymous by necessity** — the login page, the invitation-acceptance page and the browser tab icon all need it before a token exists.

**Response (200), on an installation that has a light logo and nothing else:**

```json
{
  "platformName": "AuthSystem",
  "logoUrl": "https://localhost:5101/uploads/images/9c1f4e2ab7d4436f9c0e5a1b2c3d4e5f.webp"
}
```

**`logoUrlDark` and `faviconUrl` are not in that body at all, and that is what "not set" looks like** — null properties are omitted from every response in this API, so an unset image is an absent key rather than a null one. Do not test for null; test for presence, and fall back the way the applications do: no dark logo means use the light one; no favicon means use the theme logo, then your own default icon.

**Absolute addresses come from `ImageStorage:PublicBaseUrl`**, which in development is `https://localhost:5101/uploads/images`. The stored value is only a key; the API composes the address when it answers. Changing that setting changes every logo address at once.

**This endpoint reads the same record that [5.21](#521-platform-settings) writes.** It simply returns less of it, and asks for nothing.

---

### 5.21 Platform Settings

**Base route:** `/api/v1/admin/platform-settings`

The administrator's side of the same record: the platform name and the three image addresses, with the audit fields showing who changed them last.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/admin/platform-settings` | The branding values, plus who last modified them and when | `platform-settings:manage` |
| PUT | `/api/v1/admin/platform-settings` | Replace `platformName`, `logoUrl`, `logoUrlDark` and `faviconUrl` | `platform-settings:manage` |

**There is no separate read permission.** Both endpoints require `platform-settings:manage`, so anybody who may look here may also change what every sign-in screen displays. In the console this is the **Platform settings** page.

**`platform-settings:manage` does have a row in a freshly published database — but no role holds it.** The seed creates the permission and files it under the `auth:*` family, expecting the seeded `admin` role to inherit it; that inheritance does not exist at run time, because a parent code grants nothing below it and `auth:*` does not match a code beginning `platform-settings:`. On a clean install only the global `*` reaches this area. [4.4](#44-permission-based-authorization) explains the matching rule and [Section 11](#11-permission-matrix) has the whole picture.

#### GET `/api/v1/admin/platform-settings`

**Permission:** `platform-settings:manage`

**Response (200):**

```json
{
  "platformName": "AuthSystem",
  "logoUrl": "https://localhost:5101/uploads/images/9c1f4e2ab7d4436f9c0e5a1b2c3d4e5f.webp",
  "modifiedAt": "2026-03-01T10:00:00Z",
  "modifiedBy": "00000000-0000-0000-0000-000000000001",
  "modifiedByName": "Platform Admin"
}
```

**An image that has never been set is absent from the body**, not present as null — `logoUrlDark` and `faviconUrl` are missing above for that reason. The three `modified*` fields are absent too until somebody saves the record for the first time.

**To change a logo you upload the image first and set the branding second.** `POST /api/v1/Images` ([5.23](#523-images)) stores the file and returns both a key and an address; the `PUT` here then records it in `logoUrl`, `logoUrlDark` or `faviconUrl`. Nothing on this endpoint accepts file content.

**You may send either the key or the full address, and the result is the same.** The endpoint strips the configured public base from whatever you send and stores the bare key, so that changing `ImageStorage:PublicBaseUrl` later moves every image at once instead of stranding stored addresses. Send `null` to clear an image.
*In code:* `Auth/Auth.Application/Common/ImageUrlComposer.cs:37-51`.

---

### 5.22 System Settings

**Base route:** `/api/v1/admin/system-settings`

**This is how an administrator changes the platform's behaviour — password rules, token lifetimes, mail settings — without editing a configuration file or redeploying anything.** The database stores only the values that have been *overridden*; everything else keeps coming from `appsettings.json`. In the console it is the **System settings** page.

| Method | Path | What it does | Permission |
|---|---|---|---|
| GET | `/api/v1/admin/system-settings` | Every section, with each field's effective value, its database override, its file baseline, the shipped default, and whether a restart is pending | `system-settings:manage` |
| PUT | `/api/v1/admin/system-settings/{sectionKey}` | Replace one section's overrides. Returns the updated section | `system-settings:manage` |
| POST | `/api/v1/admin/system-settings/{sectionKey}/reset` | Drop every override in one section, so it falls back to the files. Returns the section | `system-settings:manage` |
| POST | `/api/v1/admin/system-settings/email/test` | Send a diagnostic email to the calling administrator using the settings in force right now. Returns 204 | `system-settings:manage` |

**`sectionKey` is one of these 22 names, spelled exactly as shown:** `Jwt`, `Password`, `Session`, `Gateway`, `Cors`, `RateLimiting`, `GatewayRateLimiting`, `ExternalAuth`, `IdentityProvider`, `Email`, `Notifications`, `GeoIp`, `ImageStorage`, `AccountDeletion`, `DataRetention`, `DataController`, `Maintenance`, `HealthChecks`, `Serilog`, `DataProtection`, `SecretManagement`, `ConnectionStrings`. Any other value returns **404**.

**Four rules govern writing, and breaking any of them is a wasted afternoon.**

1. **A `PUT` replaces the section's whole override set. It is not a patch.** Any field you leave out of the payload stops being overridden and reverts to its configuration-file value. Send the complete set every time, including the fields you are not changing.
2. **You must echo the `rowVersion` you read.** It is the section's concurrency token. If somebody else saved that section since you loaded it, the call returns **409** and nothing is written. A section that has never been overridden has `rowVersion: null`; send that.
3. **The last three sections cannot be written at all.** `DataProtection`, `SecretManagement` and `ConnectionStrings` come back with `"editable": false`, because they are consumed before the database layer exists — the process must read them to reach the database that would hold their overrides. They are information cards. Key material is managed through [5.12](#512-secrets-admin) instead.
4. **A sensitive field never shows its value**, in any section. `effectiveValue` comes back null for those, so a password cannot be read back out of this endpoint.

**`system-settings:manage` exists in a freshly published database, but no role holds it.** As with the areas above, only the global `*` of `super-admin` can open this page until the code is granted to a role. See [Section 11](#11-permission-matrix).

#### GET `/api/v1/admin/system-settings`

The call that tells you not just what a setting is, but where the value came from.

**Permission:** `system-settings:manage`

**Response (200), trimmed to one section and two fields — a real response carries all 22 sections:**

```json
{
  "restartPending": false,
  "dbOverridesUnavailable": false,
  "sections": [
    {
      "key": "Password",
      "group": "security",
      "editable": true,
      "version": 2,
      "rowVersion": "AAAAAAAAB9E=",
      "modifiedAt": "2026-03-01T10:00:00Z",
      "modifiedBy": "00000000-0000-0000-0000-000000000001",
      "modifiedByName": "Platform Admin",
      "fields": [
        {
          "path": "MinimumLength",
          "kind": "int",
          "effectiveValue": 12,
          "overrideValue": 12,
          "baselineValue": 8,
          "defaultValue": 8,
          "source": "database",
          "restartRequired": false,
          "isPendingRestart": false,
          "readOnly": false,
          "sensitive": false,
          "min": 6,
          "max": 128
        },
        {
          "path": "RequireUppercase",
          "kind": "bool",
          "effectiveValue": true,
          "baselineValue": true,
          "defaultValue": true,
          "source": "file",
          "restartRequired": false,
          "isPendingRestart": false,
          "readOnly": false,
          "sensitive": false
        }
      ]
    }
  ]
}
```

**Four values describe every field, and they answer four different questions.**

| Field | The question it answers |
|---|---|
| `effectiveValue` | What is the API running with right now? |
| `overrideValue` | What has been saved into the database? Absent when nothing has |
| `baselineValue` | What would it fall back to if the override were removed — the configuration-file value, or the shipped default when the files say nothing |
| `defaultValue` | What did this system ship with, regardless of anything this deployment configured? |

`source` names the winner in one word: `database`, `file`, `default` or `secrets`. `kind` is one of `bool`, `int`, `string`, `enum` or `stringArray`, and `min`, `max` and `allowedValues` appear only where a bound exists — send a value outside them and the `PUT` returns 400.

**Some changes only take effect after a restart, and the response says which.** A field with `"restartRequired": true` — `Jwt:Issuer` is one — keeps serving the old value until the process is recycled. Once such a field has been saved but not yet applied, its `isPendingRestart` turns true and the top-level `restartPending` turns true with it, which is what the console's banner reads.

**`dbOverridesUnavailable: true` means the last attempt to load overrides from the database failed and you may be looking at stale file values.** The settings layer fails open on purpose: a database that is briefly unreachable degrades this page rather than stopping the API.

**One key you may expect to find here is deliberately absent.** `SecretManagement:RequiredPermission` is not in the registry because nothing reads it — the permission that guards the secrets endpoints is compiled into the controller and cannot be changed by configuration.
*In code:* `Auth/Auth.Application/SystemSettings/SystemSettingsRegistry.cs`.

---

### 5.23 Images

**Base route:** `/api/v1/Images`

One endpoint, and it is the only way to get a picture into this system. Profile photographs, organization logos, application logos and platform branding all upload here first and are then attached to a record by whichever endpoint owns it.

| Method | Path | What it does | Auth |
|---|---|---|---|
| POST | `/api/v1/Images` | Upload and process an image; returns its storage key and public address | **Authenticated, with no permission code** |

**Any signed-in user may upload an image.** There is no permission attribute on this action, because every account needs to be able to set its own profile picture. What an uploaded image can be *attached* to is gated separately by the endpoint that attaches it.

#### POST `/api/v1/Images`

**Auth:** Any valid access token.

**Request:** `multipart/form-data` with **one form field named `file`**. Not JSON, and not a base64 string in a JSON body.

```bash
curl -X POST "https://localhost:5101/api/v1/Images" \
  -H "Authorization: Bearer <your access token>" \
  -F "file=@logo.png"
```

**Run that from the directory that contains `logo.png`**, and replace `<your access token>` with the `token.accessToken` value a sign-in returned. Success prints the two-field JSON body below. A `401` means the token is missing or expired; a certificate complaint means the development certificate is not trusted yet — [§3.7](#37-verifying-the-setup) explains how to fix that once, or add `-k` for a one-off probe.

**Response (200):**

```json
{
  "key": "9c1f4e2ab7d4436f9c0e5a1b2c3d4e5f.webp",
  "url": "https://localhost:5101/uploads/images/9c1f4e2ab7d4436f9c0e5a1b2c3d4e5f.webp"
}
```

**Store the `key`, not the `url`.** The key is what the record keeps; the address is composed from `ImageStorage:PublicBaseUrl` each time it is read, so moving the images to a different host later is a configuration change rather than a data migration.

**Your file is not stored as you sent it.** Every upload is re-encoded to WebP at quality 90, resized so its longest edge is at most 1,024 pixels, stripped of metadata including any GPS coordinates, and written under a random name. The original filename is discarded — which is why the key always ends in `.webp` whatever you sent.

**The limits, and what each one rejects:**

| Setting | Shipped value | Effect |
|---|---|---|
| `ImageStorage:AllowedContentTypes` | `image/png`, `image/jpeg`, `image/webp`, `image/gif` | Anything else is refused |
| `ImageStorage:MaxSizeBytes` | `4194304` — 4 MB | A larger file is refused. The request body limit follows this setting live, so the two can never disagree |
| `ImageStorage:MaxMegapixels` | `24` | A file within the byte limit but enormous in dimensions is refused, which is what stops a decompression attack. Every admitted megapixel costs 4 MB of memory during the decode, so this is a memory budget, not a compatibility ceiling |
| `ImageStorage:MaxEdgePx` | `1024` | Not a rejection — anything larger is scaled down |
| `RateLimiting:ImageUploadConcurrencyLimit` | `2` | How many uploads may be decoding at the same moment, process-wide; the next four wait, anything beyond that is refused with `429` |

**Failures on this endpoint do not look like failures anywhere else in this API.** This controller returns a bespoke body — a single `error` string — instead of the ProblemDetails object everything else returns. You will see `400` with `{"error": "No file provided."}`, `{"error": "File exceeds the maximum size of 4194304 bytes."}`, `{"error": "Unsupported image type 'image/bmp'."}` or `{"error": "The uploaded file is not a valid image."}`, `500` with `{"error": "…"}` when the storage itself is at fault — for example an uploads directory the application cannot write to — and `429` with `{"error": "…", "retryAfter": 5}` when more uploads are decoding than `RateLimiting:ImageUploadConcurrencyLimit` allows and the short queue behind it is full. Branch on the HTTP status code, not on a `title` field, because there is none here.
*In code:* `Auth/Auth_API/Modules/Media/Controllers/ImagesController.cs`; the processing is `Auth/Auth.Infrastructure/Services/FileSystemImageStorageService.cs`.

**Uploaded files are served back as static files from `/uploads/images/...`, with no token required.** Anyone holding the address can fetch the image, so do not upload anything through this endpoint that should not be public.

---

## 6. Common Workflows

Each workflow below is a complete ordered sequence: every call in the order it must happen, what comes back from each one, and — where one of the two shipped web applications already does the work — the screen that does it. The applications themselves are described in [4.12](#412-the-two-web-applications); the endpoint contracts they call are in [Section 5](#5-api-reference).

**Two things are assumed true before any of these, because they are the two commonest reasons a first attempt fails.** The API is running on its `https` launch profile ([3.6](#36-running-the-api-and-gateway)), and you are signed in as an account that holds the permission each step names ([3.6c](#36c-sign-in-for-the-first-time)). Where a workflow needs a permission that a freshly published database does not grant to anybody, it says so at the top rather than at the point of failure.

### 6.1 Register, Verify the Email Address, and End Up Signed In

**Registration emails the verification code by itself, and there is no token to hold until the address is confirmed.** Those two facts decide the whole shape of this flow: it is two calls, not four, and the middle one is not `send-verification-email`.

**Step 1 — Create the account.** `POST /api/v1/auth/register`, anonymous.

```json
{
  "email": "newuser@example.com",
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe"
}
```

**Success is 201** with a body that carries **no tokens at all**:

```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "maskedEmail": "new***@example.com",
  "message": "Registration successful. Please verify your email.",
  "organizationCreated": false,
  "verificationCodeExpiresAt": "2026-03-12T10:15:00Z"
}
```

`verificationCodeExpiresAt` is the deadline on the code that registration has already emailed — use it to show a countdown. **If that field is missing, the email failed to send**, and the person needs Step 3 before they can do anything.

**Step 2 — Confirm the address, which also signs the person in.** `POST /api/v1/auth/verify-email`, anonymous. Send `email`, not `userId`:

```json
{
  "email": "newuser@example.com",
  "otp": "123456"
}
```

**Success is 200 carrying a full login response** — access token, refresh token and user — and the identity-provider sign-in cookie is set on the same response. **The person is now signed in. There is no separate login call.** Nobody has to type a password again immediately after proving they own the mailbox.

**Step 3 — Only when the code never arrived.** `POST /api/v1/auth/resend-verification-email` with `{ "email": "newuser@example.com" }`, anonymous, returns 200 with a fresh `expiresAt`.

**Do not reach for `POST /api/v1/auth/send-verification-email` here.** That endpoint requires a bearer token, and nobody has one until Step 2 succeeds. It exists for a person who is already signed in and whose address is still unconfirmed.

**There is an administrative variant of Step 2, and it behaves differently.** Sending `userId` instead of `email` marks the address confirmed, signs nobody in, and returns **204 No Content**. A client that assumes one status code breaks on the other path; branch on the status code. Both are set out in [5.2](#52-authentication).

**In the accounts application this is `/register` followed by `/verify-email`.** The two paths converge on the same screen: signing in with a correct password on an account whose address is still unconfirmed does not show an error either, it sends the person to `/verify-email`, where confirming the code signs them in.
*In code:* `Auth_UI/packages/auth/src/pages/login.tsx:110-115`.

**No welcome email is sent after registration.** `welcome-email` is a seeded notification type, and it is the only seeded type with no template of its own, so there is nothing for the system to render.

### 6.2 Sign a Person In From Your Own Application (Authorization Code + PKCE)

**This is how an application that is not part of this repository signs people in, and it is what both shipped applications do.** Your application never sees anyone's password. It sends the browser here, the person signs in on the accounts application, and your application receives a one-time code that it trades for tokens. PKCE — Proof Key for Code Exchange — is what stops a stolen code from being usable by anyone but the application that started the flow. The full eight-step mechanism and both endpoint contracts are in [5.2](#52-authentication); this workflow is the setup you must do first, and the order to do it in.

**Three things must be true before the first redirect, and each one fails in a different way.**

1. **The application must be registered, and your callback address must be on its list.** `POST /api/v1/applications` with `code` — which is what OAuth calls the `client_id` — and `redirectUris` containing the exact address, character for character. In the console this is the **Applications** page, the **New application** button, and then the application's own page. Permission: `applications:create`, which no seeded role holds; see the warning at the top of [6.6](#66-set-up-an-application-with-its-own-roles-and-permissions).
2. **If the application's access mode is `Restricted`, the person must be on its access list.** `Restricted` is the default for a newly created application. Add them with `POST /api/v1/applications/{id}/access`, supplying `userId` and, ideally, a `roleId` — the access list opens the door but grants nothing, so somebody admitted without a role signs in able to do nothing.
3. **The accounts application must be running and reachable at the address in `IdentityProvider:AccountsBaseUrl`.** That is where the browser is sent to type a password. In development it is `https://localhost:5174`.

**Then the flow itself, in five steps.**

1. **Your application invents a random string, keeps it, and hashes it.** The string is the `code_verifier` and never leaves your application. Its SHA-256 hash, base64url-encoded, is the `code_challenge`.
2. **Send the browser to the authorize endpoint** with the challenge and your callback address:

   ```text
   GET https://localhost:5101/api/v1/auth/authorize
       ?response_type=code
       &client_id=my-app
       &redirect_uri=https://localhost:5173/callback
       &code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM
       &code_challenge_method=S256
       &state=opaque-value-the-app-checks-later
   ```

3. **The person signs in — but only if they have to.** If the browser already carries a valid `auth_idp` cookie from an earlier sign-in, this step is skipped entirely and they are never shown a password box. Otherwise the browser is redirected to the accounts application's `/login` page, and returned here afterwards.
4. **The browser arrives at your `redirect_uri` with `code` and your original `state` attached.** Check that `state` matches the one you sent; that check is how you detect a forged return. **The code is valid for 60 seconds and can be used exactly once.**
5. **Exchange the code for tokens from your own server-side or client code.** This call is a form post, not JSON, and its response uses `snake_case` — both differ from every other endpoint in this API. Run this from any directory; it talks to the API over the network:

   ```bash
   curl -X POST https://localhost:5101/api/v1/auth/token \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d "grant_type=authorization_code" \
     -d "code=THE_ONE_TIME_CODE" \
     -d "redirect_uri=https://localhost:5173/callback" \
     -d "client_id=my-app" \
     -d "code_verifier=THE_RANDOM_STRING_FROM_STEP_1"
   ```

   **You should see** HTTP 200 and a body containing `access_token`, `token_type`, `expires_in`, `refresh_token` and `refresh_expires_in`. Replace the two capitalised values with the code from Step 4 and the verifier from Step 1.

**There is no client secret anywhere in this flow, and that is deliberate.** Every registered application here is a public client, so nothing authenticates itself at the token endpoint. PKCE takes the secret's place and is mandatory.

**Three failures account for almost every problem, and they look nothing alike.**

- **400 Bad Request with no redirect at all** means the `client_id` is unknown or the `redirect_uri` is not on that application's list. The API refuses to redirect to an address it has not verified.
- **The sign-in loops back to `/login` forever, with no error message**, means the browser is discarding the `auth_idp` cookie. That happens when anything in the chain is plain HTTP instead of HTTPS, or when `Cors:AllowedOrigins` contains `"*"`. [Section 10](#10-troubleshooting) covers both.
- **The token exchange returns an error about the code** means the 60 seconds elapsed, or the code was already spent. Start again from Step 2.

### 6.3 Turn On Two-Factor Authentication, and Handle the Sign-In It Changes

Two-factor authentication means the person proves who they are twice: with a password, and then with a six-digit code from an authenticator application on their phone. TOTP — Time-based One-Time Password — is the standard those applications implement.

**Step 1 — Ask for a secret.** `POST /api/v1/auth/2fa/setup`, authenticated. Returns 200 with `secret` and `qrCodeUri`.

**Step 2 — The person scans that QR code** with an authenticator application such as Google Authenticator or Authy. Nothing has changed on the account yet.

**Step 3 — Prove the scan worked, and switch it on.** `POST /api/v1/auth/2fa/enable`, authenticated, with `{ "code": "123456" }` taken from the application. Returns 200 with an array of recovery codes.

**Step 4 — Store the recovery codes.** **They are shown exactly once.** They are the only way back in for somebody who loses the phone.

**Step 5 — Change your sign-in code, because signing in is now two calls instead of one.** This is the step most clients forget, and forgetting it produces a client that appears to sign people in and then fails on every request afterwards.

From now on `POST /api/v1/auth/login` for this account returns **200 with `requiresTwoFactor: true` and a `twoFactorChallengeToken`, and with no tokens and no user object**. Nobody is signed in yet. The client shows a code box and then calls `POST /api/v1/auth/2fa/verify` — which is **anonymous**, because the caller has no token — with the challenge token and the six-digit code:

```json
{
  "challengeToken": "opaque-challenge-string",
  "code": "123456",
  "useRecoveryCode": false
}
```

That second call returns the real login response and sets the sign-in cookie. Set `useRecoveryCode` to `true` when the person is typing one of their saved recovery codes instead of an application code. **Check `requiresTwoFactor` before you read `token`.**

**Turning it off** is `POST /api/v1/auth/2fa/disable`, authenticated, with a currently valid code — 204 No Content.

**In the applications:** both of them carry the same **Profile** page with its security area, so an administrator turns this on for themselves in the console and an end user turns it on in the accounts application. Steps 1 to 4 are that screen. Step 5 is the shared `/two-factor` challenge page, which both applications also have.
*In code:* `Auth_UI/packages/account/src/pages/profile/profile-security.tsx`.

Full request and response shapes are in [5.3](#53-two-factor-authentication).

### 6.4 Reset a Forgotten Password

**Step 1 — Start the reset.** `POST /api/v1/auth/forgot-password`, anonymous, with `{ "email": "user@example.com" }`. Returns 200 with a deliberately vague message: **the answer is the same whether or not that address has an account**, so the endpoint cannot be used to discover which addresses exist.

**Step 2 — The person receives an email containing a link.** The link's origin comes from the configuration key `Email:FrontendBaseUrl`, and it lands on `/reset-password` in the accounts application. **When email is enabled, that key must be an absolute address or the API refuses to start** — deliberately, because a relative value would produce reset links that go nowhere and fail silently ([3.4](#34-configuration-reference)).

**Step 3 — Set the new password.** `POST /api/v1/auth/reset-password`, anonymous:

```json
{
  "token": "reset-token-from-the-emailed-link",
  "newPassword": "NewSecureP@ss1!",
  "confirmNewPassword": "NewSecureP@ss1!",
  "terminateSessions": true
}
```

**There is no email field, and adding one would be a mistake.** The token identifies the account entirely on its own — the server looks the request up by a hash of the token. An address alongside it would add nothing and would hand an attacker a way to test whether an address exists.

**Success is 204 No Content.**

**This endpoint is on the stricter of the two rate-limit policies.** `password-reset` allows 10 requests per 60 seconds per client IP address; every other anonymous endpoint in this area uses `login`, which allows 20.

### 6.5 Invite Somebody Into an Organization, and Have Them Accept

**The flow forks depending on one thing: whether the invited person already has an account.** Skipping the call that tells you which is the usual reason this workflow breaks.

**Step 1 — Send the invitation.** `POST /api/v1/organizations/{id}/invitations`. Permission: `org:members:invite`.

```json
{
  "email": "invitee@example.com",
  "roleId": "10000000-0000-0000-0001-000000000003",
  "languageCode": "ar"
}
```

`email` and `roleId` are required; `roleId` is the organization membership role they will hold. `languageCode` is optional and decides which of the seven languages the email is written in — left out, the system uses the invitee's own profile language if they already have an account, and otherwise the language of your request.

**Success is 201** with an `OrganizationInvitationDto`. **The identifier field is `id`, not `invitationId`**, and the body includes the one-time `token`:

```json
{
  "id": "d1e2f3a4-0000-0000-0000-000000000001",
  "token": "y1Zq...redacted...",
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "organizationName": "Acme Corporation",
  "email": "invitee@example.com",
  "roleId": "10000000-0000-0000-0001-000000000003",
  "roleCode": "org-member",
  "roleName": "Organization Member",
  "status": "Pending",
  "expiresAt": "2026-03-17T00:00:00Z",
  "isExpired": false
}
```

**An invitation lasts 7 days**, which is a constant in the handler rather than a configurable setting. **If the invitation email fails to send the call still succeeds**, because the token is right there in the response and can be passed on by hand.

In the console this is **Organizations**, then the organization, then **Invite member** on its members list.

**Step 2 — Find out which path applies.** `GET /api/v1/invitations/{token}`, anonymous, shows what the invitation is for and, critically, **`userExists`**: `true` means the invited address already has an account, `false` means it does not. `isExpired` tells you whether the seven days have run out.

**Do not branch on `isAlreadyMember`.** The field is on the object and no code path ever sets it, so it is always `false` and tells you nothing.

**Step 3a — They already have an account (`userExists: true`).** They sign in first, then call `POST /api/v1/invitations/{token}/accept` with their bearer token and **no body**. Success is 200:

```json
{
  "success": true,
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "organizationName": "Acme Corporation",
  "roleName": "Organization Member",
  "message": "Successfully joined the organization.",
  "messageCode": "Invitation.Joined"
}
```

**The field is `roleName`; there is no `role` field.** Accepting an invitation that was already accepted is a **success**, not an error — the same 200 comes back with `messageCode: "Invitation.AlreadyMember"` — so a client retrying after a dropped connection does not have to treat the second attempt as a failure.

**Step 3b — They do not have an account (`userExists: false`).** They call `POST /api/v1/invitations/{token}/register`, which is **anonymous** and creates the account and joins the organization in one step. They do **not** register through the ordinary registration endpoint first.

```json
{
  "password": "SecureP@ssw0rd!",
  "firstName": "Jane",
  "lastName": "Doe"
}
```

**There is no email field here either** — the address comes from the invitation, so an invitee cannot redirect it to a different mailbox. Success is **200, not 201**, and carries **no tokens**: the new account's address is already treated as confirmed, no verification email is sent, and the person now signs in normally.

**In the applications:** both branches are the accounts application's `/accept-invitation` screen, which serves anonymous and signed-in visitors alike. The console's `/accept-invitation` route is only a redirect to it.

Full contracts are in [5.9](#59-invitations).

### 6.6 Set Up an Application With Its Own Roles and Permissions

**Read this before you start: on a freshly published database only the seeded `super-admin` account can complete this workflow.** Every one of the four steps needs a permission that no seed row creates — `applications:create`, `permissions:create`, `roles:create` and `users:manage-roles` are four of the 34 codes the API enforces but a clean publish never grants. You also cannot fix it from inside the system, because creating the missing permission rows itself requires `permissions:create`.

**So sign in as the seeded administrator, `admin@company.com`.** Its `super-admin` role holds the global `*` grant, which is the only thing that reaches those four codes. [Section 11](#11-permission-matrix) lists which codes are seeded and which are not, and [3.2](#32-database-setup) explains why the seed file that would have created them never runs.

**Step 1 — Register the application.** `POST /api/v1/applications`. Permission: `applications:create`.

```json
{
  "code": "CRM",
  "name": "Customer Relationship Manager",
  "redirectUris": ["https://crm.example.com/signin-callback"]
}
```

Success is 201 with an `ApplicationDto`. **Keep two values from it.** `id` is the identifier the next steps need. `code` is the public client identifier that the authorization-code flow calls `client_id` ([6.2](#62-sign-a-person-in-from-your-own-application-authorization-code--pkce)), and it can never be changed afterwards.

**Step 2 — Create the permission codes this application will check for.** `POST /api/v1/permissions`, once per code. Permission: `permissions:create`.

```json
{
  "applicationId": "app-guid",
  "code": "crm:leads:read",
  "name": "Read Leads"
}
```

Repeat for `crm:leads:create`, and — if you want one code that covers the others — `crm:leads:*`.

**Wildcards match by string prefix and nothing else, so pick the codes carefully.** A held code ending in `:*` matches everything that starts with the part before it: `crm:leads:*` satisfies `crm:leads:read`. But a code **without** the trailing `:*` grants nothing below it, `crm:*:read` matches nothing at all, and matching is on the text of the code, not on any tree structure. [4.4](#44-permission-based-authorization) sets out exactly what matches and what does not.

**Step 3 — Create roles that bundle those permissions.** `POST /api/v1/roles`. Permission: `roles:create`.

```json
{
  "applicationId": "app-guid",
  "code": "crm-editor",
  "name": "CRM Editor",
  "permissionIds": ["read-permission-guid", "create-permission-guid"]
}
```

**Step 4 — Assign a role to a person.** `POST /api/v1/users/{userId}/roles` with `{ "roleId": "crm-editor-guid" }`. Permission: `users:manage-roles`. Success is 204 No Content.

**Step 5 — Wait for the person's token to catch up, or make them sign in again.** A person's permissions travel inside their access token, so **assigning a role changes nothing for anybody who is already signed in until they hold a new token**. A new token arrives either when they sign in again, or on their client's next token refresh — at most `Jwt:AccessTokenLifetimeMinutes` away, which the shipped configuration sets to 15. The refresh re-reads roles and permissions from the database, so no sign-out is strictly required.
*In code:* `Auth/Auth.Application/Features/Authentication/RefreshToken/RefreshTokenCommandHandler.cs:190-197`.

**In the console** these four steps are the **Applications**, **Permissions**, **Roles** and **Users** pages, in that order.

### 6.7 Rotate an API Key Without an Outage

An API key is a long-lived secret string that a service presents instead of a person signing in. Rotating one replaces it while leaving the old one working for a while, so a running service can be switched over without downtime.

**Step 1 — Rotate.** `POST /api/v1/apikeys/{id}/rotate`. Permission: `apikeys:rotate`. The body is optional; omitting it gives a 60-minute grace period.

```json
{
  "gracePeriodMinutes": 120
}
```

Success is 200:

```json
{
  "newApiKey": "ak_prod_Zx4Nm8Qr2Tv6Yb1Dc9Fg3Hj5Kl7Pw0S",
  "newApiKeyId": "c3d4e5f6-0000-0000-0000-000000000002",
  "newKeyPrefix": "ak_prod_",
  "oldKeyExpiresAt": "2026-03-12T15:30:00Z",
  "oldApiKeyId": "b2c3d4e5-0000-0000-0000-000000000001",
  "message": "New API key generated successfully. Old key will remain valid until 2026-03-12 15:30:00 UTC. Please update your applications to use the new key before the grace period ends.",
  "messageCode": "ApiKey.Rotated"
}
```

**This response is the only time the new key is readable.** The database stores an Argon2id hash of it and nothing else, so a key that is not captured here is gone.

**`message` arrives already translated into the caller's language**, with the old key's expiry substituted into the sentence — one of only three translated success messages in the whole system. Display `message`; branch on the stable `messageCode`, and never assemble your own sentence from it.

**Step 2 — Move every consumer onto `newApiKey` before `oldKeyExpiresAt`.** Nothing does this for you, and nothing warns you as the deadline approaches.

**Step 3 — The old key stops working by itself.** Rotation does **not** revoke it; it sets an expiry on it `gracePeriodMinutes` from now. The new key inherits the old one's application, environment, rate-limit values and expiry date, and is named `<old name> (rotated)`.

**The scope list is the one thing rotation does not carry over, and nothing warns you.** An API key's scopes are rows in `ApiKeyScopes` keyed by that key's identifier, and rotation does not copy them, so the new key comes back with an empty `scopes` list. There is also no endpoint that adds a scope to a key that already exists — `permissionIds` is accepted only when a key is created. **So a scoped key cannot be rotated.** Create a replacement with `POST /api/v1/apikeys`, passing the same `permissionIds`, and revoke the old key yourself once every consumer has moved.
*In code:* `Auth/Auth.Application/Features/ApiKeys/RotateApiKey/RotateApiKeyCommandHandler.cs`.

**Rotating a key that is already revoked returns 400** with the error code `ApiKey.AlreadyRevoked`.

**Rotation is not a way to change throttling.** A key's `rateLimitPerMinute` and `rateLimitPerDay` values are stored, validated and returned, but **nothing in this repository enforces them** ([5.10](#510-api-keys)).

### 6.8 Edit and Publish a Notification Template

**Every word this system emails lives in the database, so changing an email is an edit-and-publish, not a redeployment.** In the console it is **Notifications → Templates**, then the template you want. What happens after a message is sent is [4.10](#410-how-a-notification-becomes-an-email); the endpoint contracts are [5.14](#514-notification-templates).

**Three permissions are involved, and a freshly published database grants none of them to any role.** `notification-templates:read` opens the page, `notification-templates:manage` saves a draft, `notification-templates:publish` publishes one. The rows themselves do exist, unlike some others, so you can grant them to a role yourself — but until you do, only `super-admin`'s global `*` reaches this area.

**Step 1 — Open the template.** `GET /api/v1/notification-templates/{id}` returns the template with its published version, its draft if there is one, and every translation.

**Step 2 — Understand what you are about to edit.** A template holds two pointers: a **published** version, which is what actually gets sent, and a **draft**, which is what you are editing. **A draft is never sent to anyone.** Your first save creates the draft automatically, as a copy of the published version numbered one higher; if a draft already exists, you carry on editing that one.

**Step 3 — Save the draft.** `PUT /api/v1/notification-templates/{id}/draft`:

```json
{
  "translations": [
    {
      "languageCode": "en",
      "subject": "Confirm your email address",
      "bodyHtml": "<p>Hello {{ user.firstName }}, your code is {{ code }}.</p>",
      "bodyText": null
    }
  ],
  "removeLanguages": [],
  "changeNote": "Reworded the subject line",
  "expectedModifiedAt": "2026-03-10T09:12:00Z"
}
```

**Saving runs the text through the template renderer first and refuses a syntax error**, so a broken template can never reach the draft. `expectedModifiedAt` is the concurrency check: send back the value you read, and if somebody else saved in the meantime you get **409** and nothing is written.

**Step 4 — Look at it before you commit to it.** `POST /api/v1/notification-templates/preview` renders your editor buffer on the server, using the notification type's sample data and the published layout, and saves nothing. **What you see is exactly what a real send produces.** To go further, `POST /api/v1/notification-templates/{id}/test-send` with `languageCode` and `recipientEmail` sends one real message and returns 204.

**Step 5 — Publish.** `POST /api/v1/notification-templates/{id}/publish`. In the console the **Publish** button stays disabled while you have unsaved edits, so save first.

Four things about publishing are easy to get wrong from the endpoint name alone.

1. **Publishing moves a pointer. It copies nothing and deletes nothing.** The version that was published stays in the history, which is what makes rolling back instant and lossless. Publishing also clears the draft pointer — the draft you published *is* the published version now, and there is no draft any more.
2. **All languages go live together.** There is one pointer per template, not one per language, so you cannot ship the French text while holding the Arabic back.
3. **The publish check is stricter than the save check.** Every language of the draft is rendered against the type's sample data with unknown variables treated as failures, and one bad language blocks the publish for all of them — the error names the language that failed.
4. **Publishing is refused unless the template's own default language has a translation.**

**Step 6 — Undo, three different ways.** `POST /{id}/rollback` points the published pointer back at an earlier version. `POST /{id}/versions/{versionId}/restore-draft` copies an old version into a new draft, and is refused if a draft is already pending. `DELETE /{id}/draft` throws the draft away and deletes its row, returning **200 with the template**, not 204.

**A published template is cached in the API's memory for 15 minutes.** Publishing, unpublishing and rolling back **through these endpoints** clear that cache immediately, so a change made in the console is live at once. **Editing the rows directly in SQL does not clear it**, and such a change can take up to 15 minutes to appear.
*In code:* `Auth/Auth.Infrastructure/Notifications/TemplateCache.cs`.

**A template on the `Sms` or `Push` channel cannot be delivered.** Those values exist in the enumeration and can be stored, but only the email channel has a delivery strategy registered; anything queued on the other two fails and retries until it dead-letters.

### 6.9 Change a System Setting From the Console, and Know Whether It Took Effect

**Settings changed here are stored in the database and win over the configuration files, so you do not edit `appsettings.json` and you do not redeploy.** Only the fields you actually override are stored; everything else keeps coming from the files. In the console it is the **System settings** page; the contract is [5.22](#522-system-settings). Permission: `system-settings:manage`, whose row exists in a fresh database but which no role holds.

**Step 1 — Read the section first.** `GET /api/v1/admin/system-settings` returns all 22 sections. For every field it reports four values — what the API is running with, what the database overrides, what it would fall back to, and what shipped — plus a one-word `source` naming the winner.

**Step 2 — Note two things before you change anything.** The section's `rowVersion`, which you must echo back, and the field's `restartRequired`, which decides Step 4.

**Step 3 — Write the whole section back.** `PUT /api/v1/admin/system-settings/{sectionKey}`:

```json
{
  "overrides": {
    "MinimumLength": 12,
    "RequireUppercase": true
  },
  "rowVersion": "AAAAAAAAB9E="
}
```

**This is a replace, not a patch, and getting it wrong silently undoes work.** Any field you leave out of `overrides` stops being overridden and reverts to its configuration-file value. Send the complete set every time, including the fields you are not changing. A section that has never been overridden has `rowVersion: null` — send `null`. **A stale `rowVersion` returns 409 and writes nothing**, which means somebody else saved that section since you read it.

**Step 4 — Find out whether the change is live. There are exactly two outcomes, and the field told you which in Step 2.**

- **`restartRequired: false` — the change is already in force.** Saving re-runs the database configuration provider inside the running process and every consumer rebinds. A five-minute timer is the safety net for changes made straight in SQL rather than through this endpoint. Rate limits, allowed CORS origins, logging levels and health-check detail all work this way.
- **`restartRequired: true` — nothing has changed yet, and will not until the process is recycled.** The field's `isPendingRestart` turns true and the top-level `restartPending` turns true with it, which is exactly what the console's banner is reading. These are the restart-required fields: `Jwt:Issuer`, `Jwt:Audience`, `Jwt:KeyId`, `Jwt:ClockSkewSeconds`, `Password:Argon2MemorySize`, `Password:Argon2Iterations`, `Password:Argon2Parallelism`, `Password:Pepper:Enabled`, `Password:BreachedPasswordCheck:Enabled`, `Password:BreachedPasswordCheck:TimeoutMs`, `IdentityProvider:IdpSessionCookieName`, `GeoIp:Enabled`, `GeoIp:DatabasePath`, `ImageStorage:RequestPath` and `AccountDeletion:RunEncryptionMigration`.

**To perform that restart in development:** press `Ctrl+C` in the terminal running the API, then, from `Auth/Auth_API/`, run it again:

```bash
dotnet run --launch-profile https
```

**You should see** `Now listening on: https://localhost:5101`, and `restartPending` back to `false` the next time you read the settings. On a Windows server under Internet Information Services (IIS), recycle the application pool instead.

**Step 5 — Undo a section.** `POST /api/v1/admin/system-settings/{sectionKey}/reset` drops every override in that section, so it falls back to the files.

**Three sections cannot be written at all, and the response says so with `"editable": false`.** `DataProtection`, `SecretManagement` and `ConnectionStrings` are read before the database layer exists — the process needs them to reach the very database that would hold their overrides. They are information cards. Key material is managed through [5.12](#512-secrets-admin) instead.

**A gateway setting needs one extra wait that the console cannot show you.** The API Gateway is a separate process that cannot read the database; it polls the API and picks up changed limits within one poll interval, 30 seconds by default.

**`dbOverridesUnavailable: true` in the response means you may be looking at stale file values**, because the last attempt to load overrides from the database failed. The settings layer fails open deliberately: a database blip degrades this page rather than stopping the API.

### 6.10 Delete an Account — the Self-Service Way and the Administrative Way

**These are two entirely different mechanisms, not two entrances to one.** A person deleting their own account gets a 30-day grace window and can change their mind. An administrator deleting somebody else's account performs a reversible soft delete, which a second and irreversible call can then turn into a real destruction.

#### The person deletes their own account, from inside the application

**Step 1 — Ask for a code.** `POST /api/v1/users/me/deletion/send-code`, authenticated. Returns 202 Accepted and emails a six-digit code.

**Step 2 — Confirm with that code.** `POST /api/v1/users/me/deletion` with `{ "otpCode": "123456" }`. **Being signed in is not enough, on purpose**: the code is a fresh proof that the person still controls the mailbox, and it is demanded even of accounts that sign in through Google or Apple and therefore have no password to re-enter. Returns 202 with `graceEndsAtUtc`.

**Step 3 — What happens when, which is not what most people assume.** The account is deactivated **immediately** and every session is revoked, so the person is signed out everywhere at once. The account itself is destroyed only when the grace window closes, at the moment given in `graceEndsAtUtc` — `AccountDeletion:GraceDays` after the request, which the shipped configuration sets to 30.

**Step 4 — Undoing it during the window.** `POST /api/v1/auth/deletion/recover`, anonymous, with `email`, `password` and, for an account with two-factor turned on, `twoFactorCode`. Success cancels the deletion, restores the account **and signs the person in**, returning the same body as an ordinary login. For an account with no password, `POST /api/v1/auth/deletion/recover-external` does the same with a Google or Apple identity token.

**In the accounts application:** **Profile → the danger zone**, which then lands on `/deletion-scheduled`. During the grace window signing in does not show an error — the login screen recognises the pending deletion and routes the person to `/account-recovery`, with the deadline in the message.
*In code:* `Auth_UI/packages/account/src/pages/profile/profile-danger-zone.tsx:71-99`; `Auth_UI/packages/auth/src/pages/login.tsx:119-123`.

#### The person deletes their own account without being able to sign in

**These two endpoints exist because an app-store listing must offer account deletion to somebody who has forgotten their password.**

**Step 1 — Request a code by address alone.** `POST /api/v1/auth/deletion/request` with `{ "email": "user@example.com" }`, anonymous. Returns 202. **The answer is identical whether or not that address has an account**, so it cannot be used to discover which addresses exist.

**Step 2 — Confirm.** `POST /api/v1/auth/deletion/confirm` with `email` and `otpCode`, anonymous. Returns 202. Confirming a deletion that is already pending succeeds again rather than erroring, so a double submission is harmless. From here the same 30-day grace window and the same two recovery endpoints apply.

**In the accounts application:** `/delete-account`. The login page deliberately does not link to it.

#### An administrator deletes somebody else's account

**Step 1 — Soft delete.** `DELETE /api/v1/users/{id}`. Permission: `users:delete`. Returns 204. **Nothing is destroyed.** The row stays, marked deleted with a timestamp, and disappears from ordinary listings; only a caller holding `users:manage` can see it again by asking for `includeDeleted=true`. This is the delete the console offers.

**Two rules refuse it.** The built-in system account cannot be deleted. And an account that owns an organization with other members in it cannot be deleted until that organization is transferred or emptied — an owned organization with no other members is deleted along with the account.

**Step 2 — Permanent deletion, which is optional and irreversible.** `DELETE /api/v1/users/{id}/permanent`. Permission: **`users:manage`**, deliberately a stronger code than the `users:delete` that the soft delete needs. **The account must already be soft-deleted**; calling this on a live account returns the error code `User.NotSoftDeleted`.

**What "permanent" means here, precisely — three kinds of data are treated three different ways.** A tombstone is written **first**, before anything is destroyed, so that a failure part-way through cannot lose the record that a deletion happened. Credentials and personal data are then deleted outright. The audit trail and the sign-in history are **anonymized, never deleted**: those rows survive with the identity stripped, and anything the deleted account performed is re-attributed to the built-in system account, so the security record stays intact while the person does not.

**The email address can never be registered again.** The tombstone holds a keyed hash of it, and every path that creates a user checks that registry. A re-registration attempt returns the ordinary "email already taken" conflict — byte for byte the same answer as any duplicate — so nothing about the deletion leaks. The tombstone is kept for `AccountDeletion:IdentifierReservationDays`, shipped as 1095 days. **This is why `AccountDeletion:IdentifierHmacKeyPlain` must never be rotated** ([3.3](#33-first-startup-and-secret-generation)): replacing it orphans every reservation at once.

**In the console:** **Users**, then the row's action menu for the soft delete. To reach the permanent one, turn on **Show deleted** — the toggle appears only for callers holding `users:manage` — and the deleted row's single available action is **Delete permanently**, which makes you type the account's email address to confirm.

---

## 7. Database Schema Overview

**The database has 52 tables, and they live in 6 folders that are also how you should think about them.** Each folder under `Auth/Auth_DB/dbo/Tables/` is one group, and every table in the repository is one `.sql` file in one of those folders.

| Group folder | Tables | What the group is for |
|---|---|---|
| `Core/` | 11 | Who exists, what they may do, and which applications they may do it in |
| `Authentication/` | 7 | The record of people signing in and the tokens and sessions that result |
| `Security/` | 16 | Everything that proves an identity or destroys one: codes, keys, hashes and the audit trail |
| `Organizations/` | 7 | Tenant organizations, their members and what those members hold inside them |
| `Notifications/` | 6 | Every message the platform sends, its content, its versions and its delivery log |
| `System/` | 5 | Platform-wide configuration, branding and the privacy policy |
| **Total** | **52** | |

**Everything below is one plain sentence per table.** Where a table is documented in more detail elsewhere in this guide, the section is linked.

### Core (11 tables)

| Table | What it holds |
|---|---|
| `Users` | The platform account itself: identity, credentials, profile, lockout state and soft-delete state |
| `Applications` | The client applications registered with this platform, with their sign-in policy switches |
| `ApplicationRedirectUris` | The exact-match list of callback addresses each application is allowed to be sent back to, used by the authorization-code flow |
| `ApplicationUserAccess` | One row per person individually admitted to an application; read only when that application's access mode is `Restricted` |
| `Roles` | Named role definitions, either global or scoped to one application |
| `Permissions` | The permission codes themselves, written `{application}:{resource}:{action}`, with their wildcard and level metadata |
| `RolePermissions` | Which permissions each role grants |
| `UserRoles` | Which roles each person holds, optionally scoped to one application and optionally time-limited |
| `UserPermissions` | Permissions granted straight to a person, bypassing roles entirely |
| `UserUiPreferences` | Per-person display preferences, one JSON value per allow-listed key |
| `PermissionImplications` | Rows saying "holding permission A also grants B". **These are stored for the administration screens and have no effect on authorization** — nothing walks them at sign-in time |

### Authentication (7 tables)

| Table | What it holds |
|---|---|
| `RefreshTokens` | HMAC-SHA256 hashes of issued refresh tokens, with the chain of rotations and revocations behind each one |
| `UserSessions` | One row per device session, per application, with how the session was attributed and how it ended |
| `UserKnownDevices` | Device signatures a person has signed in from before, which is what makes a new-device alert possible |
| `IdpSessions` | The server side of the identity-provider sign-in cookie — the thing that lets the authorization-code flow recognise somebody who is already signed in |
| `LoginAttempts` | One row per sign-in ceremony, not per HTTP request, with its outcome and a human-readable failure reason |
| `ExternalAuthProviders` | The catalogue of social sign-in providers and whether each is switched on |
| `UserExternalLogins` | The link between a platform account and its Google or Apple identity |

### Security (16 tables)

| Table | What it holds |
|---|---|
| `AuditLogs` | The append-only record of account and administrative actions, with the before-and-after values as JSON |
| `PasswordHistory` | A person's previous password hashes, so an old password cannot be reused |
| `EmailVerificationTokens` | Argon2id hashes of the six-digit email-verification codes, with their attempt counters |
| `PasswordResetTokens` | HMAC-SHA256 hashes of single-use password-reset link tokens |
| `AuthorizationCodes` | The one-time OAuth codes, each with its PKCE challenge and the redirect address it was issued for |
| `RevokedTokens` | The durable backing store for the revocation list that the API keeps in memory |
| `TwoFactorAuth` | A person's authenticator secret (encrypted), their recovery codes and their two-factor lockout counters |
| `TwoFactorChallenges` | The short-lived challenges issued mid-sign-in, keyed by a hash of the opaque challenge token |
| `ApiKeys` | Application API keys: the Argon2id hash, the stored rate-limit values, the address allowlists and the revocation state |
| `ApiKeyScopes` | The permissions each API key is scoped to |
| `WebhookKeys` | Per-application webhook signing keys, stored as keyed HMAC-SHA256 hashes rather than Argon2id, with the address they sign for |
| `AccountDeletionRequests` | Deletion requests as they move through their grace window to a final state |
| `AccountDeletionVerifications` | Argon2id-hashed one-time codes proving a deletion request, including the path that needs no sign-in |
| `AccountDeletionTombstones` | Keyed hashes of deleted email addresses, which is what reserves an address after the account is gone |
| `UserEncryptionKeys` | One wrapped AES-256-GCM key per person; deleting the row destroys that person's encrypted fields outright |
| `SecretOperationChallenges` | The step-up approvals that gate destructive operations on the platform's own signing keys |

### Organizations (7 tables)

| Table | What it holds |
|---|---|
| `Organizations` | The tenant organizations themselves, with their owner, branding and contact details |
| `OrganizationUsers` | Membership: which people belong to which organization, and in what organization-level role |
| `OrganizationInvitations` | Email invitations to join an organization, with their token and status |
| `OrganizationApplications` | Which applications an organization has enabled, with tier and expiry |
| `OrganizationUserRoles` | Application-level roles held inside one organization, scoped to (organization, person, application) |
| `OrganizationUserPermissions` | Permissions granted directly inside one organization, scoped the same way |
| `OwnershipTransferCodes` | One-time hashed codes confirming the transfer of an organization to a new owner |

### Notifications (6 tables)

| Table | What it holds |
|---|---|
| `NotificationTypes` | The catalogue of message kinds, each with the variables its template may use and the sample data used to preview it |
| `NotificationTemplates` | One template per (application, type, channel), holding the two pointers: which version is published and which is the draft |
| `NotificationTemplateVersions` | The numbered, unchangeable versions of a template — this is the history that makes rollback possible |
| `NotificationTemplateTranslations` | The actual subject and body text, one row per (version, language) |
| `NotificationLayouts` | The shared visual wrapper placed around every message body, held as a draft copy and a published copy |
| `NotificationOutbox` | The queue and delivery log of rendered messages, with retry state |

### System (5 tables)

| Table | What it holds |
|---|---|
| `SystemSettingsOverrides` | The settings overridden from the console, one row per section, stored as sparse JSON ([6.9](#69-change-a-system-setting-from-the-console-and-know-whether-it-took-effect)) |
| `PlatformSettings` | The single row of platform branding: name, light and dark logo, favicon |
| `PrivacyPolicyVersions` | The register of privacy-policy revisions, each with its effective date and notice status |
| `PrivacyPolicyTranslations` | The authored policy document, as JSON, per (revision, language) |
| `PrivacyPolicyArtifacts` | The rendered standalone HTML actually served to the public, frozen at the moment of publication |

### Soft Delete Applies to Exactly Two Tables

**Only `Users` and `Applications` carry `IsDeleted`.** Deleting either of those marks the row and hides it from ordinary listings, and the row is still there.

**The remaining 50 tables delete for real.** A row removed from `UserSessions`, `RefreshTokens`, `OrganizationUsers`, `NotificationTemplateVersions` or any other table in this schema is gone, and no `includeDeleted` flag will bring it back. Do not write code that assumes a general soft-delete convention, because there is not one.
*Verified by:* `grep -rl IsDeleted Auth/Auth_DB/dbo/Tables` returns those two files and no others.

The two-stage user delete built on this — soft first, then optionally permanent — is [6.10](#610-delete-an-account--the-self-service-way-and-the-administrative-way).

### Stored Procedures: 9 Exist, 4 Are Used

**There are nine stored procedures in total, in two folders, and five of them are called by nothing.** The folders are `Auth/Auth_DB/dbo/StoredProcedures/Authentication/`, which holds seven, and `Auth/Auth_DB/dbo/StoredProcedures/Users/`, which holds two. **There is no procedure for roles, permissions, applications, API keys, audit or two-factor** — those domains have none at all.

**Called by the application (4):**

| Procedure | Called from |
|---|---|
| `sp_CreateRefreshToken` | `Auth/Auth.Infrastructure/Persistence/RefreshTokenRepository.cs:51` |
| `sp_RevokeAllUserTokens` | `Auth/Auth.Infrastructure/Persistence/RefreshTokenRepository.cs:99` |
| `sp_GetUserById` | `Auth/Auth.Infrastructure/Persistence/UserRepository.cs:62` |
| `sp_GetUserByEmail` | `Auth/Auth.Infrastructure/Persistence/UserRepository.cs:159` |

**Defined but never called (5):** `sp_CheckAccountLockout`, `sp_RecordLoginAttempt`, `sp_RevokeRefreshToken`, `sp_ValidateCredentials`, `sp_ValidateRefreshToken`. They are published to the database and no C# code invokes any of them. If you are reading one of these to understand how sign-in works, stop — it is not the code that runs.

**Everything else is inline SQL through Dapper.** Dapper is a thin mapper that executes SQL you write by hand and turns the rows into objects; it is not an object-relational mapper that generates queries for you. So apart from those four calls, every read and write in this system is a SQL string in a repository class under `Auth/Auth.Infrastructure/Persistence/`. There is no Entity Framework anywhere in this repository and no migration tooling — the schema is owned entirely by the database project ([3.2](#32-database-setup)).

### What a Fresh Publish Puts in These Tables

Publishing the database package creates the 52 tables **and** runs the post-deployment script, which is what seeds the roles, permissions, users, notification types, templates and policy rows. The counts, and the important trap about six seed files that never run, are set out once in [3.2](#32-database-setup) and are not repeated here.

---

## 8. Security Best Practices

This section is a plain inventory of what protects an account here, with the values the system actually ships with. Where a value is configurable, the exact key is named. Where something looks like a protection and is not one, it says so here rather than sitting in a list that reads as shipped.

### Password Security

**Passwords are stored as Argon2id hashes.** Argon2id is a memory-hard hashing algorithm: checking one password deliberately costs a fixed amount of memory and time, which is what makes guessing billions of them expensive. The shipped parameters are 19,456 kilobytes of memory (19 MiB), 2 iterations and 1 thread, with a fresh 16-byte random salt for every password and a 32-byte result. Verification compares the two hashes in constant time, so a wrong password takes exactly as long as a right one and leaks nothing through timing.
*In code:* `Auth/Auth.Infrastructure/Authentication/Argon2PasswordHasher.cs`. Keys: `Password:Argon2MemorySize`, `Password:Argon2Iterations`, `Password:Argon2Parallelism`, `Password:SaltSize`, `Password:HashSize`.

**Raising those costs later needs no password reset.** If a stored hash was produced with different parameters than the current settings, the next successful sign-in silently re-hashes that password with the current ones.

**The minimum length is 8 characters, and deleting the setting does not weaken it.** `Auth/Auth_API/appsettings.json` sets `Password:MinimumLength` to 8, and the class that reads it also defaults to 8, so a missing key changes nothing.
*In code:* `Auth/Auth.Application/Configuration/PasswordSettings.cs`.

**The console can lower it to 6, and there is no second, hidden floor stopping you.** The system-settings registry accepts any value from 6 to 128 for this field, and the length rule in the validator is the configured number and nothing else. **6 is a genuinely weak password policy** — the Open Worldwide Application Security Project (OWASP), the usual source for this baseline, recommends 12 or more, so even the shipped 8 is a floor rather than a target. Lower it only for a deliberate reason you have written down.
*In code:* `Auth/Auth.Application/SystemSettings/SystemSettingsRegistry.cs` (the `MinimumLength` field definition).

**Four character classes are required as shipped**, each with its own switch: an uppercase letter (`Password:RequireUppercase`), a lowercase letter (`:RequireLowercase`), a digit (`:RequireDigit`) and a special character (`:RequireSpecialCharacter`). Independently of those settings, a short built-in list of obvious substrings is always rejected: `password`, `123456`, `qwerty`, `abc123`, `letmein`, `admin`, `welcome`, `monkey`, `dragon`, `master`, `login`.
*In code:* `Auth/Auth.Application/Validators/PasswordValidator.cs`.

**The last three passwords cannot be reused.** `Password:HistoryCount` is 3. The check runs on both the change-password and the reset-password paths, and it compares against the password currently in force as well as the stored history, so the effective block is "the current one plus three". Older history rows are pruned after each change.

**There is no password expiry, and nothing here will ever force a rotation on a schedule.** The `Password:ExpirationDays` key was removed because nothing ever computed a password's age. The `Users.PasswordExpiresUtc` database column still exists and is never given a value. Forced rotation is a feature somebody would have to build; it is not a setting to switch on.

**Account lockout and request rate limiting are two different defences, and the system needs both.**

- **Lockout is per account.** Five consecutive failed sign-ins lock that account for 15 minutes; the account unlocks itself on the first attempt after the window passes. Keys: `Password:MaxFailedAttempts` (5) and `Password:LockoutDurationMinutes` (15).
- **Rate limiting is per client IP address.** The `login` policy allows **20 requests per 60 seconds** from one address. Keys: `RateLimiting:LoginPermitLimit` (20) and `RateLimiting:LoginWindowSeconds` (60).
- **Why both.** Lockout alone still lets one attacker try five passwords each against thousands of accounts. An address limit alone still lets a distributed attacker grind one account slowly from many addresses. Each covers the other's blind spot, and the code says so in a comment where the limiter is registered.

**Two further protections exist and both ship turned off.**

- **A pepper** — a server-side secret mixed into every hash and kept in the secret store rather than in the database, so a stolen copy of the database alone is not enough to test password guesses offline. Turn it on with `Password:Pepper:Enabled`. Existing passwords are not disturbed: a hash stored before the change carries no pepper identifier, so it still verifies and is quietly re-hashed with the pepper at that user's next successful sign-in. **What is unforgiving is losing the pepper afterwards** — verification fails closed for any hash whose pepper identifier is unknown, which locks every peppered user out permanently. Back it up with the rest of the secret store.
- **A breached-password check** against Have I Been Pwned (HIBP), a public catalogue of passwords exposed in known breaches. It uses the keyless range interface and sends only the first five characters of a hash, never the password. `Password:BreachedPasswordCheck:Enabled` turns it on, `:Mode` chooses `Enforce` (reject the password) or `Warn` (accept it and return an `X-Password-Warning` header), and `:FailOpen` (true) means an unreachable HIBP does not block a password change.

### Token Security

- **Access tokens are signed with RS256** — RSA with SHA-256, an asymmetric signature. Any service can verify a token with the public key; only this service can mint one, because only it holds the private key. A generated key is 2048 bits.
- **Access tokens live 15 minutes** (`Jwt:AccessTokenLifetimeMinutes`), which bounds how long a stolen one is useful.
- **Refresh tokens are 64 random bytes and live 7 days** (`Jwt:RefreshTokenLifetimeDays`). **That setting, and nothing under `Session:`, is what actually governs how long a session lasts** — a session row is created with exactly this expiry.
- **Refresh tokens rotate on every use, and a reused one is treated as theft.** Presenting a token that was already exchanged revokes every token the account holds and raises an event that notifies the user by email.
- **Nothing token-shaped is stored in readable form.** Refresh tokens, password-reset tokens, two-factor challenge tokens, OAuth authorization codes, webhook keys and the identity-provider session cookie are all stored as keyed HMAC-SHA256 hashes — a deterministic hash, so the value presented can still be looked up in one query. Application programming interface (API) keys, two-factor recovery codes and email verification codes are stored as Argon2id hashes instead, which is slower to check but has no key to lose.
  *In code:* `Auth/Auth.Infrastructure/Security/RefreshTokenKeyService.cs` and `Auth/Auth.Infrastructure/Security/WebhookKeyHasher.cs` for the HMAC group; `Auth/Auth.Infrastructure/Security/ApiKeyGenerator.cs` for the Argon2id group.
- **A revoked access token stops working immediately, without waiting for it to expire.** Revocations are held in memory for speed, written through to the `RevokedTokens` table so they survive a restart, and reloaded at startup. Three scopes exist: one token, one session, or every token a user holds. The check runs after the token has been authenticated and before authorization decides anything.

### Encryption at Rest

**Secrets never live in the database and never live in source control.** They sit in one of three storage modes chosen by `SecretManagement:StorageMode`, explained in full in [3.3](#33-first-startup-and-secret-generation): `PlainText` (a git-ignored local file next to the application), `Certificate` (an encrypted file whose key is an X.509 certificate you own — portable between servers, and the mode the shipped base configuration selects) and `Dpapi` (Windows-only, machine-and-account bound). The encrypted secrets file and the Data Protection key ring both belong outside the public web root.

**Individual sensitive columns are encrypted by the application, not by SQL Server.** Every person gets their own data-encryption key, wrapped by the Data Protection key ring and stored in the `UserEncryptionKeys` table. Values are encrypted with AES-256-GCM — the Advanced Encryption Standard in Galois/Counter Mode, which both encrypts and authenticates — and each ciphertext is bound to its purpose and to that user's identifier, so a value copied into another row or another column fails to decrypt rather than quietly working. Three things are protected this way today: the two-factor secret, the phone number, and an external provider's refresh token.
*In code:* `Auth/Auth.Infrastructure/Security/PerUserCryptoService.cs`.

**SQL Server Always Encrypted is not used.** No column in this schema is declared `ENCRYPTED WITH`. If you inherit a connection string carrying `Column Encryption Setting=Enabled`, that flag does nothing here — the protection is entirely application-side, as described above.

**Back up the secret store, and the `.pfx` file in Certificate mode.** Losing either invalidates every token in circulation. If the optional pepper is enabled, losing it locks peppered users out for good.

### Transport Security

- **HTTPS is always redirected to, and outside Development it is enforced with HSTS** — HTTP Strict Transport Security, the response header that tells a browser to refuse plain HTTP for this host from now on. Shipped values: 365 days, including sub-domains, with preload requested.
- **Whether the database connection is encrypted depends entirely on your connection string.** The committed development string sets `TrustServerCertificate=True`, which encrypts the connection but skips validating the server's certificate. That is acceptable on your own machine and must not be carried into production; the production settings belong in [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md).

### Request Security

- **Gateway token validation** compares the `X-Gateway-Token` header in constant time and answers a mismatch with 403 and a problem-details body.
- **Rate limiting is uneven, deliberately.** The API defines exactly two named policies — `login` (20 requests per 60 seconds) and `password-reset` (10 per 60 seconds) — and **no global limit at all**. The gateway carries the global one: 1000 requests per 60 seconds, plus `auth` at 20/60 s, `api` at 100/60 s and `admin` at 120/60 s. The practical consequence is blunt: **an API that is reachable without going through the gateway has no rate limit on any endpoint that does not opt in.**
- **Cross-Origin Resource Sharing (CORS) uses an explicit origin list in every environment.** Never `["*"]` — see [3.4 — CORS](#cors) for why the wildcard silently breaks sign-in.
- **Security headers are written on every response:** `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `X-XSS-Protection: 1; mode=block`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: geolocation=(), microphone=(), camera=()`, and a Content Security Policy (CSP) of `default-src 'self'; frame-ancestors 'none'`. The `Server` and `X-Powered-By` headers are stripped.
- **The CSP is the one exception to "always overwritten": it is written only if the response does not already carry one.** That is what lets an endpoint returning HTML set its own stricter policy. Every other header in the list above is set unconditionally.

**One assumption deserves stating bluntly: the API trusts the `X-Forwarded-For` header from whoever sends it.** It takes the first entry of that header as the client address, and the forwarded-headers middleware is configured with no list of known proxies or networks. So any caller who can reach the API directly can choose which rate-limit bucket they land in, and can write any address they like into the audit log. Account lockout is unaffected, because it counts per account rather than per address. **This is safe only when the API is unreachable except through the gateway.** If the API is directly reachable on your network, treat every per-address protection here as advisory.
*In code:* `Auth/Auth_API/Common/ClientIpResolver.cs` and the forwarded-headers registration in `Auth/Auth_API/Program.cs`.

### Authorization

- **Access is decided by permission codes, not by role names.** Roles are just bundles of codes; the check never looks at a role.
- **The only widening rule is a trailing `:*`**, which is a string-prefix test — `users:*` satisfies `users:read` at any depth. `auth:*` does not satisfy `users:read`. The full rule, with the cases that surprise people, is [4.4](#44-permission-based-authorization).
- **Permission implications grant nothing.** The `PermissionImplications` table and the `ParentId` / `Level` / `IsWildcard` columns are display metadata for the console. Holding `users:manage` does not give you `users:read`.
- **Checks read claims out of the already-validated token, so there is no database lookup per request** — with one deliberate exception. An organization-scoped check on a token carrying no claim at all for that organization triggers a single live membership read, which also means an organization role taken away is not enforced until the token is refreshed. Both consequences are in [4.4](#44-permission-based-authorization).
- **On a freshly published database, 34 of the 50 permission codes this API enforces cannot be granted to anyone.** That is not a hardening measure; it is a seeding defect with security consequences, because it pushes every real task onto the one account holding the global `*`. [Section 11](#11-permission-matrix) is the whole picture.

### Audit and Monitoring

**Audit rows record who, what, when and where.** Each row carries the acting user, the action name, the entity type and identifier, the old and new values as JSON, the IP address, the user agent, the session, and a `PerformedBy` column that can differ from the subject — an administrator changing somebody else's password is the standard case.

**Coverage is not universal, and the audit log has no success-or-failure dimension.** Two limits, stated plainly because both are easy to assume away:

- **The `AuditLogs` table has no `IsSuccess`, `ActionType`, `ErrorMessage` or `CorrelationId` column.** The code fills those four fields with constants whenever it reads a row, so **every audit row reports success** and a failed operation cannot be told apart from a successful one. The `actionType` and `isSuccess` query parameters on the audit endpoints are accepted and then ignored — see [5.11](#511-audit-logs).
- **Some operations write no audit row at all.** Creating or revoking a webhook key raises an event that has no handler, so nothing is recorded — unlike an API key, whose creation is audited.

**Logging is structured, and request correlation is only half wired.** Serilog writes structured events to the console and to a daily rolling file with 30 files retained, enriched with the log context, the machine name and the thread identifier. The gateway generates an `X-Correlation-ID` when a caller did not send one, forwards it to the API, and puts it on its own request-log line. **The Auth API does not put that value on its log lines**; it only echoes the header back inside an error body when the caller supplied one. So do not expect to trace a single request across both processes by searching the API log for a correlation identifier.

---

## 9. Testing

### 9.1 What Exists, and What Runs It

There are two test suites, one on each side of the product, and they share nothing — not a runner, not a command, not a working directory.

| | Back end | Front end |
|---|---|---|
| Where the tests live | `Auth/Auth_API.Tests/` | inside `Auth_UI/`, next to the code they cover, plus `Auth_UI/e2e/` |
| Runner | xUnit, driven by `dotnet test` | Vitest for unit tests, Playwright for end-to-end tests |
| Run them from | `Auth/` | `Auth_UI/` |
| What must be running first | nothing at all | nothing for the unit tests; quite a lot for the end-to-end ones ([9.4](#94-the-front-end-test-suites)) |

**Nothing runs any of this automatically.** The repository has a `.github/workflows/` folder and it is empty. There is no continuous-integration pipeline, no build server, no check on a pull request. Every command below is something a person types.

### 9.2 The Back-End Test Suite

**One test project covers the entire back end:** `Auth/Auth_API.Tests/Auth_API.Tests.csproj`. There is no second one anywhere in the solution.

| Package | Version | What it does here |
|---|---|---|
| `xunit` | 2.9.3 | The test framework — supplies `[Fact]` and `[Theory]` |
| `xunit.runner.visualstudio` | 3.1.5 | Lets the standard .NET test host discover and run xUnit tests |
| `Microsoft.NET.Test.Sdk` | 18.8.1 | The test host itself |
| `Moq` | 4.20.72 | Creates stand-in objects for dependencies a test does not want to run for real |
| `FluentAssertions` | 8.10.0 | The assertion style used throughout (`result.Should().BeTrue()`) |
| `coverlet.collector` | 10.0.1 | Collects code-coverage data when you ask for it — see below |
| `Microsoft.OpenApi` | 2.11.0 | Used by the tests that check the shape of the generated API description |

**How big it is, counted rather than estimated.** The project holds **176** C# files, of which **171** contain at least one test. Between them they carry **1,412 `[Fact]` attributes** — a `[Fact]` is one test — and **68 `[Theory]` attributes**, where a theory is one test method run once per row of data. Those theories are fed by **241 `[InlineData]` rows** and **2 `[MemberData]` sources**. The number of cases the runner reports is therefore higher than 1,412, because every data row becomes its own case.

**There is no database and no web host in the test project.** Nothing spins up SQL Server, and nothing starts the API in memory. Behaviour that lives in SQL is guarded a different way: those tests read the repository source text and the database project's scripts and assert on what they find. That is why a test can catch a missing foreign key in a purge, or a seed script missing its batch separator, without a database existing.

**Run the back-end tests. Run this from `Auth/`:**

```bash
dotnet test Auth_API.Tests/Auth_API.Tests.csproj
```

**You should see:** the project build, then a run summary whose last line reports `Failed: 0` alongside the passed and total counts. A first run takes a while because it builds; if you have just built, add `--no-build` to skip that.

**Do not run `dotnet test` against `Auth/Auth.sln`.** The solution contains the SQL Server database project, which the `dotnet` command-line tool cannot build — the same failure described in [3.1](#31-clone-and-build). Name the test project explicitly, as above.

**Coverage: the collector is installed, and nothing measures anything.** You can produce raw coverage data on demand. **Run this from `Auth/`:**

```bash
dotnet test Auth_API.Tests/Auth_API.Tests.csproj --collect:"XPlat Code Coverage"
```

That writes a `coverage.cobertura.xml` file under `Auth/Auth_API.Tests/TestResults/`, in a machine-readable format you have to render yourself with a separate tool. **This repository has no coverage threshold, no report task and no build gate, and publishes no coverage figure.** Any percentage you may have seen quoted for this system is unsupported by anything in the code — which is why no number appears here either.

### 9.3 Tests That Will Fail Your Change, and Why

Most tests check one handler. A handful exist to fail the build when a *class* of mistake is made, and those are the ones that will stop you unexpectedly. Knowing they exist saves an afternoon.

| Guard test | What it refuses to let you do |
|---|---|
| `Gateway/GatewayRouteCoverageTests.cs` | Add a controller without adding a matching route to the gateway's configuration. Forget it and the whole feature is a 404 through the gateway while working perfectly when called directly. |
| `Localization/DomainErrorResourceCoverageTests.cs` | Add a domain error code without adding its text to `DomainErrors.resx`. Errors created inline in a handler must additionally be listed by hand in the test's own `HandlerInlineCodes` array. |
| `Localization/BaselineCoverageTests.cs` | Let the seven language files drift apart. Every language must declare the same keys as English *and* the same numbered placeholders inside each string. |
| `Infrastructure/PostDeploymentScriptTests.cs` | Add a seed script that does not end with its own `GO` batch separator, or declare the same variable twice across included batches. One missing `GO` once broke every database publish. |
| `Infrastructure/PlatformSeedContractTests.cs` | Re-seed the retired platform application row, or reorder the post-deployment steps so a migration runs after the seeds that depend on it. |
| `Configuration/GatewayRateLimitingParityTests.cs` | Change a gateway rate limit in one of its three homes and not the other two. Drift here is invisible at run time: the console would show one limit while the gateway enforced another. |
| `Configuration/SystemSettingsApplyCoverageTests.cs` | Add an editable setting to the console that does not actually reach the configuration key the API reads. The rule it enforces is "a value saved in the console is the value the API runs on". |

### 9.4 The Front-End Test Suites

Both suites live in the workspace root, `Auth_UI/`, and both assume you have already run `pnpm install` there once — that is Step 1 of [3.6b](#36b-install-and-run-the-two-web-applications). If you have not, no command in this part will work.

#### Unit tests — Vitest

**Vitest is the test runner that comes with Vite**, and it runs these tests inside a simulated browser (jsdom) rather than a real one. **Nothing has to be running:** not the API, not either web application, not SQL Server.

There are **40** test files containing **276** `it()` or `test()` cases. They sit beside the code they cover, under `apps/*/src/` and `packages/*/src/`.

**Run them once. Run this from `Auth_UI/`:**

```bash
pnpm test
```

**You should see:** a per-file list ending in a summary that reports every test file passed. Two variants exist for different moments: `pnpm test:watch` re-runs affected tests as you edit, and `pnpm test:coverage` produces a coverage report in the terminal and as HTML.

**Three of these unit tests are guards rather than ordinary tests**, and they are the ones most likely to fail on you: the locale parity test (every one of the six non-English language files must mirror `en.ts` key for key and placeholder for placeholder, and every literal key passed to `t("…")` anywhere in the workspace must exist in English), the query-parameter test (it parses the generated API schema and every API call in the workspace, and fails on any query parameter the endpoint does not accept), and the privacy-policy absence test (it asserts that no policy prose is bundled into the accounts application).

#### End-to-end tests — Playwright

**Playwright drives a real browser** against the two web applications. There are **5** end-to-end specification files, and `pnpm e2e` runs **4** of them — two for the console and two for the accounts application. The fifth belongs to the production suite described at the end of this part and is never run by `pnpm e2e`.

**One-time setup: install the browsers Playwright needs. Run this from `Auth_UI/`:**

```bash
pnpm exec playwright install
```

**You should see:** downloads for Chromium and its dependencies, then nothing further on subsequent runs.

**Then run the suite. Run this from `Auth_UI/`:**

```bash
pnpm e2e
```

**Playwright starts both development servers itself** — you do not start them first. It waits for `https://localhost:5173` and `https://localhost:5174` to answer.

**This is where the development HTTPS certificate becomes non-optional.** Without `DEV_HTTPS_CERT` and `DEV_HTTPS_KEY` set (Step 2 of [3.6b](#36b-install-and-run-the-two-web-applications)), the servers come up on plain HTTP, the two HTTPS addresses never answer, and Playwright fails with a start-up timeout that does not mention certificates at all.

**Two of those four specifications need more than the web applications**, and will fail if you run the suite with only the development servers up:

- The console confirm-dialog specification needs **the API running and a real sign-in**. Pass the seeded credentials as environment variables — `E2E_EMAIL` and `E2E_PASSWORD` — when you run it. Its selectors accept both English and Arabic text, because the console renders in the signed-in administrator's own language.
- The accounts account-deletion specification needs **the API on `https://localhost:5101`**, with `Email:Enabled` set to `false` and `Notifications:UseOutbox` set to `false`. **Only the first of those two is a shipped development value.** `Auth/Auth_API/appsettings.Development.json` already turns email off; `Notifications:UseOutbox` ships as `true` in `Auth/Auth_API/appsettings.json` and nothing in the development file changes it, so you have to set it to `false` yourself — in the git-ignored `Auth/Auth_API/appsettings.Development.local.json` — before this specification will pass. The specification then reads the one-time codes out of the newest Serilog file under `Auth/Auth_API/Logs/`, because with email disabled that is where they are printed, and it runs `sqlcmd` against the development database to set up and check state. It runs its cases in series on purpose, for two reasons: the log lines carry masked email addresses, so parallel cases would read each other's codes, and the deletion endpoints share one per-address rate-limit bucket.

**A separate suite tests the built output rather than the development servers.** Run this from `Auth_UI/`:

```bash
pnpm e2e:production
```

It builds both applications and serves the real `dist/` output through Vite's preview server on ports 4173 and 4174, then checks that the shell loads, the page is not blank, and the caching headers are what the deployment expects. This is the suite that catches a build-only breakage.

### 9.5 The Postman Collection

A Postman collection ships with the repository at `Auth/Auth_API/Postman/AuthSystem.postman_collection.json`. Postman is a graphical HTTP client; a collection is a saved set of requests you can import into it.

**Read this before you import it, because two things about it are misleading:**

- **It is about half of the API.** The collection holds **100 requests** against an API that exposes **199** actions. Treat it as a starting point, not as a reference — the reference is [Section 5](#5-api-reference).
- **Its base address is wrong, and it is wrong in a way that fails silently.** The collection sets its `baseUrl` variable to `http://localhost:5000`. **Nothing in this system has ever listened on port 5000.** Every request will fail to connect until you change it.

**Use it like this:**

1. Open Postman and import `Auth/Auth_API/Postman/AuthSystem.postman_collection.json`.
2. Open the collection's variables and change `baseUrl` from `http://localhost:5000` to `https://localhost:5101`. **Do this before your first request**, not after it fails.
3. If Postman rejects the development certificate, turn off SSL certificate verification in its settings, or run `dotnet dev-certs https --trust` once from any directory.
4. **Run the *Login* request first.** Its test script writes the returned access token into the `accessToken` variable, and every other request in the collection reads that variable for its `Authorization` header. Skip this and everything else returns 401.

**The collection is titled "Identity System API v1"** — an older name for this product. It is the same system.

---

## 10. Troubleshooting

**Before anything else, find the log.** The API writes to a daily file named `auth-api-<date>.log`, at the path `Logs/auth-api-.log` **relative to wherever the process is running from** — in development that is `Auth/Auth_API/Logs/`. On a server it sits next to the deployed application files. The same message is usually on the console too, but the file is the one that survives.

### 10.1 The API Refuses to Start

Several guards deliberately stop the process rather than let it run half-configured. Each writes a message naming what is missing and then exits. **This is not a crash — it is the system telling you exactly what is wrong**, and the message names the fix.

**The messages come in two shapes, which matters when you are searching a log file.** The first three rows in the table below begin with the words `Refusing to start:`, so searching for that phrase finds them. The last three do not begin with anything in common — two of them arrive wrapped in an `OptionsValidationException` and one is thrown directly — so the only reliable thing to search for is the sentence quoted in the table itself.

| The line you see | What is actually wrong | What to do |
|---|---|---|
| `Refusing to start: the AuthDb connection string is still the placeholder 'ConnectionStrings__AuthDb' from appsettings.json` | The base configuration file ships that literal string as a placeholder, and nothing has replaced it. It is the name of an environment variable, not a connection string. Confusingly, this same message also appears when the certificate or key ring failed — the guard names both causes for that reason. | Set a real `ConnectionStrings:AuthDb`. In development the committed value in `Auth/Auth_API/appsettings.Development.json` should already apply; if it does not, check whether `appsettings.Development.local.json` is overriding it. See [3.4](#34-configuration-reference). |
| `Refusing to start: N required secret(s) are missing from the resolved configuration under storage mode '<mode>'` — one line per secret | Key generation is off (`SecretManagement:AutoGenerateKeys` is `false`) or the secrets file exists but is incomplete. Missing keys are never topped up silently, on purpose. | Either turn generation back on for one run, or supply the named secrets through the secrets administration endpoints. [3.3](#33-first-startup-and-secret-generation) explains which four secrets exist and where each mode puts them. |
| `Refusing to start: plaintext secret(s) [...] were found in the Production configuration` | Readable private keys are sitting in a Production configuration file. This is the exact accident the guard exists to stop. | Remove those keys from the Production file and move them into the encrypted secret store. Never point `SecretManagement:PlainTextTargetFile` at `appsettings.Production.json`. |
| `Email:FrontendBaseUrl must be an absolute URL when Email:Enabled is true.` | Email is switched on with a relative or empty front-end address. Without an absolute one, every password-reset and verification link the system emails would be relative — that is, dead. | Set `Email:FrontendBaseUrl` to the full origin of the accounts application, for example `https://localhost:5174` in development. |
| `PrivacyPolicyPublication:PhysicalPath must not be empty.` | The folder that published privacy-policy documents are written to has not been named. | Set the key to a writable folder. |
| `CORS AllowedOrigins must be explicitly configured in production` | The origin list is empty and the environment is not Development. | List the real origins. Do not reach for a wildcard; [10.4](#104-cross-origin-errors-and-the-sign-in-loop-they-cause) explains why it makes things worse. |

**Under IIS these guards look like a blank failure.** The browser shows `HTTP Error 500.30 — ASP.NET Core app failed to start` and the application log looks empty, because the process died before the web host was running. The message is still written — look in the log file described at the top of this section, not in the browser.

**A different symptom, same area:** `SqlException: Cannot open database "..."`. The connection string is well-formed but points somewhere that does not exist. The committed development string names a **named** SQL Server Express instance and a specific catalog:

```text
Data Source=localhost\SQLEXPRESS01;Initial Catalog=Astoom_Auth;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

Two mistakes account for nearly every case: the instance is `SQLEXPRESS01`, not the more common `SQLEXPRESS`; and the database is `Astoom_Auth`, not `AuthDB`. Either publish the database project to those names ([3.2](#32-database-setup)) or override the connection string in the git-ignored `Auth/Auth_API/appsettings.Development.local.json`.

### 10.2 Secret and Key-Ring Errors

**Symptom:** `An error occurred while reading the key ring`, or `Access to the path ... is denied`.

**Cause:** `DataProtection:KeyPath` is empty, so the key ring falls back to a folder the running identity cannot write. The fallback is `%ProgramData%\AuthSystem\Keys` — one machine-wide folder, chosen deliberately so the Auth API and the API Gateway share a ring even when they run as different identities. On locked-down shared hosting (Internet Information Services under Plesk, for example) the application pool identity often has no write access to `%ProgramData%` at all, and that is this error.
*In code:* `Auth/Auth.Shared/Configuration/AuthDataProtectionExtensions.cs`, `ResolveKeyRingPath`.

**Fix:** Set `KeyPath` to a writable folder outside the public web root, use the **same** folder for the Auth API and the API Gateway, and grant the application pool identity *Modify* permission on it.

**Symptom, in `Dpapi` mode:** a `CryptographicException`, or a message about failing to decrypt because the data came from a different machine.

**Cause:** `Dpapi` mode ties the encryption to one Windows machine and one account. The secrets file was created elsewhere, or by a different account, or the key ring is gone.

**Fix:** keep the API and the gateway on one machine sharing one key ring, or switch to `Certificate` mode, which is portable between servers.

**One correction worth making explicitly, because older notes get it wrong: only `Dpapi` is Windows-only.** `Certificate` mode loads a `.pfx` file through ordinary cross-platform .NET and runs anywhere .NET 10 runs. If you are moving to a non-Windows host, `Certificate` is the mode to use — not a reason to fall back to `PlainText`.

### 10.3 Everything Through the Gateway Returns 403

**Symptom:** every request that goes through the API Gateway comes back `403 Forbidden`, while the same request sent straight to the API succeeds.

**Cause:** the gateway stamps a shared secret onto each forwarded request, and the API compares it against its own copy. The two do not match, or one of them has no value at all.

**Fix:** make the two agree, and make sure both processes use the **same** `SecretManagement:StorageMode`:

1. **In `PlainText` mode** — copy the API's generated `Gateway:ExpectedToken` into the gateway's `Gateway:Token`. Nothing does this for you; the step is written out in [3.6](#36-running-the-api-and-gateway).
2. **In `Certificate` or `Dpapi` mode** — run both on the same machine, pointed at the same key ring and the same encrypted secrets file. Both then read the token automatically. Restart both after regenerating any secret.

**A different 403-shaped symptom is not this at all.** If exactly one feature returns 404 through the gateway while working directly against the API, the cause is the gateway's route list, not the token — see [4.8](#48-api-gateway-yarp).

> For the production troubleshooting matrix — start-up error 500.30, certificate password problems, and the key-wipe-on-republish trap — see [PRODUCTION_DEPLOYMENT_GUIDE.md § D](PRODUCTION_DEPLOYMENT_GUIDE.md#d-troubleshooting).

### 10.4 Cross-Origin Errors, and the Sign-In Loop They Cause

**These two symptoms are one subject, and separating them is how people break their setup.** The usual "fix" for the first symptom — allowing every origin with a wildcard — is what produces the second.

**Symptom A:** the browser console reports that a request to the API was blocked by cross-origin resource sharing policy, and no response body arrives.

**Symptom B:** sign-in appears to work — the credentials are accepted — and then `/auth/authorize` redirects straight back to the login page. **No error appears anywhere**: not in the browser console, not in the API log.

**The fix for A, which must not create B:**

- **List every origin explicitly in `Cors:AllowedOrigins`**, each with its scheme, host and port, and keep `Cors:AllowCredentials` set to `true`. The committed development file already lists all four local origins — `http` and `https` on ports 5173 and 5174. If yours has been edited, restore that list rather than widening it.
- **Never set the list to `["*"]`, in any environment, including your own machine.** When the list contains a wildcard the code takes a different branch that allows any origin and **never allows credentials**. Without credentials permitted, the browser refuses to store the identity-provider session cookie — and a missing session cookie is exactly Symptom B. Outside Development the wildcard is worse still: that branch is Development-only, so elsewhere a wildcard produces a deny-all policy and every browser call fails. The full explanation is in [3.4 — CORS](#cors).

**The other three causes of Symptom B**, in the order worth checking:

1. **Scheme mismatch.** The identity-provider session cookie is marked `SameSite=Lax; Secure`. A browser counts `http://localhost` and `https://localhost` as different sites, so a cookie set for one is discarded when the page is the other. **Both web applications must run on HTTPS and the API must run on its `https` launch profile.** Setting up the development certificate is Step 2 of [3.6b](#36b-install-and-run-the-two-web-applications).
2. **Origin mismatch.** `IdentityProvider:PublicBaseUrl` must equal, character for character, the origin the web application was built against (`VITE_API_BASE_URL`). The authorize endpoint builds its `returnTo` value from `PublicBaseUrl`, and the front-end rejects a `returnTo` on any other origin as an attempted open redirect — silently, because that is the safe behaviour for that class of check. Read the `returnTo` value out of the address bar on the login page and compare the two origins directly.
3. **Something later in the configuration chain is winning.** If editing `appsettings.Development.json` changes nothing, the value is being overridden downstream. In order of precedence: first `appsettings.Development.local.json`, which is git-ignored and on most machines already carries its own `IdentityProvider`, `Email` and `ImageStorage` blocks copied from an older setup — **check here first**, because the committed file cannot override it; then the database-backed system settings, edited in the console under Settings, which beat both files. To take that last layer out of the picture for one run, start the API with the environment variable `AUTH_DISABLE_DB_SETTINGS` set to `true`.

### 10.5 The Web Applications Will Not Start, or Will Not Reach the API

Four failures account for almost everything that goes wrong in `Auth_UI/`. All commands below run from `Auth_UI/`.

**The development server starts on plain HTTP and sign-in loops forever.** The certificate environment variables are not set. You will have seen this warning scroll past at start-up:

```text
[dev-https] DEV_HTTPS_CERT/DEV_HTTPS_KEY not set - serving http.
OAuth sign-in will loop between /login and /auth/authorize: Chrome
drops the IdP session cookie when the SPA and the API differ in scheme.
```

The server runs, the pages load, and nothing else works properly. **Fix it by doing Step 2 of [3.6b](#36b-install-and-run-the-two-web-applications)**: export the development certificate and put `DEV_HTTPS_CERT` and `DEV_HTTPS_KEY` in `.env.development.local` inside **each** application folder.

**A second, different failure looks nothing like the first: the variables are set, but one of them names a file that is not there.** You get a Node.js `ENOENT` error naming the exact path, and the server does not start at all. That is the intended behaviour, not a bug — the shared certificate helper reads both files itself precisely so a wrong path stops you here. Handing the paths to Vite instead would let its own reader swallow the missing file and pass the path string through as though it were the certificate, which fails much later and much less clearly.
*In code:* `Auth_UI/dev-https.ts`.

**Vite exits immediately, complaining that the port is in use.** Ports 5173 and 5174 are pinned with `strictPort`, so a busy port is a hard stop rather than a fallback to 5175. **Do not change the port to work around this.** The API's allowed-origins list names exactly those two ports, so an application that quietly moved would be rejected by the browser instead, with a much more confusing error. Find what is holding the port and stop it. On Windows, from any directory:

```bash
netstat -ano | findstr :5173
```

**You should see:** one line ending in a process identifier, which you can then look up in Task Manager or stop with `taskkill /PID <that number>`. If the command prints nothing, the port is free and the failure is something else.

**The browser blocks calls from an application origin the API does not know.** This is Symptom A of [10.4](#104-cross-origin-errors-and-the-sign-in-loop-they-cause), seen from the front-end side: the page loads, and every API call fails with a cross-origin message naming your origin. It happens when a web application is served from an address that is not in `Cors:AllowedOrigins` — a different port after a port conflict, a machine name instead of `localhost`, or plain HTTP where the list has only HTTPS. **Fix it by adding the exact origin to the list, not by widening the list.** The policy is rebuilt from live configuration, so the change takes effect without a restart in most cases; restart the API if you are not sure.

**`pnpm gen:api` cannot connect.** That script regenerates the typed API client and **targets `http://localhost:5100/openapi/v1.json` — the plain-HTTP port — while the applications themselves default to `https://localhost:5101`.** The mismatch is real and it confuses everybody once. It normally works anyway, because the API's `https` launch profile binds both ports. So a connection failure means one of three things: the API is not running at all; the API is running on some other profile that does not bind 5100; or the environment is not Development, in which case there is no API description document to generate from at any address. Start the API as described in [3.6](#36-running-the-api-and-gateway) and run the command again.

### 10.6 Email Is Not Arriving

**Do not stop at "is SMTP configured".** By default this system does not send an email at the moment something happens — it writes a row into an outbox table and a background worker sends it moments later. That changes where you look.

Work down this list in order:

1. **Is email switched on at all?** `Email:Enabled` is `false` in the committed development configuration, deliberately. With it off, nothing is ever sent — and one-time codes are printed into the log file instead, which is how the end-to-end tests read them.
2. **Is `Email:FrontendBaseUrl` an absolute address?** If email is on and this is relative or empty, the API refuses to start ([10.1](#101-the-api-refuses-to-start)). If it is absolute but wrong, mail is sent with links that go nowhere.
3. **Is the SMTP password where the system can read it?** Keep it out of `appsettings.json`. Set it through the `Email__Password` environment variable, or store it in the encrypted secret store using `PUT /api/v1/admin/Secrets/smtp-password` ([5.12](#512-secrets-admin)).
4. **Look at the outbox row.** With `Notifications:UseOutbox` set to `true` — the shipped default — the send was queued, not attempted inline. Open `GET /api/v1/notification-outbox` in the console or by hand and find the row. It carries the attempt count and the last error, and that error is the real answer. The dispatcher polls on an interval, sends in batches, retries up to a maximum attempt count and then marks the row dead. [4.10](#410-how-a-notification-becomes-an-email) describes the loop.
5. **Check that the notification type has a template.** One seeded type, `welcome-email`, has **no template at all** — it is the only seeded type without one. Nothing will ever be sent for it, no matter how correct your SMTP settings are.

### 10.7 Ports, and One Header Worth Knowing

**The addresses everything uses in development:**

| Process | Addresses |
|---|---|
| Auth API | `https://localhost:5101` and `http://localhost:5100` — the `https` launch profile binds **both**, which is why some tools reach it on 5100 |
| API Gateway | `https://localhost:7159` and `http://localhost:5034` |
| Console web application | `https://localhost:5173` only |
| Accounts web application | `https://localhost:5174` only |

The two web applications are HTTPS-only and their ports are pinned; see [10.5](#105-the-web-applications-will-not-start-or-will-not-reach-the-api) before changing either. For the .NET processes, the ports live in `Properties/launchSettings.json` in each project — and if you change one, the allowed-origins list and `IdentityProvider:PublicBaseUrl` have to change with it.

**When an access token has expired, the API says so in a header.** A 401 response caused by expiry carries `Token-Expired: true`. A client can use that to tell "your token is stale, refresh it" apart from "you are not allowed to do this", and refresh silently instead of bouncing the user to a login page.

---

## 11. Permission Matrix

> **Read this before you plan any role model.** The API enforces **50** distinct permission codes. On a freshly published database, **34 of those 50 have no row in the `Permissions` table**, which means they cannot be granted to a role or to a person by any ordinary means. **Six of the 34 exist in no database script anywhere in this repository.** The practical result is blunt: **out of the box this system has exactly one working authority level — `super-admin`, which holds the global `*` grant — and everything below it is inert until you create the missing rows yourself.** [11.4](#114-how-to-get-the-missing-codes-into-the-database) explains how.

### 11.1 Every Enforced Code, and Whether a Fresh Publish Creates It

A permission code is enforced when an endpoint is marked with it; a code is *seeded* when a row for it exists in the `Permissions` table after publishing the database project. **The two lists are not the same, and the gap is the subject of this whole section.** Enforcement comes from the code and always works. Seeding comes from the post-deployment script, and it is incomplete.

Read the last column as: **Yes** = the row exists and you can grant it. **No** = the row does not exist; only the global `*` reaches that endpoint. **No, and in no script** = worse still — no file in the repository would create it even if you ran every script on disk.

| Permission code | What it guards | Row created by a fresh publish? |
|---|---|---|
| `platform-settings:manage` | Read and update the platform's branding settings | Yes |
| `system-settings:manage` | Read, update and reset system settings, and send the test email | Yes |
| `secrets.manage` | All 13 secrets-administration endpoints | **No** |
| `apikeys:read` | List API keys | **No** |
| `apikeys:create` | Create an API key | **No** |
| `apikeys:revoke` | Revoke an API key | **No** |
| `apikeys:rotate` | Rotate an API key | **No** |
| `apikeys:validate` | Validate an API key | **No, and in no script** |
| `webhookkeys:read` | List webhook keys | **No, and in no script** |
| `webhookkeys:create` | Create a webhook key | **No, and in no script** |
| `webhookkeys:validate` | Validate a webhook key | **No, and in no script** |
| `webhookkeys:revoke` | Revoke a webhook key | **No, and in no script** |
| `webhookkeys:rotate` | Rotate a webhook key | **No, and in no script** |
| `applications:read` | Seven application read endpoints, plus the dashboard's application-activity figures | **No** |
| `applications:create` | Create an application | **No** |
| `applications:update` | Update, activate, deactivate, and grant or remove a user's access to an application | **No** |
| `applications:delete` | Delete an application | **No** |
| `auditlogs:read` | Four audit-log read endpoints, plus three dashboard statistics endpoints | **No** |
| `auditlogs:export` | Export audit logs to a file | **No** |
| `notification-templates:read` | Read templates, layouts, outbox rows and notification types, and render previews | Yes |
| `notification-templates:manage` | Create, edit and delete template drafts, send a test, retry an outbox row, edit a notification type | Yes |
| `notification-templates:publish` | Publish, unpublish and roll back a template | Yes |
| `notification-layouts:manage` | Create, edit and publish an email layout | Yes |
| `privacy-policy:read` | Read privacy-policy versions and their content | Yes |
| `privacy-policy:manage` | Create, edit, publish and notify on a privacy-policy version | Yes |
| `organizations:read` | List every organization on the platform (`GET /organizations/all`) | Yes |
| `org:update` | Rename or otherwise update one organization | Yes |
| `org:members:read` | List one organization's members and invitations | Yes |
| `org:members:manage` | Change a member's role, remove a member | Yes |
| `org:members:invite` | Send and resend organization invitations | Yes |
| `org:apps:read` | List the applications enabled for an organization | Yes |
| `org:apps:manage` | Enable, update and disable an organization's applications | Yes |
| `org:permissions:read` | Read a member's roles inside an organization | Yes |
| `org:permissions:manage` | Assign and remove a member's roles, grant a member a permission | **No** — but see the note below |
| `permissions:read` | List and read permissions and their implications | **No** |
| `permissions:create` | Create a permission | **No** |
| `permissions:update` | Update a permission | **No** |
| `permissions:delete` | Delete a permission | **No** |
| `permissions:manage` | Add and remove permission implications | **No** |
| `roles:read` | List and read roles, and the users and applications attached to one | **No** |
| `roles:create` | Create a role | **No** |
| `roles:update` | Update a role | **No** |
| `roles:delete` | Delete a role | **No** |
| `users:read` | Six user read endpoints, plus the dashboard's user figures | **No** |
| `users:create` | Create a user | **No** |
| `users:update` | Update a user, and set or remove another user's profile image | **No** |
| `users:delete` | Soft-delete a user | **No** |
| `users:manage` | Permanently delete, lock, unlock, activate and deactivate a user | **No** |
| `users:manage-roles` | Assign and remove a user's roles | **No** |
| `users:manage-permissions` | Grant and revoke a user's permissions directly | **No** |

**Sixteen seeded, thirty-four not.** Everything a platform administrator would actually want to delegate — users, roles, permissions, applications, API keys, audit logs — is in the second group.

**The `org:permissions:manage` row is a special case worth understanding.** That exact code has no seeded row, so it cannot be granted by name. But the seed does create the wildcard `org:permissions:*`, and gives it to the `org-admin` role, and gives `org:*` to `org-owner`. Because a check compares the *claim strings* in a token rather than looking at rows, both of those roles satisfy `org:permissions:manage` in practice. What is impossible is granting that one code narrowly to somebody who should not also get the rest of the subtree.

**Six codes exist in no SQL file anywhere in this repository:** `apikeys:validate`, `webhookkeys:read`, `webhookkeys:create`, `webhookkeys:validate`, `webhookkeys:revoke` and `webhookkeys:rotate`. Searching every `.sql` file under `Auth/Auth_DB/` for `webhookkeys` returns nothing at all. Those six endpoints are reachable only by a holder of the global `*`, permanently, unless somebody writes the rows.

**And one code can never be reached by a wildcard at all: `secrets.manage`.** It is the only code among the 50 that uses a dot instead of a colon. The wildcard rule works by matching a prefix ending in a colon, so nothing — not `secrets:*`, not `auth:*` — can ever satisfy it. Even after you create the row, the holder needs the literal code `secrets.manage` or the global `*`. See [4.4](#44-permission-based-authorization).

### 11.2 The Eight Seeded Roles, and What Each One Can Actually Do

Publishing the database creates eight roles. **Three of them are decorative**: they hold codes in the `auth:` family, and no endpoint in this API requires any code beginning `auth:`. Because matching is a string-prefix test, `auth:users:*` does not satisfy `users:read`.

| Role code | What it holds | What it can actually do |
|---|---|---|
| `super-admin` | the global `*` | **Everything.** The only platform role that authorizes anything at all. |
| `admin` | `auth:*` | **Nothing.** Every management endpoint returns 403 to a holder of this role. |
| `user-manager` | `auth:users:*` | **Nothing.** |
| `auditor` | `auth:audit:read`, `auth:users:read` | **Nothing.** In particular it cannot open the audit log, which requires `auditlogs:read`. |
| `user` | `profile:read`, `profile:update` | **Nothing gated** — no endpoint requires either code. Harmless: the profile endpoints only require a signed-in user. |
| `org-owner` | `org:*` | Every organization endpoint: update, members read/invite/manage, applications read/manage, member permissions read/manage. |
| `org-admin` | `org:read`, `org:members:*`, `org:apps:*`, `org:permissions:*` | Members, applications and member permissions — but **not** `PUT /organizations/{id}`, which needs `org:update`. Only the owner can rename an organization. |
| `org-member` | `org:read`, `org:members:read`, `org:apps:read` | Read the organization's members, invitations and applications. Nothing else. |

**The organization half of this is correct and complete; the platform half is not.** A user who registers with an organization, or creates one, is made `org-owner` automatically, and that model works exactly as documented. What does not exist is any working platform role between "everything" and "nothing".

**Say it out loud, because it is the sentence people get wrong:** granting somebody the `auditor` role for read-only audit access does not work. They will hold a token full of `auth:` claims and receive 403 on every audit endpoint.

### 11.3 Two More Lists Worth Having

**Codes that are checked but do not gate anything.** Three codes are read inside a handler that has already authorized the caller some other way. Failing one of these does not produce 403 — it narrows what you get back:

| Code | Effect when the caller holds it |
|---|---|
| `organizations:read` | Organization list, members, invitations and application reads return **every** organization instead of only the caller's own |
| `organizations:manage` | Lets the caller delete any organization, and act on any ownership transfer, rather than only their own |
| `users:manage` | Lets `GET /users` accept `includeDeleted=true`; without it, that flag is refused |

Note also that `POST /organizations` and `DELETE /organizations/{id}` carry **no** permission requirement at all. They only require a signed-in caller; ownership is decided inside the handler. Do not describe organization creation or deletion as permission-gated.

**Codes that are seeded but checked by nothing.** These 21 rows exist in a freshly published database and match no requirement in the API. Granting one has no effect whatsoever, which is worth knowing before you build a role around it:

```text
auth:*                    auth:users:*            auth:roles:*
auth:permissions:*        auth:audit:*            auth:users:read
auth:users:create         auth:users:update       auth:users:delete
auth:users:manage-roles   auth:roles:read         auth:roles:create
auth:roles:update         auth:roles:delete       auth:audit:read
profile:read              profile:update          org:read
org:delete                org:permissions:grant   org:permissions:revoke
```

### 11.4 How to Get the Missing Codes Into the Database

**First, understand why they are missing, because it is one mechanical cause and not a design decision.** A seed file named `08_AdditionalPermissions.sql` sits in `Auth/Auth_DB/dbo/Scripts/SeedData/` and contains most of the modern permission codes — **47 rows, 47 distinct codes, which would supply 28 of the 34 missing ones**. The post-deployment script never includes it, so publishing the database never runs it. The file is carried in the project as content only. Full context is in [3.2](#32-database-setup).

**Second, understand the deadlock.** The endpoint that creates a permission row is `POST /api/v1/Permissions`, and it is guarded by `permissions:create` — which is itself one of the 34 missing codes. **You cannot create the missing permissions using a role you built out of the missing permissions.** Only a holder of the global `*` can break the circle.

**There are three ways forward. Pick one.**

**Option A — do it through the API as the seeded super administrator.** This is the path that needs no database access.

**One thing about it surprises everybody, so read it before you start.** Both `POST /api/v1/permissions` and `POST /api/v1/roles` require an `applicationId`, and it is **not** optional: the field is a plain identifier with no null allowed, the permission handler looks the application up and answers `Application.NotFound` when it is missing, and the roles table has a foreign key to `Applications`. **A freshly published database contains no application rows at all** ([7](#7-database-schema-overview)), so the very first call fails until you create one. There is no way through this API to create a permission at global scope — the `NULL` scope every seeded platform permission uses. Option B is the only route to that.

**That does not stop the permission working.** A first-party sign-in to the console mints a token with no application attached, and the query that collects a person's permission codes for such a token joins their roles to the permission rows without looking at either one's application. So an application-scoped code, attached to a role that person holds, still lands in their console token and still satisfies the check.
*In code:* `Auth/Auth.Infrastructure/Persistence/PermissionRepository.cs`, `GetUserEffectivePermissionsAsync(userId, cancellationToken)`.

1. Sign in to the console at `https://localhost:5173` as `admin@company.com`. That account holds `super-admin`, and therefore `*` ([3.6c](#36c-sign-in-for-the-first-time)).
2. **Register an application to hang the new rows on**, with `POST /api/v1/applications` ([6.6](#66-set-up-an-application-with-its-own-roles-and-permissions)). Keep the `id` it returns; every call below needs it. If you already have one registered, reuse it.
3. For each code you need, call `POST /api/v1/permissions` with that `applicationId`, the code, a display name and a description ([5.6](#56-permissions)). Create the plain codes you intend to grant; create a `resource:*` wildcard row as well only if you actually want to hand out whole subtrees.
4. Create the role you want with `POST /api/v1/roles`, passing the same `applicationId` and the new permissions in `permissionIds`.
5. Assign the role to a person with `POST /api/v1/users/{id}/roles`.
6. **Have that person sign out and back in.** Permissions are baked into the access token when it is issued; a grant made now does not appear in a token minted earlier.

**Option B — insert the rows directly with SQL.** Appropriate on a development machine, or when you want many codes at once.

**Do not simply run the unused seed file.** `08_AdditionalPermissions.sql` looks like the obvious shortcut, and it **will fail on a freshly published database** for two separate reasons. The file was written for an older shape of the schema. Read it for the list of codes, names and descriptions — that part is still useful — and write your own inserts.

1. **Every row it writes points at an application row that no longer exists.** The file stamps each permission with `ApplicationId = 00000000-0000-0000-0000-000000000001`, the old "platform application". A current publish creates no `Applications` row at all, and one of its upgrade scripts deliberately deletes that one on databases that still have it, so the foreign key from `Permissions` to `Applications` fails on the very first insert.
2. **Some of its rows point at a parent permission it then skips creating.** Each insert is wrapped in "create this only if the code is missing". Four codes it would create as parents — `org:*`, `org:members:*`, `org:apps:*` and `org:permissions:*` — are already seeded by `07_OrganizationRolesPermissions.sql` under **different** identifiers, so `08` skips them. Its child rows still name the identifiers it skipped, and the self-referencing foreign key on `ParentId` fails. `org:permissions:manage` is the clearest case.

**Two rules make a hand-written insert work:**

- **`ApplicationId` must be `NULL`.** Platform permissions live at global scope now. Any other value has to be a real row in `Applications`.
- **`ParentId` must be `NULL`, or the identifier of a row that already exists.** It is display metadata only; leaving it `NULL` costs you nothing at run time, because parents grant nothing ([4.4](#44-permission-based-authorization)).

A single row looks like this. **Run this from any directory**, replacing the server and database if yours differ:

```bash
sqlcmd -S "localhost\SQLEXPRESS01" -d Astoom_Auth -E -I -Q "SET QUOTED_IDENTIFIER ON; IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'users:read') INSERT INTO [dbo].[Permissions] ([Code],[Name],[Description],[ApplicationId],[ParentId],[Level],[IsWildcard],[IsActive],[CreatedBy]) VALUES (N'users:read', N'Read Users', N'List and view users', NULL, NULL, 3, 0, 1, N'00000000-0000-0000-0000-000000000001');"
```

**You should see:** `(1 rows affected)`, or nothing at all if the code already existed. The `-E` flag signs in with your Windows account and `-I` turns on quoted identifiers, which this table requires because it carries a filtered index — without it the statement fails with "SET options have incorrect settings".

**Then grant the codes** by following steps 4 to 6 of Option A — create the role, assign it, and have the person sign in again. Creating a permission row does not give it to anybody.

**Re-publishing the database project later leaves your rows alone.** It also will not repeat this work for you, because the file that would have is still not part of the publish.

**Option C — accept the current state deliberately.** If this deployment only ever has one administrator, running everything as `super-admin` is a defensible choice. **Write it down as a decision rather than discovering it later**, and be aware of what you are giving up: no separation of duties, no read-only auditor, and every administrative action attributed to the same account.

**Whichever route you take, three things stay true.** A grant only takes effect in tokens issued after it. A wildcard grant is a prefix match, so `users:*` hands over the entire `users:` subtree at every depth. And `secrets.manage` can never be covered by a wildcard.

---

## Appendix A. Configured but Not Working

**Everything in this appendix is real configuration that does nothing.** It is collected here so that you stop looking for the setting that will make one of these behave — there isn't one. Each item is either a key with no reader, a column nothing acts on, a filter nothing applies, or an event nobody handles.

**Settings that no code reads.** Setting these changes nothing at all:

| Key | What actually governs the behaviour |
|---|---|
| `Session:LifetimeHours` | `Jwt:RefreshTokenLifetimeDays` — that is the real session lifetime |
| `Session:ExtendOnActivity` | nothing; sessions are not extended |
| `Session:ExtensionHours` | nothing |
| `Session:IdleTimeoutMinutes` | nothing; there is no idle timeout |
| `SecretManagement:RequiredPermission` | the permission is compiled into the controller as `secrets.manage` and cannot be changed by configuration |
| `Services:AuthApi:HealthUrl` (gateway) | `Services:AuthApi:ReadyUrl`, falling back to the base address plus `/ready` |

**Columns that are stored and then ignored:**

| Column | What happens |
|---|---|
| `Users.PasswordExpiresUtc` | Written back unchanged on every update, never given a value. No password ever expires. |
| `Applications.MaxConcurrentSessions` | Stored, validated, returned and sortable — **never enforced**. The only session cap that works is the global `Session:MaxConcurrentSessions`. |
| `Applications.SessionTimeoutMinutes` | Same shape: accepted, stored, returned, and consumed by no sign-in or session path. |
| `Applications.RequireEmailVerification` | Same shape: no authentication path reads it. |
| `ApiKeys.RateLimitPerMinute`, `ApiKeys.RateLimitPerDay` | Stored, validated and returned when a key is validated. **No limiter in this system reads them.** Enforce per-key throttling in the consuming service if you need it. |

**Audit-log fields that do not exist.** The `AuditLogs` table has no `ActionType`, `IsSuccess`, `ErrorMessage` or `CorrelationId` column. The code fills those four with constants when reading a row, so every row reports success. The `actionType` and `isSuccess` query parameters are accepted by the audit endpoints and never applied to the query.

**Stored procedures that nothing calls.** Nine are defined; four are used. These five are published to the database and invoked by no code: `sp_CheckAccountLockout`, `sp_RecordLoginAttempt`, `sp_RevokeRefreshToken`, `sp_ValidateCredentials`, `sp_ValidateRefreshToken`. If you are reading one of them to learn how sign-in works, stop — it is not the code that runs. See [Section 7](#7-database-schema-overview).

**Events published to nobody.** `WebhookKeyCreatedEvent` and `WebhookKeyRevokedEvent` are raised and have no handler, so creating or revoking a webhook key writes no audit entry. Separately, the integration-event publisher that exists is a no-op: **nothing leaves this process**, and there is no message broker.

**Seed and upgrade scripts that never run.** Six of the sixteen seed scripts on disk are not included by the post-deployment script, and four of the nine upgrade scripts are not either. The one that matters is `08_AdditionalPermissions.sql` — see [11.4](#114-how-to-get-the-missing-codes-into-the-database).

**Two more things that look configured and are not.** `GeoIp:Enabled` is `false` and no database file ships, so city lookup never happens although the library is present. And `Column Encryption Setting=Enabled` in a connection string is inert here, because no column in the schema uses SQL Server's Always Encrypted feature — field protection is done by the application, as described in [Section 8](#8-security-best-practices).

**One integration defect belongs on this list too.** The client library in `Auth.Sdk` sends the gateway header twice — once from the registration and once from the client itself. A two-value header can never match the single-value comparison the gateway middleware performs, so every call the library makes through a token-validating gateway is rejected with 403. The library is also referenced by no project in this repository and is not packaged. Treat it as unfinished.

---

*This guide describes AuthSystem as it exists in this repository, running on .NET 10. Its companions in this folder are [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md) for deploying it, [APPLICATION_INTEGRATION_GUIDE.md](APPLICATION_INTEGRATION_GUIDE.md) for connecting another application to it, and [02_AUTH_SYSTEM_DOCUMENTATION_EN.md](02_AUTH_SYSTEM_DOCUMENTATION_EN.md) for the product-level view.*
