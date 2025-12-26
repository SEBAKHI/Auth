# Enterprise Authentication System Implementation Prompt for AI Agent (v9)

> **Think deeply, plan thoroughly, implement carefully, and validate relentlessly—you have the full capability to build production-grade systems, so use your complete reasoning ability on every decision, no matter how small.**

---

## Version 9 Changes Summary

This version incorporates the following architectural simplifications and additions from the v8 critical analysis:

### Structural Changes
1. **Removed Foundation_Lib** — Merged into Auth_Lib. Using existing NuGet packages instead of custom implementations:
   - `ErrorOr` for Result pattern (replaces custom Result<T>)
   - `Ardalis.GuardClauses` for guard clauses (replaces custom Guard.cs)
   - `MediatR` for domain events (replaces custom event dispatcher)
2. **Removed MessagingHub_Lib** — Replaced with MediatR for in-process domain events. If external messaging is needed later, use MassTransit.
3. **Clarified Architecture** — Auth_API is a **modular monolith** with internal modules, not separate microservices.

### New Sections Added
4. **Secrets Management** — Where and how to store sensitive configuration
5. **High Availability Design** — Multi-instance deployment, load balancing, database failover
6. **Disaster Recovery Plan** — RTO/RPO targets, backup strategy, recovery procedures
7. **Performance Baselines** — Target metrics for requests/sec, latency, throughput
8. **CI/CD Integration** — Deployment pipeline, database migrations, rollback procedures
9. **Rate Limiting Architecture** — Clear responsibilities between Gateway and Auth_API

---

## Agent Instructions

You are implementing a production system that real users will depend on.

**Before coding**: Plan the architecture and identify dependencies.
**While coding**: Think about security, failures, and edge cases for every line.
**After coding**: Validate against requirements and test your assumptions.

You have permission to question requirements, propose improvements, and stop to ask questions. Use your full reasoning capabilities—don't just pattern-match to similar code you've seen. Think through each decision as if you're the architect responsible for this system in production.

When something feels wrong or unclear, SAY SO. Don't hide uncertainty behind generic implementations.

---

## Agent Autonomy and Communication

You have permission and are encouraged to:

- **Challenge requirements** if you see conflicts, inefficiencies, or better approaches—explain your reasoning
- **Ask clarifying questions** before implementing anything ambiguous rather than making assumptions
- **Propose improvements** beyond what's specified if they significantly enhance security, performance, or maintainability
- **Stop and report** if you discover that a previous implementation needs revision based on new understanding
- **Think out loud** about complex decisions—show your reasoning process for architectural choices

**Do NOT silently make assumptions. When in doubt, ASK.**

---

## Project Context and Prerequisites

You are a senior software architect with profound expertise in Product Development Manager and designing scalable and robust service-oriented architectures, particularly within enterprise, an expert in C#, .NET 10, MS SQL, Microservices, OOP, SOLID principles, simplifying code, integration and best practices in developing enterprise apps using Agile methodologies, Security, Auditing, Authentication and Authorization system, optimize the integration and orchestration of critical backend procedures and ensure concurrency control and system integrity.

You are tasked with implementing a production-ready, enterprise-grade authentication and authorization system using .NET 10.0 **as a modular monolith architecture** (with clear internal boundaries that could be split into microservices if needed in the future).

---

## Implementation Strategy (MANDATORY)

**Before writing ANY code**, you MUST:

### 1. Create an Architecture Map
Draw out (in text/mermaid diagram) how all components connect, what depends on what, and the order of implementation. Include:
- Service dependencies
- Database relationships
- Authentication flow sequences
- API Gateway routing paths

### 2. Identify Risks
List the top 5 things most likely to go wrong and how you'll prevent them. Consider:
- Security vulnerabilities
- Performance bottlenecks
- Integration failures
- Data consistency issues
- Deployment complications

### 3. Define Your Approach
For each major component, briefly explain **WHY** you're implementing it a certain way, not just **WHAT**. Document:
- Technology choices and alternatives considered
- Design pattern selections
- Trade-offs made

### 4. Checkpoint Plan
Define what "done" looks like for each phase before starting it. Include:
- Acceptance criteria
- Test scenarios
- Integration verification steps

**Present this plan and wait for approval before proceeding with implementation.**

---

## System Scope and Purpose

### Centralized Authentication Platform

**CRITICAL**: This authentication system serves as a **centralized identity provider** for multiple enterprise applications. Users will have **one username and password** that works across all integrated applications (Single Sign-On capability). This design choice has significant architectural implications:

1. **Single Identity Store**: All user credentials, profiles, and authentication data are stored centrally in Auth_DB
2. **Cross-Application Sessions**: Users authenticate once and can access multiple applications without re-entering credentials
3. **Unified User Management**: User accounts, roles, and permissions are managed from a single location
4. **Consistent Security Policies**: Password policies, lockout rules, and 2FA requirements apply uniformly across all applications
5. **Centralized Audit Trail**: All authentication events across all applications are logged in one place
6. **Token-Based Access**: Applications validate user sessions via JWT tokens issued by this auth system
7. **Application Registration**: Each consuming application must be registered and issued API keys for secure communication

**Design Implications**:
- The system must be highly available (99.9%+ uptime) as it becomes a single point of authentication
- Performance is critical—authentication latency affects all dependent applications
- Security is paramount—a breach would compromise all connected applications
- The database schema must support application-specific roles/permissions while maintaining user identity consistency

---

## Single Source of Truth Architecture (CRITICAL)

### Fundamental Principle

**This Auth System is the SINGLE SOURCE OF TRUTH for all authentication and authorization across the enterprise.**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    SINGLE SOURCE OF TRUTH ARCHITECTURE                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌──────────────┐     ┌──────────────┐     ┌──────────────┐               │
│   │  External    │     │  External    │     │  External    │               │
│   │  System A    │     │  System B    │     │  System N    │               │
│   │  (ERP)       │     │  (CRM)       │     │  (Any App)   │               │
│   └──────┬───────┘     └──────┬───────┘     └──────┬───────┘               │
│          │                    │                    │                        │
│          │   API Calls Only   │   API Calls Only   │   API Calls Only       │
│          │   (HTTP/gRPC)      │   (HTTP/gRPC)      │   (HTTP/gRPC)          │
│          │                    │                    │                        │
│          ▼                    ▼                    ▼                        │
│   ┌─────────────────────────────────────────────────────────────────┐      │
│   │                      API GATEWAY (YARP)                         │      │
│   │              Single Entry Point for All Requests                │      │
│   └─────────────────────────────────────────────────────────────────┘      │
│                                    │                                        │
│                                    ▼                                        │
│   ┌─────────────────────────────────────────────────────────────────┐      │
│   │                                                                 │      │
│   │                    AUTH_API (MODULAR MONOLITH)                  │      │
│   │                                                                 │      │
│   │   ┌─────────────────────────────────────────────────────────┐  │      │
│   │   │                    Internal Modules                      │  │      │
│   │   │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐        │  │      │
│   │   │  │Authentication│ │UserManagement│ │RoleManagement│       │  │      │
│   │   │  └─────────────┘ └─────────────┘ └─────────────┘        │  │      │
│   │   │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐        │  │      │
│   │   │  │PermissionMgt│ │ApiKeyManagement│ │  AuditLog  │       │  │      │
│   │   │  └─────────────┘ └─────────────┘ └─────────────┘        │  │      │
│   │   └─────────────────────────────────────────────────────────┘  │      │
│   │                                                                 │      │
│   │   Communication: MediatR (in-process) — NOT HTTP between modules│      │
│   │                                                                 │      │
│   └─────────────────────────────────────────────────────────────────┘      │
│                                    │                                        │
│                                    │ ONLY Auth_API                          │
│                                    │ Can Access                             │
│                                    ▼                                        │
│   ┌─────────────────────────────────────────────────────────────────┐      │
│   │                         AUTH_DB                                 │      │
│   │                 (SQL Server 2022 - ISOLATED)                    │      │
│   │                                                                 │      │
│   │   ⛔ NO external system has access to this database             │      │
│   │   ⛔ NO connection strings shared with any external system      │      │
│   │   ⛔ NO direct queries from external systems                    │      │
│   │                                                                 │      │
│   └─────────────────────────────────────────────────────────────────┘      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Core Tenets (NON-NEGOTIABLE)

#### 1. Security: No Database Credentials Shared with External Systems

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         SECURITY BOUNDARIES                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ✅ ALLOWED:                                                                │
│  • External systems call Auth API endpoints                                 │
│  • External systems receive JWT tokens                                      │
│  • External systems validate tokens via Auth API                            │
│  • External systems receive API keys for M2M communication                  │
│  • External systems receive public JWKS endpoint URL for offline validation │
│                                                                             │
│  ⛔ STRICTLY FORBIDDEN:                                                     │
│  • Sharing Auth_DB connection strings with ANY external system              │
│  • External systems connecting directly to Auth_DB                          │
│  • Exposing Auth_DB to any network accessible by external systems           │
│  • Sharing database credentials in any form (env vars, config, secrets)     │
│  • External systems executing SQL queries against Auth_DB                   │
│  • Replicating Auth_DB data to external system databases                    │
│  • Sharing password hashes, tokens, or cryptographic keys                   │
│                                                                             │
│  ⛔ JWT SECRETS - NEVER SHARE WITH EXTERNAL SYSTEMS:                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  • JWT Private Signing Key (RS256 private key)                      │   │
│  │  • JWT Secret Key (if using symmetric algorithms - NOT RECOMMENDED) │   │
│  │  • Issuer (iss) configuration value                                 │   │
│  │  • Audience (aud) configuration values                              │   │
│  │  • Token expiration configuration                                   │   │
│  │  • Refresh token secrets                                            │   │
│  │  • Key rotation schedules                                           │   │
│  │  • Signing certificate passwords                                    │   │
│  │                                                                     │   │
│  │  WHY: If external systems have these, they could:                   │   │
│  │  • Forge valid JWT tokens                                           │   │
│  │  • Bypass authentication entirely                                   │   │
│  │  • Impersonate any user                                             │   │
│  │  • Create tokens with arbitrary permissions                         │   │
│  │  • Undermine the entire security model                              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 2. Token Validation Strategy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    TOKEN VALIDATION STRATEGY                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  TWO VALIDATION MODES (External systems choose based on their needs):       │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  MODE 1: ONLINE VALIDATION (Recommended for sensitive operations)   │   │
│  │                                                                     │   │
│  │  External System ──► Auth API /validate endpoint                    │   │
│  │                                                                     │   │
│  │  Pros:                                                              │   │
│  │  • Real-time revocation checking                                    │   │
│  │  • Always has latest user status (blocked, deleted, etc.)           │   │
│  │  • No need to handle JWKS rotation                                  │   │
│  │                                                                     │   │
│  │  Cons:                                                              │   │
│  │  • Network latency on every request                                 │   │
│  │  • Auth System becomes dependency for every request                 │   │
│  │  • Higher load on Auth System                                       │   │
│  │                                                                     │   │
│  │  Use when: Financial transactions, admin operations, audit-critical │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  MODE 2: OFFLINE VALIDATION (Recommended for most operations)       │   │
│  │                                                                     │   │
│  │  External System ──► Fetch JWKS from Auth System (cached)           │   │
│  │                  ──► Validate JWT signature locally                 │   │
│  │                                                                     │   │
│  │  Pros:                                                              │   │
│  │  • No network call per request (after JWKS cached)                  │   │
│  │  • Lower latency                                                    │   │
│  │  • Auth System can be down briefly without breaking everything      │   │
│  │                                                                     │   │
│  │  Cons:                                                              │   │
│  │  • Revoked tokens valid until expiry (mitigate with short TTL)      │   │
│  │  • Must handle JWKS rotation and caching                            │   │
│  │                                                                     │   │
│  │  Use when: Read operations, non-sensitive data, high-throughput     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  JWKS ENDPOINT (Public - Safe to expose):                                   │
│  GET https://auth.company.com/.well-known/jwks.json                         │
│                                                                             │
│  Returns: { "keys": [{ "kty": "RSA", "use": "sig", "kid": "...", ... }] }   │
│                                                                             │
│  External systems cache this and refresh every 24 hours or on 401          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 3. What External Systems Get (and DON'T Get)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│              WHAT EXTERNAL SYSTEMS RECEIVE FROM AUTH SYSTEM                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ✅ PROVIDED TO EXTERNAL SYSTEMS:                                           │
│  ─────────────────────────────────                                          │
│  1. AuthSystem.Client.SDK NuGet Package                                     │
│     • Pre-built client for Auth API                                         │
│     • Token validation middleware                                           │
│     • Automatic JWKS fetching and caching                                   │
│     • HttpClient configuration with retry policies                          │
│                                                                             │
│  2. Configuration Values (3 only):                                          │
│     • AuthSystem:BaseUrl     - e.g., "https://auth.company.com"             │
│     • AuthSystem:ApiKey      - Unique per application                       │
│     • AuthSystem:ApplicationId - UUID identifying the application           │
│                                                                             │
│  3. API Endpoints:                                                          │
│     • POST /connect/token    - OAuth 2.0 token endpoint                     │
│     • POST /api/validate     - Online token validation                      │
│     • GET  /.well-known/jwks.json - Public keys for offline validation      │
│     • GET  /.well-known/openid-configuration - OpenID Connect discovery     │
│     • POST /api/refresh      - Refresh access token                         │
│     • POST /api/revoke       - Revoke token (logout)                        │
│                                                                             │
│  4. JWT Tokens (issued to authenticated users):                             │
│     • Access Token (short-lived, 15-60 min)                                 │
│     • Refresh Token (longer-lived, secure storage required)                 │
│     • Claims: sub, email, name, roles, permissions, aud, iss, exp, iat      │
│                                                                             │
│  ⛔ NEVER PROVIDED TO EXTERNAL SYSTEMS:                                     │
│  ─────────────────────────────────────                                      │
│  • Database connection strings                                              │
│  • Direct database access                                                   │
│  • JWT signing keys (private or symmetric)                                  │
│  • Password hashes                                                          │
│  • Internal service credentials                                             │
│  • Audit log database access                                                │
│  • Admin API endpoints without proper authorization                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 4. Data Synchronization Policy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DATA SYNCHRONIZATION POLICY                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  RULE: Auth System data is AUTHORITATIVE. External systems QUERY, not COPY.│
│                                                                             │
│  ⛔ ANTI-PATTERN (Don't do this):                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  External System Database                                           │   │
│  │  ┌─────────────────────────────────────────┐                        │   │
│  │  │ Users_Sync table                        │  ← WRONG: Copied data  │   │
│  │  │ - UserId (synced from Auth System)      │     becomes stale      │   │
│  │  │ - Email (synced from Auth System)       │     and inconsistent   │   │
│  │  │ - Roles (synced from Auth System)       │                        │   │
│  │  │ - LastSyncTime                          │                        │   │
│  │  └─────────────────────────────────────────┘                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ✅ CORRECT PATTERN:                                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  External System Database                                           │   │
│  │  ┌─────────────────────────────────────────┐                        │   │
│  │  │ Orders table                            │                        │   │
│  │  │ - OrderId                               │                        │   │
│  │  │ - AuthUserId (FK reference only)        │  ← Just the ID         │   │
│  │  │ - ProductId                             │                        │   │
│  │  │ - Amount                                │                        │   │
│  │  └─────────────────────────────────────────┘                        │   │
│  │                                                                     │   │
│  │  When displaying "Order by John Doe":                               │   │
│  │  1. Get order from local DB (has AuthUserId)                        │   │
│  │  2. Call Auth API: GET /api/users/{AuthUserId}                      │   │
│  │  3. Display fresh user data from Auth System                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  CACHING GUIDELINES:                                                        │
│  • User display info (name, email): Cache 5-15 minutes                      │
│  • Permissions/roles: Cache 1-5 minutes or validate on sensitive ops        │
│  • User existence: Cache 1 hour (users rarely deleted)                      │
│  • NEVER cache passwords or tokens (except refresh tokens in secure storage)│
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 5. Audit Trail Ownership

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        AUDIT TRAIL OWNERSHIP                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  AUTH SYSTEM LOGS (Stored in Auth_DB.AuditLogs):                            │
│  ──────────────────────────────────────────────                             │
│  • Login attempts (success/failure)                                         │
│  • Password changes                                                         │
│  • 2FA enable/disable                                                       │
│  • Token issuance and revocation                                            │
│  • Permission changes                                                       │
│  • Role assignments                                                         │
│  • API key creation/revocation                                              │
│  • Account lockouts                                                         │
│  • Session management events                                                │
│                                                                             │
│  EXTERNAL SYSTEM LOGS (Stored in their own databases):                      │
│  ─────────────────────────────────────────────────────                      │
│  • Business actions (e.g., "John created invoice #123")                     │
│  • Data access (e.g., "Jane viewed customer record")                        │
│  • Application-specific events                                              │
│                                                                             │
│  CORRELATION:                                                               │
│  External systems should include the JWT 'jti' (token ID) claim in their    │
│  audit logs to correlate with Auth System logs if investigation needed.     │
│                                                                             │
│  QUERYING AUTH AUDIT LOGS:                                                  │
│  External systems can query their own audit events via API:                 │
│  GET /api/audit/my-application?from=2024-01-01&to=2024-01-31                │
│  (Filtered to only show events for their ApplicationId)                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 6. API Integration Summary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     API INTEGRATION SUMMARY                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ENDPOINT CATEGORIES:                                                       │
│                                                                             │
│  PUBLIC (No authentication required):                                       │
│  • GET  /.well-known/openid-configuration                                   │
│  • GET  /.well-known/jwks.json                                              │
│  • POST /connect/token (with valid credentials)                             │
│  • POST /api/auth/login                                                     │
│  • POST /api/auth/register (if self-registration enabled)                   │
│  • POST /api/auth/forgot-password                                           │
│                                                                             │
│  PROTECTED (Requires valid JWT):                                            │
│  • GET  /api/users/me                                                       │
│  • PUT  /api/users/me                                                       │
│  • POST /api/auth/refresh                                                   │
│  • POST /api/auth/logout                                                    │
│  • POST /api/auth/change-password                                           │
│  • GET  /api/auth/sessions                                                  │
│  • DELETE /api/auth/sessions/{id}                                           │
│                                                                             │
│  ADMIN (Requires admin role or specific permissions):                       │
│  • CRUD /api/users/*                                                        │
│  • CRUD /api/roles/*                                                        │
│  • CRUD /api/permissions/*                                                  │
│  • CRUD /api/applications/*                                                 │
│  • CRUD /api/apikeys/*                                                      │
│  • GET  /api/audit/*                                                        │
│                                                                             │
│  M2M (Requires valid API Key):                                              │
│  • POST /api/validate                                                       │
│  • GET  /api/users/{id} (limited fields)                                    │
│  • GET  /api/users/{id}/permissions                                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```


### External System Integration Guide

**Make it EASY for external systems to integrate without suffering:**

#### Simple Integration Flow

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// EXTERNAL SYSTEM INTEGRATION - THIS IS ALL THEY NEED
// ═══════════════════════════════════════════════════════════════════════════

// Step 1: Configure Auth Client (ONE-TIME SETUP)
services.AddAuthSystemClient(options =>
{
    options.BaseUrl = "https://auth.company.com";
    options.ApiKey = configuration["AuthSystem:ApiKey"];  // Only secret needed
    options.ApplicationId = configuration["AuthSystem:ApplicationId"];
});

// Step 2: Validate tokens in middleware (AUTOMATIC)
app.UseAuthentication();  // Uses Auth System for validation
app.UseAuthorization();

// Step 3: Use in controllers (SEAMLESS)
[Authorize]
[HttpGet("protected-resource")]
public IActionResult GetProtectedResource()
{
    var userId = User.FindFirst("sub")?.Value;  // Standard JWT claim
    var roles = User.FindAll("role").Select(c => c.Value);
    // ... business logic
}
```

#### Auth System Client SDK (Provided to External Systems)

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// AUTH SYSTEM CLIENT SDK - DISTRIBUTED TO ALL EXTERNAL SYSTEMS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Client SDK for integrating with the centralized Auth System.
/// This is the ONLY way external systems should interact with authentication.
/// </summary>
public interface IAuthSystemClient
{
    // ─────────────────────────────────────────────────────────────────────
    // Token Operations
    // ─────────────────────────────────────────────────────────────────────
    
    /// <summary>
    /// Validates a JWT token and returns the claims if valid.
    /// Use this to verify tokens received from users/clients.
    /// </summary>
    Task<TokenValidationResult> ValidateTokenAsync(
        string token, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Exchanges an authorization code for tokens (OAuth 2.0 flow).
    /// </summary>
    Task<TokenResponse> ExchangeCodeForTokensAsync(
        string authorizationCode,
        string redirectUri,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    Task<TokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Revokes a token (logout).
    /// </summary>
    Task<bool> RevokeTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
    
    // ─────────────────────────────────────────────────────────────────────
    // User Information
    // ─────────────────────────────────────────────────────────────────────
    
    /// <summary>
    /// Gets user information by ID.
    /// </summary>
    Task<UserInfo> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current user's information from a token.
    /// </summary>
    Task<UserInfo> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
    
    // ─────────────────────────────────────────────────────────────────────
    // Authorization Checks
    // ─────────────────────────────────────────────────────────────────────
    
    /// <summary>
    /// Checks if a user has a specific permission.
    /// </summary>
    Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a user has a specific role.
    /// </summary>
    Task<bool> HasRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all permissions for a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all roles for a user.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// RESPONSE MODELS - What external systems receive
// ═══════════════════════════════════════════════════════════════════════════

public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();
}

public class UserInfo
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public bool IsActive { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}
```

#### JWT Middleware Extension for External Systems

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// EASY INTEGRATION MIDDLEWARE - External systems just add this
// ═══════════════════════════════════════════════════════════════════════════

public static class AuthSystemExtensions
{
    /// <summary>
    /// Adds Auth System authentication to the application.
    /// This configures JWT validation against the centralized Auth System.
    /// </summary>
    public static IServiceCollection AddAuthSystemAuthentication(
        this IServiceCollection services,
        Action<AuthSystemOptions> configure)
    {
        var options = new AuthSystemOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddHttpClient<IAuthSystemClient, AuthSystemClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
            client.DefaultRequestHeaders.Add("X-Application-Id", options.ApplicationId);
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                // CRITICAL: Disable claim type mapping
                jwtOptions.TokenHandlers.Clear();
                jwtOptions.TokenHandlers.Add(new JwtSecurityTokenHandler
                {
                    MapInboundClaims = false
                });

                jwtOptions.Authority = options.BaseUrl;
                jwtOptions.Audience = options.ApplicationId;
                jwtOptions.RequireHttpsMetadata = options.RequireHttps;
                
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.BaseUrl,
                    ValidateAudience = true,
                    ValidAudience = options.ApplicationId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    MapInboundClaims = false,
                    RoleClaimType = "role",
                    NameClaimType = "name"
                };

                // Optional: Validate token against Auth System API for additional security
                if (options.ValidateTokensOnline)
                {
                    jwtOptions.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var authClient = context.HttpContext.RequestServices
                                .GetRequiredService<IAuthSystemClient>();
                            var token = context.SecurityToken as JwtSecurityToken;
                            
                            var result = await authClient.ValidateTokenAsync(
                                token?.RawData ?? string.Empty,
                                context.HttpContext.RequestAborted);
                            
                            if (!result.IsValid)
                            {
                                context.Fail("Token validation failed");
                            }
                        }
                    };
                }
            });

        return services;
    }
}

public class AuthSystemOptions
{
    /// <summary>
    /// Base URL of the Auth System (e.g., https://auth.company.com)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API Key issued to this application by Auth System
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Application ID registered in Auth System
    /// </summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>
    /// Whether to require HTTPS (should be true in production)
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Whether to validate tokens online against Auth System API
    /// (adds latency but provides real-time revocation checking)
    /// </summary>
    public bool ValidateTokensOnline { get; set; } = false;

    // ═══════════════════════════════════════════════════════════════════════
    // NOTE: Issuer, Audience, and signing keys are NOT configured here!
    // The SDK automatically fetches these from the Auth System's 
    // OpenID Connect discovery endpoint (/.well-known/openid-configuration)
    // This ensures:
    //   1. External systems cannot forge tokens (no access to signing keys)
    //   2. Configuration is always in sync with Auth System
    //   3. Key rotation is handled automatically
    //   4. No sensitive configuration in external system code/config
    // ═══════════════════════════════════════════════════════════════════════
}
```

#### Minimal Integration Example

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// EXTERNAL SYSTEM PROGRAM.CS - Complete integration in ~10 lines
// ═══════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// Add Auth System integration - THIS IS ALL THAT'S NEEDED
builder.Services.AddAuthSystemAuthentication(options =>
{
    options.BaseUrl = builder.Configuration["AuthSystem:BaseUrl"];
    options.ApiKey = builder.Configuration["AuthSystem:ApiKey"];
    options.ApplicationId = builder.Configuration["AuthSystem:ApplicationId"];
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

// ═══════════════════════════════════════════════════════════════════════════
// EXTERNAL SYSTEM APPSETTINGS.JSON - Only 3 config values needed
// ═══════════════════════════════════════════════════════════════════════════
/*
{
  "AuthSystem": {
    "BaseUrl": "https://auth.company.com",
    "ApiKey": "your-api-key-here",
    "ApplicationId": "your-app-id-here"
  }
}

NOTICE: The following are INTENTIONALLY NOT configured here:
  ❌ JWT Secret Key - NEVER shared with external systems
  ❌ JWT Private Key - NEVER shared with external systems  
  ❌ Issuer (iss) - Fetched automatically from Auth System
  ❌ Audience (aud) - Fetched automatically from Auth System
  ❌ Token Expiration - Controlled by Auth System
  ❌ Signing Algorithm - Controlled by Auth System

The SDK handles all JWT validation by:
  1. Fetching public keys from JWKS endpoint
  2. Getting issuer/audience from OpenID Connect discovery
  3. Validating signatures using PUBLIC keys only

This ensures external systems CANNOT forge tokens.
*/

// NO database connection strings
// NO auth logic implementation
// NO password hashing code
// NO token generation code
// NO session management code
```

### What This Architecture Guarantees

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ARCHITECTURE GUARANTEES                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  FOR SECURITY TEAM:                                                         │
│  ✅ Single point of security control                                        │
│  ✅ No credential sprawl across systems                                     │
│  ✅ Centralized security policy enforcement                                 │
│  ✅ Complete audit trail of all auth events                                 │
│  ✅ Single place to respond to security incidents                           │
│  ✅ Consistent encryption and hashing standards                             │
│                                                                             │
│  FOR DEVELOPMENT TEAMS:                                                     │
│  ✅ Simple integration (SDK + 3 config values)                              │
│  ✅ No need to understand auth internals                                    │
│  ✅ No database connections to manage                                       │
│  ✅ Automatic updates when auth system improves                             │
│  ✅ Focus on business logic, not auth plumbing                              │
│                                                                             │
│  FOR OPERATIONS TEAM:                                                       │
│  ✅ Single system to monitor for auth issues                                │
│  ✅ Clear scaling path (scale Auth System, not each app)                    │
│  ✅ Centralized logging and alerting                                        │
│  ✅ Simplified compliance auditing                                          │
│                                                                             │
│  FOR COMPLIANCE/AUDIT:                                                      │
│  ✅ Single source of truth for access records                               │
│  ✅ Immutable audit trail                                                   │
│  ✅ Clear data lineage                                                      │
│  ✅ Simplified SOC 2 / ISO 27001 evidence collection                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Modular Monolith Architecture

### Architecture Clarification

**IMPORTANT**: Auth_API is a **Modular Monolith**, NOT separate microservices.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MODULAR MONOLITH vs MICROSERVICES                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Auth_API is a SINGLE DEPLOYABLE UNIT with INTERNAL MODULE BOUNDARIES      │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                         Auth_API Process                            │   │
│  │                                                                     │   │
│  │   ┌───────────────┐  ┌───────────────┐  ┌───────────────┐          │   │
│  │   │Authentication │  │UserManagement │  │RoleManagement │          │   │
│  │   │    Module     │  │    Module     │  │    Module     │          │   │
│  │   └───────┬───────┘  └───────┬───────┘  └───────┬───────┘          │   │
│  │           │                  │                  │                   │   │
│  │           └──────────────────┼──────────────────┘                   │   │
│  │                              │                                      │   │
│  │                      ┌───────▼───────┐                              │   │
│  │                      │   MediatR     │  ← In-process messaging     │   │
│  │                      │  (IMediator)  │    NOT HTTP calls           │   │
│  │                      └───────────────┘                              │   │
│  │                                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  EVIDENCE THIS IS A MODULAR MONOLITH:                                       │
│  • Single Auth_API.csproj project                                           │
│  • Single deployment unit (one Docker container/process)                    │
│  • MediatR for inter-module communication (in-process, not HTTP)            │
│  • Single Auth_DB database (shared by all modules)                          │
│  • No service discovery needed (modules are in same process)                │
│  • No network latency between modules                                       │
│                                                                             │
│  IF IT WERE MICROSERVICES, YOU'D HAVE:                                      │
│  • Separate .csproj for each service                                        │
│  • Separate containers/processes                                            │
│  • HTTP/gRPC between services                                               │
│  • Database-per-service pattern                                             │
│  • Service discovery (Consul, K8s DNS)                                      │
│  • Distributed transaction handling (Saga)                                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Why Modular Monolith for Auth System?

| Aspect | Modular Monolith | True Microservices |
|--------|------------------|-------------------|
| **Transaction Integrity** | ✅ ACID across modules | ⚠️ Eventual consistency |
| **Performance** | ✅ In-process calls (μs) | ⚠️ Network calls (ms) |
| **Deployment** | ✅ Single unit | ⚠️ Orchestration needed |
| **Debugging** | ✅ Single process | ⚠️ Distributed tracing |
| **Security** | ✅ One boundary | ⚠️ Multiple attack surfaces |

**For authentication, we WANT atomic operations:**
- Create user → assign roles → log audit event = ONE transaction
- Not eventual consistency across services

### Module Structure in Auth_API

```
Auth_API/
├── Modules/
│   ├── Authentication/
│   │   ├── Controllers/
│   │   │   └── AuthController.cs
│   │   ├── Services/
│   │   │   ├── IAuthenticationService.cs
│   │   │   └── AuthenticationService.cs
│   │   ├── Commands/
│   │   │   ├── LoginCommand.cs
│   │   │   └── RefreshTokenCommand.cs
│   │   └── Events/
│   │       └── UserLoggedInEvent.cs
│   │
│   ├── UserManagement/
│   │   ├── Controllers/
│   │   │   └── UsersController.cs
│   │   ├── Services/
│   │   │   ├── IUserService.cs
│   │   │   └── UserService.cs
│   │   ├── Commands/
│   │   │   ├── CreateUserCommand.cs
│   │   │   └── UpdateUserCommand.cs
│   │   └── Events/
│   │       ├── UserCreatedEvent.cs
│   │       └── UserUpdatedEvent.cs
│   │
│   ├── RoleManagement/
│   │   ├── Controllers/
│   │   │   └── RolesController.cs
│   │   └── Services/
│   │       └── RoleService.cs
│   │
│   ├── PermissionManagement/
│   │   ├── Controllers/
│   │   │   └── PermissionsController.cs
│   │   └── Services/
│   │       └── PermissionService.cs
│   │
│   ├── ApiKeyManagement/
│   │   ├── Controllers/
│   │   │   └── ApiKeysController.cs
│   │   └── Services/
│   │       └── ApiKeyService.cs
│   │
│   └── AuditLog/
│       ├── Controllers/
│       │   └── AuditController.cs
│       ├── Services/
│       │   └── AuditService.cs
│       └── EventHandlers/
│           └── AuditEventHandler.cs  ← Listens to ALL domain events
│
├── Common/
│   ├── Behaviors/
│   │   ├── LoggingBehavior.cs
│   │   └── ValidationBehavior.cs
│   └── Middleware/
│       └── ExceptionHandlingMiddleware.cs
│
└── Program.cs
```

### Auth_API Project File (Auth_API.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Auth_API</RootNamespace>
    <AssemblyName>Auth_API</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- ASP.NET Core -->
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.*" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="7.*" />
    
    <!-- MediatR for CQRS and Domain Events -->
    <PackageReference Include="MediatR" Version="12.*" />
    
    <!-- Validation -->
    <PackageReference Include="FluentValidation.AspNetCore" Version="11.*" />
    
    <!-- Logging -->
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
    <PackageReference Include="Serilog.Sinks.Seq" Version="8.*" />
    
    <!-- Health Checks -->
    <PackageReference Include="AspNetCore.HealthChecks.SqlServer" Version="8.*" />
    
    <!-- Rate Limiting (built-in .NET 10) -->
    <!-- No package needed - use Microsoft.AspNetCore.RateLimiting -->
  </ItemGroup>

  <ItemGroup>
    <!-- Internal Project References -->
    <ProjectReference Include="..\Auth_Lib\Auth_Lib.csproj" />
    <ProjectReference Include="..\Auth_Localization\Auth_Localization.csproj" />
  </ItemGroup>

</Project>
```

### Auth_API Complete Program.cs

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Auth_API.Common.Middleware;
using Auth_Lib.Application.Behaviors;
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Infrastructure.Data;
using Auth_Lib.Infrastructure.Security;
using Auth_Localization.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// LOGGING (Serilog)
// ============================================
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .WriteTo.Console()
        .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5341");
});

