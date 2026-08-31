# AuthSystem

Enterprise identity platform for multi-application, multi-tenant organizations. AuthSystem is a
centralized service that handles **authentication** (who users are), **authorization** (what they can
do), and **audit logging** (what they did) for all of your applications from one place.

Built on .NET 10 with Clean Architecture, CQRS, and DDD. You get four things: a REST API of **199
endpoints** across 25 route-bearing controllers, **two web applications** (an admin console and an
end-user accounts portal), an optional API gateway in front of the API, and an SDK that lets your
other .NET apps validate tokens locally without ever holding a private key.

---

## Features

- **Argon2id password hashing** — OWASP-recommended, memory-hard, with per-password salt, optional
  server-side pepper, and a breached-password check (HIBP, k-anonymity).
- **JWT authentication (RS256)** — asymmetric signing; consumers verify with the public key via JWKS.
- **Single sign-on** — OpenID Connect authorization-code flow with PKCE, so users sign in once at the
  accounts application and reach every registered app.
- **Hierarchical permissions** — role-based access control with wildcards (e.g. `admin:*`) and
  time-based expiration. Wildcards match by prefix.
- **Multi-tenant organizations** — membership, invitations, per-app subscriptions, org-scoped
  permissions, and ownership transfer.
- **Two-factor authentication** — TOTP (RFC 6238) with recovery codes.
- **External login** — Google ID-token validation via a pluggable provider strategy.
- **API keys and webhook keys** — create, rotate (with grace period), and revoke.
- **Database-backed notifications** — email templates with versioning, draft/publish/rollback, seven
  languages, and a retrying outbox. Templates are edited in the console, not in code.
- **Runtime settings** — a large part of the configuration is editable in the console and stored in
  the database, overriding the files.
- **Encrypted secret vault** — three storage modes, plus admin operations guarded by an
  email-confirmed challenge.
- **Privacy and account deletion** — versioned privacy policy with publication, self-service deletion
  with a grace period, and administrative hard delete.
- **Comprehensive audit logging** — every action with user, timestamp, IP, and change history;
  exportable (CSV/JSON). Supports GDPR, SOC 2, HIPAA, and PCI-DSS evidence needs.
- **Built-in protection** — rate limiting, OWASP-compliant security headers, and encrypted secret
  storage at rest.
- **7-language support** — English, Arabic, Turkish, French, Chinese, Urdu, Persian — including RTL,
  in both the API's messages and the two web applications.
- **Automated test suite** — 1,412 `[Fact]` and 68 `[Theory]` backend tests (xUnit, Moq,
  FluentAssertions) across 171 files, plus 276 frontend unit tests and 5 Playwright end-to-end specs.

---

## Technology Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 (every project targets `net10.0`) |
| Database | SQL Server 2019 schema (SSDT project), 52 tables in 6 groups |
| Database access | Dapper 2.1.79 over hand-written SQL — no Entity Framework |
| Web applications | React 19, Vite 8, TypeScript 6, Tailwind 4, shadcn/ui, TanStack Query |
| API gateway | YARP 2.3.0 reverse proxy, 24 routes |
| Patterns | MediatR 14 (CQRS), FluentValidation, ErrorOr |
| Security | Argon2id, JWT RS256, DPAPI / certificate secret encryption |
| Localization | Embedded resources, 7 cultures, shared with the frontend |
| Logging | Serilog (structured, console + rolling file) |

There is **no** Docker, Kubernetes, Redis, message broker, external key vault, or CI pipeline in this
repository. Deployment targets IIS on Windows, and `.github/workflows/` is empty.

---

## Architecture

```text
Console SPA  ─┐
(admin)       │
              ├──▶ API Gateway (public) ──▶ Auth API (private) ──▶ SQL Server
Accounts SPA ─┘    rate limit + headers     199 endpoints          52 tables
(end user)         adds X-Gateway-Token     JWT + permissions
                                            audit logging
Your app ─────────▶ Auth.Sdk ──▶ validates tokens locally via JWKS
```

The gateway is optional — clients can reach the Auth API directly. The backend follows the five-layer
Clean Architecture model: `Auth.Domain`, `Auth.Application`, `Auth.Infrastructure`, `Auth_API`, and
the `Auth_DB` SQL project, plus `API_Gateway`, `Auth_Localization`, `Auth.Sdk`, and `Auth_Setup`.

The two web applications live in `Auth_UI/`, a pnpm workspace:

