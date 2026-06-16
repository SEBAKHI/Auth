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

---

## 1. System Overview

### 1.1 What is AuthSystem

AuthSystem is a production-grade authentication and authorization platform built on .NET 10. It provides multi-application, multi-organization identity management with hierarchical permissions, role-based access control (RBAC), two-factor authentication, external provider login (Google), API key management, session tracking, and comprehensive audit logging. It is designed as a centralized identity service that multiple applications can integrate with via REST APIs.

### 1.2 API Capabilities at a Glance

The system exposes **84+ REST API endpoints** across **12 controllers**, organized into the following feature areas:

| Feature | Endpoints | Description |
|---|---|---|
| **Discovery (OIDC)** | 3 | OpenID Connect discovery, JWKS, public key |
| **Authentication** | 18 | Login, register, external login, token refresh/revoke, password management, email verification, session management |
| **Two-Factor Auth** | 3 | TOTP setup, enable, disable |
| **Users** | 16 | CRUD, role/permission assignment, lock/unlock, activate/deactivate, self-service profile |
| **Roles** | 5 | CRUD scoped to applications |
| **Permissions** | 8 | CRUD with hierarchical implications and wildcard support |
| **Applications** | 7 | Multi-app registration with per-app roles and permissions |
| **Organizations** | 17 | Multi-tenant CRUD, member management, invitations, app subscriptions, member roles/permissions |
| **Invitations** | 1 | Accept organization invitation |
| **API Keys** | 4 | Create, list, revoke, rotate with grace period |
| **Audit Logs** | 5 | Query, filter, user/entity scoped, export (CSV/JSON) |
| **Secrets (Admin)** | 6 | DPAPI secret status, key generation, custom secret management |

> See [Section 5 — API Reference](#5-api-reference) for full endpoint details with request/response examples.

### 1.3 Architecture Diagram

```
┌──────────┐       ┌─────────────────────┐       ┌──────────────────┐       ┌────────────┐
│          │       │    API_Gateway       │       │    Auth_API       │       │            │
│  Client  │──────▶│  (YARP Proxy)       │──────▶│  (REST API)      │──────▶│ SQL Server │
│          │       │  Port: 5034/7159    │       │  Port: 5100/5101 │       │            │
└──────────┘       └─────────────────────┘       └──────────────────┘       └────────────┘
                   │ + X-Gateway-Token    │       │ + JWT Auth        │
                   │ + X-Forwarded-For    │       │ + Permission Auth │
                   │ + X-Correlation-ID   │       │ + DPAPI Secrets   │
                   │ + Rate Limiting      │       │ + Audit Logging   │
                   └─────────────────────┘       └──────────────────┘
```

### 1.4 Solution Structure

```
Auth/
├── src/
│   ├── Services/
│   │   ├── Auth.Domain          — Entities, interfaces, enums, error definitions
│   │   ├── Auth.Application     — CQRS commands/queries, DTOs, validators, configuration
│   │   ├── Auth.Infrastructure  — Dapper repos, JWT, Argon2id, DPAPI, Google auth, TOTP, SMTP
│   │   └── Auth_API             — ASP.NET Core 10 REST API (12 controllers, 84+ endpoints)
│   ├── Shared/
│   │   └── Auth_Localization    — Resource files for 7 languages (en, ar, tr, fr, zh, ur, fa)
│   ├── Gateway/
│   │   └── API_Gateway          — YARP reverse proxy with rate limiting and security headers
│   ├── Setup/
│   │   └── Auth_Setup           — Console utility for password hashing
│   └── Database/
│       └── Auth_DB              — SQL Server Database Project (26 tables, stored procedures)
├── Tests/
│   └── Auth_API.Tests           — xUnit, Moq, FluentAssertions
└── Auth.sln
```

### 1.5 Technology Stack and Rationale

| Technology | Purpose | Why This Over Alternatives |
|---|---|---|
| **.NET 10** | Runtime & framework | Latest version with native OpenAPI, performance improvements, AOT support |
| **Dapper** | Database access (micro-ORM) | Full SQL control, stored procedure support, superior performance vs Entity Framework for read-heavy auth workloads |
| **MediatR** | CQRS pattern | Decoupled command/query handlers, pipeline behaviors for cross-cutting concerns, excellent testability |
| **ErrorOr** | Error handling | Discriminated union pattern avoids exception-driven flow control; cleaner than Result pattern libraries |
| **FluentValidation** | Input validation | Declarative validation rules separated from domain logic; richer than Data Annotations |
| **RS256 JWT** | Token signing | Asymmetric keys allow external services to validate tokens using the public key without sharing the private key (unlike HS256) |
| **Argon2id** | Password hashing | OWASP 2024 recommended; memory-hard algorithm resistant to GPU/ASIC attacks (superior to bcrypt/PBKDF2) |
| **DPAPI** | Secret encryption at rest | Windows-native, machine-bound encryption; no external key vault dependency for single-server deployments |
| **YARP** | API Gateway | .NET-native reverse proxy; configured in appsettings.json; superior .NET integration vs NGINX/Ocelot |
| **Serilog** | Structured logging | Multiple sinks (console, file), enrichers, structured JSON output; industry standard for .NET |
| **SQL Server + SSDT** | Database | Enterprise-grade RDBMS; SSDT provides version-controlled schema with stored procedures for critical paths |
| **xUnit + Moq + FluentAssertions** | Testing | Most popular .NET test stack; FluentAssertions for readable assertions; Moq for lightweight mocking |
| **Otp.NET** | TOTP 2FA | Lightweight RFC 6238 implementation for time-based one-time passwords |
| **Google.Apis.Auth** | External authentication | Official Google library for ID token validation |
| **API Versioning** | Version management | URL-based versioning (`/api/v1/`) for clear API evolution without breaking existing consumers |

---

## 2. Prerequisites

| Requirement | Details |
|---|---|
| **.NET 10 SDK** | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **SQL Server** | Express or Developer edition (LocalDB also works for development) |
| **Windows OS** | Required for DPAPI secret encryption |
| **Postman** (optional) | Collection included at `Auth_API/Postman/AuthSystem.postman_collection.json` |
| **Visual Studio 2022+** (optional) | For SSDT database project publishing |

---

## 3. Getting Started

### 3.1 Clone and Build

```bash
git clone <repository-url>
cd AuthSystem
dotnet build Auth/Auth.sln
```

### 3.2 Database Setup

**Option A: SSDT Publish (Visual Studio)**

1. Open `Auth/Auth.sln` in Visual Studio
2. Right-click the `Auth_DB` project → **Publish**
3. Configure target connection string (e.g., `.\SQLEXPRESS`)
4. Click **Publish**

**Option B: Manual Setup**

Execute the SQL scripts from `Auth/Auth_DB/dbo/Tables/` in this order:

**Core Tables (8):**
- `Users`, `Applications`, `Roles`, `Permissions`
- `UserRoles`, `RolePermissions`, `UserPermissions`, `PermissionImplications`

**Authentication Tables (5):**
- `RefreshTokens`, `UserSessions`, `LoginAttempts`
- `UserExternalLogins`, `ExternalAuthProviders`

**Organization Tables (6):**
- `Organizations`, `OrganizationUsers`, `OrganizationInvitations`
- `OrganizationApplications`, `OrganizationUserRoles`, `OrganizationUserPermissions`

**Security Tables (7):**
- `ApiKeys`, `ApiKeyScopes`, `TwoFactorAuth`
- `AuditLogs`, `PasswordHistory`
- `EmailVerificationTokens`, `PasswordResetTokens`

Then execute all stored procedures from `Auth/Auth_DB/dbo/StoredProcedures/`.

### 3.3 First Startup and DPAPI Secrets

On first startup, if the secrets file does not exist and `AutoGenerateKeys` is `true`, the system automatically generates:

| Secret | Purpose |
|---|---|
| **RSA Key Pair** (2048-bit) | JWT access token signing (RS256) |
| **HMAC Key** (32 bytes) | Refresh token hashing (HMAC-SHA256) |
| **Gateway Token** (32 bytes) | Inter-service authentication between API Gateway and Auth_API |

**Storage locations:**
- Secrets file: `%LOCALAPPDATA%/AuthSystem/Secrets/secrets.dpapi`
- Data Protection keys: `%LOCALAPPDATA%/AuthSystem/Keys`

**Important:** Once generated, keys are never automatically regenerated. To regenerate, use the Secrets Admin API or CLI tools.

**CLI Key Generation:**

```bash
# Generate DPAPI-encrypted HMAC key
dotnet run --project Auth/Auth_API -- --generate-hmac-key

# Generate DPAPI-encrypted RSA key pair
dotnet run --project Auth/Auth_API -- --generate-rsa-key
```

### 3.4 Configuration Reference

All configuration is in `Auth/Auth_API/appsettings.json`. Below is every section.

#### ConnectionStrings

```json
{
  "ConnectionStrings": {
    "AuthDb": "Server=.\\SQLEXPRESS;Database=AuthDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True"
  }
}
```

#### JWT Settings

```json
{
  "Jwt": {
    "Issuer": "https://auth.yourdomain.com",
    "Audience": "https://api.yourdomain.com",
    "AccessTokenLifetimeMinutes": 15,
    "RefreshTokenLifetimeDays": 7,
    "KeyId": "auth-key-1",
    "RotateRefreshTokens": true,
    "ClockSkewSeconds": 60
  }
}
```

| Field | Description |
|---|---|
| `Issuer` | JWT `iss` claim; identifies the token issuer |
| `Audience` | JWT `aud` claim; intended recipient of the token |
| `AccessTokenLifetimeMinutes` | Access token expiration (default: 15 minutes) |
| `RefreshTokenLifetimeDays` | Refresh token expiration (default: 7 days) |
| `KeyId` | Key identifier for JWKS endpoint |
| `RotateRefreshTokens` | When `true`, refresh generates a new refresh token (rotation) |
| `ClockSkewSeconds` | Tolerance for clock differences between servers |

#### Password Policy

```json
{
  "Password": {
    "MinimumLength": 6,
    "RequireUppercase": true,
    "RequireLowercase": true,
    "RequireDigit": true,
    "RequireSpecialCharacter": true,
    "PasswordHistoryCount": 3,
    "PasswordExpirationDays": 0,
    "MaxFailedAttempts": 5,
    "LockoutDurationMinutes": 15,
    "Argon2": {
      "MemorySize": 19456,
      "Iterations": 2,
      "DegreeOfParallelism": 1
    }
  }
}
```

| Field | Description |
|---|---|
| `MinimumLength` | Minimum password length (OWASP recommends 12+) |
| `PasswordHistoryCount` | Number of previous passwords to prevent reuse |
| `PasswordExpirationDays` | Days until password expires (0 = never) |
| `MaxFailedAttempts` | Failed login attempts before lockout |
| `LockoutDurationMinutes` | Account lockout duration after max failed attempts |
| `Argon2.MemorySize` | Memory cost in KB (19456 = ~19 MB, OWASP 2024 recommended) |
| `Argon2.Iterations` | Time cost (number of iterations) |
| `Argon2.DegreeOfParallelism` | Thread count for hashing |

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
      "/openapi"
    ]
  }
}
```

| Field | Description |
|---|---|
| `ValidationEnabled` | When `true`, all requests must include X-Gateway-Token (disable in development) |
| `TokenHeaderName` | Header name for the gateway authentication token |
| `ExemptPaths` | Paths that bypass gateway token validation |

#### Session Settings

```json
{
  "Session": {
    "LifetimeHours": 24,
    "ExtensionHours": 12,
    "MaxConcurrentSessions": 5,
    "IdleTimeoutMinutes": 60
  }
}
```

#### Email Settings

```json
{
  "Email": {
    "Enabled": false,
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "UseSsl": true,
    "Username": "",
    "FromEmail": "noreply@yourdomain.com",
    "FromName": "AuthSystem",
    "OtpExpirationMinutes": 15,
    "MaxOtpAttemptsPerWindow": 3,
    "OtpWindowMinutes": 60
  }
}
```

> **Note:** SMTP password is stored in DPAPI secrets, not in appsettings.json.

#### External Authentication

```json
{
  "ExternalAuth": {
    "Google": {
      "Enabled": true,
      "ClientId": "your-google-client-id.apps.googleusercontent.com"
    }
  }
}
```

> Google Client ID is a public value (not a secret). The Google Client Secret is not needed for ID token validation.

#### CORS

```json
{
  "Cors": {
    "AllowedOrigins": ["https://app.yourdomain.com"],
    "AllowCredentials": true
  }
}
```

> **Production:** Explicit origins are **required**. Wildcards (`*`) are not permitted.
> **Development:** `["*"]` is allowed via `appsettings.Development.json`.

#### Rate Limiting

```json
{
  "RateLimiting": {
    "General": {
      "PermitLimit": 100,
      "WindowSeconds": 60
    },
    "Login": {
      "PermitLimit": 5,
      "WindowSeconds": 60
    }
  }
}
```

#### Secret Management

```json
{
  "SecretManagement": {
    "SecretsFilePath": "%LOCALAPPDATA%/AuthSystem/Secrets/secrets.dpapi",
    "AutoGenerateKeys": true,
    "EnableAdminApi": false
  }
}
```

| Field | Description |
|---|---|
| `SecretsFilePath` | Location of the DPAPI-encrypted secrets file |
| `AutoGenerateKeys` | Auto-generate RSA, HMAC, and gateway token on first startup |
| `EnableAdminApi` | Enable the `/api/v1/admin/secrets` endpoints (default: false) |

#### Data Protection

```json
{
  "DataProtection": {
    "KeyPath": "%LOCALAPPDATA%/AuthSystem/Keys"
  }
}
```

#### Serilog

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "Logs/auth-api-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

### 3.5 Development vs Production Overrides

**`appsettings.Development.json`** overrides:

| Setting | Development Value |
|---|---|
| `ConnectionStrings.AuthDb` | `.\SQLEXPRESS` with Windows Auth |
| `Jwt.Issuer` | `http://localhost:5100` |
| `Jwt.Audience` | `http://localhost:5000` |
| `Gateway.ValidationEnabled` | `false` |
| `Cors.AllowedOrigins` | `["*"]` |