// ============================================
// DATABASE CONNECTION (Dapper - NO EF Core!)
// ============================================
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("AuthDb")
        ?? throw new InvalidOperationException("AuthDb connection string not configured");
    return new SqlConnection(connectionString);
});

// ============================================
// REPOSITORIES (Dapper-based)
// ============================================
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// ============================================
// SECURITY SERVICES
// ============================================
builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();

// ============================================
// MEDIATR (CQRS + Domain Events)
// ============================================
builder.Services.AddMediatR(cfg =>
{
    // Register handlers from Auth_Lib
    cfg.RegisterServicesFromAssemblyContaining<Auth_Lib.Application.Commands.CreateUserCommand>();
    
    // Register handlers from Auth_API modules
    cfg.RegisterServicesFromAssemblyContaining<Program>();
});

// MediatR Pipeline Behaviors (order matters!)
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ============================================
// FLUENT VALIDATION
// ============================================
builder.Services.AddValidatorsFromAssemblyContaining<Auth_Lib.Application.Commands.CreateUserCommand>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ============================================
// LOCALIZATION
// ============================================
builder.Services.AddAuthLocalization();

// ============================================
// AUTHENTICATION (JWT)
// ============================================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // CRITICAL: Disable default claim type mapping
        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        });

        // Load RSA public key for token validation
        var publicKeyPem = builder.Configuration["Jwt:PublicKey"]
            ?? throw new InvalidOperationException("JWT public key not configured");
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudiences = builder.Configuration.GetSection("Jwt:Audiences").Get<string[]>(),
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            MapInboundClaims = false,
            RoleClaimType = "role",
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Warning("JWT Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// ============================================
// AUTHORIZATION
// ============================================
builder.Services.AddAuthorization();

// ============================================
// RATE LIMITING
// ============================================
builder.Services.AddRateLimiter(options =>
{
    // Global rate limit per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });

    // Strict limit for login endpoint
    options.AddPolicy("login", context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "rate_limit_exceeded",
            message = "Too many requests. Please try again later."
        }, token);
    };
});

// ============================================
// CACHING
// ============================================
builder.Services.AddMemoryCache();

// ============================================
// HEALTH CHECKS
// ============================================
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("AuthDb")!,
        name: "database",
        tags: new[] { "ready" });

// ============================================
// API CONTROLLERS & SWAGGER
// ============================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Auth API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new()
    {
        Description = "JWT Authorization header using the Bearer scheme",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer"
    });
});

// ============================================
// CORS
// ============================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowConfigured", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
            ?? Array.Empty<string>();
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ============================================
// MIDDLEWARE PIPELINE (ORDER MATTERS!)
// ============================================

// 1. Exception handling (first to catch all errors)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 2. Serilog request logging
app.UseSerilogRequestLogging();

// 3. Swagger (development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 4. HTTPS redirection
app.UseHttpsRedirection();

// 5. CORS
app.UseCors("AllowConfigured");

// 6. Rate limiting
app.UseRateLimiter();

// 7. Localization
app.UseAuthLocalization();

// 8. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 9. Gateway token validation (only accept requests through gateway)
app.UseMiddleware<GatewayTokenValidationMiddleware>();

// ============================================
// ENDPOINTS
// ============================================

// Health checks
app.MapHealthChecks("/health/live", new()
{
    Predicate = _ => false // Just checks if app is running
});

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready")
});

// API Controllers
app.MapControllers();

// ============================================
// OIDC Discovery Endpoints (for JWT validation)
// ============================================
app.MapGet("/.well-known/openid-configuration", (IConfiguration config) =>
{
    var issuer = config["Jwt:Issuer"];
    return Results.Ok(new
    {
        issuer,
        jwks_uri = $"{issuer}/.well-known/jwks.json",
        token_endpoint = $"{issuer}/api/auth/token",
        response_types_supported = new[] { "token" },
        subject_types_supported = new[] { "public" },
        id_token_signing_alg_values_supported = new[] { "RS256" }
    });
});

app.MapGet("/.well-known/jwks.json", (IJwtTokenService jwtService) =>
{
    return Results.Ok(jwtService.GetJwks());
});

app.Run();

// Make Program class accessible for testing
public partial class Program { }
```

### Gateway Token Validation Middleware

```csharp
// Common/Middleware/GatewayTokenValidationMiddleware.cs
namespace Auth_API.Common.Middleware;

/// <summary>
/// Validates that requests come through the API Gateway.
/// Rejects direct access to Auth_API bypassing the gateway.
/// </summary>
public class GatewayTokenValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _expectedToken;
    private readonly ILogger<GatewayTokenValidationMiddleware> _logger;
    private readonly bool _enforceGateway;

    public GatewayTokenValidationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<GatewayTokenValidationMiddleware> logger)
    {
        _next = next;
        _expectedToken = configuration["Gateway:InternalToken"] ?? "";
        _enforceGateway = configuration.GetValue<bool>("Gateway:EnforceValidation", true);
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation for health checks and OIDC discovery
        var path = context.Request.Path.Value?.ToLower() ?? "";
        if (path.StartsWith("/health") || path.StartsWith("/.well-known"))
        {
            await _next(context);
            return;
        }

        // In development, gateway validation can be disabled
        if (!_enforceGateway)
        {
            await _next(context);
            return;
        }

        var gatewayToken = context.Request.Headers["X-Gateway-Token"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(gatewayToken) || gatewayToken != _expectedToken)
        {
            _logger.LogWarning(
                "Request rejected: Invalid or missing gateway token from {IP}",
                context.Connection.RemoteIpAddress);
            
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "access_denied",
                message = "Direct access not allowed. Use the API Gateway."
            });
            return;
        }

        await _next(context);
    }
}
```

### Exception Handling Middleware

```csharp
// Common/Middleware/ExceptionHandlingMiddleware.cs
using System.Text.Json;
using Auth_Localization.Services;

namespace Auth_API.Common.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ILocalizationService localization)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            
            var response = new
            {
                error = "internal_error",
                message = localization.Get(LocalizationKeys.UnexpectedError),
                traceId = context.TraceIdentifier
            };
            
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
```


### Solution Structure (v9 - Simplified)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    V9 SOLUTION STRUCTURE (11 Projects)                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  AuthSystem.sln                                                             │
│  │                                                                          │
│  ├── src/                                                                   │
│  │   ├── API_Gateway/              # Independent reverse proxy (YARP)       │
│  │   ├── Auth_API/                 # Modular monolith (6 internal modules)  │
│  │   ├── Auth_DB/                  # SQL Server database project (SSDT)     │
│  │   ├── Auth_Lib/                 # Core domain + foundation code          │
│  │   ├── Auth_Localization/        # Centralized i18n resources             │
│  │   ├── Auth_Setup/               # Installation & configuration utility   │
│  │   └── AuthSystem.Client.SDK/    # NuGet package for external systems     │
│  │                                                                          │
│  └── tests/                                                                 │
│      ├── Auth_API.Tests/           # API integration & unit tests           │
│      ├── Auth_Lib.Tests/           # Domain logic unit tests                │
│      ├── Auth_Localization.Tests/  # Localization tests                     │
│      └── AuthSystem.Client.SDK.Tests/                                       │
│                                                                             │
│  REMOVED FROM V8:                                                           │
│  ❌ Foundation_Lib → Merged into Auth_Lib + NuGet packages                  │
│  ❌ MessagingHub_Lib → Replaced with MediatR                                │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Project Dependency Graph (v9)

```
                                 ┌──────────────────┐
                                 │   API_Gateway    │
                                 │  (Independent)   │
                                 └────────┬─────────┘
                                          │ HTTP
                                          ▼
┌────────────────────────────────────────────────────────────────────┐
│                           Auth_API                                  │
│                     (Modular Monolith)                              │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  Modules: Authentication, UserMgmt, RoleMgmt, PermissionMgmt, │  │
│  │           ApiKeyMgmt, AuditLog                                │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────┬─────────────────────────────┬────────────────┘
                      │                             │
                      ▼                             ▼
            ┌─────────────────┐           ┌──────────────────┐
            │    Auth_Lib     │           │ Auth_Localization │
            │  (Domain Core)  │           │     (i18n)        │
            └─────────────────┘           └──────────────────┘
                      │
                      ▼
            ┌─────────────────┐
            │    Auth_DB      │
            │   (Database)    │
            └─────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                    AuthSystem.Client.SDK                            │
│              (Distributed to External Systems)                      │
│                                                                     │
│  Dependencies: HTTP Client only (no Auth_Lib reference)             │
│  Purpose: Provides IAuthSystemClient interface for external apps    │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                         Auth_Setup                                  │
│                  (Installation Utility)                             │
│                                                                     │
│  Dependencies: Auth_Lib, Auth_Localization                          │
│  Purpose: Database setup, initial admin user, system configuration  │
└─────────────────────────────────────────────────────────────────────┘
```

### NuGet Packages (v9)

**Core Packages (All Projects):**
```xml
<PackageReference Include="ErrorOr" Version="2.*" />
<PackageReference Include="Ardalis.GuardClauses" Version="5.*" />
```

**Auth_API Packages:**
```xml
<PackageReference Include="MediatR" Version="12.*" />
<PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="11.*" />
<PackageReference Include="Dapper" Version="2.*" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
<PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="AspNetCoreRateLimit" Version="5.*" />
```

**API_Gateway Packages:**
```xml
<PackageReference Include="Yarp.ReverseProxy" Version="2.*" />
<PackageReference Include="AspNetCoreRateLimit" Version="5.*" />
```

---

## Auth_Localization Project

> **📌 NOTE**: This section provides an overview. For complete implementation including full .resx files for all languages (English, Arabic, Turkish), complete LocalizationKeys static class, and README, see **"Auth_Localization Project - Complete Implementation"** section near the end of this document.

### Overview

The `Auth_Localization` project provides centralized localization support for all user-facing messages across all modules in the authentication system. It uses `.resx` resource files with strongly-typed access and supports multiple languages.

### Project Structure

```
Auth_Localization/
├── Resources/
│   ├── SharedTexts.resx              # Default language (English)
│   ├── SharedTexts.ar.resx           # Arabic translations
│   └── SharedTexts.tr.resx           # Turkish translations
├── Services/
│   └── LocalizationService.cs
├── Extensions/
│   └── LocalizationExtensions.cs
├── Auth_Localization.csproj
└── README.md
```

### Project File (Auth_Localization.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>Auth_Localization</RootNamespace>
    <AssemblyName>Auth_Localization</AssemblyName>
    <Version>1.0.0</Version>
    <Authors>Your Company</Authors>
    <Description>Centralized localization resources for the Enterprise Authentication System</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Localization" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Localization.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <!-- Resource File Configuration for Strongly-Typed Access -->
  <ItemGroup>
    <EmbeddedResource Update="Resources\SharedTexts.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <LastGenOutput>SharedTexts.Designer.cs</LastGenOutput>
      <CustomToolNamespace>Auth_Localization.Resources</CustomToolNamespace>
    </EmbeddedResource>
    <EmbeddedResource Update="Resources\SharedTexts.ar.resx">
      <DependentUpon>SharedTexts.resx</DependentUpon>
    </EmbeddedResource>
    <EmbeddedResource Update="Resources\SharedTexts.tr.resx">
      <DependentUpon>SharedTexts.resx</DependentUpon>
    </EmbeddedResource>
    <Compile Update="Resources\SharedTexts.Designer.cs">
      <DesignTime>True</DesignTime>
      <AutoGen>True</AutoGen>
      <DependentUpon>SharedTexts.resx</DependentUpon>
    </Compile>
  </ItemGroup>

</Project>
```

### Resource Files

#### SharedTexts.resx (English - Default)

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  
  <!-- Authentication Messages -->
  <data name="InvalidCredentials" xml:space="preserve">
    <value>The username or password is incorrect.</value>
    <comment>Displayed when login credentials are invalid</comment>
  </data>
  <data name="UserBlocked" xml:space="preserve">
    <value>Your account has been blocked. Please contact support.</value>
    <comment>Displayed when a blocked user attempts to login</comment>
  </data>
  <data name="TokenExpired" xml:space="preserve">
    <value>Your session has expired. Please log in again.</value>
    <comment>Displayed when JWT token has expired</comment>
  </data>
  <data name="InvalidRefreshToken" xml:space="preserve">
    <value>Invalid or expired refresh token. Please log in again.</value>
    <comment>Displayed when refresh token is invalid or expired</comment>
  </data>
  <data name="UserNotFound" xml:space="preserve">
    <value>User not found.</value>
    <comment>Displayed when requested user does not exist</comment>
  </data>
  <data name="ActionNotAllowed" xml:space="preserve">
    <value>You do not have permission to perform this action.</value>
    <comment>Displayed when user lacks required permissions</comment>
  </data>
  <data name="UnexpectedError" xml:space="preserve">
    <value>An unexpected error occurred. Please try again later.</value>
    <comment>Generic error message for unhandled exceptions</comment>
  </data>
  
  <!-- Account Management Messages -->
  <data name="AccountLocked" xml:space="preserve">
    <value>Your account has been locked due to multiple failed login attempts. Please try again in {0} minutes.</value>
    <comment>Displayed when account is temporarily locked. {0} = minutes remaining</comment>
  </data>
  <data name="PasswordResetRequired" xml:space="preserve">
    <value>You must reset your password before continuing.</value>
    <comment>Displayed when password reset is required</comment>
  </data>
  <data name="PasswordChangedSuccessfully" xml:space="preserve">
    <value>Your password has been changed successfully.</value>
    <comment>Confirmation message after password change</comment>
  </data>
  <data name="PasswordTooWeak" xml:space="preserve">
    <value>Password does not meet the security requirements.</value>
    <comment>Displayed when password fails complexity check</comment>
  </data>
  <data name="PasswordHistoryViolation" xml:space="preserve">
    <value>You cannot reuse your previous {0} passwords.</value>
    <comment>Displayed when new password matches a recent password. {0} = number of passwords</comment>
  </data>
  
  <!-- Two-Factor Authentication Messages -->
  <data name="TwoFactorRequired" xml:space="preserve">
    <value>Two-factor authentication is required.</value>
    <comment>Displayed when 2FA verification is needed</comment>
  </data>
  <data name="InvalidTwoFactorCode" xml:space="preserve">
    <value>The verification code is invalid or has expired.</value>
    <comment>Displayed when 2FA code is incorrect</comment>
  </data>
  <data name="TwoFactorEnabledSuccessfully" xml:space="preserve">
    <value>Two-factor authentication has been enabled successfully.</value>
    <comment>Confirmation message after enabling 2FA</comment>
  </data>
  <data name="TwoFactorDisabledSuccessfully" xml:space="preserve">
    <value>Two-factor authentication has been disabled.</value>
    <comment>Confirmation message after disabling 2FA</comment>
  </data>
  
  <!-- API Key Messages -->
  <data name="InvalidApiKey" xml:space="preserve">
    <value>Invalid or revoked API key.</value>
    <comment>Displayed when API key validation fails</comment>
  </data>
  <data name="ApiKeyExpired" xml:space="preserve">
    <value>API key has expired. Please request a new key.</value>
    <comment>Displayed when API key is past expiration</comment>
  </data>
  <data name="ApiKeyRateLimitExceeded" xml:space="preserve">
    <value>API key rate limit exceeded. Please try again later.</value>
    <comment>Displayed when API key hits rate limit</comment>
  </data>
  <data name="ApiKeyCreatedSuccessfully" xml:space="preserve">
    <value>API key has been created successfully.</value>
    <comment>Confirmation message after API key creation</comment>
  </data>
  
  <!-- Session Messages -->
  <data name="SessionExpired" xml:space="preserve">
    <value>Your session has expired. Please log in again.</value>
    <comment>Displayed when user session expires</comment>
  </data>
  <data name="ConcurrentSessionLimitReached" xml:space="preserve">
    <value>Maximum concurrent sessions reached. Please log out from another device.</value>
    <comment>Displayed when user exceeds max sessions</comment>
  </data>
  <data name="LogoutSuccessful" xml:space="preserve">
    <value>You have been logged out successfully.</value>
    <comment>Confirmation message after logout</comment>
  </data>
  
  <!-- Validation Messages -->
  <data name="RequiredField" xml:space="preserve">
    <value>This field is required.</value>
    <comment>Generic required field message</comment>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>Please enter a valid email address.</value>
    <comment>Displayed when email format is invalid</comment>
  </data>
  <data name="InvalidUsernameFormat" xml:space="preserve">
    <value>Username must be between {0} and {1} characters and contain only letters, numbers, and underscores.</value>
    <comment>Displayed when username format is invalid. {0} = min length, {1} = max length</comment>
  </data>

</root>
```

#### SharedTexts.ar.resx (Arabic)

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <!-- Schema definition same as default file -->
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms</value>
  </resheader>
  
  <!-- Authentication Messages -->
  <data name="InvalidCredentials" xml:space="preserve">
    <value>اسم المستخدم أو كلمة المرور غير صحيحة.</value>
  </data>
  <data name="UserBlocked" xml:space="preserve">
    <value>تم حظر حسابك. يرجى الاتصال بالدعم.</value>
  </data>
  <data name="TokenExpired" xml:space="preserve">
    <value>انتهت صلاحية جلستك. يرجى تسجيل الدخول مرة أخرى.</value>
  </data>
  <data name="InvalidRefreshToken" xml:space="preserve">
    <value>رمز التحديث غير صالح أو منتهي الصلاحية. يرجى تسجيل الدخول مرة أخرى.</value>
  </data>
  <data name="UserNotFound" xml:space="preserve">
    <value>المستخدم غير موجود.</value>
  </data>
  <data name="ActionNotAllowed" xml:space="preserve">
    <value>ليس لديك صلاحية للقيام بهذا الإجراء.</value>
  </data>
  <data name="UnexpectedError" xml:space="preserve">
    <value>حدث خطأ غير متوقع. يرجى المحاولة مرة أخرى لاحقاً.</value>
  </data>
  
  <!-- Account Management Messages -->
  <data name="AccountLocked" xml:space="preserve">
    <value>تم قفل حسابك بسبب محاولات تسجيل دخول فاشلة متعددة. يرجى المحاولة مرة أخرى بعد {0} دقيقة.</value>
  </data>
  <data name="PasswordResetRequired" xml:space="preserve">
    <value>يجب إعادة تعيين كلمة المرور قبل المتابعة.</value>
  </data>
  <data name="PasswordChangedSuccessfully" xml:space="preserve">
    <value>تم تغيير كلمة المرور بنجاح.</value>
  </data>
  <data name="PasswordTooWeak" xml:space="preserve">
    <value>كلمة المرور لا تستوفي متطلبات الأمان.</value>
  </data>
  <data name="PasswordHistoryViolation" xml:space="preserve">
    <value>لا يمكنك إعادة استخدام آخر {0} كلمات مرور.</value>
  </data>
  
  <!-- Two-Factor Authentication Messages -->
  <data name="TwoFactorRequired" xml:space="preserve">
    <value>المصادقة الثنائية مطلوبة.</value>
  </data>
  <data name="InvalidTwoFactorCode" xml:space="preserve">
    <value>رمز التحقق غير صالح أو منتهي الصلاحية.</value>
  </data>
  <data name="TwoFactorEnabledSuccessfully" xml:space="preserve">
    <value>تم تفعيل المصادقة الثنائية بنجاح.</value>
  </data>
  <data name="TwoFactorDisabledSuccessfully" xml:space="preserve">
    <value>تم تعطيل المصادقة الثنائية.</value>
  </data>
  
  <!-- API Key Messages -->
  <data name="InvalidApiKey" xml:space="preserve">
    <value>مفتاح API غير صالح أو تم إلغاؤه.</value>
  </data>
  <data name="ApiKeyExpired" xml:space="preserve">
    <value>انتهت صلاحية مفتاح API. يرجى طلب مفتاح جديد.</value>
  </data>
  <data name="ApiKeyRateLimitExceeded" xml:space="preserve">
    <value>تم تجاوز حد معدل استخدام مفتاح API. يرجى المحاولة لاحقاً.</value>
  </data>
  <data name="ApiKeyCreatedSuccessfully" xml:space="preserve">
    <value>تم إنشاء مفتاح API بنجاح.</value>
  </data>
  
  <!-- Session Messages -->
  <data name="SessionExpired" xml:space="preserve">
    <value>انتهت صلاحية جلستك. يرجى تسجيل الدخول مرة أخرى.</value>
  </data>
  <data name="ConcurrentSessionLimitReached" xml:space="preserve">
    <value>تم الوصول للحد الأقصى من الجلسات المتزامنة. يرجى تسجيل الخروج من جهاز آخر.</value>
  </data>
  <data name="LogoutSuccessful" xml:space="preserve">
    <value>تم تسجيل الخروج بنجاح.</value>
  </data>
  
  <!-- Validation Messages -->
  <data name="RequiredField" xml:space="preserve">
    <value>هذا الحقل مطلوب.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>يرجى إدخال بريد إلكتروني صالح.</value>
  </data>
  <data name="InvalidUsernameFormat" xml:space="preserve">
    <value>يجب أن يكون اسم المستخدم بين {0} و {1} حرفاً ويحتوي فقط على أحرف وأرقام وشرطات سفلية.</value>
  </data>

</root>
```

#### SharedTexts.tr.resx (Turkish)

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <!-- Schema definition same as default file -->
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms</value>
  </resheader>
  
  <!-- Authentication Messages -->
  <data name="InvalidCredentials" xml:space="preserve">
    <value>Kullanıcı adı veya şifre yanlış.</value>
  </data>
  <data name="UserBlocked" xml:space="preserve">
    <value>Hesabınız engellenmiştir. Lütfen destek ile iletişime geçin.</value>
  </data>
  <data name="TokenExpired" xml:space="preserve">
    <value>Oturumunuz sona erdi. Lütfen tekrar giriş yapın.</value>
  </data>
  <data name="InvalidRefreshToken" xml:space="preserve">
    <value>Geçersiz veya süresi dolmuş yenileme belirteci. Lütfen tekrar giriş yapın.</value>
  </data>
  <data name="UserNotFound" xml:space="preserve">
    <value>Kullanıcı bulunamadı.</value>
  </data>
  <data name="ActionNotAllowed" xml:space="preserve">
    <value>Bu işlemi gerçekleştirmek için yetkiniz yok.</value>
  </data>
  <data name="UnexpectedError" xml:space="preserve">
    <value>Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.</value>
  </data>
  
  <!-- Account Management Messages -->
  <data name="AccountLocked" xml:space="preserve">
    <value>Çok sayıda başarısız giriş denemesi nedeniyle hesabınız kilitlendi. Lütfen {0} dakika sonra tekrar deneyin.</value>
  </data>
  <data name="PasswordResetRequired" xml:space="preserve">
    <value>Devam etmeden önce şifrenizi sıfırlamalısınız.</value>
  </data>
  <data name="PasswordChangedSuccessfully" xml:space="preserve">
    <value>Şifreniz başarıyla değiştirildi.</value>
  </data>
  <data name="PasswordTooWeak" xml:space="preserve">
    <value>Şifre güvenlik gereksinimlerini karşılamıyor.</value>
  </data>
  <data name="PasswordHistoryViolation" xml:space="preserve">
    <value>Son {0} şifrenizi tekrar kullanamazsınız.</value>
  </data>
  
  <!-- Two-Factor Authentication Messages -->
  <data name="TwoFactorRequired" xml:space="preserve">
    <value>İki faktörlü kimlik doğrulama gereklidir.</value>
  </data>
  <data name="InvalidTwoFactorCode" xml:space="preserve">
    <value>Doğrulama kodu geçersiz veya süresi dolmuş.</value>
  </data>
  <data name="TwoFactorEnabledSuccessfully" xml:space="preserve">
    <value>İki faktörlü kimlik doğrulama başarıyla etkinleştirildi.</value>
  </data>
  <data name="TwoFactorDisabledSuccessfully" xml:space="preserve">
    <value>İki faktörlü kimlik doğrulama devre dışı bırakıldı.</value>
  </data>
  
  <!-- API Key Messages -->
  <data name="InvalidApiKey" xml:space="preserve">
    <value>API anahtarı geçersiz veya iptal edilmiş.</value>
  </data>
  <data name="ApiKeyExpired" xml:space="preserve">
    <value>API anahtarının süresi dolmuş. Lütfen yeni bir anahtar talep edin.</value>
  </data>
  <data name="ApiKeyRateLimitExceeded" xml:space="preserve">
    <value>API anahtarı hız limiti aşıldı. Lütfen daha sonra tekrar deneyin.</value>
  </data>
  <data name="ApiKeyCreatedSuccessfully" xml:space="preserve">
    <value>API anahtarı başarıyla oluşturuldu.</value>
  </data>
  
  <!-- Session Messages -->
  <data name="SessionExpired" xml:space="preserve">
    <value>Oturumunuz sona erdi. Lütfen tekrar giriş yapın.</value>
  </data>
  <data name="ConcurrentSessionLimitReached" xml:space="preserve">
    <value>Maksimum eşzamanlı oturum sayısına ulaşıldı. Lütfen başka bir cihazdan çıkış yapın.</value>
  </data>
  <data name="LogoutSuccessful" xml:space="preserve">
    <value>Başarıyla çıkış yaptınız.</value>
  </data>
  
  <!-- Validation Messages -->
  <data name="RequiredField" xml:space="preserve">
    <value>Bu alan zorunludur.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>Lütfen geçerli bir e-posta adresi girin.</value>
  </data>
  <data name="InvalidUsernameFormat" xml:space="preserve">
    <value>Kullanıcı adı {0} ile {1} karakter arasında olmalı ve yalnızca harf, rakam ve alt çizgi içermelidir.</value>
  </data>

</root>
```

### LocalizationService.cs

```csharp
using System.Globalization;
using Auth_Localization.Resources;
using Microsoft.Extensions.Localization;

namespace Auth_Localization.Services;

/// <summary>
/// Provides localized strings for the authentication system.
/// Supports both IStringLocalizer injection and direct static access.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IStringLocalizer<SharedTexts> _localizer;

    public LocalizationService(IStringLocalizer<SharedTexts> localizer)
    {
        _localizer = localizer;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authentication Messages
    // ═══════════════════════════════════════════════════════════════════════════

    public string InvalidCredentials => _localizer[nameof(SharedTexts.InvalidCredentials)];
    public string UserBlocked => _localizer[nameof(SharedTexts.UserBlocked)];
    public string TokenExpired => _localizer[nameof(SharedTexts.TokenExpired)];
    public string InvalidRefreshToken => _localizer[nameof(SharedTexts.InvalidRefreshToken)];
    public string UserNotFound => _localizer[nameof(SharedTexts.UserNotFound)];
    public string ActionNotAllowed => _localizer[nameof(SharedTexts.ActionNotAllowed)];
    public string UnexpectedError => _localizer[nameof(SharedTexts.UnexpectedError)];

    // ═══════════════════════════════════════════════════════════════════════════
    // Account Management Messages
    // ═══════════════════════════════════════════════════════════════════════════

    public string AccountLocked(int minutes) => 
        string.Format(_localizer[nameof(SharedTexts.AccountLocked)], minutes);
    
    public string PasswordResetRequired => _localizer[nameof(SharedTexts.PasswordResetRequired)];
    public string PasswordChangedSuccessfully => _localizer[nameof(SharedTexts.PasswordChangedSuccessfully)];
    public string PasswordTooWeak => _localizer[nameof(SharedTexts.PasswordTooWeak)];
    
    public string PasswordHistoryViolation(int count) => 
        string.Format(_localizer[nameof(SharedTexts.PasswordHistoryViolation)], count);

    // ═══════════════════════════════════════════════════════════════════════════
    // Two-Factor Authentication Messages
    // ═══════════════════════════════════════════════════════════════════════════

    public string TwoFactorRequired => _localizer[nameof(SharedTexts.TwoFactorRequired)];
    public string InvalidTwoFactorCode => _localizer[nameof(SharedTexts.InvalidTwoFactorCode)];
    public string TwoFactorEnabledSuccessfully => _localizer[nameof(SharedTexts.TwoFactorEnabledSuccessfully)];
    public string TwoFactorDisabledSuccessfully => _localizer[nameof(SharedTexts.TwoFactorDisabledSuccessfully)];

    // ═══════════════════════════════════════════════════════════════════════════
    // API Key Messages
    // ═══════════════════════════════════════════════════════════════════════════

    public string InvalidApiKey => _localizer[nameof(SharedTexts.InvalidApiKey)];
    public string ApiKeyExpired => _localizer[nameof(SharedTexts.ApiKeyExpired)];
    public string ApiKeyRateLimitExceeded => _localizer[nameof(SharedTexts.ApiKeyRateLimitExceeded)];
    public string ApiKeyCreatedSuccessfully => _localizer[nameof(SharedTexts.ApiKeyCreatedSuccessfully)];

    // ═══════════════════════════════════════════════════════════════════════════
    // Session Messages
    // ═══════════════════════════════════════════════════════════════════════════

    public string SessionExpired => _localizer[nameof(SharedTexts.SessionExpired)];
    public string ConcurrentSessionLimitReached => _localizer[nameof(SharedTexts.ConcurrentSessionLimitReached)];
    public string LogoutSuccessful => _localizer[nameof(SharedTexts.LogoutSuccessful)];

    // ═══════════════════════════════════════════════════════════════════════════
    // Validation Messages
    // ═══════════════════════════════════════════════════════════════════════════

    public string RequiredField => _localizer[nameof(SharedTexts.RequiredField)];
    public string InvalidEmailFormat => _localizer[nameof(SharedTexts.InvalidEmailFormat)];
    
    public string InvalidUsernameFormat(int minLength, int maxLength) => 
        string.Format(_localizer[nameof(SharedTexts.InvalidUsernameFormat)], minLength, maxLength);

    // ═══════════════════════════════════════════════════════════════════════════
    // Generic Access
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets a localized string by key name.
    /// </summary>
    public string GetString(string key) => _localizer[key];

    /// <summary>
    /// Gets a localized string by key name with format arguments.
    /// </summary>
    public string GetString(string key, params object[] args) => 
        string.Format(_localizer[key], args);
}

/// <summary>
/// Interface for localization service to enable dependency injection and testing.
/// </summary>
public interface ILocalizationService
{
    // Authentication
    string InvalidCredentials { get; }
    string UserBlocked { get; }
    string TokenExpired { get; }
    string InvalidRefreshToken { get; }
    string UserNotFound { get; }
    string ActionNotAllowed { get; }
    string UnexpectedError { get; }

    // Account Management
    string AccountLocked(int minutes);
    string PasswordResetRequired { get; }
    string PasswordChangedSuccessfully { get; }
    string PasswordTooWeak { get; }
    string PasswordHistoryViolation(int count);

    // Two-Factor
    string TwoFactorRequired { get; }
    string InvalidTwoFactorCode { get; }
    string TwoFactorEnabledSuccessfully { get; }
    string TwoFactorDisabledSuccessfully { get; }

    // API Keys
    string InvalidApiKey { get; }
    string ApiKeyExpired { get; }
    string ApiKeyRateLimitExceeded { get; }
    string ApiKeyCreatedSuccessfully { get; }

    // Sessions
    string SessionExpired { get; }
    string ConcurrentSessionLimitReached { get; }
    string LogoutSuccessful { get; }