| Path | What it is | Dev URL |
|---|---|---|
| `Auth_UI/apps/console` | Admin console — users, roles, applications, organizations, audit, settings, notification templates | `https://localhost:5173` |
| `Auth_UI/apps/accounts` | End-user portal — sign-in, profile, sessions, two-factor, my organizations, account deletion | `https://localhost:5174` |

Both are HTTPS-only in development, deliberately, so the browser keeps the identity-provider session
cookie. They share five internal packages: `@authsystem/api` (typed client generated from the API's
own OpenAPI document), `@authsystem/auth`, `@authsystem/i18n`, `@authsystem/ui`, `@authsystem/account`.

---

## Prerequisites

- **.NET 10 SDK** (build machine) and the **.NET 10 Hosting Bundle** (IIS server)
- **SQL Server** (Express/Developer/LocalDB) and a SQL client (SSMS or Azure Data Studio)
- **Node.js** and **pnpm**, to build the two web applications. The repository pins neither: the
  installed dependencies require Node `^20.19.0 || ^22.13.0 || >=24`, and `pnpm-lock.yaml` is
  lockfile version `9.0`, so use a pnpm release that reads it (pnpm 9 or newer).
- **Visual Studio with SQL Server Data Tools**, or another tool that can publish a DACPAC — this is
  how the database is created.
- A **Windows host** if you use DPAPI or Certificate secret-storage mode

---

## Production Quick Start

Ordered steps. Before starting, read **Part 1** of the
[Production Deployment Guide](ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md) to make two upfront decisions:
your **domains** and your **secret-storage mode** (`PlainText`, `Certificate`, or `Dpapi`).

**1. Build**

```bash
git clone <repository-url>
cd AuthSystem
dotnet build Auth/Auth_API/Auth_API.csproj -c Release
```

Build the projects individually. **Do not run `dotnet build Auth/Auth.sln`** — the solution includes
the SSDT database project `Auth_DB`, whose build targets only Visual Studio's MSBuild can load, so the
command-line build reports `MSB4278` and `Build FAILED` even when every C# project compiled. Build
`Auth/API_Gateway/API_Gateway.csproj` the same way if you plan to deploy the gateway.

**2. Database** — create an empty database and a least-privilege SQL login (not `sa`), then publish
the schema + seed data into it. In Visual Studio, right-click **Auth_DB** → **Publish**, point it at
your database, and hit Publish. (Publish profiles are per-environment and gitignored — yours is
created on first publish.)

```bash
# The seed creates the admin with NO password. Set one:
dotnet run --project Auth/Auth_Setup -c Release -- "<the password you chose>"
```

`Auth_Setup` prints an `UPDATE [dbo].[Users] ...` statement — run it against your database. Give it
no argument and it prompts instead, keeping the password out of your shell history. Until you run
that statement, sign-in as the admin is refused on the server: the seed leaves `PasswordHash` null
so that no deployment of this system ships a password anyone could look up.

**3. Configure** — **create** `Auth/Auth_API/appsettings.Production.json`. It is not in the repository:
a clean clone has no production configuration at all, and no `web.config` and no DACPAC publish
profile either. You write them. Never put production secrets in the base `appsettings.json`.

```json
{
  "ConnectionStrings": {
    "AuthDb": "Data Source=<SQL_SERVER>;Initial Catalog=<DB>;User Id=<USER>;Password=<PWD>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=true"
  },
  "Jwt": { "Issuer": "https://auth.<yourdomain>.com", "Audience": "https://auth.<yourdomain>.com" },
  "SecretManagement": { "StorageMode": "Certificate", "AutoGenerateKeys": true, "EnableAdminApi": false },
  "Cors": {
    "AllowedOrigins": [ "https://console.<yourdomain>.com", "https://accounts.<yourdomain>.com" ],
    "AllowCredentials": true
  },
  "Email": { "Enabled": false }
}
```

**List every browser origin explicitly, and never use `"*"`.** Get this wrong in two different ways
and you get two different failures. An **empty** array refuses to start outside Development, which is
loud and easy to diagnose. A **wildcard** is the dangerous one: the app starts normally and then
denies every browser request, because the wildcard skips the branch that would have allowed
credentials, and the permissive fallback behind it is Development-only. Both web applications need
their origins listed here or neither can talk to the API.

On IIS or shared hosting, also set `DataProtection:KeyPath` to a writable folder outside the web root.
See the deployment guide for the full configuration matrix and for choosing a storage mode —
`Certificate` is the portable choice and the one to prefer on shared hosting.

**4. Publish and deploy the Auth API**