### 3.6 Running the API and Gateway

**Start the Auth API:**

```bash
dotnet run --project Auth/Auth_API
# Listening on: http://localhost:5100, https://localhost:5101
```

**Start the API Gateway (optional for development):**

```bash
dotnet run --project Auth/API_Gateway
# Listening on: http://localhost:5034, https://localhost:7159
```

> In development, you can call Auth_API directly (gateway token validation is disabled). In production, all requests should flow through the API Gateway.

### 3.7 Verifying the Setup

```bash
# Health check
curl http://localhost:5100/health

# Readiness check (includes database)
curl http://localhost:5100/ready

# OIDC Discovery
curl http://localhost:5100/.well-known/openid-configuration
```

A successful OIDC response confirms JWT signing keys are loaded and the API is ready.

---

## 4. Architecture Deep Dive

### 4.1 Clean Architecture Layers

```
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

**Dependency Rule:** Dependencies point inward only. Domain has zero external dependencies. Application depends only on Domain. Infrastructure depends on Domain and Application. The API layer depends on Infrastructure (which transitively brings everything).

### 4.2 CQRS with MediatR

Every API endpoint dispatches a **Command** (write) or **Query** (read) via MediatR.

**Naming convention:**
- Command: `LoginCommand` → `LoginCommandHandler`
- Query: `GetUserByIdQuery` → `GetUserByIdQueryHandler`

**File organization:**
```
Auth.Application/Features/
├── Authentication/
│   ├── Commands/Login/
│   │   ├── LoginCommand.cs
│   │   └── LoginCommandHandler.cs
│   ├── Commands/Register/
│   └── Queries/GetSessions/
├── UserManagement/
├── RoleManagement/
└── ...
```

Each handler implements `IRequestHandler<TRequest, ErrorOr<TResponse>>` and receives dependencies via constructor injection.

### 4.3 Error Handling (ErrorOr Pattern)

Handlers return `ErrorOr<T>` instead of throwing exceptions. Controllers map results to HTTP responses:

```
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

**ProblemDetails response format:**

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "User.InvalidCredentials",
  "status": 400,
  "detail": "The provided credentials are invalid.",
  "instance": "/api/v1/auth/login",
  "correlationId": "abc-123"
}
```

### 4.4 Permission-Based Authorization

The system uses a custom permission-based authorization system (not ASP.NET Identity roles).

**How it works:**

1. `[RequirePermission("users:read")]` attribute is applied to a controller action
2. `PermissionPolicyProvider` dynamically creates an authorization policy for the permission
3. `PermissionRequirementHandler` checks the JWT `permissions` claims against the requirement
4. Wildcard matching is supported:
   - Exact: `users:read` matches `users:read`
   - Wildcard: `users:*` matches `users:read`, `users:create`, etc.
   - Global: `*` matches everything

**Permission code format:** `{resource}:{action}` or `{app}:{resource}:{action}`

Examples: `users:read`, `roles:create`, `crm:leads:read`, `org:members:manage`

### 4.5 Middleware Pipeline

Requests flow through middleware in this order:

```
Request
  │
  ▼
