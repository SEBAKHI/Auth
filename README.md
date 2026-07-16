# AuthSystem

Enterprise identity platform for multi-application, multi-tenant organizations. AuthSystem is a
centralized service that handles **authentication** (who users are), **authorization** (what they can
do), and **audit logging** (what they did) for all of your applications from one place.

Built on .NET 10 with Clean Architecture, CQRS, and DDD. It exposes 84+ REST endpoints, an optional
API gateway, and an SDK that lets your other apps validate tokens without ever holding a private key,
and ships with an automated xUnit test suite.

---

## Features

- **Argon2id password hashing** — OWASP-recommended, memory-hard, with per-password salt, optional
  server-side pepper, and a breached-password check (HIBP, k-anonymity).
- **JWT authentication (RS256)** — asymmetric signing; consumers verify with the public key via JWKS.
- **Hierarchical permissions** — role-based access control with wildcards (e.g. `admin:*`) and
  time-based expiration.
- **Multi-application SSO** — one identity across every registered app.
- **Multi-tenant organizations** — member management, invitations, and per-app subscriptions.
- **Two-factor authentication** — TOTP (RFC 6238).
- **External login** — Google ID-token validation via a pluggable provider strategy.
- **API keys** — create, rotate (with grace period), and revoke.
- **Comprehensive audit logging** — every action with user, timestamp, IP, and change history;
  exportable (CSV/JSON). Supports GDPR, SOC 2, HIPAA, and PCI-DSS evidence needs.
- **Built-in protection** — rate limiting, OWASP-compliant security headers, and encrypted secret
  storage at rest.
- **7-language support** — including RTL (Arabic, Urdu, Persian).
- **Automated test suite** — xUnit tests with Moq and FluentAssertions (`Auth_API.Tests`).

---

## Technology Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 |
| Database access | Dapper (micro-ORM) over SQL Server |
| API gateway | YARP reverse proxy |
| Patterns | MediatR (CQRS), FluentValidation, ErrorOr |
| Security | Argon2id, JWT RS256, DPAPI / certificate secret encryption |
| Logging | Serilog (structured) |

---

## Architecture

```
Client ──▶ API Gateway (public)  ──▶  Auth API (private)  ──▶  SQL Server
           rate limit + headers       JWT + permissions         database
           adds X-Gateway-Token       audit logging
```

The gateway is optional — clients can hit the Auth API directly. The solution follows the five-layer
Clean Architecture model: `Auth.Domain`, `Auth.Application`, `Auth.Infrastructure`, `Auth_API`, and
the `Auth_DB` SQL project, plus `API_Gateway`, `Auth_Localization`, `Auth.Sdk`, and `Auth_Setup`.

---

## Prerequisites

- **.NET 10 SDK** (build machine) and **.NET 10 Hosting Runtime** (server)
- **SQL Server** (Express/Developer/LocalDB) and a SQL client (SSMS or Azure Data Studio)
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
dotnet build Auth/Auth.sln -c Release
```

**2. Database** — create an empty database and a least-privilege SQL login (not `sa`), then publish
the schema + seed data into it. In Visual Studio, right-click **Auth_DB** → **Publish**, point it at
your database, and hit Publish. (Publish profiles are per-environment and gitignored — yours is
created on first publish.)

```bash
# Set a real admin password (the seed ships a non-working placeholder):
dotnet run --project Auth/Auth_Setup -c Release
```

`Auth_Setup` prints an `UPDATE [dbo].[Users] ...` statement — run it against your database. The
default password is `Admin@123!`, and you are forced to change it on first login.

**3. Configure** — edit `Auth/Auth_API/appsettings.Production.json` (never put production secrets in
the base `appsettings.json`):

```json
{
  "ConnectionStrings": {
    "AuthDb": "Data Source=<SQL_SERVER>;Initial Catalog=<DB>;User Id=<USER>;Password=<PWD>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=true"
  },
  "Jwt": { "Issuer": "https://auth.<yourdomain>.com", "Audience": "https://auth.<yourdomain>.com" },
  "SecretManagement": { "StorageMode": "PlainText", "AutoGenerateKeys": true, "EnableAdminApi": false },
  "Cors": { "AllowedOrigins": [ "https://app.<yourdomain>.com" ], "AllowCredentials": true },
  "Email": { "Enabled": false }
}
```

CORS must list explicit origins in production (`*` or empty makes the app refuse to start). On IIS /
shared hosting, also set `DataProtection:KeyPath` to a writable folder outside the web root. See the
guide for the full configuration matrix.

**4. Publish and deploy the Auth API**

```bash
dotnet publish Auth/Auth_API/Auth_API.csproj -c Release -o ./publish/auth-api
```

Set `ASPNETCORE_ENVIRONMENT=Production` in the generated `web.config`, then upload the output. On
IIS / Plesk the app auto-starts and generates its secret keys on first run.

**5. (Optional) API Gateway** — publish `Auth/API_Gateway/API_Gateway.csproj`, point it at the Auth
API, and use the **same** secret-storage mode. See Phase 5 of the guide.

**6. Verify**

```bash
curl https://auth.<yourdomain>.com/health   # app is up
curl https://auth.<yourdomain>.com/ready    # up AND can reach the database
curl https://auth.<yourdomain>.com/.well-known/jwks.json   # signing keys loaded
```

Then complete the **go-live checklist** in the deployment guide (HTTPS/HSTS, admin password changed,
least-privilege SQL user, secrets backed up, database backups scheduled).

> The full guide covers secret-storage setup, bring-your-own-key migration, the password pepper and
> breach check, and a complete troubleshooting matrix. Do not skip Part 1 for a real deployment.

---

## Documentation

| Document | Purpose |
|---|---|
| [Executive Summary](ReadMe/01_AUTH_SYSTEM_EXECUTIVE_SUMMARY_EN.md) | One-page business overview |
| [System Documentation](ReadMe/02_AUTH_SYSTEM_DOCUMENTATION_EN.md) | Full feature reference |
| [Technical Deep Dive](ReadMe/03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md) | Architecture and implementation detail |
| [Developer Guide](ReadMe/DEVELOPER_GUIDE.md) | Local setup, API reference, common workflows |
| [Production Deployment Guide](ReadMe/PRODUCTION_DEPLOYMENT_GUIDE.md) | End-to-end production deployment |
| [SDK Publishing Guide](ReadMe/SDK_PUBLISHING_GUIDE.md) | Packaging and publishing `Auth.Sdk` |
| [Application Integration Guide](ReadMe/APPLICATION_INTEGRATION_GUIDE.md) | Connecting a consumer app via the SDK |

Arabic editions of the summary, documentation, deep dive, and developer guide are available in the
[`ReadMe/`](ReadMe/) folder.