    // Validation
    string RequiredField { get; }
    string InvalidEmailFormat { get; }
    string InvalidUsernameFormat(int minLength, int maxLength);

    // Generic
    string GetString(string key);
    string GetString(string key, params object[] args);
}
```

### LocalizationExtensions.cs

```csharp
using Auth_Localization.Resources;
using Auth_Localization.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Auth_Localization.Extensions;

/// <summary>
/// Extension methods for registering localization services.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// Adds Auth_Localization services to the service collection.
    /// </summary>
    public static IServiceCollection AddAuthLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddScoped<ILocalizationService, LocalizationService>();
        
        return services;
    }

    /// <summary>
    /// Adds Auth_Localization with custom supported cultures.
    /// </summary>
    public static IServiceCollection AddAuthLocalization(
        this IServiceCollection services,
        string[] supportedCultures)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddScoped<ILocalizationService, LocalizationService>();
        
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = supportedCultures.Select(c => new CultureInfo(c)).ToArray();
            options.DefaultRequestCulture = new RequestCulture(cultures[0]);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
        });
        
        return services;
    }
}
```


---

## Auth_Lib Project (Core Business Logic)

### Overview

Auth_Lib contains the domain models, business logic, and foundation code for the authentication system. In v9, it now includes the foundation classes that were previously in the removed Foundation_Lib project.

### Project File (Auth_Lib.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>Auth_Lib</RootNamespace>
    <AssemblyName>Auth_Lib</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- v9: Using NuGet packages instead of custom Foundation_Lib -->
    <PackageReference Include="ErrorOr" Version="2.*" />
    <PackageReference Include="Ardalis.GuardClauses" Version="5.*" />
    <PackageReference Include="MediatR" Version="12.*" />
    <PackageReference Include="FluentValidation" Version="11.*" />
    <PackageReference Include="Dapper" Version="2.*" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />
    <PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Auth_Localization\Auth_Localization.csproj" />
  </ItemGroup>

</Project>
```

### Complete Project Structure

```
Auth_Lib/
├── Foundation/                           # v9: Moved from Foundation_Lib
│   ├── EntityBase.cs                     # Base entity with Id
│   ├── AuditableEntityBase.cs            # Entity with audit fields
│   ├── IAuditableEntity.cs               # Audit interface
│   └── IRepository.cs                    # Generic repository interface
│
├── Domain/
│   ├── Entities/
│   │   ├── User.cs
│   │   ├── Role.cs
│   │   ├── Permission.cs
│   │   ├── UserRole.cs
│   │   ├── UserPermission.cs
│   │   ├── RolePermission.cs
│   │   ├── PermissionImplication.cs
│   │   ├── Application.cs
│   │   ├── RefreshToken.cs
│   │   ├── UserSession.cs
│   │   ├── ApiKey.cs
│   │   ├── ApiKeyScope.cs
│   │   ├── LoginAttempt.cs
│   │   ├── TwoFactorAuth.cs
│   │   ├── AuditLog.cs
│   │   ├── SecurityEvent.cs
│   │   ├── PasswordHistory.cs
│   │   ├── UserLockout.cs
│   │   └── IpWhitelist.cs
│   │
│   ├── Enums/
│   │   ├── UserStatus.cs
│   │   ├── TokenType.cs
│   │   ├── AuditAction.cs
│   │   ├── SecurityEventType.cs
│   │   └── PermissionLevel.cs
│   │
│   ├── ValueObjects/
│   │   ├── Email.cs
│   │   ├── Username.cs
│   │   ├── HashedPassword.cs
│   │   └── IpAddress.cs
│   │
│   └── Events/                           # v9: MediatR INotification
│       ├── UserCreatedEvent.cs
│       ├── UserUpdatedEvent.cs
│       ├── UserDeletedEvent.cs
│       ├── UserLoggedInEvent.cs
│       ├── UserLoggedOutEvent.cs
│       ├── UserLockedOutEvent.cs
│       ├── PasswordChangedEvent.cs
│       ├── TwoFactorEnabledEvent.cs
│       ├── RoleAssignedEvent.cs
│       ├── PermissionGrantedEvent.cs
│       └── ApiKeyCreatedEvent.cs
│
├── Application/
│   ├── Interfaces/
│   │   ├── IUserRepository.cs
│   │   ├── IRoleRepository.cs
│   │   ├── IPermissionRepository.cs
│   │   ├── IApplicationRepository.cs
│   │   ├── IRefreshTokenRepository.cs
│   │   ├── ISessionRepository.cs
│   │   ├── IApiKeyRepository.cs
│   │   ├── IAuditLogRepository.cs
│   │   ├── IPasswordHasher.cs
│   │   ├── IJwtTokenService.cs
│   │   ├── IPermissionChecker.cs
│   │   └── ICurrentUserService.cs
│   │
│   ├── DTOs/
│   │   ├── UserDto.cs
│   │   ├── RoleDto.cs
│   │   ├── PermissionDto.cs
│   │   ├── TokenDto.cs
│   │   ├── LoginResultDto.cs
│   │   └── AuditLogDto.cs
│   │
│   ├── Commands/                         # v9: MediatR IRequest
│   │   ├── Authentication/
│   │   │   ├── LoginCommand.cs
│   │   │   ├── LogoutCommand.cs
│   │   │   ├── RefreshTokenCommand.cs
│   │   │   └── ValidateTokenCommand.cs
│   │   ├── Users/
│   │   │   ├── CreateUserCommand.cs
│   │   │   ├── UpdateUserCommand.cs
│   │   │   ├── DeleteUserCommand.cs
│   │   │   ├── ChangePasswordCommand.cs
│   │   │   └── ResetPasswordCommand.cs
│   │   ├── Roles/
│   │   │   ├── CreateRoleCommand.cs
│   │   │   ├── UpdateRoleCommand.cs
│   │   │   ├── DeleteRoleCommand.cs
│   │   │   ├── AssignRoleToUserCommand.cs
│   │   │   └── RemoveRoleFromUserCommand.cs
│   │   ├── Permissions/
│   │   │   ├── CreatePermissionCommand.cs
│   │   │   ├── GrantPermissionCommand.cs
│   │   │   └── RevokePermissionCommand.cs
│   │   └── ApiKeys/
│   │       ├── CreateApiKeyCommand.cs
│   │       ├── RevokeApiKeyCommand.cs
│   │       └── RotateApiKeyCommand.cs
│   │
│   ├── Queries/                          # v9: MediatR IRequest
│   │   ├── Users/
│   │   │   ├── GetUserByIdQuery.cs
│   │   │   ├── GetUserByEmailQuery.cs
│   │   │   ├── GetUsersQuery.cs
│   │   │   └── GetUserPermissionsQuery.cs
│   │   ├── Roles/
│   │   │   ├── GetRoleByIdQuery.cs
│   │   │   └── GetRolesQuery.cs
│   │   ├── Permissions/
│   │   │   ├── GetPermissionsQuery.cs
│   │   │   └── CheckPermissionQuery.cs
│   │   └── Audit/
│   │       ├── GetAuditLogsQuery.cs
│   │       └── GetUserActivityQuery.cs
│   │
│   ├── Validators/                       # FluentValidation
│   │   ├── LoginCommandValidator.cs
│   │   ├── CreateUserCommandValidator.cs
│   │   ├── ChangePasswordCommandValidator.cs
│   │   └── CreateApiKeyCommandValidator.cs
│   │
│   └── Behaviors/                        # v9: MediatR Pipeline Behaviors
│       ├── ValidationBehavior.cs
│       ├── LoggingBehavior.cs
│       └── AuthorizationBehavior.cs
│
├── Infrastructure/
│   ├── Security/
│   │   ├── Argon2PasswordHasher.cs
│   │   ├── JwtTokenService.cs
│   │   ├── PermissionChecker.cs
│   │   └── SecurityConstants.cs
│   │
│   ├── Repositories/                     # Dapper implementations
│   │   ├── UserRepository.cs
│   │   ├── RoleRepository.cs
│   │   ├── PermissionRepository.cs
│   │   ├── ApplicationRepository.cs
│   │   ├── RefreshTokenRepository.cs
│   │   ├── SessionRepository.cs
│   │   ├── ApiKeyRepository.cs
│   │   └── AuditLogRepository.cs
│   │
│   └── Data/
│       ├── DbConnectionFactory.cs
│       └── DapperExtensions.cs
│
├── Errors/                               # v9: ErrorOr error definitions
│   ├── AuthErrors.cs
│   ├── UserErrors.cs
│   ├── RoleErrors.cs
│   ├── PermissionErrors.cs
│   └── ApiKeyErrors.cs
│
└── Auth_Lib.csproj
```

### Foundation Classes (v9 - Moved from Foundation_Lib)

**EntityBase.cs**
```csharp
namespace Auth_Lib.Foundation;

/// <summary>
/// Base entity with GUID identifier.
/// </summary>
public abstract class EntityBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public override bool Equals(object? obj)
    {
        if (obj is not EntityBase other)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (GetType() != other.GetType())
            return false;

        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(EntityBase? left, EntityBase? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(EntityBase? left, EntityBase? right) => !(left == right);
}
```

**AuditableEntityBase.cs**
```csharp
namespace Auth_Lib.Foundation;

/// <summary>
/// Entity with audit tracking fields.
/// </summary>
public abstract class AuditableEntityBase : EntityBase, IAuditableEntity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    Guid CreatedBy { get; set; }
    DateTime? ModifiedAt { get; set; }
    Guid? ModifiedBy { get; set; }
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
```

**IRepository.cs**
```csharp
namespace Auth_Lib.Foundation;

/// <summary>
/// Generic repository interface.
/// </summary>
public interface IRepository<T> where T : EntityBase
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Guid> AddAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
```

---

### Data Access Strategy (CRITICAL - Dapper Only, NO Entity Framework!)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DATA ACCESS STRATEGY - DAPPER ONLY                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ⛔ DO NOT USE ENTITY FRAMEWORK CORE                                        │
│                                                                             │
│  This project uses DAPPER exclusively for data access because:              │
│                                                                             │
│  1. PERFORMANCE: Dapper is significantly faster than EF Core                │
│  2. CONTROL: Direct SQL/stored procedures give precise query control        │
│  3. SECURITY: Stored procedures provide additional security layer           │
│  4. SIMPLICITY: No complex change tracking or lazy loading issues           │
│  5. DATABASE-FIRST: Auth_DB is an SSDT project with full schema control     │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│  DATA ACCESS RULES:                                                         │
│  ──────────────────                                                         │
│  ✅ Use IDbConnection (injected via DI)                                     │
│  ✅ Use Dapper extension methods (Query, Execute, etc.)                     │
│  ✅ Use stored procedures for complex queries                               │
│  ✅ Use parameterized queries for simple operations                         │
│  ✅ Use transactions via IDbTransaction when needed                         │
│                                                                             │
│  ❌ NO DbContext                                                            │
│  ❌ NO Entity Framework packages                                            │
│  ❌ NO migrations (use Auth_DB SSDT project instead)                        │
│  ❌ NO LINQ-to-SQL                                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Required NuGet Packages for Data Access:**

```xml
<!-- In Auth_Lib.csproj -->
<PackageReference Include="Dapper" Version="2.*" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />
```

**Connection Registration in Program.cs:**

```csharp
// Register IDbConnection for Dapper
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("AuthDb")
        ?? throw new InvalidOperationException("AuthDb connection string not configured");
    return new SqlConnection(connectionString);
});
```

### Repository Implementation Example: UserRepository (Dapper)

```csharp
// Infrastructure/Data/Repositories/UserRepository.cs
using System.Data;
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Dapper;

namespace Auth_Lib.Infrastructure.Data.Repositories;

/// <summary>
/// User repository implementation using Dapper.
/// NO Entity Framework - uses stored procedures and parameterized SQL.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly IDbConnection _connection;

    public UserRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, Username, NormalizedUsername, Email, NormalizedEmail, 
                   PasswordHash, FullName, Status, IsTwoFactorEnabled,
                   TwoFactorSecret, FailedLoginAttempts, LockoutEndUtc,
                   MustChangePassword, PasswordChangedAt, LastLoginUtc,
                   LastLoginIp, SecurityStamp, CreatedAt, CreatedBy,
                   ModifiedAt, ModifiedBy, IsDeleted
            FROM Users
            WHERE Id = @Id AND IsDeleted = 0
            """;

        var command = new CommandDefinition(sql, new { Id = id }, cancellationToken: cancellationToken);
        return await _connection.QuerySingleOrDefaultAsync<User>(command);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, Username, NormalizedUsername, Email, NormalizedEmail, 
                   PasswordHash, FullName, Status, IsTwoFactorEnabled,
                   TwoFactorSecret, FailedLoginAttempts, LockoutEndUtc,
                   MustChangePassword, PasswordChangedAt, LastLoginUtc,
                   LastLoginIp, SecurityStamp, CreatedAt, CreatedBy,
                   ModifiedAt, ModifiedBy, IsDeleted
            FROM Users
            WHERE NormalizedEmail = @NormalizedEmail AND IsDeleted = 0
            """;

        var normalizedEmail = email.ToUpperInvariant();
        var command = new CommandDefinition(sql, new { NormalizedEmail = normalizedEmail }, cancellationToken: cancellationToken);
        return await _connection.QuerySingleOrDefaultAsync<User>(command);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, Username, NormalizedUsername, Email, NormalizedEmail, 
                   PasswordHash, FullName, Status, IsTwoFactorEnabled,
                   TwoFactorSecret, FailedLoginAttempts, LockoutEndUtc,
                   MustChangePassword, PasswordChangedAt, LastLoginUtc,
                   LastLoginIp, SecurityStamp, CreatedAt, CreatedBy,
                   ModifiedAt, ModifiedBy, IsDeleted
            FROM Users
            WHERE NormalizedUsername = @NormalizedUsername AND IsDeleted = 0
            """;

        var normalizedUsername = username.ToUpperInvariant();
        var command = new CommandDefinition(sql, new { NormalizedUsername = normalizedUsername }, cancellationToken: cancellationToken);
        return await _connection.QuerySingleOrDefaultAsync<User>(command);
    }

    public async Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Id, Username, NormalizedUsername, Email, NormalizedEmail, 
                   PasswordHash, FullName, Status, IsTwoFactorEnabled,
                   TwoFactorSecret, FailedLoginAttempts, LockoutEndUtc,
                   MustChangePassword, PasswordChangedAt, LastLoginUtc,
                   LastLoginIp, SecurityStamp, CreatedAt, CreatedBy,
                   ModifiedAt, ModifiedBy, IsDeleted
            FROM Users
            WHERE IsDeleted = 0
            ORDER BY Username
            """;

        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        return await _connection.QueryAsync<User>(command);
    }

    public async Task<(IEnumerable<User> Users, int TotalCount)> GetPagedAsync(
        int page, 
        int pageSize, 
        string? searchTerm = null,
        CancellationToken cancellationToken = default)
    {
        var offset = (page - 1) * pageSize;
        
        var sql = """
            SELECT Id, Username, NormalizedUsername, Email, NormalizedEmail, 
                   PasswordHash, FullName, Status, IsTwoFactorEnabled,
                   TwoFactorSecret, FailedLoginAttempts, LockoutEndUtc,
                   MustChangePassword, PasswordChangedAt, LastLoginUtc,
                   LastLoginIp, SecurityStamp, CreatedAt, CreatedBy,
                   ModifiedAt, ModifiedBy, IsDeleted
            FROM Users
            WHERE IsDeleted = 0
            """;
        
        var countSql = "SELECT COUNT(*) FROM Users WHERE IsDeleted = 0";
        
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var searchFilter = " AND (Username LIKE @Search OR Email LIKE @Search OR FullName LIKE @Search)";
            sql += searchFilter;
            countSql += searchFilter;
        }
        
        sql += " ORDER BY Username OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var parameters = new
        {
            Offset = offset,
            PageSize = pageSize,
            Search = $"%{searchTerm}%"
        };

        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var countCommand = new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken);

        var users = await _connection.QueryAsync<User>(command);
        var totalCount = await _connection.ExecuteScalarAsync<int>(countCommand);

        return (users, totalCount);
    }

    public async Task<Guid> AddAsync(User entity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO Users (
                Id, Username, NormalizedUsername, Email, NormalizedEmail,
                PasswordHash, FullName, Status, IsTwoFactorEnabled,
                TwoFactorSecret, FailedLoginAttempts, LockoutEndUtc,
                MustChangePassword, PasswordChangedAt, LastLoginUtc,
                LastLoginIp, SecurityStamp, CreatedAt, CreatedBy,
                ModifiedAt, ModifiedBy, IsDeleted
            ) VALUES (
                @Id, @Username, @NormalizedUsername, @Email, @NormalizedEmail,
                @PasswordHash, @FullName, @Status, @IsTwoFactorEnabled,
                @TwoFactorSecret, @FailedLoginAttempts, @LockoutEndUtc,
                @MustChangePassword, @PasswordChangedAt, @LastLoginUtc,
                @LastLoginIp, @SecurityStamp, @CreatedAt, @CreatedBy,
                @ModifiedAt, @ModifiedBy, @IsDeleted
            )
            """;

        // Ensure ID is set
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        // Normalize values
        entity.NormalizedUsername = entity.Username.ToUpperInvariant();
        entity.NormalizedEmail = entity.Email.ToUpperInvariant();
        entity.CreatedAt = DateTime.UtcNow;

        var command = new CommandDefinition(sql, entity, cancellationToken: cancellationToken);
        await _connection.ExecuteAsync(command);

        return entity.Id;
    }

    public async Task UpdateAsync(User entity, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE Users SET
                Username = @Username,
                NormalizedUsername = @NormalizedUsername,
                Email = @Email,
                NormalizedEmail = @NormalizedEmail,
                PasswordHash = @PasswordHash,
                FullName = @FullName,
                Status = @Status,
                IsTwoFactorEnabled = @IsTwoFactorEnabled,
                TwoFactorSecret = @TwoFactorSecret,
                FailedLoginAttempts = @FailedLoginAttempts,
                LockoutEndUtc = @LockoutEndUtc,
                MustChangePassword = @MustChangePassword,
                PasswordChangedAt = @PasswordChangedAt,
                LastLoginUtc = @LastLoginUtc,
                LastLoginIp = @LastLoginIp,
                SecurityStamp = @SecurityStamp,
                ModifiedAt = @ModifiedAt,
                ModifiedBy = @ModifiedBy
            WHERE Id = @Id AND IsDeleted = 0
            """;

        entity.NormalizedUsername = entity.Username.ToUpperInvariant();
        entity.NormalizedEmail = entity.Email.ToUpperInvariant();
        entity.ModifiedAt = DateTime.UtcNow;

        var command = new CommandDefinition(sql, entity, cancellationToken: cancellationToken);
        await _connection.ExecuteAsync(command);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Soft delete
        const string sql = """
            UPDATE Users SET
                IsDeleted = 1,
                ModifiedAt = @ModifiedAt
            WHERE Id = @Id
            """;

        var command = new CommandDefinition(sql, new { Id = id, ModifiedAt = DateTime.UtcNow }, cancellationToken: cancellationToken);
        await _connection.ExecuteAsync(command);
    }

    // ============================================
    // STORED PROCEDURE EXAMPLES
    // ============================================

    public async Task<IEnumerable<string>> GetUserEffectivePermissionsAsync(
        Guid userId, 
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        // Uses stored procedure for complex permission calculation
        var command = new CommandDefinition(
            "sp_GetUserEffectivePermissions",
            new { UserId = userId, ApplicationId = applicationId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var result = await _connection.QueryAsync<PermissionResult>(command);
        return result.Select(r => r.Permission);
    }

    public async Task<User?> ValidateCredentialsAsync(
        string usernameOrEmail,
        CancellationToken cancellationToken = default)
    {
        // Uses stored procedure for credential validation
        var command = new CommandDefinition(
            "sp_ValidateCredentials",
            new { UsernameOrEmail = usernameOrEmail },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await _connection.QuerySingleOrDefaultAsync<User>(command);
    }

    public async Task RecordLoginAttemptAsync(
        Guid? userId,
        string username,
        string ipAddress,
        string? userAgent,
        bool isSuccessful,
        string? failureReason = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "sp_RecordLoginAttempt",
            new 
            { 
                UserId = userId, 
                Username = username,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                IsSuccessful = isSuccessful,
                FailureReason = failureReason
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await _connection.ExecuteAsync(command);
    }

    public async Task<LockoutInfo> CheckAccountLockoutAsync(
        Guid userId,
        int maxAttempts = 5,
        int lockoutMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var command = new CommandDefinition(
            "sp_CheckAccountLockout",
            new { UserId = userId, MaxAttempts = maxAttempts, LockoutMinutes = lockoutMinutes },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        return await _connection.QuerySingleAsync<LockoutInfo>(command);
    }

    // ============================================
    // TRANSACTION EXAMPLE
    // ============================================

    public async Task<bool> UpdateUserWithRolesAsync(
        User user, 
        IEnumerable<Guid> roleIds,
        Guid modifiedBy,
        CancellationToken cancellationToken = default)
    {
        // Open connection if not already open
        if (_connection.State != ConnectionState.Open)
            _connection.Open();

        using var transaction = _connection.BeginTransaction();

        try
        {
            // Update user
            const string updateUserSql = """
                UPDATE Users SET
                    FullName = @FullName,
                    Status = @Status,
                    ModifiedAt = @ModifiedAt,
                    ModifiedBy = @ModifiedBy
                WHERE Id = @Id
                """;

            await _connection.ExecuteAsync(
                updateUserSql,
                new { user.Id, user.FullName, user.Status, ModifiedAt = DateTime.UtcNow, ModifiedBy = modifiedBy },
                transaction);

            // Remove existing roles
            await _connection.ExecuteAsync(
                "DELETE FROM UserRoles WHERE UserId = @UserId",
                new { UserId = user.Id },
                transaction);

            // Add new roles
            const string insertRoleSql = """
                INSERT INTO UserRoles (UserId, RoleId, AssignedAt, AssignedBy, IsActive)
                VALUES (@UserId, @RoleId, @AssignedAt, @AssignedBy, 1)
                """;

            foreach (var roleId in roleIds)
            {
                await _connection.ExecuteAsync(
                    insertRoleSql,
                    new { UserId = user.Id, RoleId = roleId, AssignedAt = DateTime.UtcNow, AssignedBy = modifiedBy },
                    transaction);
            }

            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // Helper classes for stored procedure results
    private record PermissionResult(string Permission, string GrantedVia, Guid? ApplicationId);
}

public record LockoutInfo(bool IsLocked, DateTime? LockoutEndUtc, int MinutesRemaining);
```

### IUserRepository Interface

```csharp
// Application/Interfaces/IUserRepository.cs
using Auth_Lib.Domain.Entities;

namespace Auth_Lib.Application.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<(IEnumerable<User> Users, int TotalCount)> GetPagedAsync(
        int page, int pageSize, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetUserEffectivePermissionsAsync(
        Guid userId, Guid? applicationId = null, CancellationToken cancellationToken = default);
    Task<User?> ValidateCredentialsAsync(string usernameOrEmail, CancellationToken cancellationToken = default);
    Task RecordLoginAttemptAsync(
        Guid? userId, string username, string ipAddress, string? userAgent,
        bool isSuccessful, string? failureReason = null, CancellationToken cancellationToken = default);
    Task<LockoutInfo> CheckAccountLockoutAsync(
        Guid userId, int maxAttempts = 5, int lockoutMinutes = 15, CancellationToken cancellationToken = default);
    Task<bool> UpdateUserWithRolesAsync(User user, IEnumerable<Guid> roleIds, Guid modifiedBy, CancellationToken cancellationToken = default);
}
```

---

### Using ErrorOr Instead of Custom Result Pattern

**v9 Change**: We now use the `ErrorOr` NuGet package instead of custom `Result<T>`.

**Errors/UserErrors.cs**
```csharp
using ErrorOr;

namespace Auth_Lib.Errors;

/// <summary>
/// User-related error definitions using ErrorOr.
/// </summary>
public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        code: "User.NotFound",
        description: $"User with ID '{userId}' was not found.");

    public static Error NotFoundByEmail(string email) => Error.NotFound(
        code: "User.NotFoundByEmail",
        description: $"User with email '{email}' was not found.");

    public static Error DuplicateEmail(string email) => Error.Conflict(
        code: "User.DuplicateEmail",
        description: $"A user with email '{email}' already exists.");

    public static Error DuplicateUsername(string username) => Error.Conflict(
        code: "User.DuplicateUsername",
        description: $"A user with username '{username}' already exists.");

    public static Error InvalidCredentials => Error.Validation(
        code: "User.InvalidCredentials",
        description: "The provided credentials are invalid.");

    public static Error AccountLocked(DateTime unlockTime) => Error.Forbidden(
        code: "User.AccountLocked",
        description: $"Account is locked until {unlockTime:u}.");

    public static Error AccountBlocked => Error.Forbidden(
        code: "User.AccountBlocked",
        description: "This account has been blocked.");

    public static Error Inactive => Error.Forbidden(
        code: "User.Inactive",
        description: "This account is inactive.");

    public static Error PasswordTooWeak => Error.Validation(
        code: "User.PasswordTooWeak",
        description: "Password does not meet complexity requirements.");

    public static Error PasswordHistoryViolation => Error.Validation(
        code: "User.PasswordHistoryViolation",
        description: "Cannot reuse recent passwords.");
}
```

**Errors/AuthErrors.cs**
```csharp
using ErrorOr;

namespace Auth_Lib.Errors;

/// <summary>
/// Authentication-related error definitions.
/// </summary>
public static class AuthErrors
{
    public static Error InvalidToken => Error.Unauthorized(
        code: "Auth.InvalidToken",
        description: "The provided token is invalid.");

    public static Error TokenExpired => Error.Unauthorized(
        code: "Auth.TokenExpired",
        description: "The token has expired.");

    public static Error InvalidRefreshToken => Error.Unauthorized(
        code: "Auth.InvalidRefreshToken",
        description: "The refresh token is invalid or has been revoked.");

    public static Error TwoFactorRequired => Error.Validation(
        code: "Auth.TwoFactorRequired",
        description: "Two-factor authentication is required.");

    public static Error InvalidTwoFactorCode => Error.Validation(
        code: "Auth.InvalidTwoFactorCode",
        description: "The two-factor code is invalid or expired.");

    public static Error SessionExpired => Error.Unauthorized(
        code: "Auth.SessionExpired",
        description: "Your session has expired.");

    public static Error ConcurrentSessionLimit => Error.Forbidden(
        code: "Auth.ConcurrentSessionLimit",
        description: "Maximum concurrent sessions reached.");
}
```

**Example Service Using ErrorOr**
```csharp
using ErrorOr;
using Auth_Lib.Application.Commands.Users;
using Auth_Lib.Application.DTOs;
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Errors;
using MediatR;

namespace Auth_Lib.Application.Handlers;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, ErrorOr<UserDto>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMediator _mediator;

    public CreateUserCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IMediator mediator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _mediator = mediator;
    }

    public async Task<ErrorOr<UserDto>> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // Check for duplicate email
        var existingUser = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existingUser is not null)
        {
            return UserErrors.DuplicateEmail(request.Email);
        }

        // Check for duplicate username
        var existingUsername = await _userRepository.GetByUsernameAsync(request.Username, cancellationToken);
        if (existingUsername is not null)
        {
            return UserErrors.DuplicateUsername(request.Username);
        }

        // Create user
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            NormalizedEmail = request.Email.ToUpperInvariant(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedBy = request.CreatedBy
        };

        await _userRepository.AddAsync(user, cancellationToken);

        // Publish domain event
        await _mediator.Publish(new UserCreatedEvent(user.Id, user.Username, user.Email), cancellationToken);

        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            IsActive = true
        };
    }
}
```

### Using MediatR for Domain Events

**v9 Change**: We now use MediatR instead of custom MessagingHub.

**Domain/Events/UserCreatedEvent.cs**
```csharp
using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a new user is created.
/// </summary>
public record UserCreatedEvent(
    Guid UserId,
    string Username,
    string Email) : INotification;
```

**Domain/Events/UserLoggedInEvent.cs**
```csharp
using MediatR;

namespace Auth_Lib.Domain.Events;

/// <summary>
/// Published when a user successfully logs in.
/// </summary>
public record UserLoggedInEvent(
    Guid UserId,
    string Username,
    string IpAddress,
    string UserAgent,
    Guid SessionId) : INotification;
```

**Event Handler Example (AuditLog Module)**
```csharp
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Enums;
using Auth_Lib.Domain.Events;
using MediatR;

namespace Auth_API.Modules.AuditLog.EventHandlers;

/// <summary>
/// Handles all domain events and creates audit log entries.
/// </summary>
public class AuditEventHandler :
    INotificationHandler<UserCreatedEvent>,
    INotificationHandler<UserLoggedInEvent>,
    INotificationHandler<UserLoggedOutEvent>,
    INotificationHandler<PasswordChangedEvent>,
    INotificationHandler<RoleAssignedEvent>
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AuditEventHandler(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task Handle(UserCreatedEvent notification, CancellationToken cancellationToken)
    {
        var log = new AuditLogEntry
        {
            Action = AuditAction.UserCreated,
            UserId = notification.UserId,
            Details = $"User '{notification.Username}' created with email '{notification.Email}'",
            Timestamp = DateTime.UtcNow
        };
        await _auditLogRepository.AddAsync(log, cancellationToken);
    }

    public async Task Handle(UserLoggedInEvent notification, CancellationToken cancellationToken)
    {
        var log = new AuditLogEntry
        {
            Action = AuditAction.Login,
            UserId = notification.UserId,
            IpAddress = notification.IpAddress,
            UserAgent = notification.UserAgent,
            SessionId = notification.SessionId,
            Details = $"User '{notification.Username}' logged in",
            Timestamp = DateTime.UtcNow
        };
        await _auditLogRepository.AddAsync(log, cancellationToken);
    }

    public async Task Handle(UserLoggedOutEvent notification, CancellationToken cancellationToken)
    {
        var log = new AuditLogEntry
        {
            Action = AuditAction.Logout,
            UserId = notification.UserId,
            SessionId = notification.SessionId,
            Details = "User logged out",
            Timestamp = DateTime.UtcNow
        };
        await _auditLogRepository.AddAsync(log, cancellationToken);
    }

    public async Task Handle(PasswordChangedEvent notification, CancellationToken cancellationToken)
    {
        var log = new AuditLogEntry
        {
            Action = AuditAction.PasswordChanged,
            UserId = notification.UserId,
            Details = notification.WasReset ? "Password was reset" : "Password was changed",
            Timestamp = DateTime.UtcNow
        };
        await _auditLogRepository.AddAsync(log, cancellationToken);
    }

    public async Task Handle(RoleAssignedEvent notification, CancellationToken cancellationToken)
    {
        var log = new AuditLogEntry
        {
            Action = AuditAction.RoleAssigned,
            UserId = notification.UserId,
            PerformedBy = notification.AssignedBy,
            Details = $"Role '{notification.RoleName}' assigned to user",
            Timestamp = DateTime.UtcNow
        };
        await _auditLogRepository.AddAsync(log, cancellationToken);
    }
}
```

### MediatR Pipeline Behaviors

**Application/Behaviors/ValidationBehavior.cs**
```csharp
using ErrorOr;
using FluentValidation;
using MediatR;

namespace Auth_Lib.Application.Behaviors;

/// <summary>
/// Pipeline behavior that validates requests before handling.
/// </summary>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IErrorOr
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .Select(f => Error.Validation(f.PropertyName, f.ErrorMessage))
            .ToList();

        if (errors.Any())
        {
            return (dynamic)errors;
        }

        return await next();
    }
}
```

**Application/Behaviors/LoggingBehavior.cs**
```csharp
using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth_Lib.Application.Behaviors;

/// <summary>
/// Pipeline behavior that logs request handling.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        
        _logger.LogInformation("Handling {RequestName}", requestName);
        
        var stopwatch = Stopwatch.StartNew();
        var response = await next();
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "Long running request: {RequestName} ({ElapsedMilliseconds}ms)",
                requestName,
                stopwatch.ElapsedMilliseconds);
        }

        _logger.LogInformation(
            "Handled {RequestName} in {ElapsedMilliseconds}ms",
            requestName,
            stopwatch.ElapsedMilliseconds);

        return response;
    }
}
```


---

## Auth_DB Project (Database)

### Database Design and Implementation

Design and create the complete database schema including:

#### 1. Core Tables:

- Users table with columns for authentication, profile, and audit information
- Roles table for role definitions
- Permissions table for granular permission definitions
- UserRoles junction table for user-role assignments
- RolePermissions junction table for role-permission mappings
- UserPermissions table for direct user permissions (bypassing roles)

#### 2. Authentication Tables:

- RefreshTokens table for refresh token storage and rotation
- UserSessions table for active session tracking
- ApiKeys table for external system API keys
- LoginAttempts table for tracking failed login attempts
- TwoFactorAuth table for 2FA configurations and backup codes

#### 3. Audit and Security Tables:

- AuditLogs table for comprehensive activity logging
- SecurityEvents table for security-related incidents
- PasswordHistory table for password reuse prevention
- UserLockouts table for account lockout management

#### 4. Supporting Tables:

- EmailTemplates table for notification templates
- SystemSettings table for configurable system parameters
- IpWhitelist table for IP-based access control
- ApiKeyPermissions table for API key scope management
- **Applications table for registered client applications (supporting multi-application SSO)**

#### 5. Database Objects:

- **All database objects must be in SSDT declarative format and compatible with SQL Server 2022**
- Create all necessary stored procedures for CRUD operations
- Create views for commonly accessed data combinations
- Create functions for complex business logic (e.g., permission checking)
- Create triggers for audit logging and data integrity
- Create constraints for data integrity (foreign keys, check constraints, unique constraints)
- **Put each CREATE INDEX clause in the same .sql file as its corresponding CREATE TABLE statement (do not create separate files for indexes)**

#### 6. Database Scripts:

- Use T-SQL in code (via Dapper, NEVER use EF Core) for standard CRUD and simple queries
- Use stored procedures for complex reporting, batch operations, or when security/performance is critical
- A hybrid approach often works best—keep simple queries in code and complex/shared logic in procedures
- Initial schema creation script
- Seed data script for default roles, permissions, and admin user
- Migration scripts for version updates
- Rollback scripts for each migration
- Performance tuning scripts (index maintenance, statistics updates)
- Backup and restore procedures

---

### Auth_DB Complete Folder Structure

```
Auth_DB/
├── Auth_DB.sqlproj
│
├── Tables/
│   ├── Core/
│   │   ├── Users.sql
│   │   ├── Roles.sql
│   │   ├── Permissions.sql
│   │   ├── UserRoles.sql
│   │   ├── UserPermissions.sql
│   │   ├── RolePermissions.sql
│   │   ├── PermissionImplications.sql
│   │   └── Applications.sql
│   │
│   ├── Authentication/
│   │   ├── RefreshTokens.sql
│   │   ├── UserSessions.sql
│   │   ├── ApiKeys.sql
│   │   ├── ApiKeyScopes.sql
│   │   ├── LoginAttempts.sql
│   │   └── TwoFactorAuth.sql
│   │
│   ├── Security/
│   │   ├── AuditLogs.sql
│   │   ├── SecurityEvents.sql
│   │   ├── PasswordHistory.sql
│   │   ├── UserLockouts.sql
│   │   └── IpWhitelist.sql
│   │
│   └── System/
│       ├── EmailTemplates.sql
│       └── SystemSettings.sql
│
├── StoredProcedures/
│   ├── Users/
│   │   ├── sp_GetUserById.sql
│   │   ├── sp_GetUserByEmail.sql
│   │   ├── sp_GetUserByUsername.sql
│   │   ├── sp_CreateUser.sql
│   │   ├── sp_UpdateUser.sql
│   │   ├── sp_DeleteUser.sql
│   │   ├── sp_GetUsers.sql
│   │   ├── sp_DeactivateUser.sql
│   │   └── sp_SearchUsers.sql
│   │
│   ├── Authentication/
│   │   ├── sp_ValidateCredentials.sql
│   │   ├── sp_CreateRefreshToken.sql
│   │   ├── sp_ValidateRefreshToken.sql
│   │   ├── sp_RevokeRefreshToken.sql
│   │   ├── sp_RevokeAllUserTokens.sql
│   │   ├── sp_CreateUserSession.sql
│   │   ├── sp_EndUserSession.sql
│   │   ├── sp_GetActiveSessions.sql
│   │   ├── sp_RecordLoginAttempt.sql
│   │   ├── sp_CheckAccountLockout.sql
│   │   ├── sp_LockAccount.sql
│   │   ├── sp_UnlockAccount.sql
│   │   ├── sp_UpdatePassword.sql
│   │   └── sp_AddPasswordHistory.sql
│   │
│   ├── Authorization/
│   │   ├── sp_GetUserEffectivePermissions.sql
│   │   ├── sp_GetUserRoles.sql
│   │   ├── sp_GetUserRolesByApplication.sql
│   │   ├── sp_CheckUserPermission.sql
│   │   ├── sp_AssignRoleToUser.sql
│   │   ├── sp_RemoveRoleFromUser.sql
│   │   ├── sp_GrantPermissionToUser.sql
│   │   ├── sp_RevokePermissionFromUser.sql
│   │   ├── sp_GetPermissionImplications.sql
│   │   ├── sp_GetPermissionsImplying.sql
│   │   └── sp_GetUserIdsByRole.sql
│   │
│   ├── ApiKeys/
│   │   ├── sp_CreateApiKey.sql
│   │   ├── sp_ValidateApiKey.sql
│   │   ├── sp_RevokeApiKey.sql
│   │   ├── sp_RotateApiKey.sql
│   │   ├── sp_GetApiKeysByApplication.sql
│   │   ├── sp_CheckApiKeyScope.sql
│   │   ├── sp_RecordApiKeyUsage.sql
│   │   └── sp_GetApiKeyUsageStats.sql
│   │
│   ├── Roles/
│   │   ├── sp_CreateRole.sql
│   │   ├── sp_UpdateRole.sql
│   │   ├── sp_DeleteRole.sql
│   │   ├── sp_GetRoleById.sql
│   │   ├── sp_GetRoles.sql
│   │   ├── sp_GetRolesByApplication.sql
│   │   ├── sp_AssignPermissionToRole.sql
│   │   ├── sp_RemovePermissionFromRole.sql
│   │   └── sp_GetRolePermissions.sql
│   │
│   ├── Permissions/
│   │   ├── sp_CreatePermission.sql
│   │   ├── sp_UpdatePermission.sql
│   │   ├── sp_DeletePermission.sql
│   │   ├── sp_GetPermissions.sql
│   │   ├── sp_GetPermissionsByApplication.sql
│   │   ├── sp_CreatePermissionImplication.sql
│   │   └── sp_DeletePermissionImplication.sql
│   │
│   ├── Applications/
│   │   ├── sp_RegisterApplication.sql
│   │   ├── sp_UpdateApplication.sql
│   │   ├── sp_DeactivateApplication.sql
│   │   ├── sp_GetApplicationById.sql
│   │   ├── sp_GetApplicationByCode.sql
│   │   └── sp_GetAllApplications.sql
│   │
│   ├── TwoFactor/
│   │   ├── sp_EnableTwoFactor.sql
│   │   ├── sp_DisableTwoFactor.sql
│   │   ├── sp_GetTwoFactorSettings.sql
│   │   └── sp_ValidateTwoFactorCode.sql
│   │
│   └── Audit/
│       ├── sp_CreateAuditLog.sql
│       ├── sp_GetAuditLogs.sql
│       ├── sp_GetAuditLogsByUser.sql
│       ├── sp_GetAuditLogsByApplication.sql
│       ├── sp_CreateSecurityEvent.sql
│       └── sp_GetSecurityEvents.sql
│
├── Views/
│   ├── vw_UserWithRoles.sql
│   ├── vw_UserEffectivePermissions.sql
│   ├── vw_ActiveSessions.sql
│   ├── vw_ActiveApiKeys.sql
│   ├── vw_ApiKeyUsageStats.sql
│   ├── vw_RecentAuditLogs.sql
│   ├── vw_RecentSecurityEvents.sql
│   ├── vw_LockedAccounts.sql
│   ├── vw_RolePermissionMatrix.sql
│   └── vw_ApplicationPermissions.sql
│
├── Functions/
│   ├── Scalar/
│   │   ├── fn_HashApiKey.sql
│   │   ├── fn_IsAccountLocked.sql
│   │   ├── fn_GetLockoutEndTime.sql
│   │   └── fn_IsTokenExpired.sql
│   │
│   └── TableValued/
│       ├── fn_GetUserEffectivePermissions.sql
│       ├── fn_GetUserRolesForApplication.sql
│       ├── fn_GetPermissionHierarchy.sql
│       ├── fn_GetImpliedPermissions.sql
│       └── fn_SplitString.sql
│
├── Triggers/
│   ├── trg_Users_Audit.sql
│   ├── trg_Users_ModifiedAt.sql
│   ├── trg_Roles_Audit.sql
│   ├── trg_Roles_ModifiedAt.sql
│   ├── trg_Permissions_Audit.sql
│   ├── trg_UserRoles_Audit.sql
│   ├── trg_UserPermissions_Audit.sql
│   ├── trg_RolePermissions_Audit.sql
│   ├── trg_ApiKeys_Audit.sql
│   └── trg_Applications_Audit.sql
│
├── Types/
│   ├── udt_PermissionCodeList.sql
│   ├── udt_RoleIdList.sql
│   ├── udt_UserIdList.sql
│   └── udt_GuidList.sql
│
├── Security/
│   ├── Schemas.sql
│   ├── Roles.sql
│   └── Permissions.sql
│
├── Scripts/
│   ├── SeedData/
│   │   ├── 01_DefaultApplications.sql
│   │   ├── 02_DefaultRoles.sql
│   │   ├── 03_DefaultPermissions.sql
│   │   ├── 04_PermissionImplications.sql
│   │   ├── 05_RolePermissions.sql
│   │   ├── 06_AdminUser.sql
│   │   ├── 07_SystemSettings.sql
│   │   └── 08_EmailTemplates.sql
│   │
│   └── Migrations/
│       ├── V1_0_0__Initial_Schema.sql
│       ├── V1_0_1__Add_Application_Scoping.sql
│       └── V1_0_2__Add_Permission_Implications.sql
│
└── PostDeployment/
    └── Script.PostDeployment.sql
```

---

### Complete Table Definitions

#### Core Tables

**Users.sql**
```sql
CREATE TABLE [dbo].[Users]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Username] NVARCHAR(50) NOT NULL,
    [Email] NVARCHAR(255) NOT NULL,
    [NormalizedEmail] NVARCHAR(255) NOT NULL,
    [PasswordHash] NVARCHAR(500) NOT NULL,
    [FirstName] NVARCHAR(100) NULL,
    [LastName] NVARCHAR(100) NULL,
    [FullName] AS (ISNULL([FirstName], '') + ' ' + ISNULL([LastName], '')) PERSISTED,
    [PhoneNumber] NVARCHAR(20) NULL,
    [ProfileImageUrl] NVARCHAR(500) NULL,
    [IsEmailConfirmed] BIT NOT NULL DEFAULT 0,
    [IsPhoneConfirmed] BIT NOT NULL DEFAULT 0,
    [IsTwoFactorEnabled] BIT NOT NULL DEFAULT 0,
    [Status] TINYINT NOT NULL DEFAULT 1,  -- 1=Active, 2=Inactive, 3=Locked, 4=PendingVerification
    [FailedLoginAttempts] INT NOT NULL DEFAULT 0,
    [LockoutEndUtc] DATETIME2 NULL,
    [LastLoginUtc] DATETIME2 NULL,
    [LastLoginIp] NVARCHAR(45) NULL,
    [LastPasswordChangeUtc] DATETIME2 NULL,
    [MustChangePassword] BIT NOT NULL DEFAULT 0,
    [PasswordExpiresUtc] DATETIME2 NULL,
    [SecurityStamp] NVARCHAR(100) NOT NULL DEFAULT NEWID(),
    [ConcurrencyStamp] NVARCHAR(100) NOT NULL DEFAULT NEWID(),
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    [IsDeleted] BIT NOT NULL DEFAULT 0,
    [DeletedAt] DATETIME2 NULL,
    [DeletedBy] UNIQUEIDENTIFIER NULL,
    
    CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Users_Username] UNIQUE ([Username]),
    CONSTRAINT [UQ_Users_NormalizedEmail] UNIQUE ([NormalizedEmail]),
    CONSTRAINT [CK_Users_Status] CHECK ([Status] IN (1, 2, 3, 4))
);

-- Indexes (in same file as table)
CREATE NONCLUSTERED INDEX [IX_Users_NormalizedEmail] ON [dbo].[Users] ([NormalizedEmail]) WHERE [IsDeleted] = 0;
CREATE NONCLUSTERED INDEX [IX_Users_Username] ON [dbo].[Users] ([Username]) WHERE [IsDeleted] = 0;
CREATE NONCLUSTERED INDEX [IX_Users_Status] ON [dbo].[Users] ([Status]) WHERE [IsDeleted] = 0;
CREATE NONCLUSTERED INDEX [IX_Users_CreatedAt] ON [dbo].[Users] ([CreatedAt] DESC);
```

**Roles.sql**
```sql
CREATE TABLE [dbo].[Roles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Code] NVARCHAR(100) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,  -- NULL = global role
    [IsSystem] BIT NOT NULL DEFAULT 0,      -- System roles cannot be deleted
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    
    CONSTRAINT [PK_Roles] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Roles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_Roles_Code_Application] UNIQUE ([Code], [ApplicationId])
);

CREATE NONCLUSTERED INDEX [IX_Roles_ApplicationId] ON [dbo].[Roles] ([ApplicationId]) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Roles_Code] ON [dbo].[Roles] ([Code]) WHERE [IsActive] = 1;
```

**Permissions.sql**
```sql
CREATE TABLE [dbo].[Permissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Code] NVARCHAR(100) NOT NULL,          -- e.g., 'crm:leads:read'
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,  -- NULL = global permission
    [ParentId] UNIQUEIDENTIFIER NULL,       -- For hierarchy
    [Level] TINYINT NOT NULL DEFAULT 0,     -- 0=global, 1=app, 2=resource, 3=action
    [IsWildcard] BIT NOT NULL DEFAULT 0,    -- True for :* permissions
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    
    CONSTRAINT [PK_Permissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Permissions_Parent] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_Permissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_Permissions_Code] UNIQUE ([Code])
);

CREATE NONCLUSTERED INDEX [IX_Permissions_ParentId] ON [dbo].[Permissions] ([ParentId]);
CREATE NONCLUSTERED INDEX [IX_Permissions_ApplicationId] ON [dbo].[Permissions] ([ApplicationId]) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Permissions_Code] ON [dbo].[Permissions] ([Code]) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Permissions_Level] ON [dbo].[Permissions] ([Level]);
```

**Applications.sql**
```sql
CREATE TABLE [dbo].[Applications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [Code] NVARCHAR(50) NOT NULL,           -- e.g., 'crm', 'erp', 'hr'
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [BaseUrl] NVARCHAR(500) NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [ContactEmail] NVARCHAR(255) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [AllowSelfRegistration] BIT NOT NULL DEFAULT 0,
    [RequireTwoFactor] BIT NOT NULL DEFAULT 0,
    [SessionTimeoutMinutes] INT NOT NULL DEFAULT 60,
    [MaxConcurrentSessions] INT NOT NULL DEFAULT 5,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    
    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Applications_Code] UNIQUE ([Code])
);

CREATE NONCLUSTERED INDEX [IX_Applications_Code] ON [dbo].[Applications] ([Code]) WHERE [IsActive] = 1;
```

**UserRoles.sql**
```sql
CREATE TABLE [dbo].[UserRoles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,  -- NULL = global assignment
    [AssignedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [AssignedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,             -- Optional time-limited assignment
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT [PK_UserRoles] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserRoles_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserRoles_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_UserRoles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_UserRoles] UNIQUE ([UserId], [RoleId], [ApplicationId])
);

CREATE NONCLUSTERED INDEX [IX_UserRoles_UserId] ON [dbo].[UserRoles] ([UserId]) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_UserRoles_RoleId] ON [dbo].[UserRoles] ([RoleId]) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_UserRoles_ApplicationId] ON [dbo].[UserRoles] ([ApplicationId]);
```

**UserPermissions.sql**
```sql
CREATE TABLE [dbo].[UserPermissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,  -- NULL = global
    [GrantedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT [PK_UserPermissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserPermissions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserPermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_UserPermissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]),
    CONSTRAINT [UQ_UserPermissions] UNIQUE ([UserId], [PermissionId], [ApplicationId])
);

CREATE NONCLUSTERED INDEX [IX_UserPermissions_UserId] ON [dbo].[UserPermissions] ([UserId]) WHERE [IsActive] = 1;
```

**RolePermissions.sql**
```sql
CREATE TABLE [dbo].[RolePermissions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [GrantedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,
    
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_RolePermissions_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]),
    CONSTRAINT [FK_RolePermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [UQ_RolePermissions] UNIQUE ([RoleId], [PermissionId])
);

CREATE NONCLUSTERED INDEX [IX_RolePermissions_RoleId] ON [dbo].[RolePermissions] ([RoleId]);
CREATE NONCLUSTERED INDEX [IX_RolePermissions_PermissionId] ON [dbo].[RolePermissions] ([PermissionId]);
```

**PermissionImplications.sql**
```sql
CREATE TABLE [dbo].[PermissionImplications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,       -- The permission that grants
    [ImpliedPermissionId] UNIQUEIDENTIFIER NOT NULL, -- The permission being granted
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    
    CONSTRAINT [PK_PermissionImplications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_PermImpl_Permission] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [FK_PermImpl_Implied] FOREIGN KEY ([ImpliedPermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [UQ_PermissionImplication] UNIQUE ([PermissionId], [ImpliedPermissionId])
);

CREATE NONCLUSTERED INDEX [IX_PermImpl_PermissionId] ON [dbo].[PermissionImplications] ([PermissionId]);
CREATE NONCLUSTERED INDEX [IX_PermImpl_ImpliedPermissionId] ON [dbo].[PermissionImplications] ([ImpliedPermissionId]);
```

#### Authentication Tables

**RefreshTokens.sql**
```sql
CREATE TABLE [dbo].[RefreshTokens]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [Token] NVARCHAR(500) NOT NULL,
    [TokenHash] NVARCHAR(128) NOT NULL,     -- SHA256 hash for lookup
    [JwtId] NVARCHAR(100) NOT NULL,         -- Links to access token 'jti' claim
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [DeviceInfo] NVARCHAR(500) NULL,
    [IpAddress] NVARCHAR(45) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [ReplacedByToken] NVARCHAR(500) NULL,   -- For token rotation
    [ReasonRevoked] NVARCHAR(200) NULL,
    
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_RefreshTokens_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);

CREATE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash] ON [dbo].[RefreshTokens] ([TokenHash]);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_UserId] ON [dbo].[RefreshTokens] ([UserId]);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_ExpiresAt] ON [dbo].[RefreshTokens] ([ExpiresAt]) WHERE [RevokedAt] IS NULL;
```

**UserSessions.sql**
```sql
CREATE TABLE [dbo].[UserSessions]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [SessionToken] NVARCHAR(500) NOT NULL,
    [IpAddress] NVARCHAR(45) NOT NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [DeviceType] NVARCHAR(50) NULL,
    [Location] NVARCHAR(200) NULL,
    [StartedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [LastActivityAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,
    [EndedAt] DATETIME2 NULL,
    [EndReason] NVARCHAR(100) NULL,         -- 'logout', 'timeout', 'forced', 'security'
    
    CONSTRAINT [PK_UserSessions] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserSessions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserSessions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);

CREATE NONCLUSTERED INDEX [IX_UserSessions_UserId] ON [dbo].[UserSessions] ([UserId]) WHERE [EndedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_UserSessions_SessionToken] ON [dbo].[UserSessions] ([SessionToken]);
CREATE NONCLUSTERED INDEX [IX_UserSessions_ExpiresAt] ON [dbo].[UserSessions] ([ExpiresAt]) WHERE [EndedAt] IS NULL;
```

**ApiKeys.sql**
```sql
CREATE TABLE [dbo].[ApiKeys]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [KeyPrefix] NVARCHAR(10) NOT NULL,      -- First chars for identification (e.g., 'ak_prod_')
    [KeyHash] NVARCHAR(128) NOT NULL,       -- SHA256 hash of the full key
    [Environment] NVARCHAR(20) NOT NULL DEFAULT 'production',  -- 'production', 'staging', 'development'
    [RateLimitPerMinute] INT NOT NULL DEFAULT 60,
    [RateLimitPerDay] INT NOT NULL DEFAULT 10000,
    [AllowedIps] NVARCHAR(MAX) NULL,        -- JSON array of allowed IPs (NULL = all)
    [AllowedOrigins] NVARCHAR(MAX) NULL,    -- JSON array of allowed CORS origins
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [RevokedAt] DATETIME2 NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [RevokeReason] NVARCHAR(200) NULL,
    
    CONSTRAINT [PK_ApiKeys] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApiKeys_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);

CREATE NONCLUSTERED INDEX [IX_ApiKeys_KeyHash] ON [dbo].[ApiKeys] ([KeyHash]) WHERE [RevokedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_ApiKeys_ApplicationId] ON [dbo].[ApiKeys] ([ApplicationId]) WHERE [RevokedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_ApiKeys_KeyPrefix] ON [dbo].[ApiKeys] ([KeyPrefix]);
```

**ApiKeyScopes.sql**
```sql
CREATE TABLE [dbo].[ApiKeyScopes]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [ApiKeyId] UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [GrantedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [GrantedBy] UNIQUEIDENTIFIER NOT NULL,
    
    CONSTRAINT [PK_ApiKeyScopes] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_ApiKeyScopes_ApiKeys] FOREIGN KEY ([ApiKeyId]) REFERENCES [dbo].[ApiKeys]([Id]),
    CONSTRAINT [FK_ApiKeyScopes_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions]([Id]),
    CONSTRAINT [UQ_ApiKeyScopes] UNIQUE ([ApiKeyId], [PermissionId])
);

CREATE NONCLUSTERED INDEX [IX_ApiKeyScopes_ApiKeyId] ON [dbo].[ApiKeyScopes] ([ApiKeyId]);
```

**LoginAttempts.sql**
```sql
CREATE TABLE [dbo].[LoginAttempts]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,         -- NULL if user not found
    [Username] NVARCHAR(255) NOT NULL,      -- The attempted username/email
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [IpAddress] NVARCHAR(45) NOT NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [AttemptedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [IsSuccessful] BIT NOT NULL DEFAULT 0,
    [FailureReason] NVARCHAR(100) NULL,     -- 'invalid_password', 'user_not_found', 'account_locked', '2fa_failed'
    
    CONSTRAINT [PK_LoginAttempts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_LoginAttempts_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_LoginAttempts_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);

CREATE NONCLUSTERED INDEX [IX_LoginAttempts_UserId] ON [dbo].[LoginAttempts] ([UserId], [AttemptedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_IpAddress] ON [dbo].[LoginAttempts] ([IpAddress], [AttemptedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_AttemptedAt] ON [dbo].[LoginAttempts] ([AttemptedAt] DESC);
```

**TwoFactorAuth.sql**
```sql
CREATE TABLE [dbo].[TwoFactorAuth]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [SecretKey] NVARCHAR(200) NOT NULL,     -- TOTP secret (encrypted)
    [RecoveryCodes] NVARCHAR(MAX) NULL,     -- JSON array of hashed recovery codes
    [IsEnabled] BIT NOT NULL DEFAULT 0,
    [EnabledAt] DATETIME2 NULL,
    [LastUsedAt] DATETIME2 NULL,
    [FailedAttempts] INT NOT NULL DEFAULT 0,
    [LockedUntil] DATETIME2 NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedAt] DATETIME2 NULL,
    
    CONSTRAINT [PK_TwoFactorAuth] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_TwoFactorAuth_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_TwoFactorAuth_UserId] UNIQUE ([UserId])
);
```

#### Security Tables

**AuditLogs.sql**
```sql
CREATE TABLE [dbo].[AuditLogs]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [SessionId] UNIQUEIDENTIFIER NULL,
    [Action] NVARCHAR(100) NOT NULL,        -- 'login', 'logout', 'password_change', 'role_assigned', etc.
    [EntityType] NVARCHAR(100) NULL,        -- 'User', 'Role', 'Permission', etc.
    [EntityId] UNIQUEIDENTIFIER NULL,
    [OldValues] NVARCHAR(MAX) NULL,         -- JSON of previous state
    [NewValues] NVARCHAR(MAX) NULL,         -- JSON of new state
    [IpAddress] NVARCHAR(45) NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [Details] NVARCHAR(MAX) NULL,           -- Additional context
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [PerformedBy] UNIQUEIDENTIFIER NULL,    -- Who performed the action (may differ from UserId)
    
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id])
);

CREATE NONCLUSTERED INDEX [IX_AuditLogs_UserId] ON [dbo].[AuditLogs] ([UserId], [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_Action] ON [dbo].[AuditLogs] ([Action], [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_Timestamp] ON [dbo].[AuditLogs] ([Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_ApplicationId] ON [dbo].[AuditLogs] ([ApplicationId], [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_EntityType_EntityId] ON [dbo].[AuditLogs] ([EntityType], [EntityId]);
```

**PasswordHistory.sql**
```sql
CREATE TABLE [dbo].[PasswordHistory]
(
    [Id] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [PasswordHash] NVARCHAR(500) NOT NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT [PK_PasswordHistory] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_PasswordHistory_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
);

CREATE NONCLUSTERED INDEX [IX_PasswordHistory_UserId] ON [dbo].[PasswordHistory] ([UserId], [CreatedAt] DESC);
```


---

## Authorization Architecture (Comprehensive)

This section details how authorization works across the Auth System and external applications.

### Permission Structure: Hierarchical Model

**The Auth System uses a hierarchical permission model with wildcard support:**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PERMISSION NAMING CONVENTION                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Format: {application}:{resource}:{action}                                  │
│                                                                             │
│  Examples:                                                                  │
│  • crm:leads:read         (read leads in CRM)                               │
│  • crm:leads:write        (create/update leads in CRM)                      │
│  • crm:leads:delete       (delete leads in CRM)                             │
│  • crm:leads:*            (all lead actions in CRM)                         │
│  • crm:*                  (all CRM permissions)                             │
│  • *                      (super admin - everything)                        │
│                                                                             │
│  HIERARCHY LEVELS:                                                          │
│  ─────────────────                                                          │
│  Level 0: Global          *                   (everything)                  │
│  Level 1: Application     crm:*               (all CRM)                     │
│  Level 2: Resource        crm:leads:*         (all lead operations)         │
│  Level 3: Action          crm:leads:read      (specific action)             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Permission Hierarchy Tree:**

```
                              ┌─────────┐
                              │    *    │  ← Super Admin (everything)
                              └────┬────┘
                                   │
              ┌────────────────────┼────────────────────┐
              │                    │                    │
              ▼                    ▼                    ▼
        ┌──────────┐        ┌──────────┐        ┌──────────┐
        │  crm:*   │        │  erp:*   │        │  hr:*    │
        └────┬─────┘        └────┬─────┘        └────┬─────┘
             │                   │                   │
    ┌────────┼────────┐         ...                 ...
    │        │        │
    ▼        ▼        ▼
┌────────┐┌────────┐┌────────┐
│ crm:   ││ crm:   ││ crm:   │
│ leads: ││reports:││ users: │
│   *    ││   *    ││   *    │
└───┬────┘└────────┘└────────┘
    │
┌───┼───────┬───────────┐
│   │       │           │
▼   ▼       ▼           ▼
┌──────┐┌──────┐┌──────┐┌──────┐
│leads:││leads:││leads:││leads:│
│ read ││write ││delete││export│
└──────┘└──────┘└──────┘└──────┘
```

