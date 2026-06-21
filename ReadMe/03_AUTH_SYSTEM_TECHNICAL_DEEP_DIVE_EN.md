# AuthSystem — Technical Deep Dive

## Architecture, Implementation & Operations Guide

---

## Table of Contents

1. [System Architecture](#1-system-architecture)
2. [Security Implementation](#2-security-implementation)
3. [Performance Architecture](#3-performance-architecture)
4. [API Design & Patterns](#4-api-design--patterns)
5. [Design Principles](#5-design-principles)
6. [Deployment & Operations](#6-deployment--operations)
7. [Monitoring & Observability](#7-monitoring--observability)
8. [Disaster Recovery & Business Continuity](#8-disaster-recovery--business-continuity)
9. [Integration Capabilities](#9-integration-capabilities)
10. [Secret Management & Key Rotation](#10-secret-management--key-rotation)
11. [Testing Strategy](#11-testing-strategy)
12. [Configuration Reference](#12-configuration-reference)
13. [API Endpoints Reference](#13-api-endpoints-reference)

---

## 1. System Architecture

### Layered Architecture

AuthSystem follows a strict layered architecture where dependencies flow in one direction only — outer layers depend on inner layers, never the reverse.

```
+-------------------------------------------------------------+
|                     LAYER 4: DATABASE                        |
|           (Most protected, farthest from users)              |
|     +------------------------------------------------+      |
|     |              SQL Server                         |      |
|     |    [Users] [Roles] [Permissions] [Audit]       |      |
|     +------------------------+-----------------------+      |
+------------------------------+-------------------------------+
                               |
+------------------------------+-------------------------------+
|                              v     LAYER 3: DATA ACCESS      |
|     +------------------------------------------------+      |
|     |              Repositories (Dapper)              |      |
|     |    [UserRepo] [RoleRepo] [AuditRepo]           |      |
|     +------------------------+-----------------------+      |
+------------------------------+-------------------------------+
                               |
+------------------------------+-------------------------------+
|                              v     LAYER 2: BUSINESS LOGIC   |
|     +------------------------------------------------+      |
|     |              Services                           |      |
|     |    [AuthService] [UserService] [RoleService]   |      |
|     +------------------------+-----------------------+      |
+------------------------------+-------------------------------+
                               |
+------------------------------+-------------------------------+
|                              v     LAYER 1: API              |
|     +------------------------------------------------+      |
|     |              Auth API (Controllers)             |      |
|     |    [Login] [Users] [Roles] [Permissions]       |      |
|     +------------------------+-----------------------+      |
+------------------------------+-------------------------------+
                               |
+------------------------------+-------------------------------+
|                              v     LAYER 0: PRESENTATION     |
|               (Entry point for all traffic)                  |
|     +-----------------+    +-----------------+               |
|     |   Admin UI      |    |   API Gateway   |               |
|     |   (Blazor)      |    |   (YARP)        |               |
|     +-----------------+    +-----------------+               |
+--------------------------------------------------------------+
```

### Layer Responsibilities

| Layer | Responsibility | Rules |
|-------|---------------|-------|
| **Presentation** | UI rendering, user interaction | Calls API only; no business logic |
| **API** | Request routing, input validation, response formatting | Calls Services only; no database access |
| **Services** | Business logic, orchestration, transaction management | Calls Repositories only; contains all business rules |
| **Repositories** | Database access, query execution | Calls Database only; no business logic |
| **Database** | Data storage and retrieval | Accessed only through Repositories |

### Dependency Rule

```
Database <- Repositories <- Services <- API <- Presentation
                     (dependencies flow inward only)
```

Each layer communicates only with the layer directly above it. The presentation layer can never reach the database directly.

**Why this matters:** If you need to change the database (e.g., from SQL Server to PostgreSQL), you only change the Repository layer. Nothing else in the system needs to know.

### Request Flow Pipeline

Every request flows through a clean pipeline where each stage has a single responsibility:

```
+-----------+    +------------+    +-----------+    +------------+
|  Request  | -> | Controller | -> |  Service  | -> | Repository |
|  (Input)  |    | (Validate) |    | (Logic)   |    | (Database) |
+-----------+    +------------+    +-----------+    +------------+
                                                          |
                                                          v
+-----------+    +------------+    +-----------+    +------------+
| Response  | <- | Controller | <- |  Service  | <- |   Data     |
| (Output)  |    | (Format)   |    | (Process) |    | (Retrieve) |
+-----------+    +------------+    +-----------+    +------------+
```

- **Controller**: Only handles HTTP concerns — validates input and formats output. No business logic.
- **Service**: Contains all business rules — the "brain" of the operation.
- **Repository**: Only talks to the database. No business logic. The single authorized gateway to data.

---

## 2. Security Implementation

### Password Hashing: Argon2id

AuthSystem uses **Argon2id exclusively** — the algorithm recommended by OWASP (2024) and winner of the international Password Hashing Competition (2015).

#### Why Argon2id Over Alternatives?

| Algorithm | Status | Key Limitation |
|-----------|--------|----------------|
| MD5 | **Never use** | Crackable in seconds |
| SHA-256 | **Not for passwords** | Too fast — no brute-force resistance |
| bcrypt | **Secure but surpassed** | CPU-only — does not resist GPU-based attacks as effectively |
| PBKDF2 | **Secure but surpassed** | Lacks memory-hardness; weaker against modern hardware |
| **Argon2id** | **Current gold standard** | No practical attacks known when properly configured |

> Argon2id combines the best properties of Argon2i (resistance to side-channel attacks) and Argon2d (resistance to GPU cracking). It requires significant memory per attempt, making mass password-guessing attacks economically impractical.

#### Configuration (OWASP 2024 Recommended)

| Parameter | Value | Purpose |
|-----------|-------|---------|
| Memory | 19 MiB (19,456 KB) | Makes each attempt expensive in memory |
| Iterations | 2 | Number of passes through memory |
| Parallelism | 1 | Threads per hash operation |
| Salt | 16 bytes (cryptographically random) | Unique per password — prevents rainbow table attacks |
| Hash length | 32 bytes | Output hash size |

**Stored format:** `$argon2id$v=19$m=19456,t=2,p=1$[salt]$[hash]`

**Rehashing:** The system automatically detects when stored hashes need rehashing due to configuration changes and upgrades them on next successful login.

#### Implementation Details

- **Library:** Konscious.Security.Cryptography.Argon2 v1.3.1
- **Timing-safe comparison:** Uses fixed-time comparison to prevent timing attacks
- **Password history:** Last 3 passwords stored (hashed) to prevent reuse

#### Optional Hardening: Pepper & Breached-Password Screening

Two opt-in layers complement Argon2id (both default off, configured under `Password`):

- **Pepper** (`Password:Pepper`) — a server-side secret (Argon2id `KnownSecret`) mixed into every hash and held in the secret store, never the database. It defends a database-only breach: without the pepper, stolen hashes resist brute force. Unpeppered hashes upgrade transparently on next login; the key material is backed up like the JWT/HMAC keys.
- **Breached-password screening** (`Password:BreachedPasswordCheck`) — checks new passwords against the HIBP Pwned Passwords range API with k-anonymity (only a SHA-1 prefix leaves the server). `Enforce` rejects breached passwords; `Warn` allows them but returns an `X-Password-Warning` header. `FailOpen` controls behaviour when HIBP is unreachable.

### Token Authentication: JWT RS256

AuthSystem uses asymmetric JWT signing (RS256) for token-based authentication.

| Token Type | Contents | Lifetime |
|------------|----------|----------|
| **Access Token** | User ID, roles, permissions | 15 minutes |
| **Refresh Token** | Session identifier | 7 days |

**Why RS256 (asymmetric) over HS256 (symmetric)?**

```
Symmetric (HS256):
  Same key to sign AND verify = must share secret with all verifiers

Asymmetric (RS256):
  Private key: Signs tokens (kept secret on auth server)
  Public key: Verifies tokens (safely distributed to any service)
  Result: Other services verify tokens without knowing the signing secret
```

**Token lifecycle:**
1. User authenticates → receives Access Token + Refresh Token
2. Access Token used for API requests (15-minute window)
3. When Access Token expires → Refresh Token used to obtain new pair
4. On logout or security event → tokens added to blacklist
5. Token blacklist checked on every request before granting access

### Key Management

- **Storage modes** (`SecretManagement:StorageMode`): **PlainText** (keys in `appsettings.Production.json`, cross-platform), **Certificate** (keys encrypted in `secrets.dpapi`, protected by an X.509 cert you own — portable across servers), or **Dpapi** (Windows machine-bound encryption). Certificate is recommended for shared hosting; PlainText is the default and the only cross-platform option (Linux/cPanel).
- **Key ring location:** `DataProtection:KeyPath` (defaults to `%ProgramData%/AuthSystem/Keys`; point the Auth API and Gateway at the same folder so they share one ring)
- **Auto-generation:** RSA, HMAC, and gateway token auto-generate on first startup; set `AutoGenerateKeys: false` afterwards so missing secrets fail loudly instead of silently regenerating
- **Bring-your-own-keys (BYOK):** import your own RSA/HMAC/gateway material via the admin secrets API (Certificate/Dpapi) or `appsettings.Production.json` (PlainText) — enables zero-logout server migration
- **Rotation:** key rotation supported without invalidating existing tokens (multiple active keys)

### Rate Limiting

| Endpoint | Limit | Window | Purpose |
|----------|-------|--------|---------|
| **Login** | 5 requests | 60 seconds | Ensures only legitimate login attempts succeed |
| **Auth endpoints** | 20 requests | 60 seconds | Protects authentication operations |
| **General API** | 100 requests | 60 seconds | Maintains service availability |
| **Gateway (global)** | 1,000 requests | 60 seconds | System-wide traffic management |

When a limit is exceeded, the server responds with HTTP 429 (Too Many Requests) and a `Retry-After` header indicating when the client can retry.

### Security Headers (OWASP Compliance)

Every response includes protective headers:

| Header | Value | Protects Against |
|--------|-------|-----------------|
| X-Frame-Options | DENY | Clickjacking attacks |
| X-Content-Type-Options | nosniff | MIME type sniffing |
| X-XSS-Protection | 1; mode=block | Cross-Site Scripting (XSS) |
| Content-Security-Policy | default-src 'self' | Script injection |
| Strict-Transport-Security | max-age=31536000 | Protocol downgrade to HTTP |

### Account Protection

| Protection | Value |
|------------|-------|
| Failed login attempts before lockout | 5 attempts |
| Lockout duration | 15 minutes |
| Password history (no reuse) | Last 3 passwords |
| Minimum password length | 8 characters (12+ recommended) |
| Session idle timeout | 30 minutes |
| Max concurrent sessions | 5 per user |
| Two-Factor Authentication | TOTP with backup codes |

### Vulnerabilities Mitigated

| Vulnerability | How AuthSystem Prevents It |
|---------------|---------------------------|
| **SQL Injection** | Parameterized queries via Dapper — user input never treated as SQL code |
| **XSS** | Content Security Policy headers and input encoding |
| **CSRF** | SameSite cookies and CSRF tokens |
| **Brute Force** | Rate limiting (5 attempts/60s) and automatic account lockout |
| **Session Hijacking** | Secure cookies (HttpOnly, Secure flags) and token rotation |
| **Man-in-the-Middle** | HTTPS-only with HSTS enforcement |

---

## 3. Performance Architecture

AuthSystem achieves high performance through four key design decisions:

### 1. Dapper: Direct SQL Execution

Instead of using a heavy ORM that generates SQL automatically, AuthSystem uses **Dapper** — a micro-ORM that executes hand-written, optimized SQL directly.

| Aspect | Full ORM (e.g., Entity Framework) | Dapper (Micro-ORM) |
|--------|-----------------------------------|---------------------|
| Query execution | Auto-generated SQL, may be inefficient | Hand-written, optimized SQL |
| Memory overhead | Higher (object tracking, change detection) | Minimal (no tracking) |
| SQL control | Abstracted away | Full control |
| Complexity | Higher abstraction | Direct and transparent |

> Performance benchmarks vary by environment and query complexity. Dapper's advantage is most significant in read-heavy workloads with indexed tables.

### 2. Async/Await: Non-Blocking Operations

Every database call is asynchronous with **CancellationToken** support. The server does not wait for one operation to complete before starting another — multiple requests are processed concurrently.

### 3. Connection Pooling

Database connections are reused from a pool rather than created fresh per request. This eliminates the overhead of establishing new connections (typically 50ms each), resulting in near-zero connection latency.

### 4. In-Memory Caching

Frequently accessed data is cached in memory:

| Data | Cache Strategy |
|------|----------------|
| Token Blacklist | In-memory (checked on every request) |
| Permission lookups | Per-request memoization |
| Configuration | Loaded at startup |

---

## 4. API Design & Patterns

### RESTful Design

AuthSystem follows REST principles:

| Principle | Implementation |
|-----------|---------------|
| **Resources as nouns** | `/users`, `/roles` (not `/getUsers`) |
| **HTTP verbs for actions** | GET (read), POST (create), PUT (update), DELETE (remove) |
| **Stateless** | Each request carries all needed information (JWT token) |
| **Consistent URLs** | `/api/v1/{resource}/{id}/{sub-resource}` |

### Single Responsibility Per Endpoint

```
GET  /api/v1/users         -> List users
GET  /api/v1/users/{id}    -> Get one user
POST /api/v1/users         -> Create user
PUT  /api/v1/users/{id}    -> Update user
```

### Database Access Isolation

**Only Repositories can access the database.**

```
CORRECT:
Controller -> Service -> Repository -> Database

WRONG:
Controller -> Database (bypassing layers)
Service -> Database (bypassing Repository)
```

---

## 5. Design Principles

### DRY (Don't Repeat Yourself)

Shared logic is centralized in reusable services. Example: password hashing logic exists in one place (`Argon2PasswordHasher`), used by both login and registration flows. A bug fix in one place fixes it everywhere.

### SOLID Principles

| Principle | Application in AuthSystem |
|-----------|--------------------------|
| **Single Responsibility** | Each service/controller/repository has one job |
| **Open/Closed** | New permission types or authentication methods can be added without modifying existing code |
| **Liskov Substitution** | Repository interfaces are interchangeable (e.g., swap SQL Server for PostgreSQL) |
| **Interface Segregation** | Separate read/write repository interfaces — consumers only depend on what they use |
| **Dependency Inversion** | All services depend on interfaces (abstractions), not concrete implementations; wired via DI |

---

## 6. Deployment & Operations

> For step-by-step production deployment (storage modes, IIS / shared hosting, the API Gateway, and BYOK migration), see **PRODUCTION_DEPLOYMENT_GUIDE.md**.

### Technology Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| **.NET Runtime** | .NET 10.0 | .NET 10.0 |
| **SQL Server** | SQL Server 2019 | SQL Server 2022 |
| **RAM** | 2 GB | 4 GB+ |
| **CPU** | 2 cores | 4 cores+ |
| **Disk** | 10 GB | 50 GB+ (depends on audit log volume) |
| **OS** | Windows Server 2019 / Linux | Windows Server 2022 / Linux |

### Deployment Options

| Method | Description | Best For |
|--------|-------------|----------|
| **IIS** | Deploy as ASP.NET application on IIS | Windows Server environments |
| **Docker** | Containerized deployment | Cloud-native, microservices |
| **Kubernetes** | Orchestrated container deployment | High availability, auto-scaling |
| **Azure App Service** | PaaS deployment on Azure | Managed infrastructure |

### Horizontal Scaling

```
                              +--------------+
                              | AuthSystem 1 |
                              +--------------+
+--------------+              | AuthSystem 2 |
| Load         | -----------> +--------------+
| Balancer     |              | AuthSystem 3 |
+--------------+              +------+-------+
                                     |
                              +--------------+
                              |  Database    |
                              +--------------+
```

**Why it scales:**
- **Stateless API**: Any instance can handle any request
- **JWT Tokens**: No server-side session storage needed
- **Database indexing**: Consistent query performance at any scale
- **Connection pooling**: Efficient resource utilization

### Database Migrations

- Schema changes managed through versioned SQL migration scripts
- Migrations run as part of the deployment pipeline
- Rollback scripts provided for each migration

### API Versioning

- URL-based versioning: `/api/v1/`, `/api/v2/`
- Breaking changes introduced in new versions only
- Previous versions maintained during transition period

---

## 7. Monitoring & Observability

### Structured Logging (Serilog)

All components use Serilog for structured, queryable logging:

| Configuration | Auth API | API Gateway |
|---------------|----------|-------------|
| **Output format** | JSON (structured) | JSON (structured) |
| **Sinks** | Console + File | Console + File |
| **Rolling interval** | Daily | Daily |
| **Retention** | 30 days | 90 days |
| **Enrichment** | LogContext, MachineName, ThreadId | LogContext, MachineName, ThreadId |
| **Log path** | `Logs/auth-api-{date}.log` | `Logs/api-gateway-{date}.log` |

### Health Checks

| Endpoint | Checks | Purpose |
|----------|--------|---------|
| `/health` | Application alive | Load balancer health probe |
| Database connectivity | SQL Server reachable | Infrastructure status |

### Recommended APM Integration

AuthSystem's structured logging is compatible with:
- **Application Insights** (Azure)
- **Elastic Stack** (ELK)
- **Seq** (structured log server)
- **Grafana + Loki** (open-source)

### Correlation IDs

Every request is tagged with a correlation ID (`X-Correlation-Id` header) that flows through all layers — from the API Gateway through Services to Repositories. This enables end-to-end request tracing.

---

## 8. Disaster Recovery & Business Continuity

### Backup Strategy

| Component | Backup Method | Frequency | Retention |
|-----------|--------------|-----------|-----------|
| **Database** | SQL Server full + differential backup | Full: daily; Differential: every 6 hours | 30 days |
| **Configuration** | Source control (appsettings, migrations) | On every change | Indefinite |
| **Secret store** (RSA/HMAC/gateway token, pepper) | `secrets.dpapi` + key ring (+ the `.pfx` in Certificate mode); or `appsettings.Production.json` in PlainText | On rotation | Until all tokens signed with the old key expire |
| **Audit Logs** | Database backup + optional CSV export | Daily | Per compliance requirement (typically 1-7 years) |

### Recovery Objectives (Recommended Targets)

| Metric | Target | Description |
|--------|--------|-------------|
| **RPO** (Recovery Point Objective) | 6 hours | Maximum acceptable data loss |
| **RTO** (Recovery Time Objective) | 1 hour | Maximum acceptable downtime |

### Incident Response

1. **Detection**: Automated alerts on failed authentication spikes, rate limit breaches, or health check failures
2. **Containment**: Automatic account lockout, token blacklisting, rate limiting escalation
3. **Investigation**: Correlation ID-based request tracing through structured logs
4. **Recovery**: Database restore, key rotation, forced password reset if needed
5. **Post-incident**: Audit log review, root cause analysis, policy updates

---

## 9. Integration Capabilities

### Current Integration Points

| Integration | Method | Status |
|-------------|--------|--------|
| **REST API consumers** | JWT Bearer tokens | Available |
| **Blazor Admin UI** | Direct API integration | Available |
| **Multiple applications** | SSO via shared JWT validation (public key) | Available |
| **External services** | API Keys (hashed with Argon2id) | Available |

### Future Integration Roadmap

| Integration | Description | Priority |
|-------------|-------------|----------|
| **OAuth2 / OpenID Connect** | Act as an Identity Provider for third-party apps | High |
| **LDAP / Active Directory** | Sync users from corporate directories | High |
| **SAML 2.0** | Enterprise SSO federation | Medium |
| **Webhook notifications** | Real-time event notifications to external systems | Medium |
| **Azure AD / Entra ID** | External identity provider integration | Medium |

### API Key Authentication

For service-to-service communication, AuthSystem supports API Keys:
- Keys are hashed with Argon2id before storage (same security as passwords)
- Keys can be scoped to specific permissions
- Keys can be revoked at any time
- All API key usage is audit-logged

---

## 10. Secret Management & Key Rotation

### Current Secret Protection

| Secret | Protection Method |
|--------|------------------|
| RSA signing keys, HMAC key, gateway token | `StorageMode`: PlainText (`appsettings.Production.json`), Certificate (`secrets.dpapi` + X.509 cert), or Dpapi (Windows machine-bound) |
| Database connection string | `ConnectionStrings__AuthDb` env var, or encrypted in `secrets.dpapi` (Certificate/Dpapi) |
| SMTP password | `Email__Password` env var, or encrypted in `secrets.dpapi` |
| Password pepper | Stored in the active secret store (when enabled) |
| API Keys (stored) | Hashed with Argon2id |
| Gateway token (in transit) | Sent via the `X-Gateway-Token` header |

### Key Rotation Process

1. **Generate new key pair** — new RSA keys created
2. **Dual-key period** — both old and new keys active for token verification
3. **Transition** — new tokens signed with new key; old tokens still verifiable
4. **Retirement** — old key removed after all tokens signed with it have expired (max: refresh token lifetime = 7 days)

> **Provisioning:** secrets auto-generate on first startup (`AutoGenerateKeys: true`); set it to `false` afterwards. To control the key material yourself, use the admin `import/*` endpoints (Certificate/Dpapi) or edit `appsettings.Production.json` (PlainText) — see PRODUCTION_DEPLOYMENT_GUIDE.md §G.

### Recommended: External Secret Management

For production deployments, integrate with:
- **Azure Key Vault**
- **HashiCorp Vault**
- **AWS Secrets Manager**

---

## 11. Testing Strategy

### Recommended Test Coverage

| Layer | Test Type | Focus Areas |
|-------|-----------|-------------|
| **Services** | Unit tests | Business logic, validation, edge cases |
| **Repositories** | Integration tests | Query correctness, data integrity |
| **Controllers** | Integration tests | Request/response handling, authorization |
| **End-to-End** | E2E tests | Full authentication flows, SSO scenarios |
| **Security** | Security tests | Injection attempts, rate limit effectiveness |
| **Performance** | Load tests | Concurrent user handling, response times |

### Security Testing Checklist

- [ ] SQL injection attempts on all input fields
- [ ] XSS payload testing on all text inputs
- [ ] Rate limit enforcement verification
- [ ] Token expiration and blacklisting
- [ ] CSRF protection on state-changing endpoints
- [ ] Permission escalation attempts
- [ ] Brute force lockout verification
- [ ] Session fixation testing

---

## 12. Configuration Reference

| Setting | Value | Configurable |
|---------|-------|-------------|
| Access Token Lifetime | 15 minutes | Yes (appsettings.json) |
| Refresh Token Lifetime | 7 days | Yes (appsettings.json) |
| Failed Logins Before Lockout | 5 attempts | Yes |
| Lockout Duration | 15 minutes | Yes |
| Password Minimum Length | 8 characters (12+ recommended) | Yes |
| Password History | 3 passwords | Yes |
| Rate Limit (Global) | 100 req/60s | Yes |
| Rate Limit (Login) | 5 req/60s | Yes |
| Rate Limit (Gateway) | 1,000 req/60s | Yes |
| Session Idle Timeout | 30 minutes | Yes |
| Max Concurrent Sessions | 5 | Yes |
| Argon2id Memory | 19,456 KB | Yes |
| Argon2id Iterations | 2 | Yes |
| Argon2id Parallelism | 1 | Yes |
| Password Pepper | Disabled (opt-in) | Yes |
| Breached-Password Check | Disabled (opt-in) | Yes |
| Secret Storage Mode | PlainText | Yes |

All values are configurable through `appsettings.json` without code changes.

---

## 13. API Endpoints Reference

| Module | Base Path | Key Operations |
|--------|-----------|----------------|
| **Auth** | `/api/v1/auth/` | login, refresh, logout, password management, 2FA |
| **Users** | `/api/v1/users/` | CRUD, role assignment, permission management, lock/unlock |
| **Roles** | `/api/v1/roles/` | CRUD, permission assignment |
| **Permissions** | `/api/v1/permissions/` | CRUD, hierarchy management |
| **API Keys** | `/api/v1/apikeys/` | Create, revoke, list |
| **Audit Logs** | `/api/v1/audit-logs/` | Query, filter, export (CSV) |
| **Organizations** | `/api/v1/organizations/` | CRUD, member management, invitations |
| **Health** | `/health` | Health check endpoint |

---

*Document Version: 1.0*
*Last Updated: June 2026*