```bash
dotnet publish Auth/Auth_API/Auth_API.csproj -c Release -o ./publish/auth-api
```

Set `ASPNETCORE_ENVIRONMENT=Production` in the generated `web.config`, then upload the output. On
IIS / Plesk the app auto-starts and generates its secret keys on first run.

**5. (Optional) API Gateway** — publish `Auth/API_Gateway/API_Gateway.csproj`, point it at the Auth
API, and use the **same** secret-storage mode. See Phase 5 of the guide.

**6. Build and deploy the two web applications** — without this step you have an API and no way for
anyone to use it. Run these from the `Auth_UI/` folder. Each application reads its API address at
build time, so set the variables before building, not after.

```bash
pnpm install
```

```bash
VITE_API_BASE_URL=https://auth.<yourdomain>.com VITE_ACCOUNTS_URL=https://accounts.<yourdomain>.com pnpm build
```

That produces `Auth_UI/apps/console/dist/` and `Auth_UI/apps/accounts/dist/`. Each already contains a
`web.config` that tells IIS to serve `index.html` for unknown paths, which is what a single-page
application needs. Upload each `dist/` folder to its own site, and make sure both origins appear in
`Cors:AllowedOrigins` from step 3.

**Nothing in this repository automates this step** — no publish profile, script, or pipeline targets
`apps/*/dist`. Copying the files up is manual.

**7. Verify**

```bash
curl https://auth.<yourdomain>.com/health   # app is up
curl https://auth.<yourdomain>.com/ready    # up AND can reach the database
curl https://auth.<yourdomain>.com/.well-known/jwks.json   # signing keys loaded
```

Then open the console in a browser and sign in as `admin@company.com`. You are forced to change the
password before you can reach anything else.

Finally complete the **go-live checklist** in the deployment guide (HTTPS/HSTS, admin password
changed, least-privilege SQL user, secrets backed up, database backups scheduled).

> **Before you design roles, read this.** On a clean database publish, 34 of the 50 permission codes
> the API enforces have no row in the `Permissions` table, and 6 of those exist in no SQL script at
> all. Wildcards match by prefix, so the seeded `auth:users:*` does **not** satisfy a `users:read`
> check. Until you load the missing codes, only the `super-admin` role's global `*` reaches those
> endpoints — an admin role granted granular codes will get 403s that look inexplicable. The
> deployment guide explains what to load and how.

> The full guide covers secret-storage setup, bring-your-own-key migration, the password pepper and
> breach check, and a complete troubleshooting matrix. Do not skip Part 1 for a real deployment.

---

## Documentation

| Document | Purpose | العربية |
|---|---|---|
| [Executive Summary](ReadMe/01_AUTH_SYSTEM_EXECUTIVE_SUMMARY_EN.md) | One-page business overview | [عربي](ReadMe/01_AUTH_SYSTEM_EXECUTIVE_SUMMARY_AR.md) |
| [System Documentation](ReadMe/02_AUTH_SYSTEM_DOCUMENTATION_EN.md) | Full feature reference, including both web applications | [عربي](ReadMe/02_AUTH_SYSTEM_DOCUMENTATION_AR.md) |
| [Technical Deep Dive](ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md) | Architecture, security implementation, and operations | [عربي](ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_AR.md) |
| [Developer Guide](ReadMe/DEVELOPER_GUIDE.md) | Local setup, all 199 endpoints, workflows, troubleshooting | [عربي](ReadMe/DEVELOPER_GUIDE.ar.md) |
| [Production Deployment Guide](ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md) | End-to-end production deployment, in ordered phases | — |
| [Application Integration Guide](ReadMe/APPLICATION_INTEGRATION_GUIDE.md) | Connecting one of your own apps via the SDK | — |
| [SDK Publishing Guide](ReadMe/SDK_PUBLISHING_GUIDE.md) | Packaging and publishing `Auth.Sdk` | — |

Start with the **Developer Guide** to run the system locally, or the **Production Deployment Guide**
to put it on a server. Both are written for someone meeting this codebase for the first time.

---

## Security

Found a vulnerability? Please read [SECURITY.md](SECURITY.md) and report it privately. Do not open a
public issue — this is an identity provider, and a public report is a public exploit for every
deployment that has not patched yet.

## License

[MIT](LICENSE). Copyright (c) 2026 Omar Sebakhi.

Publishing a repository does not license it; without this file the code was legally "all rights
reserved" and no one could adopt it. If your organisation needs an explicit patent grant, Apache-2.0
is the usual alternative and swapping to it is a one-file change.