**Permission Implication Rules:**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PERMISSION IMPLICATIONS                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  WILDCARD INHERITANCE (Automatic):                                          │
│  ─────────────────────────────────                                          │
│  • crm:leads:* grants crm:leads:read, write, delete, export                 │
│  • crm:* grants ALL crm:* permissions                                       │
│  • * grants EVERYTHING                                                      │
│                                                                             │
│  ACTION IMPLICATIONS (Configurable):                                        │
│  ──────────────────────────────────                                         │
│  • crm:leads:delete  →  implies crm:leads:read (must see to delete)         │
│  • crm:leads:write   →  implies crm:leads:read (must see to edit)           │
│  • crm:leads:export  →  implies crm:leads:read (must see to export)         │
│                                                                             │
│  NOT IMPLIED (Must be explicitly granted):                                  │
│  ─────────────────────────────────────────                                  │
│  • crm:leads:read    ↛  crm:leads:write  (read doesn't grant write)         │
│  • crm:leads:write   ↛  crm:leads:delete (write doesn't grant delete)       │
│  • crm:leads:*       ↛  crm:reports:*    (different resources)              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Permission Checker Implementation

```csharp
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Auth_Lib.Infrastructure.Security;

/// <summary>
/// Checks permissions considering hierarchy, wildcards, and implications.
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserRepository _userRepository;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public PermissionChecker(
        IPermissionRepository permissionRepository,
        IUserRepository userRepository,
        IMemoryCache cache)
    {
        _permissionRepository = permissionRepository;
        _userRepository = userRepository;
        _cache = cache;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId, 
        string requiredPermission,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Get user's effective permissions (from roles + direct assignments)
        var userPermissions = await GetUserPermissionsAsync(userId, applicationId, cancellationToken);
        
        if (!userPermissions.Any())
            return false;

        // 2. Check for exact match
        if (userPermissions.Contains(requiredPermission))
            return true;

        // 3. Check for wildcard matches
        if (MatchesWildcard(userPermissions, requiredPermission))
            return true;

        // 4. Check for implied permissions
        var impliedPermissions = await GetImpliedPermissionsAsync(userPermissions, cancellationToken);
        if (impliedPermissions.Contains(requiredPermission))
            return true;

        return false;
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"user_permissions_{userId}_{applicationId}";
        
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached))
            return cached!;

        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(
            userId, applicationId, cancellationToken);

        _cache.Set(cacheKey, permissions, _cacheExpiration);
        return permissions;
    }

    private bool MatchesWildcard(IEnumerable<string> userPermissions, string requiredPermission)
    {
        // Check if user has a wildcard that covers the required permission
        // e.g., user has "crm:*" and needs "crm:leads:read"
        
        var parts = requiredPermission.Split(':');
        
        for (int i = 1; i <= parts.Length; i++)
        {
            var wildcardCheck = string.Join(":", parts.Take(i - 1).Append("*"));
            if (userPermissions.Contains(wildcardCheck))
                return true;
        }

        // Check for global wildcard
        if (userPermissions.Contains("*"))
            return true;

        return false;
    }

    private async Task<IReadOnlySet<string>> GetImpliedPermissionsAsync(
        IEnumerable<string> directPermissions,
        CancellationToken cancellationToken)
    {
        var implied = new HashSet<string>();
        
        foreach (var permission in directPermissions)
        {
            var implications = await _permissionRepository.GetImpliedPermissionsAsync(
                permission, cancellationToken);
            
            foreach (var imp in implications)
            {
                implied.Add(imp);
            }
        }

        return implied;
    }

    public void InvalidateUserCache(Guid userId)
    {
        // Called when user's roles/permissions change
        var pattern = $"user_permissions_{userId}_";
        // In production, use distributed cache with pattern-based invalidation
    }
}
```

### Role-Based Access Control (RBAC) with Application Scoping

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    RBAC WITH APPLICATION SCOPING                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  SCENARIO: John has different roles in different applications               │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                         USER: JOHN                                  │   │
│  │                                                                     │   │
│  │   GLOBAL ROLES (all applications):                                  │   │
│  │   └── employee                                                      │   │
│  │                                                                     │   │
│  │   CRM ROLES (ApplicationId = crm-uuid):                             │   │
│  │   ├── admin                                                         │   │
│  │   └── sales-manager                                                 │   │
│  │                                                                     │   │
│  │   ERP ROLES (ApplicationId = erp-uuid):                             │   │
│  │   └── viewer                                                        │   │
│  │                                                                     │   │
│  │   HR SYSTEM:                                                        │   │
│  │   └── (no roles - John cannot access HR)                            │   │
│  │                                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  EFFECTIVE PERMISSIONS BY APPLICATION:                                      │
│                                                                             │
│  When John accesses CRM:                                                    │
│  • Global: profile:read, profile:update (from 'employee' role)              │
│  • CRM: crm:* (from 'admin' role)                                           │
│  • CRM: crm:leads:*, crm:reports:read (from 'sales-manager' role)           │
│                                                                             │
│  When John accesses ERP:                                                    │
│  • Global: profile:read, profile:update (from 'employee' role)              │
│  • ERP: erp:orders:read, erp:inventory:read (from 'viewer' role)            │
│                                                                             │
│  When John tries to access HR:                                              │
│  • Global: profile:read, profile:update (from 'employee' role)              │
│  • HR: (none) → Access denied to HR-specific features                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### JWT Token with Application-Specific Claims

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                JWT STRUCTURE PER APPLICATION                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  When John logs into CRM, he gets JWT with CRM-specific permissions:        │
│                                                                             │
│  {                                                                          │
│    "sub": "john-uuid",                                                      │
│    "email": "john@company.com",                                             │
│    "name": "John Doe",                                                      │
│    "iss": "https://auth.company.com",                                       │
│    "aud": "crm-system-uuid",            ← Audience is CRM                   │
│    "iat": 1704067200,                                                       │
│    "exp": 1704070800,                                                       │
│    "jti": "unique-token-id",            ← For audit correlation             │
│    "app": "crm",                         ← Application code                 │
│                                                                             │
│    // Application-specific                                                  │
│    "roles": ["admin", "sales-manager"],                                     │
│    "permissions": [                                                         │
│      "crm:leads:*",                                                         │
│      "crm:reports:read",                                                    │
│      "crm:dashboard:view"                                                   │
│    ],                                                                       │
│                                                                             │
│    // Global (available in all apps)                                        │
│    "global_roles": ["employee"],                                            │
│    "global_permissions": ["profile:read", "profile:update"]                 │
│  }                                                                          │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│  When John logs into ERP, he gets DIFFERENT JWT:                            │
│                                                                             │
│  {                                                                          │
│    "sub": "john-uuid",                   ← Same user                        │
│    "email": "john@company.com",                                             │
│    "aud": "erp-system-uuid",            ← Different audience                │
│    "app": "erp",                                                            │
│                                                                             │
│    "roles": ["viewer"],                  ← Different roles                  │
│    "permissions": [                      ← Different permissions            │
│      "erp:orders:read",                                                     │
│      "erp:inventory:read"                                                   │
│    ],                                                                       │
│                                                                             │
│    "global_roles": ["employee"],         ← Same global roles                │
│    "global_permissions": ["profile:read", "profile:update"]                 │
│  }                                                                          │
│                                                                             │
│  SECURITY: CRM token CANNOT be used in ERP (audience mismatch)              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Stored Procedure: Get User Effective Permissions

```sql
CREATE PROCEDURE [dbo].[sp_GetUserEffectivePermissions]
    @UserId UNIQUEIDENTIFIER,
    @ApplicationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Returns all effective permissions from:
    -- 1. Global roles (ApplicationId IS NULL)
    -- 2. Application-specific roles
    -- 3. Direct user permissions (global and app-specific)
    -- 4. Implied permissions from PermissionImplications table
    
    ;WITH DirectPermissions AS (
        -- Permissions from roles
        SELECT DISTINCT 
            p.Code AS Permission,
            'role:' + r.Code AS GrantedVia,
            p.ApplicationId
        FROM Permissions p
        INNER JOIN RolePermissions rp ON p.Id = rp.PermissionId
        INNER JOIN Roles r ON rp.RoleId = r.Id AND r.IsActive = 1
        INNER JOIN UserRoles ur ON r.Id = ur.RoleId 
            AND ur.UserId = @UserId
            AND ur.IsActive = 1
            AND (ur.ExpiresAt IS NULL OR ur.ExpiresAt > GETUTCDATE())
            AND (ur.ApplicationId IS NULL OR ur.ApplicationId = @ApplicationId)
        WHERE p.IsActive = 1
          AND (p.ApplicationId IS NULL OR p.ApplicationId = @ApplicationId)
        
        UNION
        
        -- Direct user permissions
        SELECT DISTINCT 
            p.Code AS Permission,
            'direct' AS GrantedVia,
            p.ApplicationId
        FROM Permissions p
        INNER JOIN UserPermissions up ON p.Id = up.PermissionId 
            AND up.UserId = @UserId
            AND up.IsActive = 1
            AND (up.ExpiresAt IS NULL OR up.ExpiresAt > GETUTCDATE())
            AND (up.ApplicationId IS NULL OR up.ApplicationId = @ApplicationId)
        WHERE p.IsActive = 1
          AND (p.ApplicationId IS NULL OR p.ApplicationId = @ApplicationId)
    ),
    ImpliedPermissions AS (
        -- Get implied permissions
        SELECT DISTINCT 
            implied.Code AS Permission,
            'implied:' + dp.Permission AS GrantedVia,
            implied.ApplicationId
        FROM DirectPermissions dp
        INNER JOIN Permissions granting ON granting.Code = dp.Permission
        INNER JOIN PermissionImplications pi ON pi.PermissionId = granting.Id
        INNER JOIN Permissions implied ON implied.Id = pi.ImpliedPermissionId
        WHERE implied.IsActive = 1
    )
    SELECT Permission, GrantedVia, ApplicationId
    FROM DirectPermissions
    UNION
    SELECT Permission, GrantedVia, ApplicationId
    FROM ImpliedPermissions
    ORDER BY Permission;
END
GO
```

### Authorization in Controllers (Using Attributes)

```csharp
using Auth_Lib.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Modules.UserManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IPermissionChecker _permissionChecker;

    public UsersController(IMediator mediator, IPermissionChecker permissionChecker)
    {
        _mediator = mediator;
        _permissionChecker = permissionChecker;
    }

    /// <summary>
    /// Get all users - requires 'users:read' permission
    /// </summary>
    [HttpGet]
    [RequirePermission("users:read")]
    public async Task<IActionResult> GetUsers([FromQuery] GetUsersQuery query)
    {
        var result = await _mediator.Send(query);
        return result.Match(
            users => Ok(users),
            errors => Problem(errors));
    }

    /// <summary>
    /// Create user - requires 'users:create' permission
    /// </summary>
    [HttpPost]
    [RequirePermission("users:create")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command)
    {
        var result = await _mediator.Send(command);
        return result.Match(
            user => CreatedAtAction(nameof(GetUser), new { id = user.Id }, user),
            errors => Problem(errors));
    }

    /// <summary>
    /// Update user - requires 'users:update' permission
    /// </summary>
    [HttpPut("{id}")]
    [RequirePermission("users:update")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserCommand command)
    {
        command.Id = id;
        var result = await _mediator.Send(command);
        return result.Match(
            user => Ok(user),
            errors => Problem(errors));
    }

    /// <summary>
    /// Delete user - requires 'users:delete' permission
    /// </summary>
    [HttpDelete("{id}")]
    [RequirePermission("users:delete")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        var result = await _mediator.Send(new DeleteUserCommand(id));
        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }

    /// <summary>
    /// Get single user - requires 'users:read' permission
    /// </summary>
    [HttpGet("{id}")]
    [RequirePermission("users:read")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var result = await _mediator.Send(new GetUserByIdQuery(id));
        return result.Match(
            user => Ok(user),
            errors => Problem(errors));
    }

    /// <summary>
    /// Assign role to user - requires 'users:manage-roles' permission
    /// </summary>
    [HttpPost("{id}/roles")]
    [RequirePermission("users:manage-roles")]
    public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleCommand command)
    {
        command.UserId = id;
        var result = await _mediator.Send(command);
        return result.Match(
            _ => NoContent(),
            errors => Problem(errors));
    }
}
```

### Custom Authorization Attribute

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Auth_API.Authorization;

/// <summary>
/// Attribute to require a specific permission for an endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "Permission:";
    
    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = $"{PolicyPrefix}{permission}";
    }

    public string Permission { get; }
}

/// <summary>
/// Policy provider that creates policies for permission requirements.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix))
        {
            var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallbackPolicyProvider.GetFallbackPolicyAsync();
}

/// <summary>
/// Requirement for a specific permission.
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    
    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// Handler that checks if user has the required permission.
/// </summary>
public class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionChecker _permissionChecker;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionRequirementHandler(
        IPermissionChecker permissionChecker,
        IHttpContextAccessor httpContextAccessor)
    {
        _permissionChecker = permissionChecker;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return; // Not authenticated
        }

        // Get application ID from the JWT 'aud' claim or route
        Guid? applicationId = null;
        var audienceClaim = context.User.FindFirst("aud")?.Value;
        if (!string.IsNullOrEmpty(audienceClaim) && Guid.TryParse(audienceClaim, out var appId))
        {
            applicationId = appId;
        }

        var hasPermission = await _permissionChecker.HasPermissionAsync(
            userId, 
            requirement.Permission, 
            applicationId);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
```

---

## Technical Requirements

### Security Implementation

#### Password Hashing with Argon2id

**MANDATORY**: Use Argon2id for password hashing. Do NOT use BCrypt, SCrypt, or PBKDF2.

```csharp
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

namespace Auth_Lib.Infrastructure.Security;

/// <summary>
/// Argon2id password hasher following OWASP recommendations.
/// </summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    // OWASP recommended parameters for Argon2id
    private const int DegreeOfParallelism = 4;  // Number of threads
    private const int MemorySize = 65536;        // 64 MB
    private const int Iterations = 3;            // Time cost
    private const int HashLength = 32;           // 256 bits
    private const int SaltLength = 16;           // 128 bits

    /// <summary>
    /// Hashes a password using Argon2id.
    /// Returns: $argon2id$v=19$m=65536,t=3,p=4${base64Salt}${base64Hash}
    /// </summary>
    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = HashPasswordInternal(password, salt);
        
        // Format: compatible with other Argon2 implementations
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a password against a hash.
    /// </summary>
    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            var parts = hash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id")
                return false;

            // Parse parameters
            var paramParts = parts[3].Split(',');
            var memory = int.Parse(paramParts[0].Split('=')[1]);
            var iterations = int.Parse(paramParts[1].Split('=')[1]);
            var parallelism = int.Parse(paramParts[2].Split('=')[1]);
            
            var salt = Convert.FromBase64String(parts[4]);
            var expectedHash = Convert.FromBase64String(parts[5]);

            var actualHash = HashPasswordInternal(password, salt, memory, iterations, parallelism);
            
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a hash needs to be upgraded (parameters changed).
    /// </summary>
    public bool NeedsRehash(string hash)
    {
        try
        {
            var parts = hash.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id")
                return true; // Different algorithm or invalid format

            var paramParts = parts[3].Split(',');
            var memory = int.Parse(paramParts[0].Split('=')[1]);
            var iterations = int.Parse(paramParts[1].Split('=')[1]);
            var parallelism = int.Parse(paramParts[2].Split('=')[1]);

            // Check if parameters match current settings
            return memory != MemorySize || 
                   iterations != Iterations || 
                   parallelism != DegreeOfParallelism;
        }
        catch
        {
            return true;
        }
    }

    private byte[] HashPasswordInternal(
        string password, 
        byte[] salt,
        int memory = MemorySize,
        int iterations = Iterations,
        int parallelism = DegreeOfParallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            MemorySize = memory,
            Iterations = iterations
        };

        return argon2.GetBytes(HashLength);
    }
}
```

#### JWT Token Service

```csharp
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Auth_Lib.Infrastructure.Security;

/// <summary>
/// JWT token service using RS256 (asymmetric) signing.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly RSA _privateKey;
    private readonly RSA _publicKey;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRoleRepository _roleRepository;

    public JwtTokenService(
        IOptions<JwtSettings> settings,
        IPermissionRepository permissionRepository,
        IRoleRepository roleRepository)
    {
        _settings = settings.Value;
        _permissionRepository = permissionRepository;
        _roleRepository = roleRepository;
        
        // Load RSA keys (from Key Vault in production)
        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(_settings.PrivateKey);
        
        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(_settings.PublicKey);
    }

    /// <summary>
    /// Generates an access token for a user.
    /// </summary>
    public async Task<TokenResult> GenerateAccessTokenAsync(
        User user,
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_settings.AccessTokenExpirationMinutes);

        // Get user's roles and permissions for this application
        var roles = await _roleRepository.GetUserRolesAsync(user.Id, applicationId, cancellationToken);
        var globalRoles = await _roleRepository.GetUserRolesAsync(user.Id, null, cancellationToken);
        var permissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, applicationId, cancellationToken);
        var globalPermissions = await _permissionRepository.GetUserEffectivePermissionsAsync(user.Id, null, cancellationToken);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("name", user.FullName ?? user.Username),
            new("username", user.Username),
            new("app", applicationId.ToString()),
        };

        // Add application-specific roles
        foreach (var role in roles)
        {
            claims.Add(new Claim("role", role.Code));
        }

        // Add global roles
        foreach (var role in globalRoles)
        {
            claims.Add(new Claim("global_role", role.Code));
        }

        // Add permissions (consider size limits - may need to use reference token for large permission sets)
        if (permissions.Count <= 50) // Inline if reasonable size
        {
            foreach (var permission in permissions)
            {
                claims.Add(new Claim("permission", permission));
            }
        }
        else
        {
            claims.Add(new Claim("permissions_ref", "true")); // Client should call API for full list
        }

        var key = new RsaSecurityKey(_privateKey) { KeyId = _settings.KeyId };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: applicationId.ToString(),
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials
        );

        var tokenHandler = new JwtSecurityTokenHandler();
        var accessToken = tokenHandler.WriteToken(token);

        return new TokenResult
        {
            AccessToken = accessToken,
            ExpiresAt = expires,
            TokenType = "Bearer",
            Jti = jti
        };
    }

    /// <summary>
    /// Generates a refresh token.
    /// </summary>
    public RefreshTokenResult GenerateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expires = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpirationDays);

        return new RefreshTokenResult
        {
            Token = token,
            ExpiresAt = expires
        };
    }

    /// <summary>
    /// Validates an access token and returns claims.
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        
        try
        {
            var key = new RsaSecurityKey(_publicKey) { KeyId = _settings.KeyId };
            
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = false, // Audience validated separately per-application
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the JWKS (JSON Web Key Set) for public key distribution.
    /// </summary>
    public JsonWebKeySet GetJwks()
    {
        var key = new RsaSecurityKey(_publicKey) { KeyId = _settings.KeyId };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(key);
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;
        
        return new JsonWebKeySet { Keys = { jwk } };
    }
}

public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
```


---

## API Gateway Project (API_Gateway)

> **📌 NOTE**: This section provides an overview. For complete implementation details including full Program.cs, all middleware implementations, caching services, and aggregation patterns, see **"API Gateway Project - Complete Implementation"** section near the end of this document.

### Overview

The API Gateway is an **independent** project using YARP (Yet Another Reverse Proxy). It handles routing, rate limiting at the edge, and provides a single entry point for all external traffic.

### Project Independence

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    API GATEWAY INDEPENDENCE                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  The API Gateway has NO project references to other Auth System projects    │
│                                                                             │
│  API_Gateway.csproj:                                                        │
│  ├── References: ONLY YARP and standard ASP.NET packages                    │
│  ├── NO reference to: Auth_Lib                                              │
│  ├── NO reference to: Auth_Localization                                     │
│  └── NO reference to: Any internal service projects                         │
│                                                                             │
│  WHY?                                                                       │
│  • Gateway can be deployed independently                                    │
│  • Gateway can be scaled separately                                         │
│  • Gateway crashes don't bring down auth logic                              │
│  • Clear security boundary                                                  │
│  • Can be replaced with different gateway (Kong, etc.) if needed            │
│                                                                             │
│  WHAT GATEWAY DOES:                                                         │
│  • Routes requests to Auth_API                                              │
│  • Applies edge rate limiting                                               │
│  • Handles SSL termination                                                  │
│  • Adds security headers                                                    │
│  • Logs all incoming requests                                               │
│  • Prevents Gateway token bypass attacks                                    │
│                                                                             │
│  WHAT GATEWAY DOES NOT DO:                                                  │
│  • Authenticate users (Auth_API does this)                                  │
│  • Validate JWTs (Auth_API does this)                                       │
│  • Access database (only Auth_API does this)                                │
│  • Business logic (Auth_API does this)                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Project Structure

```
API_Gateway/
├── API_Gateway.csproj
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Production.json
├── Program.cs
├── Middleware/
│   ├── RequestLoggingMiddleware.cs
│   ├── SecurityHeadersMiddleware.cs
│   └── GatewayTokenMiddleware.cs         # CRITICAL: Prevents bypass attacks
├── Configuration/
│   ├── YarpConfig.cs
│   └── RateLimitConfig.cs
└── HealthChecks/
    └── DownstreamHealthCheck.cs
```

### Project File (API_Gateway.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>API_Gateway</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- YARP Reverse Proxy -->
    <PackageReference Include="Yarp.ReverseProxy" Version="2.*" />
    
    <!-- Rate Limiting -->
    <PackageReference Include="AspNetCoreRateLimit" Version="5.*" />
    
    <!-- Health Checks -->
    <PackageReference Include="AspNetCore.HealthChecks.Uris" Version="8.*" />
    
    <!-- Logging -->
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
    <PackageReference Include="Serilog.Sinks.Console" Version="5.*" />
    <PackageReference Include="Serilog.Sinks.File" Version="5.*" />
  </ItemGroup>
  
  <!-- NO references to Auth_Lib, Auth_Localization, or other internal projects -->

</Project>
```

### Gateway Token Middleware (CRITICAL - Bypass Prevention)

```csharp
using System.Security.Cryptography;

namespace API_Gateway.Middleware;

/// <summary>
/// CRITICAL SECURITY: Prevents attackers from bypassing the gateway.
/// 
/// Attack scenario without this middleware:
/// 1. Attacker discovers Auth_API is running on internal port 5001
/// 2. Attacker sends requests directly to http://internal:5001/api/...
/// 3. Attacker bypasses all gateway protections (rate limiting, WAF, etc.)
/// 
/// This middleware adds a secret header that Auth_API validates:
/// - Gateway adds: X-Gateway-Token: {HMAC signature}
/// - Auth_API validates: Rejects any request without valid gateway token
/// - Attacker cannot forge: HMAC requires shared secret
/// </summary>
public class GatewayTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _secretKey;
    private readonly ILogger<GatewayTokenMiddleware> _logger;

    public GatewayTokenMiddleware(
        RequestDelegate next, 
        IConfiguration configuration,
        ILogger<GatewayTokenMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        
        // Secret shared between Gateway and Auth_API (from Key Vault)
        var secret = configuration["Gateway:SharedSecret"] 
            ?? throw new InvalidOperationException("Gateway:SharedSecret not configured");
        _secretKey = Convert.FromBase64String(secret);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Generate timestamp-based token to prevent replay attacks
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var requestPath = context.Request.Path.Value ?? "/";
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Create HMAC signature
        var dataToSign = $"{timestamp}|{requestPath}|{clientIp}";
        using var hmac = new HMACSHA256(_secretKey);
        var signature = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dataToSign)));
        
        // Add headers that Auth_API will validate
        context.Request.Headers["X-Gateway-Timestamp"] = timestamp;
        context.Request.Headers["X-Gateway-Signature"] = signature;
        context.Request.Headers["X-Forwarded-For"] = clientIp;
        
        // Remove any existing gateway headers that attacker might have added
        // (Defense in depth - attacker can't inject these)
        context.Request.Headers.Remove("X-Gateway-Token-Fake");
        
        await _next(context);
    }
}
```

### Auth_API Gateway Validation Middleware

```csharp
// This goes in Auth_API project, validates requests came through gateway

namespace Auth_API.Middleware;

/// <summary>
/// Validates that requests came through the API Gateway.
/// Rejects direct access attempts.
/// </summary>
public class GatewayValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly byte[] _secretKey;
    private readonly ILogger<GatewayValidationMiddleware> _logger;
    private readonly bool _requireGateway;

    public GatewayValidationMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        ILogger<GatewayValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        
        _requireGateway = configuration.GetValue<bool>("Security:RequireGateway", true);
        
        var secret = configuration["Gateway:SharedSecret"];
        _secretKey = string.IsNullOrEmpty(secret) 
            ? Array.Empty<byte>() 
            : Convert.FromBase64String(secret);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation for health checks
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // In development, gateway validation can be disabled
        if (!_requireGateway)
        {
            await _next(context);
            return;
        }

        // Validate gateway headers
        if (!context.Request.Headers.TryGetValue("X-Gateway-Timestamp", out var timestamp) ||
            !context.Request.Headers.TryGetValue("X-Gateway-Signature", out var signature))
        {
            _logger.LogWarning(
                "Direct access attempt detected from {IP} to {Path}",
                context.Connection.RemoteIpAddress,
                context.Request.Path);
            
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Direct access not allowed" });
            return;
        }

        // Validate timestamp (prevent replay attacks - 5 minute window)
        if (!long.TryParse(timestamp, out var ts))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(ts);
        var age = DateTimeOffset.UtcNow - requestTime;
        if (Math.Abs(age.TotalMinutes) > 5)
        {
            _logger.LogWarning("Gateway token expired or clock skew detected");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // Validate signature
        var clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault() 
            ?? context.Connection.RemoteIpAddress?.ToString() 
            ?? "unknown";
        var dataToSign = $"{timestamp}|{context.Request.Path}|{clientIp}";
        
        using var hmac = new HMACSHA256(_secretKey);
        var expectedSignature = Convert.ToBase64String(
            hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dataToSign)));

        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(signature!),
            System.Text.Encoding.UTF8.GetBytes(expectedSignature)))
        {
            _logger.LogWarning(
                "Invalid gateway signature from {IP}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}
```

### YARP Configuration (appsettings.json)

```json
{
  "ReverseProxy": {
    "Routes": {
      "auth-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "{**catch-all}"
        },
        "Transforms": [
          { "RequestHeadersCopy": "true" },
          { "RequestHeaderOriginalHost": "true" }
        ]
      }
    },
    "Clusters": {
      "auth-cluster": {
        "LoadBalancingPolicy": "RoundRobin",
        "HealthCheck": {
          "Active": {
            "Enabled": "true",
            "Interval": "00:00:10",
            "Timeout": "00:00:05",
            "Policy": "ConsecutiveFailures",
            "Path": "/health"
          }
        },
        "Destinations": {
          "auth-api-1": {
            "Address": "http://auth-api-1:5001"
          },
          "auth-api-2": {
            "Address": "http://auth-api-2:5001"
          }
        }
      }
    }
  },
  "Gateway": {
    "SharedSecret": "{{FROM_KEY_VAULT}}"
  },
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Forwarded-For",
    "ClientIdHeader": "X-ClientId",
    "HttpStatusCode": 429,
    "GeneralRules": [
      {
        "Endpoint": "*:/api/auth/login",
        "Period": "1m",
        "Limit": 10
      },
      {
        "Endpoint": "*:/api/auth/register",
        "Period": "1h",
        "Limit": 5
      },
      {
        "Endpoint": "*:/api/*",
        "Period": "1m",
        "Limit": 100
      }
    ]
  }
}
```

### Gateway Program.cs

```csharp
using API_Gateway.Middleware;
using AspNetCoreRateLimit;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/gateway-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add rate limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Add YARP
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add health checks
builder.Services.AddHealthChecks()
    .AddUrlGroup(new Uri("http://auth-api:5001/health"), "auth-api");

var app = builder.Build();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    await next();
});

// Rate limiting (edge)
app.UseIpRateLimiting();

// Gateway token injection (for downstream validation)
app.UseMiddleware<GatewayTokenMiddleware>();

// Health check endpoint
app.MapHealthChecks("/health");

// YARP reverse proxy
app.MapReverseProxy();

app.Run();
```

---

## AuthSystem.Client.SDK Project

### Overview

The SDK is distributed to external systems as a NuGet package. It provides everything they need to integrate with the Auth System without knowing internal details.

### Project Structure

```
AuthSystem.Client.SDK/
├── AuthSystem.Client.SDK.csproj
├── IAuthSystemClient.cs
├── AuthSystemClient.cs
├── Models/
│   ├── TokenValidationResult.cs
│   ├── TokenResponse.cs
│   ├── UserInfo.cs
│   ├── AuthSystemOptions.cs
│   └── AuthError.cs
├── Extensions/
│   ├── ServiceCollectionExtensions.cs
│   └── AuthenticationBuilderExtensions.cs
├── Middleware/
│   └── AuthSystemAuthenticationHandler.cs
└── README.md
```

### Project File (AuthSystem.Client.SDK.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    
    <!-- NuGet Package Configuration -->
    <PackageId>AuthSystem.Client.SDK</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Company</Authors>
    <Company>Your Company</Company>
    <Description>Client SDK for integrating with the Enterprise Authentication System</Description>
    <PackageTags>authentication;authorization;security;jwt</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/yourcompany/auth-system</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.*" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="7.*" />
  </ItemGroup>
  
  <ItemGroup>
    <None Include="README.md" Pack="true" PackagePath="\" />
  </ItemGroup>

  <!-- NO references to internal Auth System projects -->

</Project>
```

### AuthSystemClient Implementation

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using AuthSystem.Client.SDK.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AuthSystem.Client.SDK;

/// <summary>
/// HTTP client for communicating with the Auth System.
/// </summary>
public class AuthSystemClient : IAuthSystemClient
{
    private readonly HttpClient _httpClient;
    private readonly AuthSystemOptions _options;
    private readonly IMemoryCache _cache;
    private readonly JsonSerializerOptions _jsonOptions;

    public AuthSystemClient(
        HttpClient httpClient,
        IOptions<AuthSystemOptions> options,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _cache = cache;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Token Operations
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/validate",
                new { token },
                _jsonOptions,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TokenValidationResult>(
                    _jsonOptions, cancellationToken) ?? new TokenValidationResult { IsValid = false };
            }

            var error = await response.Content.ReadFromJsonAsync<AuthError>(
                _jsonOptions, cancellationToken);
            
            return new TokenValidationResult
            {
                IsValid = false,
                ErrorCode = error?.Code ?? "unknown",
                ErrorMessage = error?.Message ?? "Token validation failed"
            };
        }
        catch (Exception ex)
        {
            return new TokenValidationResult
            {
                IsValid = false,
                ErrorCode = "connection_error",
                ErrorMessage = $"Failed to connect to Auth System: {ex.Message}"
            };
        }
    }

    public async Task<TokenResponse?> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken },
            _jsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<TokenResponse>(
                _jsonOptions, cancellationToken);
        }

        return null;
    }

    public async Task<bool> RevokeTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/revoke",
            new { token },
            _jsonOptions,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // User Information
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<UserInfo?> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"user_info_{userId}";
        
        if (_cache.TryGetValue(cacheKey, out UserInfo? cached))
            return cached;

        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}",
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var userInfo = await response.Content.ReadFromJsonAsync<UserInfo>(
                _jsonOptions, cancellationToken);
            
            if (userInfo != null)
            {
                _cache.Set(cacheKey, userInfo, TimeSpan.FromMinutes(5));
            }
            
            return userInfo;
        }

        return null;
    }

    public async Task<UserInfo?> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        
        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<UserInfo>(
                _jsonOptions, cancellationToken);
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization Checks
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/permissions/{Uri.EscapeDataString(permission)}/check",
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> HasRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/roles/{Uri.EscapeDataString(role)}/check",
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/permissions",
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<string>>(
                _jsonOptions, cancellationToken) ?? new List<string>();
        }

        return Array.Empty<string>();
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/roles",
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<string>>(
                _jsonOptions, cancellationToken) ?? new List<string>();
        }

        return Array.Empty<string>();
    }
}
```

### Service Collection Extensions

```csharp
using AuthSystem.Client.SDK.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;

namespace AuthSystem.Client.SDK.Extensions;

/// <summary>
/// Extension methods for adding Auth System integration to an ASP.NET Core application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Auth System client services with authentication middleware.
    /// </summary>
    public static IServiceCollection AddAuthSystemAuthentication(
        this IServiceCollection services,
        Action<AuthSystemOptions> configureOptions)
    {
        var options = new AuthSystemOptions();
        configureOptions(options);
        
        services.Configure<AuthSystemOptions>(o =>
        {
            o.BaseUrl = options.BaseUrl;
            o.ApiKey = options.ApiKey;
            o.ApplicationId = options.ApplicationId;
            o.RequireHttps = options.RequireHttps;
            o.ValidateTokensOnline = options.ValidateTokensOnline;
        });

        services.AddMemoryCache();

        // Configure HttpClient with retry policy
        services.AddHttpClient<IAuthSystemClient, AuthSystemClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
            client.DefaultRequestHeaders.Add("X-Application-Id", options.ApplicationId);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddPolicyHandler(GetRetryPolicy())
        .AddPolicyHandler(GetCircuitBreakerPolicy());

        // Configure JWT authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                jwtOptions.Authority = options.BaseUrl;
                jwtOptions.Audience = options.ApplicationId;
                jwtOptions.RequireHttpsMetadata = options.RequireHttps;
                
                // Disable claim type mapping
                jwtOptions.TokenHandlers.Clear();
                jwtOptions.TokenHandlers.Add(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler
                {
                    MapInboundClaims = false
                });

                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.BaseUrl,
                    ValidateAudience = true,
                    ValidAudience = options.ApplicationId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    MapInboundClaims = false,
                    RoleClaimType = "role",
                    NameClaimType = "name"
                };

                // Online validation if configured
                if (options.ValidateTokensOnline)
                {
                    jwtOptions.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var authClient = context.HttpContext.RequestServices
                                .GetRequiredService<IAuthSystemClient>();
                            
                            var token = context.SecurityToken as System.IdentityModel.Tokens.Jwt.JwtSecurityToken;
                            if (token == null)
                            {
                                context.Fail("Invalid token format");
                                return;
                            }

                            var result = await authClient.ValidateTokenAsync(
                                token.RawData,
                                context.HttpContext.RequestAborted);

                            if (!result.IsValid)
                            {
                                context.Fail(result.ErrorMessage ?? "Token validation failed");
                            }
                        }
                    };
                }
            });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
```

---

## OpenID Connect Discovery Endpoints (MANDATORY)

Auth_API must expose standard OpenID Connect endpoints:

### /.well-known/openid-configuration

```json
{
  "issuer": "https://auth.company.com",
  "authorization_endpoint": "https://auth.company.com/connect/authorize",
  "token_endpoint": "https://auth.company.com/connect/token",
  "userinfo_endpoint": "https://auth.company.com/connect/userinfo",
  "jwks_uri": "https://auth.company.com/.well-known/jwks.json",
  "revocation_endpoint": "https://auth.company.com/connect/revoke",
  "introspection_endpoint": "https://auth.company.com/connect/introspect",
  "end_session_endpoint": "https://auth.company.com/connect/logout",
  "scopes_supported": [
    "openid",
    "profile",
    "email",
    "offline_access"
  ],
  "response_types_supported": [
    "code",
    "token",
    "id_token",
    "code token",
    "code id_token"
  ],
  "grant_types_supported": [
    "authorization_code",
    "refresh_token",
    "client_credentials"
  ],
  "token_endpoint_auth_methods_supported": [
    "client_secret_basic",
    "client_secret_post"
  ],
  "subject_types_supported": ["public"],
  "id_token_signing_alg_values_supported": ["RS256"],
  "claims_supported": [
    "sub",
    "name",
    "email",
    "email_verified",
    "role",
    "permission"
  ]
}
```

### /.well-known/jwks.json

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "auth-key-2024-01",
      "alg": "RS256",
      "n": "...(base64url encoded modulus)...",
      "e": "AQAB"
    }
  ]
}
```

### Discovery Controller

```csharp
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Controllers;

[ApiController]
[Route(".well-known")]
public class DiscoveryController : ControllerBase
{
    private readonly IJwtTokenService _jwtService;
    private readonly IConfiguration _configuration;

    public DiscoveryController(
        IJwtTokenService jwtService,
        IConfiguration configuration)
    {
        _jwtService = jwtService;
        _configuration = configuration;
    }