SecurityHeadersMiddleware        — Adds OWASP security headers, removes Server header
  │
  ▼
ExceptionHandlingMiddleware      — Global exception catch → ProblemDetails
  │
  ▼
GatewayTokenValidationMiddleware — Validates X-Gateway-Token (production only)
  │
  ▼
Serilog Request Logging          — Structured HTTP request/response logging
  │
  ▼
Rate Limiting                    — Fixed window rate limiter
  │
  ▼
JwtBlacklistValidationMiddleware — Checks token against revocation blacklist
  │
  ▼
JWT Authentication               — Validates Bearer token, sets ClaimsPrincipal
  │
  ▼
Authorization                    — Permission-based access control
  │
  ▼
Controller Action
```

### 4.6 DPAPI Secret Management

Windows DPAPI encrypts sensitive configuration at rest:

```
Startup Flow:
1. DataProtectionProvider initialized with key ring at %LOCALAPPDATA%/AuthSystem/Keys
2. DpapiSecretService loads secrets.dpapi file
3. If file missing AND AutoGenerateKeys=true → generate RSA, HMAC, Gateway token
4. Secrets decrypted and injected into IConfiguration via AddDpapiSecrets()
5. JwtTokenService, RefreshTokenKeyService, GatewayMiddleware read from IConfiguration
```

**Stored secrets:**
- `Jwt:PrivateKeyEncrypted` — RSA private key (PEM, DPAPI-encrypted)
- `Jwt:PublicKeyPem` — RSA public key (PEM, plaintext for JWKS)
- `Jwt:RefreshTokenHmacKey` — HMAC-SHA256 key for refresh token hashing
- `Gateway:Token` — Shared secret between Gateway and API
- `Email:SmtpPassword` — SMTP authentication password
- `Custom:*` — User-defined custom secrets

### 4.7 JWT Token Lifecycle

```
Login
  │
  ├──▶ Access Token (RS256, 15 min)
  │     Claims: sub, email, name, roles[], permissions[], jti, iat, exp
  │
  └──▶ Refresh Token (random 64 bytes, 7 days)
        Stored as HMAC-SHA256 hash in database

Refresh
  │
  ├──▶ New Access Token
  └──▶ New Refresh Token (old one revoked — rotation)

Logout
  │
  ├──▶ Access Token JTI added to blacklist
  └──▶ Refresh Token revoked in database