    [HttpGet("openid-configuration")]
    public IActionResult GetOpenIdConfiguration()
    {
        var issuer = _configuration["Jwt:Issuer"];
        
        return Ok(new
        {
            issuer,
            authorization_endpoint = $"{issuer}/connect/authorize",
            token_endpoint = $"{issuer}/connect/token",
            userinfo_endpoint = $"{issuer}/connect/userinfo",
            jwks_uri = $"{issuer}/.well-known/jwks.json",
            revocation_endpoint = $"{issuer}/connect/revoke",
            scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
            response_types_supported = new[] { "code", "token", "id_token" },
            grant_types_supported = new[] { "authorization_code", "refresh_token", "client_credentials" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            claims_supported = new[] { "sub", "name", "email", "email_verified", "role", "permission" }
        });
    }

    [HttpGet("jwks.json")]
    public IActionResult GetJwks()
    {
        var jwks = _jwtService.GetJwks();
        return Ok(jwks);
    }
}
```


---

## Secrets Management (MANDATORY)

### Overview

**NEVER store secrets in code, configuration files, or environment variables that are committed to source control.**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    SECRETS MANAGEMENT HIERARCHY                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PRODUCTION:                                                                │
│  └── Azure Key Vault / AWS Secrets Manager / HashiCorp Vault               │
│                                                                             │
│  STAGING:                                                                   │
│  └── Azure Key Vault (separate instance from production)                    │
│                                                                             │
│  DEVELOPMENT:                                                               │
│  └── .NET User Secrets (dotnet user-secrets)                               │
│                                                                             │
│  ⛔ NEVER:                                                                  │
│  └── appsettings.json, environment variables in CI/CD, code comments       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Secrets to Store

| Secret | Description | Rotation Frequency |
|--------|-------------|-------------------|
| `Jwt:PrivateKey` | RSA private key for JWT signing | Annually |
| `Jwt:PublicKey` | RSA public key for JWT validation | Annually |
| `Database:ConnectionString` | Auth_DB connection string | 90 days |
| `Gateway:SharedSecret` | HMAC key for gateway validation | 90 days |
| `Argon2:Pepper` | Additional secret for password hashing | Never (would invalidate all passwords) |
| `TwoFactor:EncryptionKey` | AES key for 2FA secrets | Annually |
| `ApiKey:EncryptionKey` | AES key for API key encryption | 90 days |

### Key Vault Integration Code

```csharp
// Program.cs
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Add Key Vault configuration in production
if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
{
    var keyVaultUrl = builder.Configuration["KeyVault:Url"] 
        ?? throw new InvalidOperationException("KeyVault:Url not configured");
    
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUrl),
        new DefaultAzureCredential(),
        new AzureKeyVaultConfigurationOptions
        {
            ReloadInterval = TimeSpan.FromMinutes(5) // Auto-reload secrets
        });
}

// In development, use User Secrets
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}
```

### User Secrets Setup (Development)

```bash
# Initialize user secrets for the project
cd src/Auth_API
dotnet user-secrets init

# Set secrets
dotnet user-secrets set "Jwt:PrivateKey" "-----BEGIN RSA PRIVATE KEY-----\n..."
dotnet user-secrets set "Jwt:PublicKey" "-----BEGIN PUBLIC KEY-----\n..."
dotnet user-secrets set "Database:ConnectionString" "Server=localhost;Database=Auth_DB;..."
dotnet user-secrets set "Gateway:SharedSecret" "base64-encoded-secret..."
```

---

## High Availability Design (MANDATORY)

### Multi-Instance Deployment

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    HIGH AVAILABILITY ARCHITECTURE                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│                         ┌─────────────────┐                                 │
│                         │  Load Balancer  │                                 │
│                         │   (Layer 7)     │                                 │
│                         └────────┬────────┘                                 │
│                                  │                                          │
│              ┌───────────────────┼───────────────────┐                      │
│              │                   │                   │                      │
│              ▼                   ▼                   ▼                      │
│       ┌────────────┐      ┌────────────┐      ┌────────────┐               │
│       │ Gateway 1  │      │ Gateway 2  │      │ Gateway 3  │               │
│       └─────┬──────┘      └─────┬──────┘      └─────┬──────┘               │
│             │                   │                   │                      │
│             └───────────────────┼───────────────────┘                      │
│                                 │                                          │
│              ┌──────────────────┼──────────────────┐                       │
│              │                  │                  │                       │
│              ▼                  ▼                  ▼                       │
│       ┌────────────┐     ┌────────────┐     ┌────────────┐                │
│       │ Auth_API 1 │     │ Auth_API 2 │     │ Auth_API 3 │                │
│       └─────┬──────┘     └─────┬──────┘     └─────┬──────┘                │
│             │                  │                  │                       │
│             └──────────────────┼──────────────────┘                       │
│                                │                                          │
│                    ┌───────────┴───────────┐                              │
│                    │                       │                              │
│                    ▼                       ▼                              │
│           ┌──────────────┐        ┌──────────────┐                        │
│           │  Auth_DB     │        │  Auth_DB     │                        │
│           │  (Primary)   │◄──────►│  (Secondary) │                        │
│           │  Read/Write  │  Sync  │  Read Only   │                        │
│           └──────────────┘        └──────────────┘                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Stateless Design Requirements

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    STATELESS SERVICE REQUIREMENTS                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Auth_API instances MUST be stateless:                                      │
│                                                                             │
│  ✅ DO:                                                                     │
│  • Store session data in database (UserSessions table)                      │
│  • Store refresh tokens in database (RefreshTokens table)                   │
│  • Use distributed cache (Redis) for hot data                               │
│  • Use JWT tokens (self-contained, no server state)                         │
│                                                                             │
│  ⛔ DON'T:                                                                  │
│  • Store session data in memory                                             │
│  • Use in-memory cache for critical data                                    │
│  • Rely on sticky sessions                                                  │
│  • Store user state in static variables                                     │
│                                                                             │
│  WHY?                                                                       │
│  • Any instance can handle any request                                      │
│  • Instances can be added/removed dynamically                               │
│  • No data loss if instance crashes                                         │
│  • Enables zero-downtime deployments                                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Load Balancer Configuration

```yaml
# Kubernetes Ingress example
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: auth-system-ingress
  annotations:
    kubernetes.io/ingress.class: nginx
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
    nginx.ingress.kubernetes.io/proxy-read-timeout: "60"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "60"
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
spec:
  tls:
  - hosts:
    - auth.company.com
    secretName: auth-tls-secret
  rules:
  - host: auth.company.com
    http:
      paths:
      - path: /
        pathType: Prefix
        backend:
          service:
            name: api-gateway
            port:
              number: 80
---
apiVersion: v1
kind: Service
metadata:
  name: api-gateway
spec:
  type: ClusterIP
  selector:
    app: api-gateway
  ports:
  - port: 80
    targetPort: 8080
  sessionAffinity: None  # No sticky sessions!
```

### Health Check Endpoints

```csharp
// Auth_API health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("AuthDb")!,
        name: "database",
        tags: new[] { "db", "critical" })
    .AddCheck<JwtKeyHealthCheck>("jwt-keys", tags: new[] { "security", "critical" })
    .AddCheck<RedisHealthCheck>("redis", tags: new[] { "cache" });

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("critical"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Just checks if app is running
});
```

---

## Disaster Recovery Plan (MANDATORY)

### RTO/RPO Targets

| Metric | Target | Description |
|--------|--------|-------------|
| **RTO** (Recovery Time Objective) | 1 hour | Maximum time to restore service |
| **RPO** (Recovery Point Objective) | 15 minutes | Maximum data loss acceptable |

### Backup Strategy

| Component | Backup Method | Frequency | Retention |
|-----------|--------------|-----------|-----------|
| Auth_DB | Full backup | Daily | 30 days |
| Auth_DB | Differential | Every 4 hours | 7 days |
| Auth_DB | Transaction log | Every 15 minutes | 48 hours |
| RSA Keys | Manual export to secure storage | On rotation | Forever |
| Configuration | Git + Key Vault backup | On change | Forever |

### Recovery Runbook (Checklist)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DISASTER RECOVERY RUNBOOK                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. ASSESS IMPACT (5 minutes)                                               │
│     □ Identify which components are affected                                │
│     □ Determine if data loss occurred                                       │
│     □ Notify stakeholders                                                   │
│                                                                             │
│  2. DATABASE RECOVERY (30 minutes)                                          │
│     □ Identify latest good backup                                           │
│     □ Restore to standby server                                             │
│     □ Apply transaction logs to minimize data loss                          │
│     □ Verify data integrity                                                 │
│     □ Update connection strings                                             │
│                                                                             │
│  3. APPLICATION RECOVERY (15 minutes)                                       │
│     □ Deploy latest stable version                                          │
│     □ Verify Key Vault connectivity                                         │
│     □ Run health checks                                                     │
│     □ Verify JWT signing works                                              │
│                                                                             │
│  4. VERIFICATION (10 minutes)                                               │
│     □ Test login flow                                                       │
│     □ Test token validation                                                 │
│     □ Test API key authentication                                           │
│     □ Verify external systems can connect                                   │
│                                                                             │
│  5. POST-INCIDENT                                                           │
│     □ Document timeline                                                     │
│     □ Root cause analysis                                                   │
│     □ Update runbook if needed                                              │
│     □ Schedule post-mortem meeting                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Performance Baselines (MANDATORY)

### Target Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Login requests/sec | 500+ | P95 |
| Token validation requests/sec | 2,000+ | P95 |
| Login latency | < 200ms | P95 |
| Token validation latency | < 50ms | P95 |
| API response latency | < 100ms | P95 |
| Error rate | < 0.1% | Average |
| Availability | 99.9% | Monthly |

### Database Query Performance

| Query | Target | Index Strategy |
|-------|--------|----------------|
| Get user by email | < 5ms | IX_Users_NormalizedEmail |
| Get user by ID | < 2ms | PK_Users (clustered) |
| Get user permissions | < 10ms | Covering indexes + cache |
| Validate refresh token | < 5ms | IX_RefreshTokens_TokenHash |
| Write audit log | < 5ms | Async write + batch |

### Load Testing Requirements

```csharp
// k6 load test script example
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
    stages: [
        { duration: '2m', target: 100 },   // Ramp up
        { duration: '5m', target: 500 },   // Sustained load
        { duration: '2m', target: 1000 },  // Peak load
        { duration: '2m', target: 0 },     // Ramp down
    ],
    thresholds: {
        http_req_duration: ['p(95)<200'],  // 95% under 200ms
        http_req_failed: ['rate<0.01'],    // Error rate under 1%
    },
};

export default function () {
    // Test login endpoint
    const loginRes = http.post('https://auth.company.com/api/auth/login', 
        JSON.stringify({
            email: `user${__VU}@test.com`,
            password: 'TestPassword123!'
        }),
        { headers: { 'Content-Type': 'application/json' } }
    );

    check(loginRes, {
        'login status is 200': (r) => r.status === 200,
        'login has access token': (r) => r.json('accessToken') !== undefined,
    });

    sleep(1);
}
```

---

## Rate Limiting Architecture (MANDATORY)

### Two-Layer Rate Limiting

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    RATE LIMITING LAYERS                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  LAYER 1: API GATEWAY (Edge Rate Limiting)                                  │
│  ─────────────────────────────────────────                                  │
│  Purpose: Protect against DDoS, brute force, abuse                          │
│  Scope: All incoming requests                                               │
│  Algorithm: Token bucket (per IP)                                           │
│  Storage: In-memory (Redis in clustered setup)                              │
│                                                                             │
│  Rules:                                                                     │
│  • /api/auth/login:      10 req/min per IP                                  │
│  • /api/auth/register:   5 req/hour per IP                                  │
│  • /api/*:               100 req/min per IP                                 │
│  • Blocked IPs:          0 req (immediate 403)                              │
│                                                                             │
│  LAYER 2: AUTH_API (Application Rate Limiting)                              │
│  ─────────────────────────────────────────────                              │
│  Purpose: Enforce business rules, per-user/per-application limits           │
│  Scope: Authenticated requests                                              │
│  Algorithm: Sliding window (per user/API key)                               │
│  Storage: Redis (distributed)                                               │
│                                                                             │
│  Rules:                                                                     │
│  • Per User:             1000 req/hour                                      │
│  • Per API Key:          Configurable per key (default 10000/day)           │
│  • Admin endpoints:      100 req/min per user                               │
│  • Bulk operations:      10 req/min per user                                │
│                                                                             │
│  IMPORTANT: Both layers work together!                                      │
│  Gateway blocks obvious abuse → Auth_API enforces business limits           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Rate Limit Response

```json
HTTP/1.1 429 Too Many Requests
Retry-After: 60
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1704067260

{
  "error": {
    "code": "rate_limit_exceeded",
    "message": "Rate limit exceeded. Please retry after 60 seconds.",
    "retryAfter": 60
  }
}
```

---

## CI/CD Integration (MANDATORY)

### Pipeline Stages

```yaml
# Azure DevOps / GitHub Actions example
stages:
  - name: Build
    jobs:
      - job: BuildAndTest
        steps:
          - task: DotNetCoreCLI@2
            displayName: 'Restore'
            inputs:
              command: restore
              
          - task: DotNetCoreCLI@2
            displayName: 'Build'
            inputs:
              command: build
              arguments: '--configuration Release --no-restore'
              
          - task: DotNetCoreCLI@2
            displayName: 'Test'
            inputs:
              command: test
              arguments: '--configuration Release --no-build --collect:"XPlat Code Coverage"'
              
          - task: PublishCodeCoverageResults@1
            displayName: 'Publish Coverage'
            inputs:
              codeCoverageTool: Cobertura
              summaryFileLocation: '$(Agent.TempDirectory)/**/coverage.cobertura.xml'

  - name: Security
    jobs:
      - job: SecurityScan
        steps:
          - task: CredScan@3
            displayName: 'Credential Scan'
            
          - task: SonarQubePrepare@5
            displayName: 'SonarQube Analysis'

  - name: DeployStaging
    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
    jobs:
      - deployment: DeployToStaging
        environment: staging
        strategy:
          runOnce:
            deploy:
              steps:
                - task: AzureWebApp@1
                  displayName: 'Deploy to Staging'

  - name: DeployProduction
    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))
    jobs:
      - deployment: DeployToProduction
        environment: production
        strategy:
          runOnce:
            deploy:
              steps:
                - task: AzureWebApp@1
                  displayName: 'Deploy to Production'
                  inputs:
                    deploymentMethod: 'auto'
                    enableCustomDeployment: true
                    deploymentType: 'webDeploy'
```

### Database Migration Strategy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DATABASE MIGRATION STRATEGY                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. BACKWARD COMPATIBLE CHANGES ONLY                                        │
│     • Add columns with DEFAULT values                                       │
│     • Add new tables                                                        │
│     • Add indexes                                                           │
│     • NEVER rename/drop columns in same release as code change              │
│                                                                             │
│  2. TWO-PHASE DEPLOYMENT                                                    │
│     Phase 1: Deploy database changes (backward compatible)                  │
│     Phase 2: Deploy application code                                        │
│     Phase 3: (Next release) Remove deprecated columns/tables                │
│                                                                             │
│  3. MIGRATION EXECUTION                                                     │
│     • Migrations run BEFORE application deployment                          │
│     • Use SSDT publish with block on possible data loss                     │
│     • Always have rollback script ready                                     │
│                                                                             │
│  4. VERIFICATION                                                            │
│     • Compare schema before/after                                           │
│     • Run smoke tests                                                       │
│     • Monitor for errors                                                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Rollback Procedure

```bash
# 1. Immediate rollback (within deployment window)
kubectl rollout undo deployment/auth-api

# 2. Database rollback (if needed)
sqlcmd -S server -d Auth_DB -i rollback_v1.0.1_to_v1.0.0.sql

# 3. Verify rollback
curl -s https://auth.company.com/health | jq .

# 4. Notify stakeholders
```


---

### Critical Stored Procedures

**sp_GetUserEffectivePermissions.sql**
```sql
CREATE PROCEDURE [dbo].[sp_GetUserEffectivePermissions]
    @UserId UNIQUEIDENTIFIER,
    @ApplicationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Returns all effective permissions from:
    -- 1. Global roles (ApplicationId IS NULL)
    -- 2. Application-specific roles
    -- 3. Direct user permissions (global and app-specific)
    -- 4. Implied permissions (through permission implications)
    
    ;WITH DirectPermissions AS (
        -- Permissions from roles
        SELECT DISTINCT 
            p.Id AS PermissionId,
            p.Code AS Permission,
            'role:' + r.Code AS GrantedVia,
            p.ApplicationId
        FROM Permissions p
        INNER JOIN RolePermissions rp ON p.Id = rp.PermissionId
        INNER JOIN Roles r ON rp.RoleId = r.Id AND r.IsActive = 1
        INNER JOIN UserRoles ur ON r.Id = ur.RoleId 
            AND ur.UserId = @UserId
            AND ur.IsActive = 1
            AND (ur.ExpiresAt IS NULL OR ur.ExpiresAt > GETUTCDATE())
            AND (ur.ApplicationId IS NULL OR ur.ApplicationId = @ApplicationId)
        WHERE p.IsActive = 1
          AND (p.ApplicationId IS NULL OR p.ApplicationId = @ApplicationId)
        
        UNION
        
        -- Direct user permissions
        SELECT DISTINCT 
            p.Id AS PermissionId,
            p.Code AS Permission,
            'direct' AS GrantedVia,
            p.ApplicationId
        FROM Permissions p
        INNER JOIN UserPermissions up ON p.Id = up.PermissionId 
            AND up.UserId = @UserId
            AND up.IsActive = 1
            AND (up.ExpiresAt IS NULL OR up.ExpiresAt > GETUTCDATE())
            AND (up.ApplicationId IS NULL OR up.ApplicationId = @ApplicationId)
        WHERE p.IsActive = 1
          AND (p.ApplicationId IS NULL OR p.ApplicationId = @ApplicationId)
    ),
    ImpliedPermissions AS (
        -- Get implied permissions recursively
        SELECT 
            dp.PermissionId,
            dp.Permission,
            dp.GrantedVia,
            dp.ApplicationId
        FROM DirectPermissions dp
        
        UNION ALL
        
        SELECT 
            p.Id,
            p.Code,
            'implied:' + ip.Permission,
            p.ApplicationId
        FROM ImpliedPermissions ip
        INNER JOIN PermissionImplications pi ON pi.PermissionId = ip.PermissionId
        INNER JOIN Permissions p ON pi.ImpliedPermissionId = p.Id
        WHERE p.IsActive = 1
    )
    SELECT DISTINCT 
        Permission,
        GrantedVia,
        ApplicationId
    FROM ImpliedPermissions
    ORDER BY Permission;
END
```

**sp_ValidateCredentials.sql**
```sql
CREATE PROCEDURE [dbo].[sp_ValidateCredentials]
    @UsernameOrEmail NVARCHAR(255),
    @ApplicationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        u.Id,
        u.Username,
        u.Email,
        u.PasswordHash,
        u.Status,
        u.IsTwoFactorEnabled,
        u.FailedLoginAttempts,
        u.LockoutEndUtc,
        u.MustChangePassword,
        u.SecurityStamp
    FROM Users u
    WHERE (u.Username = @UsernameOrEmail OR u.NormalizedEmail = UPPER(@UsernameOrEmail))
      AND u.IsDeleted = 0;
END
```

**sp_RecordLoginAttempt.sql**
```sql
CREATE PROCEDURE [dbo].[sp_RecordLoginAttempt]
    @UserId UNIQUEIDENTIFIER = NULL,
    @Username NVARCHAR(255),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @IpAddress NVARCHAR(45),
    @UserAgent NVARCHAR(500) = NULL,
    @IsSuccessful BIT,
    @FailureReason NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO LoginAttempts (UserId, Username, ApplicationId, IpAddress, UserAgent, IsSuccessful, FailureReason)
    VALUES (@UserId, @Username, @ApplicationId, @IpAddress, @UserAgent, @IsSuccessful, @FailureReason);
    
    -- Update user's failed login count if applicable
    IF @UserId IS NOT NULL AND @IsSuccessful = 0
    BEGIN
        UPDATE Users
        SET FailedLoginAttempts = FailedLoginAttempts + 1,
            ModifiedAt = GETUTCDATE()
        WHERE Id = @UserId;
    END
    ELSE IF @UserId IS NOT NULL AND @IsSuccessful = 1
    BEGIN
        UPDATE Users
        SET FailedLoginAttempts = 0,
            LastLoginUtc = GETUTCDATE(),
            LastLoginIp = @IpAddress,
            ModifiedAt = GETUTCDATE()
        WHERE Id = @UserId;
    END
END
```

**sp_CheckAccountLockout.sql**
```sql
CREATE PROCEDURE [dbo].[sp_CheckAccountLockout]
    @UserId UNIQUEIDENTIFIER,
    @MaxAttempts INT = 5,
    @LockoutMinutes INT = 15
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @FailedAttempts INT;
    DECLARE @LockoutEnd DATETIME2;
    DECLARE @ShouldLock BIT = 0;
    
    SELECT 
        @FailedAttempts = FailedLoginAttempts,
        @LockoutEnd = LockoutEndUtc
    FROM Users
    WHERE Id = @UserId;
    
    -- Check if already locked
    IF @LockoutEnd IS NOT NULL AND @LockoutEnd > GETUTCDATE()
    BEGIN
        SELECT 
            1 AS IsLocked,
            @LockoutEnd AS LockoutEndUtc,
            DATEDIFF(MINUTE, GETUTCDATE(), @LockoutEnd) AS MinutesRemaining;
        RETURN;
    END
    
    -- Check if should be locked
    IF @FailedAttempts >= @MaxAttempts
    BEGIN
        SET @LockoutEnd = DATEADD(MINUTE, @LockoutMinutes, GETUTCDATE());
        
        UPDATE Users
        SET LockoutEndUtc = @LockoutEnd,
            Status = 3, -- Locked
            ModifiedAt = GETUTCDATE()
        WHERE Id = @UserId;
        
        SELECT 
            1 AS IsLocked,
            @LockoutEnd AS LockoutEndUtc,
            @LockoutMinutes AS MinutesRemaining;
        RETURN;
    END
    
    SELECT 
        0 AS IsLocked,
        NULL AS LockoutEndUtc,
        0 AS MinutesRemaining;
END
```

### User-Defined Types

**udt_PermissionCodeList.sql**
```sql
CREATE TYPE [dbo].[udt_PermissionCodeList] AS TABLE
(
    [Code] NVARCHAR(100) NOT NULL
);
```

**udt_GuidList.sql**
```sql
CREATE TYPE [dbo].[udt_GuidList] AS TABLE
(
    [Id] UNIQUEIDENTIFIER NOT NULL
);
```

### Seed Data Scripts

**01_DefaultApplications.sql**
```sql
-- Auth System itself as an application
INSERT INTO Applications (Id, Code, Name, Description, IsActive, RequireTwoFactor, SessionTimeoutMinutes, CreatedBy)
VALUES 
    ('00000000-0000-0000-0000-000000000001', 'auth', 'Authentication System', 'Central authentication and authorization system', 1, 0, 60, '00000000-0000-0000-0000-000000000000');
```

**02_DefaultRoles.sql**
```sql
-- Global system roles
INSERT INTO Roles (Id, Code, Name, Description, ApplicationId, IsSystem, IsActive, CreatedBy)
VALUES 
    ('00000000-0000-0000-0001-000000000001', 'super-admin', 'Super Administrator', 'Full system access', NULL, 1, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0001-000000000002', 'admin', 'Administrator', 'Administrative access', NULL, 1, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0001-000000000003', 'user', 'User', 'Standard user access', NULL, 1, 1, '00000000-0000-0000-0000-000000000000');
```

**03_DefaultPermissions.sql**
```sql
-- Global wildcard permission
INSERT INTO Permissions (Id, Code, Name, Description, ApplicationId, ParentId, Level, IsWildcard, IsActive, CreatedBy)
VALUES 
    ('00000000-0000-0000-0002-000000000001', '*', 'Super Admin', 'All permissions', NULL, NULL, 0, 1, 1, '00000000-0000-0000-0000-000000000000');

-- Auth system permissions
INSERT INTO Permissions (Id, Code, Name, Description, ApplicationId, ParentId, Level, IsWildcard, IsActive, CreatedBy)
VALUES 
    ('00000000-0000-0000-0002-000000000010', 'auth:*', 'All Auth Permissions', 'All authentication system permissions', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000001', 1, 1, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000011', 'auth:users:*', 'All User Permissions', 'All user management permissions', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000010', 2, 1, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000012', 'auth:users:read', 'Read Users', 'View user information', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000011', 3, 0, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000013', 'auth:users:write', 'Write Users', 'Create and update users', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000011', 3, 0, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000014', 'auth:users:delete', 'Delete Users', 'Delete users', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000011', 3, 0, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000020', 'auth:roles:*', 'All Role Permissions', 'All role management permissions', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000010', 2, 1, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000021', 'auth:roles:read', 'Read Roles', 'View role information', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000020', 3, 0, 1, '00000000-0000-0000-0000-000000000000'),
    ('00000000-0000-0000-0002-000000000022', 'auth:roles:write', 'Write Roles', 'Create and update roles', '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0002-000000000020', 3, 0, 1, '00000000-0000-0000-0000-000000000000');
```

**04_PermissionImplications.sql**
```sql
-- Write implies Read
INSERT INTO PermissionImplications (PermissionId, ImpliedPermissionId, CreatedBy)
VALUES 
    ('00000000-0000-0000-0002-000000000013', '00000000-0000-0000-0002-000000000012', '00000000-0000-0000-0000-000000000000'), -- users:write implies users:read
    ('00000000-0000-0000-0002-000000000014', '00000000-0000-0000-0002-000000000012', '00000000-0000-0000-0000-000000000000'), -- users:delete implies users:read
    ('00000000-0000-0000-0002-000000000022', '00000000-0000-0000-0002-000000000021', '00000000-0000-0000-0000-000000000000'); -- roles:write implies roles:read
```

**05_RolePermissions.sql**
```sql
-- Super Admin gets everything
INSERT INTO RolePermissions (RoleId, PermissionId, GrantedBy)
VALUES 
    ('00000000-0000-0000-0001-000000000001', '00000000-0000-0000-0002-000000000001', '00000000-0000-0000-0000-000000000000');

-- Admin gets auth:*
INSERT INTO RolePermissions (RoleId, PermissionId, GrantedBy)
VALUES 
    ('00000000-0000-0000-0001-000000000002', '00000000-0000-0000-0002-000000000010', '00000000-0000-0000-0000-000000000000');
```

---

## Authorization Architecture (Comprehensive)

### Permission Structure: Hierarchical Model

**The Auth System uses a hierarchical permission model with wildcard support:**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PERMISSION NAMING CONVENTION                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Format: {application}:{resource}:{action}                                  │
│                                                                             │
│  Examples:                                                                  │
│  • crm:leads:read         (read leads in CRM)                               │
│  • crm:leads:write        (create/update leads in CRM)                      │
│  • crm:leads:delete       (delete leads in CRM)                             │
│  • crm:leads:*            (all lead actions in CRM)                         │
│  • crm:*                  (all CRM permissions)                             │
│  • *                      (super admin - everything)                        │
│                                                                             │
│  HIERARCHY LEVELS:                                                          │
│  ─────────────────                                                          │
│  Level 0: Global          *                   (everything)                  │
│  Level 1: Application     crm:*               (all CRM)                     │
│  Level 2: Resource        crm:leads:*         (all lead operations)         │
│  Level 3: Action          crm:leads:read      (specific action)             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Permission Hierarchy Tree:**

```
                              ┌─────────┐
                              │    *    │  ← Super Admin (everything)
                              └────┬────┘
                                   │
              ┌────────────────────┼────────────────────┐
              │                    │                    │
              ▼                    ▼                    ▼
        ┌──────────┐        ┌──────────┐        ┌──────────┐
        │  crm:*   │        │  erp:*   │        │  hr:*    │
        └────┬─────┘        └────┬─────┘        └────┬─────┘
             │                   │                   │
    ┌────────┼────────┐         ...                 ...
    │        │        │
    ▼        ▼        ▼
┌────────┐┌────────┐┌────────┐
│ crm:   ││ crm:   ││ crm:   │
│ leads: ││reports:││ users: │
│   *    ││   *    ││   *    │
└───┬────┘└────────┘└────────┘
    │
┌───┼───────┬───────────┐
│   │       │           │
▼   ▼       ▼           ▼
┌──────┐┌──────┐┌──────┐┌──────┐
│leads:││leads:││leads:││leads:│
│ read ││write ││delete││export│
└──────┘└──────┘└──────┘└──────┘
```

**Permission Implication Rules:**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PERMISSION IMPLICATIONS                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  WILDCARD INHERITANCE (Automatic):                                          │
│  ─────────────────────────────────                                          │
│  • crm:leads:* grants crm:leads:read, write, delete, export                 │
│  • crm:* grants ALL crm:* permissions                                       │
│  • * grants EVERYTHING                                                      │
│                                                                             │
│  ACTION IMPLICATIONS (Configurable):                                        │
│  ──────────────────────────────────                                         │
│  • crm:leads:delete  →  implies crm:leads:read (must see to delete)         │
│  • crm:leads:write   →  implies crm:leads:read (must see to edit)           │
│  • crm:leads:export  →  implies crm:leads:read (must see to export)         │
│                                                                             │
│  NOT IMPLIED (Must be explicitly granted):                                  │
│  ─────────────────────────────────────────                                  │
│  • crm:leads:read    ↛  crm:leads:write  (read doesn't grant write)         │
│  • crm:leads:write   ↛  crm:leads:delete (write doesn't grant delete)       │
│  • crm:leads:*       ↛  crm:reports:*    (different resources)              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### JWT Token with Application-Specific Claims

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                JWT STRUCTURE PER APPLICATION                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  When John logs into CRM, he gets JWT with CRM-specific permissions:        │
│                                                                             │
│  {                                                                          │
│    "sub": "john-uuid",                                                      │
│    "email": "john@company.com",                                             │
│    "name": "John Doe",                                                      │
│    "iss": "https://auth.company.com",                                       │
│    "aud": "crm-system-uuid",            ← Audience is CRM                   │
│    "iat": 1704067200,                                                       │
│    "exp": 1704070800,                                                       │
│    "jti": "unique-token-id",            ← For audit correlation             │
│    "app": "crm",                         ← Application code                 │
│                                                                             │
│    // Application-specific                                                  │
│    "roles": ["admin", "sales-manager"],                                     │
│    "permissions": [                                                         │
│      "crm:leads:*",                                                         │
│      "crm:reports:read",                                                    │
│      "crm:dashboard:view"                                                   │
│    ],                                                                       │
│                                                                             │
│    // Global (available in all apps)                                        │
│    "global_roles": ["employee"],                                            │
│    "global_permissions": ["profile:read", "profile:update"]                 │
│  }                                                                          │
│                                                                             │
│  ─────────────────────────────────────────────────────────────────────────  │
│                                                                             │
│  When John logs into ERP, he gets DIFFERENT JWT:                            │
│                                                                             │
│  {                                                                          │
│    "sub": "john-uuid",                   ← Same user                        │
│    "email": "john@company.com",                                             │
│    "aud": "erp-system-uuid",            ← Different audience                │
│    "app": "erp",                                                            │
│                                                                             │
│    "roles": ["viewer"],                  ← Different roles                  │
│    "permissions": [                      ← Different permissions            │
│      "erp:orders:read",                                                     │
│      "erp:inventory:read"                                                   │
│    ],                                                                       │
│                                                                             │
│    "global_roles": ["employee"],         ← Same global roles                │
│    "global_permissions": ["profile:read", "profile:update"]                 │
│  }                                                                          │
│                                                                             │
│  SECURITY: CRM token CANNOT be used in ERP (audience mismatch)              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Permission Checker Implementation

```csharp
using Auth_Lib.Application.Interfaces;
using Auth_Lib.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Auth_Lib.Infrastructure.Security;

/// <summary>
/// Checks permissions considering hierarchy, wildcards, and implications.
/// </summary>
public class PermissionChecker : IPermissionChecker
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public PermissionChecker(
        IPermissionRepository permissionRepository,
        IMemoryCache cache)
    {
        _permissionRepository = permissionRepository;
        _cache = cache;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId, 
        string requiredPermission,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Get user's effective permissions (cached)
        var cacheKey = $"permissions:{userId}:{applicationId}";
        var userPermissions = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _permissionRepository.GetUserEffectivePermissionsAsync(
                userId, applicationId, cancellationToken);
        });

        if (userPermissions is null || !userPermissions.Any())
            return false;

        // 2. Check for exact match
        if (userPermissions.Contains(requiredPermission))
            return true;

        // 3. Check for wildcard matches
        // If user has "crm:leads:*", they can access "crm:leads:read"
        // If user has "crm:*", they can access "crm:leads:read"
        // If user has "*", they can access everything
        
        var parts = requiredPermission.Split(':');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var wildcardPermission = string.Join(":", parts.Take(i)) + (i > 0 ? ":*" : "*");
            if (userPermissions.Contains(wildcardPermission))
                return true;
        }

        // 4. Check global wildcard
        if (userPermissions.Contains("*"))
            return true;

        return false;
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(
        Guid userId,
        Guid? applicationId = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"permissions:{userId}:{applicationId}";
        var permissions = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await _permissionRepository.GetUserEffectivePermissionsAsync(
                userId, applicationId, cancellationToken);
        });

        return permissions?.ToList() ?? new List<string>();
    }

    public void InvalidateCache(Guid userId)
    {
        // Remove all cached permissions for this user
        // In production, use a distributed cache with pattern-based invalidation
        _cache.Remove($"permissions:{userId}:");
    }
}
```


---

## API Gateway Project (API_Gateway)

### Overview

The API Gateway serves as the single entry point for all external requests. It handles routing, rate limiting, and basic request validation using YARP (Yet Another Reverse Proxy).

### Project Structure

```
API_Gateway/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── API_Gateway.csproj
│
├── Configuration/
│   ├── YarpConfig.cs
│   └── RateLimitConfig.cs
│
├── Middleware/
│   ├── RequestLoggingMiddleware.cs
│   ├── ApiKeyValidationMiddleware.cs
│   └── GatewayTokenMiddleware.cs
│
├── Health/
│   └── GatewayHealthCheck.cs
│
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

### Project File (API_Gateway.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" Version="2.*" />
    <PackageReference Include="AspNetCoreRateLimit" Version="5.*" />
    <PackageReference Include="Serilog.AspNetCore" Version="8.*" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="10.*" />
  </ItemGroup>

</Project>
```

### Gateway Token Middleware (Bypass Prevention)

```csharp
namespace API_Gateway.Middleware;

/// <summary>
/// CRITICAL: Prevents external requests from forging internal gateway headers.
/// This middleware MUST be first in the pipeline.
/// </summary>
public class GatewayTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _gatewayToken;
    private const string GatewayTokenHeader = "X-Gateway-Token";
    private const string GatewayVerifiedHeader = "X-Gateway-Verified";

    public GatewayTokenMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _gatewayToken = configuration["Gateway:InternalToken"] 
            ?? throw new InvalidOperationException("Gateway:InternalToken not configured");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // SECURITY: Strip any incoming gateway headers - external clients cannot set these
        context.Request.Headers.Remove(GatewayTokenHeader);
        context.Request.Headers.Remove(GatewayVerifiedHeader);
        
        // Add gateway verification for downstream services
        context.Request.Headers.Add(GatewayTokenHeader, _gatewayToken);
        context.Request.Headers.Add(GatewayVerifiedHeader, "true");

        await _next(context);
    }
}
```

### Gateway Program.cs

```csharp
using API_Gateway.Middleware;
using AspNetCoreRateLimit;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((context, config) => 
    config.ReadFrom.Configuration(context.Configuration));

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Rate Limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<ClientRateLimitOptions>(builder.Configuration.GetSection("ClientRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<GatewayHealthCheck>("gateway");

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowConfigured", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// CRITICAL: Gateway token middleware MUST be first
app.UseMiddleware<GatewayTokenMiddleware>();

app.UseIpRateLimiting();
app.UseClientRateLimiting();

app.UseCors("AllowConfigured");

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
```

### Gateway appsettings.json

```json
{
  "Gateway": {
    "InternalToken": "CHANGE-THIS-TO-A-SECURE-RANDOM-VALUE"
  },
  "ReverseProxy": {
    "Routes": {
      "auth-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/api/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api" }
        ]
      },
      "well-known-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/.well-known/{**catch-all}"
        }
      },
      "connect-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/connect/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "auth-cluster": {
        "Destinations": {
          "auth-api": {
            "Address": "http://localhost:5001/"
          }
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:10",
            "Timeout": "00:00:05",
            "Policy": "ConsecutiveFailures",
            "Path": "/health"
          }
        }
      }
    }
  },
  "IpRateLimiting": {
    "EnableEndpointRateLimiting": true,
    "StackBlockedRequests": false,
    "RealIpHeader": "X-Forwarded-For",
    "ClientIdHeader": "X-ClientId",
    "HttpStatusCode": 429,
    "GeneralRules": [
      {
        "Endpoint": "*:/api/auth/login",
        "Period": "1m",
        "Limit": 10
      },
      {
        "Endpoint": "*:/api/auth/register",
        "Period": "1h",
        "Limit": 5
      },
      {
        "Endpoint": "*",
        "Period": "1s",
        "Limit": 100
      }
    ]
  },
  "Cors": {
    "AllowedOrigins": [
      "https://app.company.com",
      "https://admin.company.com"
    ]
  }
}
```

---

## AuthSystem.Client.SDK Project

### Overview

The AuthSystem.Client.SDK is a NuGet package distributed to external systems for easy integration with the Auth System. It provides a simple interface for token validation, user information retrieval, and permission checking.

### Project Structure

```
AuthSystem.Client.SDK/
├── AuthSystem.Client.SDK.csproj
├── IAuthSystemClient.cs
├── AuthSystemClient.cs
├── AuthSystemOptions.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs
├── Models/
│   ├── TokenValidationResult.cs
│   ├── TokenResponse.cs
│   ├── UserInfo.cs
│   └── ErrorResponse.cs
├── Handlers/
│   └── ApiKeyHandler.cs
└── README.md
```

### Project File (AuthSystem.Client.SDK.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
    <PackageId>AuthSystem.Client.SDK</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Company</Authors>
    <Description>Client SDK for integrating with the Enterprise Authentication System</Description>
    <PackageTags>authentication;authorization;jwt;oauth</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.*" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.*" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.*" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.*" />
    <PackageReference Include="Polly.Extensions.Http" Version="3.*" />
  </ItemGroup>

</Project>
```

### AuthSystemClient.cs (Implementation)

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using AuthSystem.Client.SDK.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AuthSystem.Client.SDK;

public class AuthSystemClient : IAuthSystemClient
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly AuthSystemOptions _options;

    public AuthSystemClient(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<AuthSystemOptions> options)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/validate",
            new { Token = token },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
            return new TokenValidationResult
            {
                IsValid = false,
                ErrorCode = error?.Code ?? "unknown_error",
                ErrorMessage = error?.Message ?? "Token validation failed"
            };
        }

        return await response.Content.ReadFromJsonAsync<TokenValidationResult>(cancellationToken)
            ?? new TokenValidationResult { IsValid = false, ErrorCode = "parse_error" };
    }

    public async Task<TokenResponse> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/refresh",
            new { RefreshToken = refreshToken },
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse token response");
    }

    public async Task<bool> RevokeTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "/api/auth/revoke",
            new { Token = token },
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<UserInfo?> GetUserInfoAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"user:{userId}";
        
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            
            var response = await _httpClient.GetAsync(
                $"/api/users/{userId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<UserInfo>(cancellationToken);
        });
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/permissions/check?permission={Uri.EscapeDataString(permission)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<PermissionCheckResult>(cancellationToken);
        return result?.HasPermission ?? false;
    }

    public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/permissions",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Array.Empty<string>();

        var result = await response.Content.ReadFromJsonAsync<PermissionsResponse>(cancellationToken);
        return result?.Permissions ?? Array.Empty<string>();
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/users/{userId}/roles",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Array.Empty<string>();

        var result = await response.Content.ReadFromJsonAsync<RolesResponse>(cancellationToken);
        return result?.Roles ?? Array.Empty<string>();
    }

    private record PermissionCheckResult(bool HasPermission);
    private record PermissionsResponse(IReadOnlyList<string> Permissions);
    private record RolesResponse(IReadOnlyList<string> Roles);
}
```

### ServiceCollectionExtensions.cs

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;
using System.IdentityModel.Tokens.Jwt;

namespace AuthSystem.Client.SDK.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Auth System client and JWT authentication to the application.
    /// </summary>
    public static IServiceCollection AddAuthSystemClient(
        this IServiceCollection services,
        Action<AuthSystemOptions> configure)
    {
        var options = new AuthSystemOptions();
        configure(options);

        services.Configure(configure);
        services.AddMemoryCache();

        // Configure HttpClient with retry policy
        services.AddHttpClient<IAuthSystemClient, AuthSystemClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
            client.DefaultRequestHeaders.Add("X-Application-Id", options.ApplicationId);
        })
        .AddPolicyHandler(GetRetryPolicy())
        .AddPolicyHandler(GetCircuitBreakerPolicy());

        // Configure JWT Bearer authentication
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                // CRITICAL: Disable claim type mapping
                jwtOptions.TokenHandlers.Clear();
                jwtOptions.TokenHandlers.Add(new JwtSecurityTokenHandler
                {
                    MapInboundClaims = false
                });

                jwtOptions.Authority = options.BaseUrl;
                jwtOptions.Audience = options.ApplicationId;
                jwtOptions.RequireHttpsMetadata = options.RequireHttps;

                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.BaseUrl,
                    ValidateAudience = true,
                    ValidAudience = options.ApplicationId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    MapInboundClaims = false,
                    RoleClaimType = "role",
                    NameClaimType = "name"
                };

                if (options.ValidateTokensOnline)
                {
                    jwtOptions.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var authClient = context.HttpContext.RequestServices
                                .GetRequiredService<IAuthSystemClient>();
                            var token = context.SecurityToken as JwtSecurityToken;

                            var result = await authClient.ValidateTokenAsync(
                                token?.RawData ?? string.Empty,
                                context.HttpContext.RequestAborted);

                            if (!result.IsValid)
                            {
                                context.Fail("Token validation failed");
                            }
                        }
                    };
                }
            });

        return services;
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
    }
}
```

---

## Technical Requirements

### Security Implementation

#### Password Hashing (Argon2id - MANDATORY)

```csharp
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

namespace Auth_Lib.Infrastructure.Security;

/// <summary>
/// Argon2id password hasher - OWASP recommended algorithm.
/// </summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    // OWASP recommended parameters for Argon2id
    private const int DegreeOfParallelism = 4;  // Number of threads
    private const int MemorySize = 65536;        // 64 MB in KB
    private const int Iterations = 3;            // Number of iterations
    private const int HashLength = 32;           // Output hash length in bytes
    private const int SaltLength = 16;           // Salt length in bytes

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = HashWithArgon2(password, salt);
        
        // Format: $argon2id$v=19$m=65536,t=3,p=4$<base64-salt>$<base64-hash>
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        try
        {
            var parts = hashedPassword.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id")
                return false;

            var salt = Convert.FromBase64String(parts[4]);
            var expectedHash = Convert.FromBase64String(parts[5]);

            var actualHash = HashWithArgon2(password, salt);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    public bool NeedsRehash(string hashedPassword)
    {
        // Check if hash was created with current parameters
        if (!hashedPassword.StartsWith($"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}$"))
            return true;

        return false;
    }

    private byte[] HashWithArgon2(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySize,
            Iterations = Iterations
        };

        return argon2.GetBytes(HashLength);
    }
}
```

#### JWT Token Service

```csharp
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace Auth_Lib.Infrastructure.Security;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly RSA _privateKey;
    private readonly RSA _publicKey;

    public JwtTokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
        
        // Load RSA keys from configuration
        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(_settings.PrivateKey);
        
        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(_settings.PublicKey);
    }

    public TokenResult GenerateTokens(User user, Application application, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        var tokenId = Guid.NewGuid().ToString();
        var issuedAt = DateTime.UtcNow;
        var accessTokenExpiry = issuedAt.AddMinutes(_settings.AccessTokenExpiryMinutes);
        var refreshTokenExpiry = issuedAt.AddDays(_settings.RefreshTokenExpiryDays);

        // Access Token Claims
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName ?? user.Username),
            new(JwtRegisteredClaimNames.Jti, tokenId),
            new(JwtRegisteredClaimNames.Iat, issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("app", application.Code),
            new("username", user.Username)
        };

        // Add roles
        foreach (var role in roles)
        {
            claims.Add(new Claim("role", role));
        }

        // Add permissions
        foreach (var permission in permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        // Create access token
        var accessToken = CreateToken(claims, accessTokenExpiry, application.Id.ToString());

        // Create refresh token (simpler, just for refresh)
        var refreshToken = GenerateRefreshToken();

        return new TokenResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiry = accessTokenExpiry,
            RefreshTokenExpiry = refreshTokenExpiry,
            TokenId = tokenId
        };
    }

    private string CreateToken(IEnumerable<Claim> claims, DateTime expiry, string audience)
    {
        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(_privateKey),
            SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiry,
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = false, // Audience is validated at API level
                ValidateLifetime = true,
                IssuerSigningKey = new RsaSecurityKey(_publicKey),
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }
}

public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}

public class TokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiry { get; set; }
    public DateTime RefreshTokenExpiry { get; set; }
    public string TokenId { get; set; } = string.Empty;
}
```


---

## Secrets Management (MANDATORY)

### Where to Store Secrets

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    SECRETS STORAGE HIERARCHY                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PRODUCTION (Azure Key Vault / AWS Secrets Manager / HashiCorp Vault):      │
│  ─────────────────────────────────────────────────────────────────────────  │
│  • JWT Private Key (RS256)                                                  │
│  • Database connection strings                                              │
│  • API encryption keys                                                      │
│  • Gateway internal token                                                   │
│  • 2FA encryption key                                                       │
│  • External service credentials                                             │
│                                                                             │
│  STAGING/DEVELOPMENT (User Secrets / Environment Variables):                │
│  ─────────────────────────────────────────────────────────────────────────  │
│  • Same secrets, different values                                           │
│  • NEVER share between environments                                         │
│                                                                             │
│  ⛔ NEVER IN SOURCE CONTROL:                                                │
│  • appsettings.Production.json with real secrets                            │
│  • .env files with production values                                        │
│  • Any file containing actual credentials                                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Secrets to Store

| Secret | Description | Rotation Frequency |
|--------|-------------|-------------------|
| `Jwt:PrivateKey` | RSA private key for signing JWTs | Annually |
| `Jwt:PublicKey` | RSA public key for verification | Annually |
| `ConnectionStrings:AuthDb` | SQL Server connection string | As needed |
| `Gateway:InternalToken` | Token for gateway → API communication | Quarterly |
| `TwoFactor:EncryptionKey` | AES key for 2FA secrets | Annually |
| `ApiKey:EncryptionKey` | Key for encrypting API key data | Annually |

### Key Vault Integration Code

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Azure Key Vault in production
if (builder.Environment.IsProduction())
{
    var keyVaultUri = builder.Configuration["KeyVault:Uri"];
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri!),
        new DefaultAzureCredential());
}

// Secrets are now available via Configuration
var jwtPrivateKey = builder.Configuration["Jwt:PrivateKey"];
```

---

## High Availability Design (MANDATORY)

### Multi-Instance Deployment

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    HIGH AVAILABILITY ARCHITECTURE                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│                         ┌─────────────────┐                                 │
│                         │  Load Balancer  │                                 │
│                         │    (Layer 7)    │                                 │
│                         └────────┬────────┘                                 │
│                                  │                                          │
│              ┌───────────────────┼───────────────────┐                      │
│              │                   │                   │                      │
│              ▼                   ▼                   ▼                      │
│       ┌────────────┐     ┌────────────┐     ┌────────────┐                 │
│       │ Gateway-1  │     │ Gateway-2  │     │ Gateway-3  │                 │
│       └─────┬──────┘     └─────┬──────┘     └─────┬──────┘                 │
│             │                  │                  │                         │
│             └──────────────────┼──────────────────┘                         │
│                                │                                            │
│              ┌─────────────────┼─────────────────┐                          │
│              │                 │                 │                          │
│              ▼                 ▼                 ▼                          │
│       ┌────────────┐   ┌────────────┐   ┌────────────┐                     │
│       │ Auth_API-1 │   │ Auth_API-2 │   │ Auth_API-3 │                     │
│       └─────┬──────┘   └─────┬──────┘   └─────┬──────┘                     │
│             │                │                │                             │
│             └────────────────┼────────────────┘                             │
│                              │                                              │
│                              ▼                                              │
│               ┌──────────────────────────────┐                              │
│               │     SQL Server (Primary)     │                              │
│               │      Always On AG / FCI      │                              │
│               └──────────────┬───────────────┘                              │
│                              │                                              │
│                              ▼                                              │
│               ┌──────────────────────────────┐                              │
│               │   SQL Server (Secondary)     │                              │
│               │    Synchronous Replica       │                              │
│               └──────────────────────────────┘                              │
│                                                                             │
│  Requirements:                                                              │
│  • Minimum 3 instances of each service                                      │
│  • Stateless services (no in-memory state)                                  │
│  • Database: SQL Server Always On Availability Groups                       │
│  • Redis for distributed caching (optional but recommended)                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Load Balancer Configuration

```yaml
# Kubernetes Service example
apiVersion: v1
kind: Service
metadata:
  name: auth-api-lb
spec:
  type: LoadBalancer
  selector:
    app: auth-api
  ports:
    - protocol: TCP
      port: 443
      targetPort: 8080
  sessionAffinity: None  # Stateless - no sticky sessions needed
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: auth-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: auth-api
  template:
    spec:
      containers:
      - name: auth-api
        resources:
          requests:
            memory: "512Mi"
            cpu: "500m"
          limits:
            memory: "1Gi"
            cpu: "1000m"
        readinessProbe:
          httpGet:
            path: /health/ready
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 10
        livenessProbe:
          httpGet:
            path: /health/live
            port: 8080
          initialDelaySeconds: 15
          periodSeconds: 20
```

### Health Check Endpoints

```csharp
// Auth_API health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("AuthDb")!,
        name: "database",
        tags: new[] { "ready" })
    .AddCheck<JwtKeyHealthCheck>("jwt-keys", tags: new[] { "ready" });

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

---

## Disaster Recovery Plan (MANDATORY)

### RTO/RPO Targets

| Metric | Target | Description |
|--------|--------|-------------|
| **RTO** (Recovery Time Objective) | 15 minutes | Maximum time to restore service |
| **RPO** (Recovery Point Objective) | 5 minutes | Maximum data loss acceptable |

### Backup Strategy

| Data | Frequency | Retention | Location |
|------|-----------|-----------|----------|
| Full Database | Daily | 30 days | Geo-redundant storage |
| Transaction Logs | Every 5 minutes | 7 days | Geo-redundant storage |
| Configuration | On change | 90 days | Git + Key Vault |
| JWT Keys | On rotation | Indefinite | Key Vault |

### Recovery Runbook (Checklist)

```markdown
## Disaster Recovery Checklist

### 1. Assess the Situation (5 min max)
- [ ] Identify failure scope (single instance, region, global)
- [ ] Check monitoring dashboards
- [ ] Notify incident commander

### 2. Database Recovery (if needed)
- [ ] Verify backup availability
- [ ] Initiate point-in-time restore
- [ ] Update connection strings if new instance

### 3. Service Recovery
- [ ] Scale up healthy instances
- [ ] Deploy to backup region if regional failure
- [ ] Verify Key Vault access

### 4. Validation
- [ ] Run health checks
- [ ] Test authentication flow
- [ ] Test token validation
- [ ] Verify audit logging

### 5. Communication
- [ ] Update status page
- [ ] Notify affected teams
- [ ] Document incident timeline
```

---

## Performance Baselines (MANDATORY)

### Target Metrics

| Operation | P50 Latency | P99 Latency | Target RPS |
|-----------|-------------|-------------|------------|
| Login | < 200ms | < 500ms | 500 |
| Token Validation | < 20ms | < 100ms | 5,000 |
| Token Refresh | < 100ms | < 300ms | 1,000 |
| User Lookup | < 50ms | < 200ms | 2,000 |
| Permission Check | < 30ms | < 150ms | 3,000 |

### Load Testing Requirements

```yaml
# k6 load test configuration
scenarios:
  login_flow:
    executor: 'ramping-vus'
    startVUs: 0
    stages:
      - duration: '2m', target: 100
      - duration: '5m', target: 500
      - duration: '2m', target: 0
    exec: loginScenario

  token_validation:
    executor: 'constant-arrival-rate'
    rate: 5000
    timeUnit: '1s'
    duration: '10m'
    preAllocatedVUs: 100
    maxVUs: 200
    exec: validateTokenScenario

thresholds:
  http_req_duration{scenario:login_flow}: ['p(95)<500']
  http_req_duration{scenario:token_validation}: ['p(99)<100']
  http_req_failed: ['rate<0.01']
```

---

## Rate Limiting Architecture (MANDATORY)

### Rate Limiting Responsibilities

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    RATE LIMITING ARCHITECTURE                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  API GATEWAY LEVEL (Global Protection):                                     │
│  ─────────────────────────────────────                                      │
│  • IP-based rate limiting (prevent DDoS)                                    │
│  • Global request throttling                                                │
│  • Endpoint-specific limits (e.g., /login stricter than /users)             │
│                                                                             │
│  AUTH_API LEVEL (Business Logic):                                           │
│  ────────────────────────────────                                           │
│  • API Key rate limiting (per application)                                  │
│  • User-specific limits (e.g., password attempts)                           │
│  • Tenant-based quotas (if multi-tenant)                                    │
│                                                                             │
│  RATE LIMIT HEADERS (Returned to Client):                                   │
│  ─────────────────────────────────────────                                  │
│  • X-RateLimit-Limit: 100                                                   │
│  • X-RateLimit-Remaining: 95                                                │
│  • X-RateLimit-Reset: 1640000000                                            │
│  • Retry-After: 30 (when 429 returned)                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Rate Limit Configuration

```csharp
// Auth_API rate limiting for API keys
services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var apiKey = context.Request.Headers["X-API-Key"].ToString();
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            return RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: apiKey,
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6
                });
        }
        
        // IP-based for non-API requests
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ipAddress,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "30";
        
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "rate_limit_exceeded",
            message = "Too many requests. Please try again later.",
            retryAfter = 30
        }, token);
    };
});
```

---

## CI/CD Integration (MANDATORY)

### Pipeline Stages

```yaml
# Azure DevOps / GitHub Actions pipeline
stages:
  - stage: Build
    jobs:
      - job: BuildAndTest
        steps:
          - task: UseDotNet@2
            inputs:
              version: '10.x'
          
          - script: dotnet restore
          - script: dotnet build --configuration Release
          - script: dotnet test --no-build --configuration Release
          
          - task: PublishBuildArtifacts@1
            inputs:
              pathToPublish: '$(Build.ArtifactStagingDirectory)'

  - stage: DeployStaging
    dependsOn: Build
    jobs:
      - deployment: DeployToStaging
        environment: 'staging'
        strategy:
          runOnce:
            deploy:
              steps:
                - script: |
                    # Run database migrations FIRST
                    dotnet run --project Auth_Setup -- migrate --connection "$(StagingConnectionString)"
                    
                - task: AzureWebApp@1
                  inputs:
                    appName: 'auth-api-staging'
                    package: '$(Pipeline.Workspace)/**/*.zip'

  - stage: DeployProduction
    dependsOn: DeployStaging
    jobs:
      - deployment: DeployToProduction
        environment: 'production'
        strategy:
          runOnce:
            deploy:
              steps:
                - script: |
                    # Blue-green deployment with database migrations
                    dotnet run --project Auth_Setup -- migrate --connection "$(ProdConnectionString)"
                    
                - task: AzureWebApp@1
                  inputs:
                    appName: 'auth-api-production'
                    deploymentMethod: 'auto'  # Blue-green
```

### Database Migration Strategy

```csharp
// Auth_Setup migration command
public class MigrateCommand : ICommand
{
    public async Task<int> ExecuteAsync(string connectionString)
    {
        // 1. Take backup before migration
        await BackupDatabaseAsync(connectionString);
        
        // 2. Run migrations in transaction
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        try
        {
            var migrations = GetPendingMigrations();
            foreach (var migration in migrations)
            {
                await ExecuteMigrationAsync(connection, transaction, migration);
            }
            
            transaction.Commit();
            return 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
```

### Rollback Procedure

```markdown
## Rollback Procedure

### Application Rollback
1. Azure: Use deployment slots to swap back to previous version
2. Kubernetes: `kubectl rollout undo deployment/auth-api`

### Database Rollback
1. Identify the migration to rollback
2. Run: `dotnet run --project Auth_Setup -- rollback --migration V1_0_2`
3. Rollback scripts are paired with each migration

### Emergency Rollback (if all else fails)
1. Restore database from backup
2. Deploy last known good version from artifact storage
3. Update DNS/Load Balancer to point to restored services
```

---

## Implementation Phases

### Phase 1: Core Authentication (Weeks 1-2)
- [ ] Project setup and solution structure
- [ ] Database schema creation
- [ ] User entity and repository
- [ ] Password hashing (Argon2id)
- [ ] Login/logout endpoints
- [ ] JWT token generation

### Phase 2: API Gateway (Weeks 3-4)
- [ ] YARP configuration
- [ ] Rate limiting setup
- [ ] Gateway token middleware
- [ ] Health checks
- [ ] Request logging

### Phase 3: User Management (Weeks 5-6)
- [ ] CRUD operations for users
- [ ] Email verification
- [ ] Password reset flow
- [ ] Account lockout logic
- [ ] Session management

### Phase 4: Authorization (Weeks 7-8)
- [ ] Role management
- [ ] Permission management
- [ ] Permission hierarchy
- [ ] Permission checking service
- [ ] Application-scoped permissions

### Phase 5: API Key Management (Weeks 9-10)
- [ ] API key generation
- [ ] API key validation
- [ ] Scoped permissions for API keys
- [ ] Rate limiting per API key
- [ ] API key rotation

### Phase 6: Advanced Features (Weeks 11-12)
- [ ] Two-factor authentication
- [ ] Audit logging
- [ ] Client SDK finalization
- [ ] Performance optimization
- [ ] Load testing

---

## Success Criteria

### Core Functionality
- [ ] Users can register, login, and logout
- [ ] JWT tokens are properly signed and validated
- [ ] Refresh token rotation works correctly
- [ ] Password reset flow is complete
- [ ] Account lockout triggers after N failed attempts

### Security
- [ ] All passwords hashed with Argon2id
- [ ] JWT signed with RS256 (asymmetric)
- [ ] No secrets in source control
- [ ] Rate limiting prevents brute force
- [ ] Gateway prevents header spoofing

### Architecture
- [ ] Single database (Auth_DB) for all auth data
- [ ] MediatR for domain events
- [ ] ErrorOr for result pattern
- [ ] No external system has DB access
- [ ] Client SDK works for external integration

### Operations
- [ ] Health checks pass
- [ ] Metrics are exposed
- [ ] Logs are structured (JSON)
- [ ] Database migrations are reversible
- [ ] Backup/restore tested

---

## Final Checklist

### Code Review
- [ ] No hardcoded secrets
- [ ] All async operations use CancellationToken
- [ ] Guard clauses use Ardalis.GuardClauses
- [ ] Result types use ErrorOr
- [ ] Domain events use MediatR INotification

### Security Review
- [ ] OWASP Top 10 addressed
- [ ] JWT implementation follows best practices
- [ ] Password policy enforced
- [ ] Rate limiting configured
- [ ] Audit logging comprehensive

### Architecture Review
- [ ] Modular monolith boundaries clear
- [ ] No circular dependencies
- [ ] Database schema normalized
- [ ] Indexes on frequently queried columns
- [ ] Stored procedures for complex queries

### Operations Review
- [ ] Health checks implemented
- [ ] Graceful shutdown handled
- [ ] Configuration externalized
- [ ] Secrets in Key Vault
- [ ] Monitoring dashboards created

---

## Implementation Reminder (Read This Before EVERY Session)

> **This is a centralized authentication system. EVERY decision affects security, performance, and reliability for ALL connected applications.**

### Key v9 Changes to Remember:
- ❌ No Foundation_Lib → Use ErrorOr, Ardalis.GuardClauses
- ❌ No MessagingHub_Lib → Use MediatR for domain events
- ✅ Auth_API is a modular monolith (MediatR between modules, NOT HTTP)
- ✅ Single Auth_DB database (no database-per-service)

### When in Doubt, Choose:
- **Security** over convenience
- **Simplicity** over cleverness
- **Explicit** over implicit
- **Tested** over assumed
- **Standard library** over custom implementation

### Key Principles to Remember:
- External systems get SDK + 3 config values (BaseUrl, ApiKey, ApplicationId)
- External systems NEVER get database access
- JWT private key NEVER leaves the auth system
- All auth events are audited
- Passwords use Argon2id (OWASP recommended)


---

## API Gateway Project - Complete Implementation (API_Gateway)

> **Note**: This section expands on the API Gateway overview with complete implementation details.

### Project Independence (CRITICAL)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    API_GATEWAY - INDEPENDENT PROJECT                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  API_Gateway References (MINIMAL):                                          │
│  ─────────────────────────────────                                          │
│  • Yarp.ReverseProxy                                                        │
│  • Microsoft.AspNetCore.Authentication.JwtBearer                            │
│  • AspNetCoreRateLimit                                                      │
│  • Serilog.AspNetCore                                                       │
│  • Prometheus-net                                                           │
│                                                                             │
│  ├── NO reference to: Auth_Lib                                              │
│  ├── NO reference to: Auth_Localization                                     │
│  └── NO reference to: Any internal project                                  │
│                                                                             │
│  WHY:                                                                       │
│  • Gateway uses STANDARD protocols (OIDC, JWT)                              │
│  • Gateway fetches public keys via /.well-known/jwks.json                   │
│  • Gateway fetches config via /.well-known/openid-configuration             │
│  • No coupling to internal implementation details                           │
│  • Can be deployed independently of Auth System updates                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Complete Project Structure

```
API_Gateway/
├── Configuration/
│   ├── YarpConfig.cs
│   ├── RateLimitingConfig.cs
│   ├── CacheConfig.cs
│   └── LoggingConfig.cs
├── Middleware/
│   ├── GatewayTokenMiddleware.cs
│   ├── ApiKeyAuthenticationMiddleware.cs
│   ├── RequestLoggingMiddleware.cs
│   ├── CorrelationIdMiddleware.cs
│   └── ExceptionHandlingMiddleware.cs
├── Constants/
│   └── AuthClaimTypes.cs
├── Services/
│   ├── Aggregation/
│   │   ├── IAggregationService.cs
│   │   ├── UserProfileAggregator.cs
│   │   └── DashboardAggregator.cs
│   ├── Caching/
│   │   ├── IGatewayCacheService.cs
│   │   └── RedisCacheService.cs
│   └── HealthCheck/
│       └── ServiceHealthCheck.cs
├── Transforms/
│   ├── RequestTransforms.cs
│   └── ResponseTransforms.cs
├── Metrics/
│   └── GatewayMetrics.cs
├── Models/
│   ├── GatewayRequest.cs
│   ├── GatewayResponse.cs
│   └── AggregatedResponse.cs
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Production.json
├── Program.cs
└── API_Gateway.csproj
```

### Local Claim Type Constants

```csharp
// Constants/AuthClaimTypes.cs
namespace API_Gateway.Constants;

/// <summary>
/// Custom claim types used by the Auth System.
/// These are duplicated here intentionally to keep API_Gateway independent.
/// </summary>
public static class AuthClaimTypes
{
    public const string Permission = "permission";
    public const string GlobalPermission = "global_permission";
    public const string ApplicationCode = "app";
}
```

### Full YARP Configuration (appsettings.json)

```json
{
  "ReverseProxy": {
    "Routes": {
      "auth-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/api/v1/auth/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/v1" }
        ]
      },
      "users-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/api/v1/users/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/v1" }
        ]
      },
      "roles-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/api/v1/roles/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/v1" }
        ]
      },
      "permissions-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/api/v1/permissions/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/v1" }
        ]
      },
      "audit-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/api/v1/audit/{**catch-all}"
        },
        "Transforms": [
          { "PathRemovePrefix": "/api/v1" }
        ],
        "AuthorizationPolicy": "AdminOnly"
      },
      "well-known-route": {
        "ClusterId": "auth-cluster",
        "Match": {
          "Path": "/.well-known/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "auth-cluster": {
        "Destinations": {
          "auth-api": {
            "Address": "http://localhost:5001/"
          }
        },
        "HealthCheck": {
          "Active": {
            "Enabled": true,
            "Interval": "00:00:30",
            "Timeout": "00:00:10",
            "Path": "/health"
          }
        }
      }
    }
  }
}
```