```

**External validation:** Any service can validate access tokens using:
- `GET /.well-known/jwks.json` — JSON Web Key Set
- `GET /.well-known/public-key.pem` — PEM public key

### 4.8 API Gateway (YARP)

The API Gateway provides a single entry point with:

| Feature | Configuration |
|---|---|
| **Routing** | Path-based: `/api/v1/auth/**`, `/api/v1/users/**`, etc. |
| **Rate Limiting** | Global: 1000/60s, Auth: 20/60s, API: 100/60s |
| **Header Injection** | X-Gateway-Token, X-Forwarded-For/Host/Proto, X-Correlation-ID |
| **Health Monitoring** | Active health checks on Auth_API every 30s |
| **Security Headers** | Same OWASP headers as Auth_API |

**YARP routes are configured in** `API_Gateway/appsettings.json` under `ReverseProxy.Routes`.

### 4.9 Localization

The system supports 7 languages via embedded resource files:

| Code | Language |
|---|---|
| `en` | English (default) |
| `ar` | Arabic |
| `tr` | Turkish |
| `fr` | French |
| `zh` | Chinese |
| `ur` | Urdu |
| `fa` | Farsi (Persian) |

Users set their preferred language via `preferredLanguage` field. Error messages and notifications are returned in the user's language.

---

## 5. API Reference

**Base URL:** `http://localhost:5100` (direct) or `http://localhost:5034` (via gateway)

**Authentication:** Most endpoints require `Authorization: Bearer <access_token>` header.

**API Version:** All versioned endpoints use `/api/v1/` prefix.

**Common response codes across all endpoints:**

| Code | Meaning |
|---|---|
| 200 | Success |
| 201 | Created |
| 204 | No Content (success, no body) |
| 400 | Bad Request (validation error) |
| 401 | Unauthorized (missing/invalid token) |
| 403 | Forbidden (insufficient permissions) |
| 404 | Not Found |
| 409 | Conflict (duplicate) |
| 429 | Too Many Requests (rate limited) |
| 500 | Internal Server Error |

### 5.0 Endpoint Index

#### Discovery (OIDC) — 3 endpoints

| Method | Endpoint | Auth |
|---|---|---|
| GET | `/.well-known/openid-configuration` | Anonymous |
| GET | `/.well-known/jwks.json` | Anonymous |
| GET | `/.well-known/public-key.pem` | Anonymous |

#### Authentication — 18 endpoints

| Method | Endpoint | Auth | Rate Limited |
|---|---|---|---|
| POST | `/api/v1/auth/login` | Anonymous | Yes |
| POST | `/api/v1/auth/register` | Anonymous | Yes |
| GET | `/api/v1/auth/external-providers` | Anonymous | — |
| POST | `/api/v1/auth/external-login` | Anonymous | Yes |
| POST | `/api/v1/auth/refresh` | Anonymous | — |
| POST | `/api/v1/auth/logout` | Authenticated | — |
| POST | `/api/v1/auth/change-password` | Authenticated | — |
| POST | `/api/v1/auth/forgot-password` | Anonymous | Yes |
| POST | `/api/v1/auth/reset-password` | Anonymous | — |
| GET | `/api/v1/auth/sessions` | Authenticated | — |
| DELETE | `/api/v1/auth/sessions/{sessionId}` | Authenticated | — |
| DELETE | `/api/v1/auth/sessions` | Authenticated | — |
| GET | `/api/v1/auth/me` | Authenticated | — |
| POST | `/api/v1/auth/revoke` | Anonymous/Authenticated | — |
| POST | `/api/v1/auth/introspect` | Authenticated | — |
| POST | `/api/v1/auth/send-verification-email` | Authenticated | Yes |
| POST | `/api/v1/auth/verify-email` | Anonymous | Yes |
| POST | `/api/v1/auth/resend-verification-email` | Anonymous | Yes |

#### Two-Factor Authentication — 3 endpoints

| Method | Endpoint | Auth |
|---|---|---|
| POST | `/api/v1/auth/2fa/setup` | Authenticated |
| POST | `/api/v1/auth/2fa/enable` | Authenticated |
| POST | `/api/v1/auth/2fa/disable` | Authenticated |

#### Users — 16 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/users` | `users:read` |
| GET | `/api/v1/users/{id}` | `users:read` |
| POST | `/api/v1/users` | `users:create` |
| PUT | `/api/v1/users/{id}` | `users:update` |
| DELETE | `/api/v1/users/{id}` | `users:delete` |
| POST | `/api/v1/users/{id}/roles` | `users:manage-roles` |
| GET | `/api/v1/users/{id}/roles` | `users:read` |
| DELETE | `/api/v1/users/{id}/roles/{roleId}` | `users:manage-roles` |
| GET | `/api/v1/users/{id}/permissions` | `users:read` |
| POST | `/api/v1/users/{id}/permissions` | `users:manage-permissions` |
| DELETE | `/api/v1/users/{id}/permissions/{permissionId}` | `users:manage-permissions` |
| POST | `/api/v1/users/{id}/lock` | `users:manage` |
| POST | `/api/v1/users/{id}/unlock` | `users:manage` |
| POST | `/api/v1/users/{id}/activate` | `users:manage` |
| POST | `/api/v1/users/{id}/deactivate` | `users:manage` |
| GET | `/api/v1/users/me` | Authenticated |
| PUT | `/api/v1/users/me` | Authenticated |

#### Roles — 5 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/roles` | `roles:read` |
| GET | `/api/v1/roles/{id}` | `roles:read` |
| POST | `/api/v1/roles` | `roles:create` |
| PUT | `/api/v1/roles/{id}` | `roles:update` |
| DELETE | `/api/v1/roles/{id}` | `roles:delete` |

#### Permissions — 8 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/permissions` | `permissions:read` |
| GET | `/api/v1/permissions/{id}` | `permissions:read` |
| POST | `/api/v1/permissions` | `permissions:create` |
| PUT | `/api/v1/permissions/{id}` | `permissions:update` |
| DELETE | `/api/v1/permissions/{id}` | `permissions:delete` |
| GET | `/api/v1/permissions/{id}/implications` | `permissions:read` |
| POST | `/api/v1/permissions/{id}/implications` | `permissions:manage` |
| DELETE | `/api/v1/permissions/{id}/implications/{impliedId}` | `permissions:manage` |

#### Applications — 7 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/applications` | `applications:read` |
| GET | `/api/v1/applications/{id}` | `applications:read` |
| GET | `/api/v1/applications/{id}/roles` | `applications:read` |
| GET | `/api/v1/applications/{id}/permissions` | `applications:read` |
| POST | `/api/v1/applications` | `applications:create` |
| PUT | `/api/v1/applications/{id}` | `applications:update` |
| DELETE | `/api/v1/applications/{id}` | `applications:delete` |

#### Organizations — 17 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/organizations` | Authenticated |
| GET | `/api/v1/organizations/{id}` | Authenticated |
| POST | `/api/v1/organizations` | Authenticated |
| PUT | `/api/v1/organizations/{id}` | `org:update` |
| DELETE | `/api/v1/organizations/{id}` | Owner |
| GET | `/api/v1/organizations/{id}/members` | `org:members:read` |
| PUT | `/api/v1/organizations/{orgId}/members/{userId}/role` | `org:members:manage` |
| DELETE | `/api/v1/organizations/{orgId}/members/{userId}` | `org:members:manage` |
| GET | `/api/v1/organizations/{id}/invitations` | `org:members:read` |
| POST | `/api/v1/organizations/{id}/invitations` | `org:members:invite` |
| GET | `/api/v1/organizations/{id}/applications` | `org:apps:read` |
| POST | `/api/v1/organizations/{id}/applications` | `org:apps:manage` |
| PUT | `/api/v1/organizations/{id}/applications/{applicationId}` | `org:apps:manage` |
| DELETE | `/api/v1/organizations/{id}/applications/{applicationId}` | `org:apps:manage` |
| POST | `/api/v1/organizations/{orgId}/members/{userId}/roles` | `org:permissions:manage` |
| POST | `/api/v1/organizations/{orgId}/members/{userId}/permissions` | `org:permissions:manage` |

#### Invitations — 1 endpoint

| Method | Endpoint | Auth |
|---|---|---|
| POST | `/api/v1/invitations/{token}/accept` | Authenticated |

#### API Keys — 4 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/apikeys` | `apikeys:read` |
| POST | `/api/v1/apikeys` | `apikeys:create` |
| POST | `/api/v1/apikeys/{id}/revoke` | `apikeys:revoke` |
| POST | `/api/v1/apikeys/{id}/rotate` | `apikeys:rotate` |

#### Audit Logs — 5 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/audit-logs` | `auditlogs:read` |
| GET | `/api/v1/audit-logs/{id}` | `auditlogs:read` |
| GET | `/api/v1/audit-logs/users/{userId}` | `auditlogs:read` |
| GET | `/api/v1/audit-logs/entities/{entityType}/{entityId}` | `auditlogs:read` |
| POST | `/api/v1/audit-logs/export` | `auditlogs:export` |

#### Secrets (Admin) — 6 endpoints

| Method | Endpoint | Permission |
|---|---|---|
| GET | `/api/v1/admin/secrets/status` | `secrets.manage` |
| POST | `/api/v1/admin/secrets/generate/rsa` | `secrets.manage` |
| POST | `/api/v1/admin/secrets/generate/hmac` | `secrets.manage` |
| POST | `/api/v1/admin/secrets/generate/gateway-token` | `secrets.manage` |
| PUT | `/api/v1/admin/secrets/custom/{key}` | `secrets.manage` |
| DELETE | `/api/v1/admin/secrets/custom/{key}` | `secrets.manage` |

---

### 5.1 Discovery (OIDC)

These endpoints follow the OpenID Connect Discovery specification. They are **version-neutral** (no `/api/v1/` prefix) and **anonymous**.

#### GET `/.well-known/openid-configuration`

Returns the OpenID Connect discovery document.

**Auth:** Anonymous

**Response:**

```json
{
  "issuer": "http://localhost:5100",
  "authorization_endpoint": "http://localhost:5100/api/v1/auth/login",
  "token_endpoint": "http://localhost:5100/api/v1/auth/login",
  "userinfo_endpoint": "http://localhost:5100/api/v1/auth/me",
  "jwks_uri": "http://localhost:5100/.well-known/jwks.json",
  "revocation_endpoint": "http://localhost:5100/api/v1/auth/revoke",
  "introspection_endpoint": "http://localhost:5100/api/v1/auth/introspect",
  "grant_types_supported": ["password", "refresh_token"],
  "response_types_supported": ["token"],
  "subject_types_supported": ["public"],
  "id_token_signing_alg_values_supported": ["RS256"],
  "token_endpoint_auth_methods_supported": ["client_secret_post"],
  "claims_supported": ["sub", "email", "name", "given_name", "family_name", "locale", "timezone", "roles", "permissions"]
}
```

#### GET `/.well-known/jwks.json`

Returns the JSON Web Key Set for external token validation.

**Auth:** Anonymous

**Response:**

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "auth-key-1",
      "alg": "RS256",
      "n": "<modulus>",
      "e": "AQAB"
    }
  ]
}
```

#### GET `/.well-known/public-key.pem`

Returns the RSA public key in PEM format.

**Auth:** Anonymous

**Response:** `text/plain`

```
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA...
-----END PUBLIC KEY-----
```

---

### 5.2 Authentication

**Base route:** `/api/v1/auth`

#### POST `/api/v1/auth/login`

Authenticate a user with email and password.

**Auth:** Anonymous | **Rate Limited:** `login` policy (5 req/60s)

**Request:**

```json
{
  "email": "user@example.com",
  "password": "SecureP@ssw0rd!",
  "deviceId": "optional-device-identifier"
}
```

**Response (200):**

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
    "displayName": "John Doe"
  },
  "requiresPasswordChange": false,
  "requiresTwoFactor": false
}
```

**Error codes:** `User.InvalidCredentials`, `User.AccountLocked`, `User.AccountInactive`, `User.AccountPending`

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
  "organizationCreated": false
}
```

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

**Validations:** Password policy enforcement, password history check (last 5 passwords).

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

Complete password reset using the token received via email.

**Auth:** Anonymous

**Request:**

```json
{
  "email": "user@example.com",
  "token": "reset-token-from-email",
  "newPassword": "NewSecureP@ss1!",
  "confirmNewPassword": "NewSecureP@ss1!",
  "terminateSessions": true
}
```

**Response:** 204 No Content

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

#### GET `/api/v1/auth/me`

Get the authenticated user's profile with roles and permissions.

**Auth:** Authenticated

**Response (200):**

```json
{
  "id": "3fa85f64-...",
  "email": "user@example.com",
  "firstName": "John",
  "lastName": "Doe",
  "displayName": "John Doe",
  "phoneNumber": "+1234567890",
  "preferredLanguage": "en",
  "timeZone": "UTC",
  "emailConfirmed": true,
  "twoFactorEnabled": false,
  "roles": ["admin", "user"],
  "permissions": ["users:read", "users:create", "roles:read"]
}
```

#### POST `/api/v1/auth/revoke`

Revoke a token (RFC 7009 compliant).

**Auth:** Anonymous (token self-revocation) or Authenticated

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
  "email": "user@example.com",
  "exp": 1710244200,
  "iat": 1710243300,
  "iss": "http://localhost:5100",
  "aud": "http://localhost:5000",
  "tokenType": "access_token"
}
```

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

Verify email using the OTP code received via email.

**Auth:** Anonymous | **Rate Limited:** `login` policy

**Request:**

```json
{
  "userId": "3fa85f64-...",
  "otp": "123456"
}
```

**Response:** 204 No Content

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

---

### 5.3 Two-Factor Authentication

**Base route:** `/api/v1/auth/2fa`

All endpoints require authentication.

#### POST `/api/v1/auth/2fa/setup`

Generate a TOTP secret and QR code URI for 2FA setup.

**Auth:** Authenticated

**Response (200):**

```json
{
  "secret": "JBSWY3DPEHPK3PXP",
  "qrCodeUri": "otpauth://totp/AuthSystem:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=AuthSystem&digits=6&period=30"
}
```

> The user scans the QR code with an authenticator app (Google Authenticator, Authy, etc.) and then calls the enable endpoint with a valid code.

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
    "ABCD-1234-EFGH",
    "IJKL-5678-MNOP",
    "QRST-9012-UVWX"
  ]
}
```

> **Important:** Recovery codes are shown only once. The user must save them securely.

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

All endpoints require authentication and specific permissions.

#### GET `/api/v1/users`

List users with pagination and search.

**Permission:** `users:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | int | 1 | Page number |
| `pageSize` | int | 10 | Items per page |
| `searchTerm` | string | null | Search by name or email |

**Response (200):**

```json
{
  "items": [
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
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 42,
  "totalPages": 5
}
```

#### GET `/api/v1/users/{id}`

Get a user by ID.

**Permission:** `users:read`

**Response (200):** `UserDto` (same shape as list items with additional fields).

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

Delete a user (soft delete).

**Permission:** `users:delete`

**Response:** 204 No Content

> System users cannot be deleted.

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

---

### 5.5 Roles

**Base route:** `/api/v1/roles`

#### GET `/api/v1/roles`

List all roles, optionally filtered by application.

**Permission:** `roles:read`

**Query parameters:**

| Parameter | Type | Description |
|---|---|---|
| `applicationId` | Guid? | Filter roles by application (null = global roles) |

**Response (200):**

```json
[
  {
    "id": "role-guid",
    "code": "admin",
    "name": "Administrator",
    "description": "Full system access",
    "applicationId": null,
    "isSystem": true,
    "isActive": true,
    "permissionCount": 15
  }
]
```

#### GET `/api/v1/roles/{id}`

Get a role by ID.

**Permission:** `roles:read`

**Response (200):** `RoleDto` (includes permissions list)

#### POST `/api/v1/roles`

Create a new role.

**Permission:** `roles:create`

**Request:**

```json
{
  "applicationId": "app-guid-or-null",
  "code": "editor",
  "name": "Content Editor",
  "description": "Can edit and publish content",
  "permissionIds": ["perm-guid-1", "perm-guid-2"]
}
```

| Field | Description |
|---|---|
| `applicationId` | Null for global roles; set to scope the role to a specific application |
| `code` | Unique code within the application scope |
| `permissionIds` | Optional; permissions to assign to the role |

**Response (201):** `RoleDto`

#### PUT `/api/v1/roles/{id}`

Update a role.

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

> System roles cannot be deleted.

---

### 5.6 Permissions

**Base route:** `/api/v1/permissions`

#### GET `/api/v1/permissions`

List all permissions, optionally filtered by application.

**Permission:** `permissions:read`

**Query parameters:**

| Parameter | Type | Description |
|---|---|---|
| `applicationId` | Guid? | Filter by application |

**Response (200):**

```json
[
  {
    "id": "perm-guid",
    "code": "users:read",
    "name": "Read Users",
    "description": "View user profiles",
    "applicationId": null,
    "parentId": null,
    "level": 3,
    "isWildcard": false,
    "isActive": true
  }
]
```

#### GET `/api/v1/permissions/{id}`

Get a permission by ID.

**Permission:** `permissions:read`

**Response (200):** `PermissionDto`

#### POST `/api/v1/permissions`

Create a new permission.

**Permission:** `permissions:create`

**Request:**

```json
{
  "applicationId": "app-guid",
  "code": "crm:leads:read",
  "name": "Read Leads",
  "description": "View CRM leads",
  "parentId": "parent-perm-guid"
}
```

**Permission code hierarchy:**
- Level 0: `*` (global wildcard)
- Level 1: `crm:*` (application wildcard)
- Level 2: `crm:leads:*` (resource wildcard)
- Level 3: `crm:leads:read` (specific action)

**Response (201):** `PermissionDto`

#### PUT `/api/v1/permissions/{id}`

Update a permission.

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

#### GET `/api/v1/permissions/{id}/implications`

Get permissions implied by a given permission.

**Permission:** `permissions:read`

**Response (200):** `PermissionDto[]`

> Example: `users:manage` might imply `users:read`, meaning anyone with `users:manage` automatically has `users:read`.

#### POST `/api/v1/permissions/{id}/implications`

Add a permission implication.

**Permission:** `permissions:manage`

**Request:**

```json
{
  "impliedPermissionId": "implied-perm-guid"
}
```

**Response:** 201 Created

#### DELETE `/api/v1/permissions/{id}/implications/{impliedId}`

Remove a permission implication.

**Permission:** `permissions:manage`

**Response:** 204 No Content

---

### 5.7 Applications

**Base route:** `/api/v1/applications`

Applications represent the different systems/services that use AuthSystem for identity.

#### GET `/api/v1/applications`

List applications with pagination.

**Permission:** `applications:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | int | 1 | Page number |
| `pageSize` | int | 10 | Items per page |
| `search` | string | null | Search by name or code |
| `isActive` | bool? | null | Filter by active status |

**Response (200):**

```json
{
  "items": [
    {
      "id": "app-guid",
      "code": "CRM",
      "name": "Customer Relationship Manager",
      "description": "CRM application",
      "baseUrl": "https://crm.yourdomain.com",
      "logoUrl": "https://...",
      "contactEmail": "crm@yourdomain.com",
      "isActive": true,
      "allowSelfRegistration": false,
      "requireTwoFactor": false,
      "requireEmailVerification": true,
      "sessionTimeoutMinutes": 60,
      "maxConcurrentSessions": 5
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 3,
  "totalPages": 1
}
```

#### GET `/api/v1/applications/{id}`

Get an application by ID.

**Permission:** `applications:read`

**Response (200):** `ApplicationDto`

#### GET `/api/v1/applications/{id}/roles`

Get all roles scoped to an application.

**Permission:** `applications:read`

**Response (200):** `RoleDto[]`

#### GET `/api/v1/applications/{id}/permissions`

Get all permissions scoped to an application.

**Permission:** `applications:read`

**Response (200):** `PermissionDto[]`

#### POST `/api/v1/applications`

Create a new application.

**Permission:** `applications:create`

**Request:**

```json
{
  "code": "CRM",
  "name": "Customer Relationship Manager",
  "description": "CRM application for managing leads and contacts",
  "baseUrl": "https://crm.yourdomain.com",
  "logoUrl": "https://...",
  "contactEmail": "crm@yourdomain.com",
  "allowSelfRegistration": false,
  "requireTwoFactor": false,
  "requireEmailVerification": true,
  "sessionTimeoutMinutes": 60,
  "maxConcurrentSessions": 5
}
```

**Response (201):** `ApplicationDto`

#### PUT `/api/v1/applications/{id}`

Update an application.

**Permission:** `applications:update`

**Request:** Same fields as create (except `code` which is immutable).

**Response (200):** `ApplicationDto`

#### DELETE `/api/v1/applications/{id}`

Delete an application.

**Permission:** `applications:delete`

**Response:** 204 No Content

---

### 5.8 Organizations

**Base route:** `/api/v1/organizations`

Organizations provide multi-tenancy — users belong to organizations, and organizations subscribe to applications.

#### GET `/api/v1/organizations`

List organizations the authenticated user belongs to.

**Permission:** None (authentication only)

**Response (200):**

```json
[
  {
    "id": "org-guid",
    "code": "acme-corp",
    "name": "Acme Corporation",
    "contactEmail": "admin@acme.com",
    "isActive": true,
    "memberCount": 25,
    "role": "org-owner"
  }
]
```

#### GET `/api/v1/organizations/{id}`

Get organization details.

**Permission:** None (must be a member)

**Response (200):**

```json
{
  "id": "org-guid",
  "code": "acme-corp",
  "name": "Acme Corporation",
  "description": "A leading technology company",
  "logoUrl": "https://...",
  "website": "https://acme.com",
  "contactEmail": "admin@acme.com",
  "ownerId": "user-guid",
  "isActive": true,
  "isAutoCreated": false,
  "createdAt": "2026-01-01T00:00:00Z",
  "memberCount": 25,
  "applicationCount": 3
}
```

#### POST `/api/v1/organizations`

Create a new organization.

**Permission:** None (authentication only)

**Request:**

```json
{
  "code": "acme-corp",
  "name": "Acme Corporation",
  "contactEmail": "admin@acme.com",
  "description": "A leading technology company",
  "logoUrl": "https://...",
  "website": "https://acme.com"
}
```

**Response (201):** `OrganizationDto`

> The creating user becomes the organization owner automatically.

#### PUT `/api/v1/organizations/{id}`

Update organization details.

**Permission:** `org:update`

**Request:**

```json
{
  "name": "Acme Corp International",
  "contactEmail": "global@acme.com",
  "description": "Updated description",
  "logoUrl": "https://...",
  "website": "https://acme.global",
  "isActive": true
}
```

**Response (200):** `OrganizationDto`

#### DELETE `/api/v1/organizations/{id}`

Delete an organization.

**Permission:** None (must be owner)

**Response:** 204 No Content

#### GET `/api/v1/organizations/{id}/members`

List organization members with pagination.

**Permission:** `org:members:read`

**Query parameters:**

| Parameter | Type | Default | Description |
|---|---|---|---|
| `pageNumber` | int | 1 | Page number |
| `pageSize` | int | 10 | Items per page |
| `search` | string | null | Search by name or email |

**Response (200):**

```json
{
  "items": [
    {
      "userId": "user-guid",
      "email": "member@acme.com",
      "displayName": "John Doe",
      "roleId": "role-guid",
      "roleName": "org-admin",
      "joinedAt": "2026-01-15T00:00:00Z",
      "isActive": true
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 25,
  "totalPages": 3
}
```

#### PUT `/api/v1/organizations/{orgId}/members/{userId}/role`

Change a member's organization role.

**Permission:** `org:members:manage`

**Request:**

```json
{
  "roleId": "new-role-guid"
}
```

**Response (200):** `OrganizationMemberDto`

#### DELETE `/api/v1/organizations/{orgId}/members/{userId}`

Remove a member from the organization.

**Permission:** `org:members:manage`

**Response:** 204 No Content

#### GET `/api/v1/organizations/{id}/invitations`

List pending invitations for an organization.

**Permission:** `org:members:read`

**Response (200):**

```json
[
  {
    "id": "invitation-guid",
    "email": "invitee@example.com",
    "roleId": "role-guid",
    "roleName": "org-member",
    "status": "Pending",
    "invitedBy": "user-guid",
    "invitedAt": "2026-03-10T00:00:00Z",
    "expiresAt": "2026-03-17T00:00:00Z"
  }
]
```

#### POST `/api/v1/organizations/{id}/invitations`

Invite a user to join the organization.

**Permission:** `org:members:invite`

**Request:**

```json
{
  "email": "invitee@example.com",
  "roleId": "role-guid"
}
```

**Response (201):** `OrganizationInvitationDto`

#### GET `/api/v1/organizations/{id}/applications`

List applications enabled for the organization.

**Permission:** `org:apps:read`

**Response (200):**

```json
[
  {
    "applicationId": "app-guid",
    "applicationName": "CRM",
    "subscriptionTier": "Enterprise",
    "isActive": true,
    "enabledAt": "2026-01-01T00:00:00Z",
    "expiresAt": "2027-01-01T00:00:00Z"
  }
]
```

#### POST `/api/v1/organizations/{id}/applications`

Enable an application for the organization.

**Permission:** `org:apps:manage`

**Request:**

```json
{
  "applicationId": "app-guid",
  "subscriptionTier": "Enterprise",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**Response (201):** `OrganizationApplicationDto`

#### PUT `/api/v1/organizations/{id}/applications/{applicationId}`

Update an organization's application subscription.

**Permission:** `org:apps:manage`

**Request:**

```json
{
  "subscriptionTier": "Premium",
  "expiresAt": "2027-06-01T00:00:00Z",
  "isActive": true
}
```

**Response (200):** `OrganizationApplicationDto`

#### DELETE `/api/v1/organizations/{id}/applications/{applicationId}`

Disable an application for the organization.

**Permission:** `org:apps:manage`

**Response:** 204 No Content

#### POST `/api/v1/organizations/{orgId}/members/{userId}/roles`

Assign an application-specific role to a member within the organization context.

**Permission:** `org:permissions:manage`

**Request:**

```json
{
  "applicationId": "app-guid",
  "roleId": "role-guid",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**Response (201):** `OrganizationMemberAppRoleDto`

#### POST `/api/v1/organizations/{orgId}/members/{userId}/permissions`

Grant a permission to a member within the organization context.

**Permission:** `org:permissions:manage`

**Request:**

```json
{
  "applicationId": "app-guid",
  "permissionId": "perm-guid",
  "expiresAt": "2027-01-01T00:00:00Z"
}
```

**Response (201):** `OrganizationMemberPermissionDto`

---

### 5.9 Invitations

**Base route:** `/api/v1/invitations`

#### POST `/api/v1/invitations/{token}/accept`

Accept an organization invitation using the invitation token.

**Auth:** Authenticated

**Response (200):**

```json
{
  "organizationId": "org-guid",
  "organizationName": "Acme Corporation",
  "role": "org-member"
}
```

---

### 5.10 API Keys

**Base route:** `/api/v1/apikeys`

API keys provide programmatic access for applications and services.

#### GET `/api/v1/apikeys`

List API keys for an application.

**Permission:** `apikeys:read`

**Query parameters:**

| Parameter | Type | Description |
|---|---|---|
| `applicationId` | Guid | Filter by application (required) |

**Response (200):**

```json
[
  {
    "id": "key-guid",
    "applicationId": "app-guid",
    "name": "Production API Key",
    "description": "Main production key",
    "keyPrefix": "ak_prod_",
    "environment": "production",
    "rateLimitPerMinute": 100,
    "rateLimitPerDay": 10000,
    "createdAt": "2026-01-01T00:00:00Z",
    "expiresAt": "2027-01-01T00:00:00Z",
    "lastUsedAt": "2026-03-12T14:30:00Z",
    "isRevoked": false
  }
]
```

#### POST `/api/v1/apikeys`

Create a new API key.

**Permission:** `apikeys:create`

**Request:**

```json
{
  "applicationId": "app-guid",
  "name": "Production API Key",
  "description": "Main production key for CRM integration",
  "environment": "production",
  "rateLimitPerMinute": 100,
  "rateLimitPerDay": 10000,
  "expiresAt": "2027-01-01T00:00:00Z",
  "permissionIds": ["perm-guid-1", "perm-guid-2"]
}
```

| `environment` values | Description |
|---|---|
| `production` | Production environment |
| `staging` | Staging/testing environment |
| `development` | Development environment |

**Response (201):**

```json
{
  "id": "key-guid",
  "apiKey": "ak_prod_AbCdEfGhIjKlMnOpQrStUvWxYz...",
  "message": "Store this API key securely. It will not be shown again."
}
```

> **Important:** The plain-text API key is returned only once at creation. It is stored as an Argon2id hash in the database.

#### POST `/api/v1/apikeys/{id}/revoke`

Revoke an API key.

**Permission:** `apikeys:revoke`

**Request:**

```json
{
  "reason": "Compromised key detected"
}
```

**Response:** 204 No Content

#### POST `/api/v1/apikeys/{id}/rotate`

Rotate an API key (create new, schedule old for revocation).

**Permission:** `apikeys:rotate`

**Request:**

```json
{
  "gracePeriodMinutes": 60
}
```

| Field | Description |
|---|---|
| `gracePeriodMinutes` | Time before the old key is automatically revoked (default: 60) |

**Response (200):**

```json
{
  "newApiKey": "ak_prod_NewKeyValue...",
  "oldKeyExpiresAt": "2026-03-12T15:30:00Z",
  "message": "New key generated. Old key will be revoked after the grace period."
}
```

---

### 5.11 Audit Logs

**Base route:** `/api/v1/audit-logs`

Comprehensive audit trail for all system operations.

#### GET `/api/v1/audit-logs`

Query audit logs with filters.

**Permission:** `auditlogs:read`

**Query parameters:**

| Parameter | Type | Description |
|---|---|---|
| `pageNumber` | int | Page number (default: 1) |
| `pageSize` | int | Items per page (default: 10) |
| `userId` | Guid? | Filter by user |
| `applicationId` | Guid? | Filter by application |
| `actionType` | string? | Filter by action type (e.g., "Authentication", "UserManagement") |
| `action` | string? | Filter by specific action (e.g., "user.login", "password.changed") |
| `fromDate` | DateTime? | Start date filter |
| `toDate` | DateTime? | End date filter |
| `isSuccess` | bool? | Filter by success/failure |

**Response (200):**

```json
{
  "items": [
    {
      "id": "log-guid",
      "userId": "user-guid",
      "applicationId": null,
      "action": "user.login",
      "actionType": "Authentication",
      "entityType": "User",
      "entityId": "user-guid",
      "ipAddress": "192.168.1.1",
      "userAgent": "Mozilla/5.0...",
      "isSuccess": true,
      "timestamp": "2026-03-12T14:30:00Z"
    }
  ],
  "pageNumber": 1,
  "pageSize": 10,
  "totalCount": 1500,
  "totalPages": 150
}
```

#### GET `/api/v1/audit-logs/{id}`

Get a specific audit log entry with full details.

**Permission:** `auditlogs:read`

**Response (200):**

```json
{
  "id": "log-guid",
  "userId": "user-guid",
  "action": "user.updated",
  "entityType": "User",
  "entityId": "target-user-guid",
  "oldValues": "{\"firstName\": \"John\"}",
  "newValues": "{\"firstName\": \"Jonathan\"}",
  "ipAddress": "192.168.1.1",
  "userAgent": "Mozilla/5.0...",
  "isSuccess": true,
  "timestamp": "2026-03-12T14:30:00Z",
  "correlationId": "abc-123"
}
```

#### GET `/api/v1/audit-logs/users/{userId}`

Get audit logs for a specific user.

**Permission:** `auditlogs:read`

**Query parameters:** `pageNumber`, `pageSize`, `fromDate`, `toDate`

**Response (200):** `PagedAuditLogsDto`

#### GET `/api/v1/audit-logs/entities/{entityType}/{entityId}`

Get audit logs for a specific entity (e.g., all changes to a specific role).

**Permission:** `auditlogs:read`

**Response (200):** `AuditLogDto[]`

#### POST `/api/v1/audit-logs/export`

Export audit logs to a file.

**Permission:** `auditlogs:export`

**Request:**

```json
{
  "format": "csv",
  "userId": null,
  "applicationId": null,
  "actionType": "Authentication",
  "action": null,
  "fromDate": "2026-01-01T00:00:00Z",
  "toDate": "2026-03-12T23:59:59Z",
  "isSuccess": null,
  "maxRecords": 10000
}
```

| `format` values | Description |
|---|---|
| `csv` | Comma-separated values file |
| `json` | JSON array file |

**Response:** File download (Content-Type: `text/csv` or `application/json`)

---

### 5.12 Secrets (Admin)

**Base route:** `/api/v1/admin/secrets`

**Prerequisites:** `SecretManagement:EnableAdminApi` must be `true` in configuration.

All endpoints require `secrets.manage` permission.

#### GET `/api/v1/admin/secrets/status`

Get the status of all system secrets (no values exposed).

**Permission:** `secrets.manage`

**Response (200):**

```json
{
  "rsaKeyConfigured": true,
  "hmacKeyConfigured": true,
  "gatewayTokenConfigured": true,
  "smtpPasswordConfigured": false,
  "customSecrets": ["Custom:ApiIntegrationKey"],
  "secretsFilePath": "C:\\Users\\...\\secrets.dpapi",
  "lastModified": "2026-03-01T10:00:00Z"
}
```

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

## 6. Common Workflows

### 6.1 Register → Verify Email → Login

```
Step 1: Register
POST /api/v1/auth/register
Body: { email, password, firstName, lastName }
→ 201: { userId, maskedEmail }

Step 2: Send Verification Email (if email service enabled)
POST /api/v1/auth/send-verification-email
Header: Authorization: Bearer <access_token>
→ 200: { expiresAt, maskedEmail }

Step 3: Verify Email with OTP
POST /api/v1/auth/verify-email
Body: { userId, otp: "123456" }
→ 204

Step 4: Login
POST /api/v1/auth/login
Body: { email, password }
→ 200: { token: { accessToken, refreshToken }, user: {...} }
```

### 6.2 Enable Two-Factor Authentication

```
Step 1: Setup (get TOTP secret)
POST /api/v1/auth/2fa/setup
Header: Authorization: Bearer <access_token>
→ 200: { secret, qrCodeUri }

Step 2: Scan QR code with authenticator app

Step 3: Enable with verification code
POST /api/v1/auth/2fa/enable
Header: Authorization: Bearer <access_token>
Body: { code: "123456" }
→ 200: { recoveryCodes: [...] }

Step 4: Save recovery codes securely
```

### 6.3 Forgot Password → Reset

```
Step 1: Request password reset
POST /api/v1/auth/forgot-password
Body: { email: "user@example.com" }
→ 200: { message, maskedEmail }

Step 2: User receives email with reset token

Step 3: Reset password
POST /api/v1/auth/reset-password
Body: { email, token, newPassword, confirmNewPassword, terminateSessions: true }
→ 204
```

### 6.4 Invite User to Organization

```
Step 1: Send invitation
POST /api/v1/organizations/{orgId}/invitations
Header: Authorization: Bearer <admin_token>
Body: { email: "invitee@example.com", roleId: "member-role-guid" }
→ 201: { invitationId, token, expiresAt }

Step 2: Invitee receives email with invitation link/token

Step 3: Invitee accepts invitation
POST /api/v1/invitations/{token}/accept
Header: Authorization: Bearer <invitee_token>
→ 200: { organizationId, organizationName, role }
```

### 6.5 Set Up Application with Roles and Permissions

```
Step 1: Create application
POST /api/v1/applications
Body: { code: "CRM", name: "CRM System", ... }
→ 201: { id: "app-guid", ... }

Step 2: Create permissions for the application
POST /api/v1/permissions
Body: { applicationId: "app-guid", code: "crm:leads:read", name: "Read Leads" }
POST /api/v1/permissions
Body: { applicationId: "app-guid", code: "crm:leads:create", name: "Create Leads" }
POST /api/v1/permissions
Body: { applicationId: "app-guid", code: "crm:leads:*", name: "All Lead Operations" }

Step 3: Create roles with permissions
POST /api/v1/roles
Body: { applicationId: "app-guid", code: "crm-viewer", name: "CRM Viewer", permissionIds: ["read-perm-guid"] }
POST /api/v1/roles
Body: { applicationId: "app-guid", code: "crm-editor", name: "CRM Editor", permissionIds: ["read-guid", "create-guid"] }

Step 4: Assign role to user
POST /api/v1/users/{userId}/roles
Body: { roleId: "crm-editor-guid" }
→ 204
```

### 6.6 API Key Rotation

```
Step 1: Rotate the key (both old and new are valid during grace period)
POST /api/v1/apikeys/{keyId}/rotate
Body: { gracePeriodMinutes: 120 }
→ 200: { newApiKey: "ak_prod_...", oldKeyExpiresAt: "..." }

Step 2: Update all consumers to use the new key

Step 3: Old key is automatically revoked after the grace period
```

---

## 7. Database Schema Overview

The database contains **26 tables** organized into 4 categories:

### Core Tables (8)

| Table | Purpose |
|---|---|
| `Users` | User accounts with profile, status, lockout, and audit fields |
| `Applications` | Registered applications/services using AuthSystem |
| `Roles` | Roles scoped to applications (null = global) |
| `Permissions` | Hierarchical permissions with wildcard support |
| `UserRoles` | User-to-role assignments |
| `RolePermissions` | Role-to-permission mappings |
| `UserPermissions` | Direct user-to-permission grants |
| `PermissionImplications` | Permission hierarchy/inheritance relationships |

### Authentication Tables (5)

| Table | Purpose |
|---|---|
| `RefreshTokens` | Refresh tokens (HMAC-SHA256 hashed) with rotation tracking |
| `UserSessions` | Active sessions with device info and activity tracking |
| `LoginAttempts` | Failed login attempt tracking for lockout |
| `UserExternalLogins` | External provider links (Google, etc.) |
| `ExternalAuthProviders` | Provider configuration (Google, Apple, Facebook, etc.) |

### Organization Tables (6)

| Table | Purpose |
|---|---|
| `Organizations` | Organization/tenant records |
| `OrganizationUsers` | Membership with org-level roles |
| `OrganizationInvitations` | Time-limited invitations with status tracking |
| `OrganizationApplications` | Application subscriptions per organization |
| `OrganizationUserRoles` | App-specific roles within organization context |
| `OrganizationUserPermissions` | App-specific permissions within organization context |

### Security Tables (7)

| Table | Purpose |
|---|---|
| `ApiKeys` | API keys (Argon2id hashed) with rate limits and IP/origin restrictions |
| `ApiKeyScopes` | Permission scopes granted to API keys |
| `TwoFactorAuth` | TOTP secrets and recovery codes per user |
| `AuditLogs` | Comprehensive audit trail for all operations |
| `PasswordHistory` | Previous password hashes to prevent reuse |
| `EmailVerificationTokens` | Time-limited email verification tokens |
| `PasswordResetTokens` | Time-limited password reset tokens |

### Stored Procedures

Organized by domain:
- **Authentication:** `sp_ValidateCredentials`, `sp_CreateRefreshToken`, `sp_ValidateRefreshToken`, `sp_RevokeRefreshToken`, `sp_RevokeAllUserTokens`, `sp_CheckAccountLockout`, `sp_RecordLoginAttempt`
- **Users:** `sp_GetUserById`, `sp_GetUserByEmail`
- **Authorization, Roles, Permissions, Applications, ApiKeys, Audit, TwoFactor** — additional stored procedures per domain

---

## 8. Security Best Practices

The following security measures are implemented throughout the system:

### Password Security
- **Argon2id** hashing with OWASP 2024 recommended parameters (19 MB memory, 2 iterations, 1 thread)
- Minimum 12-character passwords with complexity requirements (uppercase, lowercase, digit, special character)
- Password history tracking (prevents reuse of last 5 passwords)
- Password expiration (configurable, default 90 days)
- Account lockout after 5 failed attempts (15-minute lockout)

### Token Security
- **RS256 asymmetric JWT signing** — external services validate tokens without knowing the private key
- Short-lived access tokens (15 minutes) reduce the window of compromise
- Refresh token rotation — each refresh generates a new refresh token, old one is revoked
- JWT blacklisting — revoked access tokens are immediately rejected via middleware
- Refresh tokens stored as HMAC-SHA256 hashes (never in plaintext)
- API keys stored as Argon2id hashes (never in plaintext)

### Encryption at Rest
- **Windows DPAPI** encrypts all secrets (RSA private key, HMAC key, gateway token, SMTP password)
- Machine-bound encryption — secrets can only be decrypted on the same machine
- Secrets file stored outside the application directory

### Transport Security
- HTTPS enforced in production via HSTS (365 days, includeSubDomains, preload)
- TLS encryption for SQL Server connections

### Request Security
- **Gateway token validation** with constant-time comparison (prevents timing attacks)
- **Rate limiting** at both gateway and API level (auth endpoints: 5 req/60s)
- **CORS** with explicit origin whitelisting in production
- **OWASP security headers**: X-Frame-Options (DENY), X-Content-Type-Options (nosniff), CSP, Referrer-Policy, Permissions-Policy
- Server and X-Powered-By headers removed

### Authorization
- Permission-based access control (not just role-based)
- Wildcard permission matching for hierarchical access
- Permission implications for inheritance
- JWT claims-based authorization (no database lookup per request)

### Audit & Monitoring
- Comprehensive audit logging for all operations (who, what, when, where)
- Structured logging with correlation IDs for request tracing
- IP address and User-Agent tracking
- Old/new value tracking for change auditing

---

## 9. Testing

### Test Stack

| Package | Purpose |
|---|---|
| **xUnit** | Test framework |
| **Moq** | Mocking library |
| **FluentAssertions** | Readable assertion syntax |
| **coverlet** | Code coverage collection |

### Running Tests

```bash
dotnet test Auth/Auth_API.Tests
```

With coverage:

```bash
dotnet test Auth/Auth_API.Tests --collect:"XPlat Code Coverage"
```

### Postman Collection

A complete Postman collection is available at:
```
Auth/Auth_API/Postman/AuthSystem.postman_collection.json
```

**Features:**
- Pre-configured variables (`baseUrl`, `accessToken`, `refreshToken`, etc.)
- Auto-populating test scripts (login response populates `accessToken` variable)
- All endpoints organized by module
- Base URL: `http://localhost:5000`

To use: Import the collection into Postman and update the `baseUrl` variable if needed.

---

## 10. Troubleshooting

### Connection String Errors

**Symptom:** `SqlException: Cannot open database "AuthDB"`

**Fix:** Verify `ConnectionStrings:AuthDb` in `appsettings.json` points to your SQL Server instance. For local development with Windows Auth:

```json
"Server=.\\SQLEXPRESS;Database=AuthDB;Trusted_Connection=True;TrustServerCertificate=True"
```

### DPAPI Errors

**Symptom:** `CryptographicException: The data protection operation was unsuccessful`

**Causes:**
- Running on a non-Windows OS (DPAPI is Windows-only)
- The secrets file was created by a different Windows user account
- The Data Protection key ring directory is missing or inaccessible

**Fix:** Ensure `%LOCALAPPDATA%/AuthSystem/Keys` exists and is writable. If migrating between machines, regenerate secrets.

### Gateway Token Mismatch

**Symptom:** `403 Forbidden` on all requests through the gateway

**Fix:** Both Auth_API and API_Gateway must share the same DPAPI secrets file. Ensure:
1. Both services are configured to use the same `SecretsFilePath`
2. Both services are running under the same Windows user account
3. Restart both services after secret regeneration

### CORS Errors

**Symptom:** Browser requests fail with CORS policy errors

**Fix:**
- Development: Ensure `appsettings.Development.json` has `"AllowedOrigins": ["*"]`
- Production: Add your frontend origin explicitly to `Cors:AllowedOrigins`

### Port Conflicts

**Default ports:**
- Auth_API: `http://localhost:5100`, `https://localhost:5101`
- API_Gateway: `http://localhost:5034`, `https://localhost:7159`

If ports are in use, modify `Properties/launchSettings.json` in the respective project.

### JWT Token Expired Header

When a JWT token expires, the API returns a `Token-Expired: true` header alongside the 401 response. Use this to trigger a token refresh flow in your client.

### Email Service Not Sending

Ensure `Email:Enabled` is `true` and SMTP credentials are configured. The SMTP password should be set via DPAPI secrets (not in appsettings.json).

---

## 11. Permission Matrix

Complete list of all permissions used across the system:

| Permission Code | Controller | Actions |
|---|---|---|
| `users:read` | UsersController | List users, get user, get user roles/permissions |
| `users:create` | UsersController | Create user |
| `users:update` | UsersController | Update user profile |
| `users:delete` | UsersController | Delete user |
| `users:manage` | UsersController | Lock, unlock, activate, deactivate accounts |
| `users:manage-roles` | UsersController | Assign and remove user roles |
| `users:manage-permissions` | UsersController | Grant and revoke user permissions |
| `roles:read` | RolesController | List and get roles |
| `roles:create` | RolesController | Create roles |
| `roles:update` | RolesController | Update roles |
| `roles:delete` | RolesController | Delete roles |
| `permissions:read` | PermissionsController | List and get permissions, view implications |
| `permissions:create` | PermissionsController | Create permissions |
| `permissions:update` | PermissionsController | Update permissions |
| `permissions:delete` | PermissionsController | Delete permissions |
| `permissions:manage` | PermissionsController | Add and remove permission implications |
| `applications:read` | ApplicationsController | List apps, get app details, roles, permissions |
| `applications:create` | ApplicationsController | Create applications |
| `applications:update` | ApplicationsController | Update applications |
| `applications:delete` | ApplicationsController | Delete applications |
| `apikeys:read` | ApiKeysController | List API keys |
| `apikeys:create` | ApiKeysController | Create API keys |
| `apikeys:revoke` | ApiKeysController | Revoke API keys |
| `apikeys:rotate` | ApiKeysController | Rotate API keys |
| `auditlogs:read` | AuditLogsController | Query and view audit logs |
| `auditlogs:export` | AuditLogsController | Export audit logs to file |
| `org:update` | OrganizationsController | Update organization details |
| `org:members:read` | OrganizationsController | List members and invitations |
| `org:members:manage` | OrganizationsController | Update member roles, remove members |
| `org:members:invite` | OrganizationsController | Send organization invitations |
| `org:apps:read` | OrganizationsController | List organization applications |
| `org:apps:manage` | OrganizationsController | Enable, update, disable organization applications |
| `org:permissions:manage` | OrganizationsController | Assign app roles and grant permissions to members |
| `secrets.manage` | SecretsController | View status, generate keys, manage custom secrets |

---

*This guide covers AuthSystem v1.0 running on .NET 10. For updates, refer to the repository changelog and Postman collection.*