### JWT Configuration with OIDC Discovery (CRITICAL)

```csharp
// Program.cs - JWT Bearer with OIDC Discovery
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // CRITICAL: Disable default claim type mapping
        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        });

        // OIDC Discovery - automatically fetches:
        // • Issuer from /.well-known/openid-configuration
        // • Signing keys from /.well-known/jwks.json
        options.Authority = builder.Configuration["AuthSystem:BaseUrl"];
        options.Audience = "api-gateway";
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // Use short claim type names (not Microsoft's long URIs)
            RoleClaimType = "role",
            NameClaimType = "name",
            MapInboundClaims = false,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                var userId = context.Principal?.FindFirst("sub")?.Value;
                logger.LogDebug("Token validated for user: {UserId}", userId);
                return Task.CompletedTask;
            }
        };
    });
```

### API Key Authentication Middleware (Full)

```csharp
// Middleware/ApiKeyAuthenticationMiddleware.cs
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace API_Gateway.Middleware;

public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly string _authSystemBaseUrl;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _httpClient = httpClientFactory.CreateClient("AuthService");
        _cache = cache;
        _logger = logger;
        _authSystemBaseUrl = configuration["AuthSystem:BaseUrl"]!;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(apiKey))
        {
            await _next(context);
            return;
        }
        
        // Check local cache first (short TTL for security)
        var cacheKey = $"apikey:{apiKey[..Math.Min(8, apiKey.Length)]}";
        if (_cache.TryGetValue(cacheKey, out ApiKeyValidationResult? cached) && cached!.IsValid)
        {
            context.User = CreatePrincipal(cached);
            context.Items["AuthMethod"] = "ApiKey";
            await _next(context);
            return;
        }
        
        // Call Auth System API to validate (no internal dependency!)
        var result = await ValidateApiKeyAsync(apiKey, context.RequestAborted);
        
        // Cache result briefly (30 seconds max for security)
        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
        
        if (result.IsValid)
        {
            context.User = CreatePrincipal(result);
            context.Items["AuthMethod"] = "ApiKey";
            _logger.LogDebug("API Key authenticated for application: {AppCode}", result.ApplicationCode);
        }
        else
        {
            _logger.LogWarning("Invalid API Key attempt from IP: {IP}", 
                context.Connection.RemoteIpAddress);
        }
        
        await _next(context);
    }
    
    private async Task<ApiKeyValidationResult> ValidateApiKeyAsync(
        string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/v1/auth/validate-api-key",
                new { ApiKey = apiKey },
                cancellationToken);
                
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ApiKeyValidationResult>(cancellationToken)
                    ?? new ApiKeyValidationResult { IsValid = false };
            }
            
            return new ApiKeyValidationResult { IsValid = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate API key");
            return new ApiKeyValidationResult { IsValid = false };
        }
    }
    
    private static ClaimsPrincipal CreatePrincipal(ApiKeyValidationResult result)
    {
        var claims = new List<Claim>
        {
            new("ApiKeyId", result.ApiKeyId.ToString()),
            new("app", result.ApplicationCode ?? ""),
            new("sub", $"apikey:{result.ApiKeyId}")
        };
        
        if (result.Permissions != null)
        {
            claims.AddRange(result.Permissions.Select(p => new Claim("permission", p)));
        }
        
        var identity = new ClaimsIdentity(claims, "ApiKey");
        return new ClaimsPrincipal(identity);
    }
}

public class ApiKeyValidationResult
{
    public bool IsValid { get; set; }
    public Guid ApiKeyId { get; set; }
    public string? ApplicationCode { get; set; }
    public IEnumerable<string>? Permissions { get; set; }
}
```

### Request Logging Middleware (Full)

```csharp
// Middleware/RequestLoggingMiddleware.cs
using System.Diagnostics;
using Serilog.Context;

namespace API_Gateway.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() 
            ?? Guid.NewGuid().ToString();
        
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path))
        using (LogContext.PushProperty("RequestMethod", context.Request.Method))
        using (LogContext.PushProperty("ClientIP", context.Connection.RemoteIpAddress?.ToString()))
        {
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Gateway Request: {Method} {Path} from {ClientIP}",
                context.Request.Method,
                context.Request.Path,
                context.Connection.RemoteIpAddress);

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                
                _logger.LogInformation(
                    "Gateway Response: {Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds);
                
                // Emit Prometheus metrics
                GatewayMetrics.RequestDuration
                    .WithLabels(
                        context.Request.Method, 
                        context.Request.Path, 
                        context.Response.StatusCode.ToString())
                    .Observe(stopwatch.Elapsed.TotalSeconds);
            }
        }
    }
}
```

### Correlation ID Middleware

```csharp
// Middleware/CorrelationIdMiddleware.cs
namespace API_Gateway.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers["X-Correlation-Id"] = correlationId;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            return Task.CompletedTask;
        });

        // Set for downstream services via YARP
        context.Items["CorrelationId"] = correlationId;

        await _next(context);
    }
}
```

### Prometheus Metrics

```csharp
// Metrics/GatewayMetrics.cs
using Prometheus;

namespace API_Gateway.Metrics;

public static class GatewayMetrics
{
    public static readonly Histogram RequestDuration = Prometheus.Metrics.CreateHistogram(
        "gateway_request_duration_seconds",
        "Duration of HTTP requests through the gateway",
        new HistogramConfiguration
        {
            LabelNames = new[] { "method", "path", "status_code" },
            Buckets = new[] { 0.01, 0.05, 0.1, 0.25, 0.5, 1.0, 2.5, 5.0, 10.0 }
        });

    public static readonly Counter RequestsTotal = Prometheus.Metrics.CreateCounter(
        "gateway_requests_total",
        "Total number of requests through the gateway",
        new CounterConfiguration
        {
            LabelNames = new[] { "method", "path", "status_code" }
        });

    public static readonly Counter AuthFailures = Prometheus.Metrics.CreateCounter(
        "gateway_auth_failures_total",
        "Total number of authentication failures",
        new CounterConfiguration
        {
            LabelNames = new[] { "reason" }
        });

    public static readonly Gauge ActiveConnections = Prometheus.Metrics.CreateGauge(
        "gateway_active_connections",
        "Number of active connections to the gateway");
}
```

### Gateway Cache Service

```csharp
// Services/Caching/IGatewayCacheService.cs
namespace API_Gateway.Services.Caching;

public interface IGatewayCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default);
}

// Services/Caching/RedisCacheService.cs
using System.Text.Json;
using StackExchange.Redis;

namespace API_Gateway.Services.Caching;

public class RedisCacheService : IGatewayCacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        
        if (value.IsNullOrEmpty)
            return default;
            
        return JsonSerializer.Deserialize<T>(value!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, serialized, expiration ?? TimeSpan.FromMinutes(5));
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }

    public async Task<T> GetOrSetAsync<T>(
        string key, 
        Func<Task<T>> factory, 
        TimeSpan? expiration = null, 
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var value = await factory();
        await SetAsync(key, value, expiration, cancellationToken);
        return value;
    }
}
```

### Response Aggregation Service

```csharp
// Services/Aggregation/UserProfileAggregator.cs
namespace API_Gateway.Services.Aggregation;

public class UserProfileAggregator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserProfileAggregator> _logger;

    public UserProfileAggregator(
        IHttpClientFactory httpClientFactory,
        ILogger<UserProfileAggregator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<UserProfileAggregated> GetUserProfileAsync(
        Guid userId, 
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("AuthService");
        
        // Parallel requests for better performance
        var userTask = client.GetFromJsonAsync<UserDetails>(
            $"/api/users/{userId}", cancellationToken);
        var rolesTask = client.GetFromJsonAsync<List<string>>(
            $"/api/users/{userId}/roles", cancellationToken);
        var permissionsTask = client.GetFromJsonAsync<List<string>>(
            $"/api/users/{userId}/permissions", cancellationToken);
        var sessionsTask = client.GetFromJsonAsync<List<SessionInfo>>(
            $"/api/users/{userId}/sessions", cancellationToken);

        await Task.WhenAll(userTask, rolesTask, permissionsTask, sessionsTask);

        return new UserProfileAggregated
        {
            User = await userTask,
            Roles = await rolesTask ?? new List<string>(),
            Permissions = await permissionsTask ?? new List<string>(),
            ActiveSessions = await sessionsTask ?? new List<SessionInfo>()
        };
    }
}

public class UserProfileAggregated
{
    public UserDetails? User { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<SessionInfo> ActiveSessions { get; set; } = new();
}

public class UserDetails
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}
```

### Complete Gateway Program.cs

```csharp
// Program.cs
using System.IdentityModel.Tokens.Jwt;
using API_Gateway.Middleware;
using API_Gateway.Metrics;
using API_Gateway.Services.Aggregation;
using API_Gateway.Services.Caching;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Serilog configuration
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .WriteTo.Console()
        .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5341");
});

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Authentication with DISABLED claim type mapping
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // CRITICAL: Disable default claim type mapping
        options.TokenHandlers.Clear();
        options.TokenHandlers.Add(new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        });

        options.Authority = builder.Configuration["AuthSystem:BaseUrl"];
        options.Audience = builder.Configuration["Jwt:Audience"] ?? "api-gateway";
        options.RequireHttpsMetadata = builder.Environment.IsProduction();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            MapInboundClaims = false,
            RoleClaimType = "role",
            NameClaimType = "name",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin", "super-admin"));
    options.AddPolicy("ApiKeyAccess", policy => 
        policy.RequireAssertion(ctx => ctx.User.HasClaim(c => c.Type == "ApiKeyId")));
});

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "rate_limit_exceeded",
            message = "Too many requests. Please try again later.",
            retryAfter = 60
        }, token);
    };
});

// Redis for caching
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost"));
builder.Services.AddSingleton<IGatewayCacheService, RedisCacheService>();

// Memory cache fallback
builder.Services.AddMemoryCache();

// HTTP clients for downstream services
builder.Services.AddHttpClient("AuthService", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AuthSystem:BaseUrl"]!);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Aggregation services
builder.Services.AddScoped<UserProfileAggregator>();

// Health checks
builder.Services.AddHealthChecks()
    .AddRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost", "redis")
    .AddUrlGroup(new Uri($"{builder.Configuration["AuthSystem:BaseUrl"]}/health"), "auth-api");

var app = builder.Build();

// Middleware Pipeline (ORDER MATTERS!)

// 1. Gateway token - MUST be first to prevent header spoofing
app.UseMiddleware<GatewayTokenMiddleware>();

// 2. Correlation ID
app.UseMiddleware<CorrelationIdMiddleware>();

// 3. Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

// 4. Rate limiting
app.UseRateLimiter();

// 5. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 6. API Key authentication (after JWT)
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

// Prometheus metrics endpoint
app.UseMetricServer();
app.UseHttpMetrics();

// Health checks
app.MapHealthChecks("/health");

// YARP reverse proxy
app.MapReverseProxy();

app.Run();
```


---

## Auth_Localization Project - Complete Implementation

> **Note**: This section expands on the Localization overview with complete resource files and service implementations.

### Complete Resource Files

#### SharedTexts.resx (English - Default) - Full File

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  
  <!-- Authentication Messages -->
  <data name="InvalidCredentials" xml:space="preserve">
    <value>The username or password is incorrect.</value>
    <comment>Displayed when login credentials are invalid</comment>
  </data>
  <data name="UserBlocked" xml:space="preserve">
    <value>Your account has been blocked. Please contact support.</value>
    <comment>Displayed when a blocked user attempts to login</comment>
  </data>
  <data name="TokenExpired" xml:space="preserve">
    <value>Your session has expired. Please log in again.</value>
    <comment>Displayed when JWT token has expired</comment>
  </data>
  <data name="InvalidRefreshToken" xml:space="preserve">
    <value>Invalid or expired refresh token. Please log in again.</value>
    <comment>Displayed when refresh token is invalid or expired</comment>
  </data>
  <data name="UserNotFound" xml:space="preserve">
    <value>User not found.</value>
    <comment>Displayed when requested user does not exist</comment>
  </data>
  <data name="ActionNotAllowed" xml:space="preserve">
    <value>You do not have permission to perform this action.</value>
    <comment>Displayed when user lacks required permissions</comment>
  </data>
  <data name="UnexpectedError" xml:space="preserve">
    <value>An unexpected error occurred. Please try again later.</value>
    <comment>Generic error message for unhandled exceptions</comment>
  </data>
  
  <!-- Account Management Messages -->
  <data name="AccountLocked" xml:space="preserve">
    <value>Your account has been locked due to multiple failed login attempts. Please try again in {0} minutes.</value>
    <comment>Displayed when account is temporarily locked. {0} = minutes remaining</comment>
  </data>
  <data name="PasswordResetRequired" xml:space="preserve">
    <value>You must reset your password before continuing.</value>
    <comment>Displayed when password reset is required</comment>
  </data>
  <data name="PasswordChangedSuccessfully" xml:space="preserve">
    <value>Your password has been changed successfully.</value>
    <comment>Confirmation message after password change</comment>
  </data>
  <data name="PasswordTooWeak" xml:space="preserve">
    <value>Password does not meet the security requirements.</value>
    <comment>Displayed when password fails complexity check</comment>
  </data>
  <data name="PasswordHistoryViolation" xml:space="preserve">
    <value>You cannot reuse your previous {0} passwords.</value>
    <comment>Displayed when new password matches a recent password. {0} = number of passwords</comment>
  </data>
  
  <!-- Two-Factor Authentication Messages -->
  <data name="TwoFactorRequired" xml:space="preserve">
    <value>Two-factor authentication is required.</value>
    <comment>Displayed when 2FA verification is needed</comment>
  </data>
  <data name="InvalidTwoFactorCode" xml:space="preserve">
    <value>The verification code is invalid or has expired.</value>
    <comment>Displayed when 2FA code is incorrect</comment>
  </data>
  <data name="TwoFactorEnabledSuccessfully" xml:space="preserve">
    <value>Two-factor authentication has been enabled successfully.</value>
    <comment>Confirmation message after enabling 2FA</comment>
  </data>
  <data name="TwoFactorDisabledSuccessfully" xml:space="preserve">
    <value>Two-factor authentication has been disabled.</value>
    <comment>Confirmation message after disabling 2FA</comment>
  </data>
  
  <!-- API Key Messages -->
  <data name="InvalidApiKey" xml:space="preserve">
    <value>The API key is invalid or has been revoked.</value>
    <comment>Displayed when API key validation fails</comment>
  </data>
  <data name="ApiKeyExpired" xml:space="preserve">
    <value>The API key has expired. Please request a new key.</value>
    <comment>Displayed when API key has passed its expiration date</comment>
  </data>
  <data name="ApiKeyRateLimitExceeded" xml:space="preserve">
    <value>API key rate limit exceeded. Please try again later.</value>
    <comment>Displayed when API key exceeds rate limits</comment>
  </data>
  <data name="ApiKeyCreatedSuccessfully" xml:space="preserve">
    <value>API key has been created successfully.</value>
    <comment>Confirmation message after creating API key</comment>
  </data>
  
  <!-- Session Messages -->
  <data name="SessionExpired" xml:space="preserve">
    <value>Your session has expired. Please log in again.</value>
    <comment>Displayed when user session expires</comment>
  </data>
  <data name="ConcurrentSessionLimitReached" xml:space="preserve">
    <value>Maximum number of concurrent sessions reached. Please log out from another device.</value>
    <comment>Displayed when concurrent session limit is exceeded</comment>
  </data>
  <data name="LogoutSuccessful" xml:space="preserve">
    <value>You have been logged out successfully.</value>
    <comment>Confirmation message after logout</comment>
  </data>
  
  <!-- Validation Messages -->
  <data name="RequiredField" xml:space="preserve">
    <value>This field is required.</value>
    <comment>Generic required field validation message</comment>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>Please enter a valid email address.</value>
    <comment>Displayed when email format is invalid</comment>
  </data>
  <data name="InvalidUsernameFormat" xml:space="preserve">
    <value>Username must be between {0} and {1} characters and contain only letters, numbers, and underscores.</value>
    <comment>Displayed when username format is invalid. {0} = min length, {1} = max length</comment>
  </data>
  
</root>
```

#### SharedTexts.ar.resx (Arabic) - Full File

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <!-- Schema definition same as default file -->
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms</value>
  </resheader>
  
  <!-- Authentication Messages -->
  <data name="InvalidCredentials" xml:space="preserve">
    <value>اسم المستخدم أو كلمة المرور غير صحيحة.</value>
  </data>
  <data name="UserBlocked" xml:space="preserve">
    <value>تم حظر حسابك. يرجى التواصل مع الدعم.</value>
  </data>
  <data name="TokenExpired" xml:space="preserve">
    <value>انتهت صلاحية جلستك. يرجى تسجيل الدخول مرة أخرى.</value>
  </data>
  <data name="InvalidRefreshToken" xml:space="preserve">
    <value>رمز التحديث غير صالح أو منتهي الصلاحية. يرجى تسجيل الدخول مرة أخرى.</value>
  </data>
  <data name="UserNotFound" xml:space="preserve">
    <value>المستخدم غير موجود.</value>
  </data>
  <data name="ActionNotAllowed" xml:space="preserve">
    <value>ليس لديك صلاحية للقيام بهذا الإجراء.</value>
  </data>
  <data name="UnexpectedError" xml:space="preserve">
    <value>حدث خطأ غير متوقع. يرجى المحاولة مرة أخرى لاحقاً.</value>
  </data>
  
  <!-- Account Management Messages -->
  <data name="AccountLocked" xml:space="preserve">
    <value>تم قفل حسابك بسبب محاولات تسجيل دخول فاشلة متعددة. يرجى المحاولة مرة أخرى بعد {0} دقيقة.</value>
  </data>
  <data name="PasswordResetRequired" xml:space="preserve">
    <value>يجب إعادة تعيين كلمة المرور قبل المتابعة.</value>
  </data>
  <data name="PasswordChangedSuccessfully" xml:space="preserve">
    <value>تم تغيير كلمة المرور بنجاح.</value>
  </data>
  <data name="PasswordTooWeak" xml:space="preserve">
    <value>كلمة المرور لا تستوفي متطلبات الأمان.</value>
  </data>
  <data name="PasswordHistoryViolation" xml:space="preserve">
    <value>لا يمكنك إعادة استخدام آخر {0} كلمات مرور.</value>
  </data>
  
  <!-- Two-Factor Authentication Messages -->
  <data name="TwoFactorRequired" xml:space="preserve">
    <value>المصادقة الثنائية مطلوبة.</value>
  </data>
  <data name="InvalidTwoFactorCode" xml:space="preserve">
    <value>رمز التحقق غير صالح أو منتهي الصلاحية.</value>
  </data>
  <data name="TwoFactorEnabledSuccessfully" xml:space="preserve">
    <value>تم تفعيل المصادقة الثنائية بنجاح.</value>
  </data>
  <data name="TwoFactorDisabledSuccessfully" xml:space="preserve">
    <value>تم تعطيل المصادقة الثنائية.</value>
  </data>
  
  <!-- API Key Messages -->
  <data name="InvalidApiKey" xml:space="preserve">
    <value>مفتاح API غير صالح أو تم إلغاؤه.</value>
  </data>
  <data name="ApiKeyExpired" xml:space="preserve">
    <value>انتهت صلاحية مفتاح API. يرجى طلب مفتاح جديد.</value>
  </data>
  <data name="ApiKeyRateLimitExceeded" xml:space="preserve">
    <value>تم تجاوز حد معدل استخدام مفتاح API. يرجى المحاولة لاحقاً.</value>
  </data>
  <data name="ApiKeyCreatedSuccessfully" xml:space="preserve">
    <value>تم إنشاء مفتاح API بنجاح.</value>
  </data>
  
  <!-- Session Messages -->
  <data name="SessionExpired" xml:space="preserve">
    <value>انتهت صلاحية جلستك. يرجى تسجيل الدخول مرة أخرى.</value>
  </data>
  <data name="ConcurrentSessionLimitReached" xml:space="preserve">
    <value>تم الوصول للحد الأقصى من الجلسات المتزامنة. يرجى تسجيل الخروج من جهاز آخر.</value>
  </data>
  <data name="LogoutSuccessful" xml:space="preserve">
    <value>تم تسجيل الخروج بنجاح.</value>
  </data>
  
  <!-- Validation Messages -->
  <data name="RequiredField" xml:space="preserve">
    <value>هذا الحقل مطلوب.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>يرجى إدخال بريد إلكتروني صالح.</value>
  </data>
  <data name="InvalidUsernameFormat" xml:space="preserve">
    <value>يجب أن يكون اسم المستخدم بين {0} و {1} حرفاً ويحتوي فقط على أحرف وأرقام وشرطات سفلية.</value>
  </data>
  
</root>
```

#### SharedTexts.tr.resx (Turkish) - Full File

```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <!-- Schema definition same as default file -->
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms</value>
  </resheader>
  
  <!-- Authentication Messages -->
  <data name="InvalidCredentials" xml:space="preserve">
    <value>Kullanıcı adı veya şifre yanlış.</value>
  </data>
  <data name="UserBlocked" xml:space="preserve">
    <value>Hesabınız engellenmiştir. Lütfen destek ile iletişime geçin.</value>
  </data>
  <data name="TokenExpired" xml:space="preserve">
    <value>Oturumunuz sona erdi. Lütfen tekrar giriş yapın.</value>
  </data>
  <data name="InvalidRefreshToken" xml:space="preserve">
    <value>Geçersiz veya süresi dolmuş yenileme belirteci. Lütfen tekrar giriş yapın.</value>
  </data>
  <data name="UserNotFound" xml:space="preserve">
    <value>Kullanıcı bulunamadı.</value>
  </data>
  <data name="ActionNotAllowed" xml:space="preserve">
    <value>Bu işlemi gerçekleştirmek için yetkiniz yok.</value>
  </data>
  <data name="UnexpectedError" xml:space="preserve">
    <value>Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.</value>
  </data>
  
  <!-- Account Management Messages -->
  <data name="AccountLocked" xml:space="preserve">
    <value>Çok sayıda başarısız giriş denemesi nedeniyle hesabınız kilitlendi. Lütfen {0} dakika sonra tekrar deneyin.</value>
  </data>
  <data name="PasswordResetRequired" xml:space="preserve">
    <value>Devam etmeden önce şifrenizi sıfırlamalısınız.</value>
  </data>
  <data name="PasswordChangedSuccessfully" xml:space="preserve">
    <value>Şifreniz başarıyla değiştirildi.</value>
  </data>
  <data name="PasswordTooWeak" xml:space="preserve">
    <value>Şifre güvenlik gereksinimlerini karşılamıyor.</value>
  </data>
  <data name="PasswordHistoryViolation" xml:space="preserve">
    <value>Son {0} şifrenizi tekrar kullanamazsınız.</value>
  </data>
  
  <!-- Two-Factor Authentication Messages -->
  <data name="TwoFactorRequired" xml:space="preserve">
    <value>İki faktörlü kimlik doğrulama gereklidir.</value>
  </data>
  <data name="InvalidTwoFactorCode" xml:space="preserve">
    <value>Doğrulama kodu geçersiz veya süresi dolmuş.</value>
  </data>
  <data name="TwoFactorEnabledSuccessfully" xml:space="preserve">
    <value>İki faktörlü kimlik doğrulama başarıyla etkinleştirildi.</value>
  </data>
  <data name="TwoFactorDisabledSuccessfully" xml:space="preserve">
    <value>İki faktörlü kimlik doğrulama devre dışı bırakıldı.</value>
  </data>
  
  <!-- API Key Messages -->
  <data name="InvalidApiKey" xml:space="preserve">
    <value>API anahtarı geçersiz veya iptal edilmiş.</value>
  </data>
  <data name="ApiKeyExpired" xml:space="preserve">
    <value>API anahtarının süresi dolmuş. Lütfen yeni bir anahtar talep edin.</value>
  </data>
  <data name="ApiKeyRateLimitExceeded" xml:space="preserve">
    <value>API anahtarı hız limiti aşıldı. Lütfen daha sonra tekrar deneyin.</value>
  </data>
  <data name="ApiKeyCreatedSuccessfully" xml:space="preserve">
    <value>API anahtarı başarıyla oluşturuldu.</value>
  </data>
  
  <!-- Session Messages -->
  <data name="SessionExpired" xml:space="preserve">
    <value>Oturumunuz sona erdi. Lütfen tekrar giriş yapın.</value>
  </data>
  <data name="ConcurrentSessionLimitReached" xml:space="preserve">
    <value>Maksimum eşzamanlı oturum sayısına ulaşıldı. Lütfen başka bir cihazdan çıkış yapın.</value>
  </data>
  <data name="LogoutSuccessful" xml:space="preserve">
    <value>Başarıyla çıkış yaptınız.</value>
  </data>
  
  <!-- Validation Messages -->
  <data name="RequiredField" xml:space="preserve">
    <value>Bu alan zorunludur.</value>
  </data>
  <data name="InvalidEmailFormat" xml:space="preserve">
    <value>Lütfen geçerli bir e-posta adresi girin.</value>
  </data>
  <data name="InvalidUsernameFormat" xml:space="preserve">
    <value>Kullanıcı adı {0} ile {1} karakter arasında olmalı ve yalnızca harf, rakam ve alt çizgi içermelidir.</value>
  </data>
  
</root>
```

### LocalizationKeys Static Class (Full)

```csharp
namespace Auth_Localization.Services;

/// <summary>
/// Strongly-typed localization keys for IntelliSense support.
/// Use these constants with ILocalizationService for compile-time key validation.
/// </summary>
/// <example>
/// // Instead of: _localization.Get("InvalidCredentials")
/// // Use: _localization.Get(LocalizationKeys.InvalidCredentials)
/// </example>
public static class LocalizationKeys
{
    // =============================================
    // Authentication Messages
    // =============================================
    
    /// <summary>Message shown when login credentials are invalid</summary>
    public const string InvalidCredentials = nameof(InvalidCredentials);
    
    /// <summary>Message shown when a blocked user attempts to login</summary>
    public const string UserBlocked = nameof(UserBlocked);
    
    /// <summary>Message shown when JWT token has expired</summary>
    public const string TokenExpired = nameof(TokenExpired);
    
    /// <summary>Message shown when refresh token is invalid or expired</summary>
    public const string InvalidRefreshToken = nameof(InvalidRefreshToken);
    
    /// <summary>Message shown when requested user does not exist</summary>
    public const string UserNotFound = nameof(UserNotFound);
    
    /// <summary>Message shown when user lacks required permissions</summary>
    public const string ActionNotAllowed = nameof(ActionNotAllowed);
    
    /// <summary>Generic error message for unhandled exceptions</summary>
    public const string UnexpectedError = nameof(UnexpectedError);

    // =============================================
    // Account Management Messages
    // =============================================
    
    /// <summary>Message shown when account is locked. Requires {0} = minutes remaining</summary>
    public const string AccountLocked = nameof(AccountLocked);
    
    /// <summary>Message shown when password reset is required</summary>
    public const string PasswordResetRequired = nameof(PasswordResetRequired);
    
    /// <summary>Confirmation message after password change</summary>
    public const string PasswordChangedSuccessfully = nameof(PasswordChangedSuccessfully);
    
    /// <summary>Message shown when password fails complexity check</summary>
    public const string PasswordTooWeak = nameof(PasswordTooWeak);
    
    /// <summary>Message shown when password matches recent password. Requires {0} = count</summary>
    public const string PasswordHistoryViolation = nameof(PasswordHistoryViolation);

    // =============================================
    // Two-Factor Authentication Messages
    // =============================================
    
    /// <summary>Message shown when 2FA verification is needed</summary>
    public const string TwoFactorRequired = nameof(TwoFactorRequired);
    
    /// <summary>Message shown when 2FA code is incorrect</summary>
    public const string InvalidTwoFactorCode = nameof(InvalidTwoFactorCode);
    
    /// <summary>Confirmation message after enabling 2FA</summary>
    public const string TwoFactorEnabledSuccessfully = nameof(TwoFactorEnabledSuccessfully);
    
    /// <summary>Confirmation message after disabling 2FA</summary>
    public const string TwoFactorDisabledSuccessfully = nameof(TwoFactorDisabledSuccessfully);

    // =============================================
    // API Key Messages
    // =============================================
    
    /// <summary>Message shown when API key validation fails</summary>
    public const string InvalidApiKey = nameof(InvalidApiKey);
    
    /// <summary>Message shown when API key has expired</summary>
    public const string ApiKeyExpired = nameof(ApiKeyExpired);
    
    /// <summary>Message shown when API key exceeds rate limits</summary>
    public const string ApiKeyRateLimitExceeded = nameof(ApiKeyRateLimitExceeded);
    
    /// <summary>Confirmation message after creating API key</summary>
    public const string ApiKeyCreatedSuccessfully = nameof(ApiKeyCreatedSuccessfully);

    // =============================================
    // Session Messages
    // =============================================
    
    /// <summary>Message shown when user session expires</summary>
    public const string SessionExpired = nameof(SessionExpired);
    
    /// <summary>Message shown when concurrent session limit is exceeded</summary>
    public const string ConcurrentSessionLimitReached = nameof(ConcurrentSessionLimitReached);
    
    /// <summary>Confirmation message after logout</summary>
    public const string LogoutSuccessful = nameof(LogoutSuccessful);

    // =============================================
    // Validation Messages
    // =============================================
    
    /// <summary>Generic required field validation message</summary>
    public const string RequiredField = nameof(RequiredField);
    
    /// <summary>Message shown when email format is invalid</summary>
    public const string InvalidEmailFormat = nameof(InvalidEmailFormat);
    
    /// <summary>Message shown when username format is invalid. Requires {0} = min, {1} = max</summary>
    public const string InvalidUsernameFormat = nameof(InvalidUsernameFormat);
}
```

### Localization README.md

```markdown
# Auth_Localization

Centralized localization resources for the Enterprise Authentication System.

## Overview

This project provides strongly-typed, culture-aware localization for all user-facing 
messages in the authentication system. It supports multiple languages and can be 
easily extended to add new languages.

## Supported Languages

- **English (en)** - Default
- **Arabic (ar)**
- **Turkish (tr)**

## Installation

Add a project reference to `Auth_Localization` in your module:

```xml
<ItemGroup>
  <ProjectReference Include="..\Auth_Localization\Auth_Localization.csproj" />
</ItemGroup>
```

## Usage

### 1. Register Services (Program.cs)

```csharp
using Auth_Localization.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Auth_Localization services (English default)
builder.Services.AddAuthLocalization();

// OR with custom default culture
builder.Services.AddAuthLocalization("ar"); // Arabic as default

var app = builder.Build();

// Use localization middleware
app.UseAuthLocalization();

app.Run();
```

### 2. Inject and Use ILocalizationService

```csharp
using Auth_Localization.Services;

public class AuthController : ControllerBase
{
    private readonly ILocalizationService _localization;

    public AuthController(ILocalizationService localization)
    {
        _localization = localization;
    }

    [HttpPost("login")]
    public IActionResult Login(LoginRequest request)
    {
        // Using strongly-typed keys (recommended)
        var errorMessage = _localization.Get(LocalizationKeys.InvalidCredentials);
        
        // With format arguments
        var lockMessage = _localization.Get(LocalizationKeys.AccountLocked, 15);
        // Returns: "Your account has been locked... try again in 15 minutes."
        
        return BadRequest(new { message = errorMessage });
    }
}
```

### 3. Culture Detection

The localization middleware automatically detects culture from:

1. **Accept-Language header** (browser preference)
2. **Query string** (`?culture=ar`)
3. **Cookie** (for persistent preference)

### 4. Adding New Languages

1. Copy `SharedTexts.resx` to `SharedTexts.{code}.resx`
2. Translate all values
3. Add culture to `SupportedCultures` in `LocalizationExtensions.cs`

## API Reference

### ILocalizationService

| Method | Description |
|--------|-------------|
| `Get(key)` | Get localized string by key |
| `Get(key, args)` | Get formatted string with arguments |
| `Get(key, culture)` | Get string for specific culture |
| `GetSupportedCultures()` | Get all supported cultures |

## Best Practices

1. Always use `LocalizationKeys` constants instead of string literals
2. Include comments in .resx files for translators
3. Use format placeholders `{0}`, `{1}` for dynamic values
4. Test all supported languages before deployment
```

