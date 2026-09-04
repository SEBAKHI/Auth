# AuthSystem

## Enterprise Identity Platform — Built for Multi-App, Multi-Tenant Organizations

---

## Executive Summary

**AuthSystem** is a comprehensive identity management platform built for real-world enterprise needs. It was designed from the ground up to address the limitations of traditional identity management systems — particularly Microsoft's default ASP.NET Identity, which offers a one-size-fits-all approach that quickly falls short in complex, multi-application environments.

AuthSystem provides a **flexible**, **secure**, and **highly customizable** solution tailored to the specific needs of growing organizations.

> **For a one-page overview**, see [01_AUTH_SYSTEM_EXECUTIVE_SUMMARY_EN.md](01_AUTH_SYSTEM_EXECUTIVE_SUMMARY_EN.md)
> **For technical architecture details**, see [03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md](03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md)

### What You Are Actually Getting

The product is three deployable things, not one. Two of them are web applications a human being opens in a browser, and this document describes all three.

| Piece | What it is | Who touches it |
|-------|------------|----------------|
| **Auth API** | A .NET 10 web service holding every identity rule, backed by SQL Server | Other applications, over HTTP |
| **API Gateway** | A separate .NET 10 process that fronts the API, applies edge rate limits, and stamps a shared secret on every forwarded request | Nobody directly; it is the public door |
| **Admin console** | A React web application for the people who run the platform | Your administrators |
| **Accounts application** | A React web application where your end users sign in and manage their own account | Your users |

The console and the accounts application are described in full in [Section 2](#2-the-two-web-applications). No part of this system requires you to build a user interface yourself.

### The Identity Challenge Your Organization Faces

| Challenge | Traditional Systems | AuthSystem |
|-----------|---------------------|------------|
| **Password Security** | PBKDF2 or bcrypt — both are strong, but neither is memory-hard, so a graphics card can try many candidates cheaply | **Argon2id** — memory-hard, so each guess costs the attacker real memory as well as time |
| **Permission Management** | Flat roles with limited flexibility | **Hierarchical permission codes** with prefix wildcards (for example `users:*` covers `users:read` and `users:create`) |
| **Multi-Application Support** | One application per identity system | **Multiple applications** under one authentication umbrella |
| **Audit Trail** | Limited (requires manual implementation) | An audit table written automatically by event handlers, recording who did what, to which record, when, and from where |
| **Customization** | Limited without heavy modification | **Built for customization** from day one |

> *Argon2id: A memory-hard password hashing algorithm that won the international Password Hashing Competition in 2015, judged by a panel of leading cryptography experts. It is the current gold standard for secure password storage, recommended by OWASP — the Open Worldwide Application Security Project, the leading non-profit in application security.*

### The Size of What You Are Adopting

Rather than quote industry breach statistics this repository cannot source, here is what the codebase actually contains. Every figure below was counted from the source tree at the commit named at the end of this document. None of them is an estimate.

| What | Count |
|------|-------|
| HTTP endpoints (controller actions) | **199**, across 25 routable controllers (26 controller files, one of which is a shared base class carrying no endpoint of its own) |
| Database tables | **52** |
| Application feature areas | **17** |
| Request handlers | **190** — 120 that change data, 70 that read it |
| Domain events raised inside the process | **34** |
| Routes published by the gateway | **24** |
| Display languages, front end and back end alike | **7** |
| Backend test cases declared in source | **1,412** `[Fact]` and **68** `[Theory]` across 171 files |
| Front-end unit test cases | **276** across 40 files |
| End-to-end browser test files | **5** |

**This document publishes no effort estimate, no benchmark, and no test-coverage percentage.** Nothing in the repository measures any of them. For an evaluator, an unsupported number is worse than no number at all.

### The Bottom Line

AuthSystem is a complete identity platform — an API, a gateway, an administrator console, and an end-user accounts application — that grows with your organization while holding a consistent security standard. Section 15 lists, plainly, everything it does **not** include.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [The Two Web Applications](#2-the-two-web-applications)
3. [Security Architecture](#3-security-architecture)
4. [Performance: The Design Choices, and What Has Not Been Measured](#4-performance-the-design-choices-and-what-has-not-been-measured)
5. [User Management](#5-user-management)
6. [Single Sign-On and the Authorization-Code Flow](#6-single-sign-on-and-the-authorization-code-flow)
7. [Multi-Application and Organization Support](#7-multi-application-and-organization-support)
8. [Roles and Permissions](#8-roles-and-permissions)
9. [Email and Notifications](#9-email-and-notifications)
10. [Multilingual Support](#10-multilingual-support)
11. [Audit Logging and Compliance](#11-audit-logging-and-compliance)
12. [Privacy, Consent and Account Deletion](#12-privacy-consent-and-account-deletion)
13. [Configuration and Operations](#13-configuration-and-operations)
14. [Scalability and Maintainability](#14-scalability-and-maintainability)
15. [What This System Does Not Include](#15-what-this-system-does-not-include)
16. [How It Is Tested](#16-how-it-is-tested)
17. [Comparison with ASP.NET Core Identity](#17-comparison-with-aspnet-core-identity)
18. [Roadmap](#18-roadmap)
19. [Conclusion](#19-conclusion)

---

## 1. System Overview

### What is AuthSystem?

AuthSystem is a **centralized authentication and authorization platform** that manages:

- **Who users are** (Authentication)
- **What users can do** (Authorization)
- **What users did** (Audit Logging)

Think of it like a **smart building security system**:
- **Authentication** = Your ID badge that proves you work here
- **Authorization** = Which doors your badge can open
- **Audit Logging** = The security camera recording who went where

### Key Features at a Glance

Each line below is a shipped capability with its own section later in this document.

- **Password storage using Argon2id**, the memory-hard algorithm recommended by OWASP (Section 3)
- **Token authentication using JWTs signed with RS256** — a JWT (JSON Web Token) is a signed statement about who the caller is; RS256 means a private key signs it and a public key checks it (Section 3)
- **Two-factor authentication** using TOTP (Time-based One-Time Password) codes from any authenticator app, plus 10 single-use recovery codes (Section 3)
- **Sign-in with Google**, on by default; **sign-in with Apple**, shipped but off until you supply Apple's credentials (Section 6)
- **Single sign-on for your own applications** through the OAuth 2.0 authorization-code flow with mandatory PKCE, plus token introspection, token revocation, and a public discovery document (Section 6)
- **A hierarchical permission system** with prefix wildcards, and permissions that can be scoped to one organization (Section 8)
- **Organizations (multi-tenancy)** with email invitations, per-member roles, per-organization application subscriptions, and ownership transfer confirmed by an emailed code (Section 7)
- **An administrator console and an end-user accounts application**, both shipped, both in 7 languages including three that read right-to-left (Section 2)
- **A database-backed email and notification system** — content, translations, and layout are edited in the console and published without a redeploy, and every send is queued and retried (Section 9)
- **Versioned, translated privacy policies** with an explicit publish step, a permanent public address per revision, and a "notify every user" action (Section 12)
- **Self-service and administrative account deletion**, with a 30-day recoverable window before anything is destroyed (Section 12)
- **Database-backed system settings** — an administrator changes password policy, token lifetimes, rate limits and more from the console, without editing a file on the server (Section 13)
- **An encrypted secrets vault** for signing keys, the gateway token, the SMTP password and the database connection string, where every destructive operation requires a code emailed to the administrator first (Section 13)
- **Image upload and platform branding** — profile pictures, organization and application logos, and the logo that appears inside outgoing email (Section 13)
- **A dashboard** with six live statistics panels (Section 13)
- **API keys and webhook signing keys** for machine-to-machine callers (Section 13)
- **An audit log** recording who did what, to which record, when, and from which address (Section 11)
- **An API gateway** — YARP (Yet Another Reverse Proxy), Microsoft's reverse-proxy library — carrying 24 routes and the edge rate limits (Section 3)
- **Security response headers** applied to every response by shared middleware (Section 3)

### Technology Stack

Backend versions are exact, taken from the project files. Front-end versions are the ranges the workspace declares.

| Component | Technology | Purpose |
|-----------|------------|---------|
| Backend runtime | .NET 10 (`net10.0`) | Every backend project targets it |
| Database | SQL Server, schema built as a SQL Server 2019 database project | The only datastore in the system |
| Data access | **Dapper 2.1.79 over hand-written SQL** | The only object-relational mapper present |
| Admin console | **React 19 + Vite 8 + TypeScript, styled with Tailwind CSS 4 and shadcn/ui** | The interface your administrators use |
| Accounts application | The same React 19 + Vite 8 stack | The interface your end users use |
| API gateway | YARP 2.3.0 | Reverse proxy, edge rate limiting, shared-secret stamping |
| Request dispatch | MediatR 14.2.0 | Routes each request to exactly one handler |
| Result type | ErrorOr 2.1.1 | Every handler returns either a value or a typed error, instead of throwing |
| Validation | FluentValidation 12.1.1 | Rejects malformed requests before a handler runs |
| Password hashing | `Konscious.Security.Cryptography.Argon2` 1.3.1 | Argon2id hashing |
| Token signing | `System.IdentityModel.Tokens.Jwt` 8.22.0, RS256 with a 2048-bit RSA key | Issues and validates access tokens |
| Two-factor codes | `Otp.NET` 1.4.1 | Generates and checks TOTP codes |
| Google sign-in | `Google.Apis.Auth` 1.75.0 | Validates Google identity tokens |
| Email delivery | MailKit 4.17.0 | Sends over SMTP (Simple Mail Transfer Protocol) |
| Email templating | `Fluid.Core` 2.31.0 | Renders the Liquid templates stored in the database |
| Image processing | SkiaSharp 4.151.0 | Resizes and re-encodes uploaded images |
| Logging | Serilog 10.0.0 | Structured logging to console and, for the API, a rolling file |
| API documentation | .NET 10's built-in OpenAPI support | Produces a machine-readable description of the API in Development only |

> *Dapper: A lightweight "micro-ORM" (object-relational mapper) for .NET that executes SQL you wrote yourself and maps the rows onto objects. It is deliberately thinner than a full ORM such as Entity Framework, which generates SQL for you. Entity Framework is named here only as the comparison; it is not a dependency of this system.*
>
> *Serilog: A structured logging library for .NET that writes logs as queryable data rather than plain sentences, so an operator can filter by user id or request id instead of grepping text.*

### What You Actually Deploy

Sizing a deployment means knowing how many moving parts there are. There are five.

1. **The Auth API** — one .NET 10 process. In development it listens on `http://localhost:5100` and `https://localhost:5101`.
2. **The API Gateway** — a second, separate .NET 10 process. In development it listens on `http://localhost:5034` and `https://localhost:7159`. It is the process the outside world is meant to reach; the API is not meant to be publicly addressable on its own.
3. **The admin console** — a folder of static files produced by a build, served by a web server. In development it runs at `https://localhost:5173`.
4. **The accounts application** — a second folder of static files. In development it runs at `https://localhost:5174`.
5. **One SQL Server database.**

The gateway stamps a shared secret on every request it forwards, in a header named `X-Gateway-Token`, and the API rejects any request that arrives without the right value with HTTP 403. A short list of paths is exempt, including `/health`, `/ready` and `/.well-known/`. Adding a new controller prefix to the API therefore also means adding a gateway route — an automated test in the backend test project fails if you forget.

---

## 2. The Two Web Applications

This is the part of the product a person sees. There are two separate web applications, both single-page applications — an SPA (single-page application) is a website that loads once and then updates the screen in place instead of reloading a new page each time you click. Both are built with React 19 and Vite 8 and live in one workspace folder called `Auth_UI`.

They exist as two applications, not one, because the two audiences are different. An administrator needs user records, roles, audit history and server settings. An end user needs their own profile and nothing else. Splitting them means an end user never loads, and never sees a hint of, the administrative surface.

| | Admin console | Accounts application |
|---|---|---|
| Who it is for | The people running the platform | Your end users, managing their own account |
| Folder | `Auth_UI/apps/console` | `Auth_UI/apps/accounts` |
| Browser tab title | `Auth Console` | `Accounts` |
| Development address | `https://localhost:5173` | `https://localhost:5174` |
| Talks to | The Auth API, `https://localhost:5101` by default | The same API |
| Navigation | Twelve sidebar entries. Ten of them are hidden unless the signed-in administrator holds the permission that entry needs; Dashboard and Organizations carry no permission and are always shown | Two entries: Profile and Organizations |

Both applications are meant to run over HTTPS even in local development, and both refuse to start on a different port if theirs is taken. The HTTPS part is deliberate: the browser treats `http://localhost` and `https://localhost` as different sites, and the sign-in cookie minted over HTTPS would be silently discarded on the HTTP version, leaving a sign-in loop with no error message. The development certificate is supplied through two environment variables, `DEV_HTTPS_CERT` and `DEV_HTTPS_KEY`. If those two variables are not set, the development server does **not** refuse to start — it prints a warning and serves plain HTTP instead, which is exactly the sign-in loop just described. Set them before you first sign in.

### What an Administrator Does in the Console

Every screen below is a real route in the shipped application. The permission column is the exact code the route requires; an administrator without it never sees the sidebar entry, and typing the address directly lands on a "403 Forbidden" page.

| Screen | Address | What it is for | Permission required |
|--------|---------|----------------|---------------------|
| Dashboard | `/` | Six statistics panels covering users, sign-ins, audit activity, sessions, per-application activity and credentials about to expire | None beyond being signed in; each panel is hidden if you cannot read its data |
| Users | `/users`, `/users/:id` | Create, edit, lock, unlock, activate, deactivate, assign roles and permissions, view a user's sign-in history | `users:read` to open |
| Roles | `/roles`, `/roles/:id` | Define roles and bundle permissions into them, per application | `roles:read` |
| Permissions | `/permissions`, `/permissions/:id` | The catalogue of individual permission codes and the "this one implies that one" links between them | `permissions:read` |
| Applications | `/applications`, `/applications/:id` | Register the applications that sign in through this platform, set their redirect addresses and their access mode | `applications:read` |
| Organizations | `/organizations`, `/organizations/:id` | Organizations, their members, invitations and application subscriptions | See the warning below |
| API keys | `/api-keys` | Issue, rotate and revoke machine credentials | `apikeys:read` |
| Webhook keys | `/webhook-keys` | Manage the signing keys a receiving system uses to verify a callback | `webhookkeys:read` |
| Audit logs | `/audit-logs` | Review security and administrative activity, and export it | `auditlogs:read` |
| Notifications | `/notifications` and four sub-screens: `/notifications/templates`, `/notifications/layouts`, `/notifications/outbox`, `/notifications/policy` | Edit and publish the content of every email the platform sends, in seven languages; inspect the delivery log; manage privacy-policy revisions | `notification-templates:read` |
| Platform settings | `/admin/platform-settings` | The platform's own name, light and dark logos, and favicon | `platform-settings:manage` |
| System settings | `/admin/system-settings`, and one screen per section | Change server behaviour — password policy, token lifetimes, session rules, rate limits, email settings — without touching a file on the server | `system-settings:manage` |
| Secret keys | `/admin/system-settings/SecretManagement/keys` | Generate or import the signing keys, the gateway token, the SMTP password and the database connection string | `secrets.manage` — and this screen is deliberately not in the sidebar |
| My profile | `/profile` | The administrator's own account, sessions and security settings | None beyond being signed in |

> **Known defect, stated plainly.** The `/organizations` route in the console has **no permission guard**. Every other administrative route is wrapped in one. Any signed-in user who types that address reaches the page; what they can then *do* is still checked by the API on every call, but the page itself opens. This is a real gap in the front end, not a design choice.

### What an End User Does in the Accounts Application

| Screen | Address | What it is for |
|--------|---------|----------------|
| Sign in | `/login` | Email and password, plus Google and Apple buttons when those providers are switched on |
| Register | `/register` | Create an account; a verification code is emailed immediately |
| Forgot password | `/forgot-password` | Request a reset link |
| Reset password | `/reset-password` | Set a new password from the emailed link |
| Two-factor verification | `/two-factor` | Enter the six-digit code, or a recovery code, when two-factor authentication is switched on |
| Verify email | `/verify-email` | Enter the emailed code. On this anonymous path a correct code also signs the user in |
| Forced password change | `/force-password-change` | Shown when the account is flagged as needing a new password before it can go anywhere else |
| My profile | `/profile` | Three tabs — Account (name, image, language, timezone), Sessions (every active session and browser, each individually revocable, plus sign-in history), Security (password, two-factor enrolment with a scannable code, recovery codes) — and a Danger Zone offering account deletion |
| My organizations | `/organizations`, `/organizations/:id` | The organizations this user belongs to, their members, and, for an owner, ownership transfer |
| Accept an invitation | `/accept-invitation` | Opened from an invitation email. Works whether or not the recipient already has an account |
| Delete my account | `/delete-account` | A public wizard that deletes an account **without signing in**, confirmed by an emailed code. It is deliberately not linked from the sign-in page |
| Deletion scheduled | `/deletion-scheduled` | Confirms the date the account becomes unrecoverable |
| Account recovery | `/account-recovery` | Cancels a scheduled deletion during the grace window |

The published privacy policy is served at `/privacy` as complete HTML by the Auth API itself, not by the React application, so it is readable even if the application fails to load. A notice appears inside the accounts application when the published policy version changes.

Both applications share five internal packages: a typed API client generated from the API's own OpenAPI description, a session and route-guard package, the seven-language translation package, the shared component library, and a package of screens that both applications mount — the profile and organization pages are literally the same code in both.

---

## 3. Security Architecture

### Password Storage: Argon2id

**Argon2id** is the **only approved password hashing algorithm** in AuthSystem. It won the international Password Hashing Competition in 2015, reviewed by leading cryptography experts worldwide.

#### Why Argon2id?

| Algorithm | Security Level | Status | Key Limitation |
|-----------|---------------|--------|----------------|
| MD5 | Broken | **Never use** | Crackable in seconds |
| SHA-256 | Not designed for passwords | **Not approved** | Too fast — no brute-force resistance |
| bcrypt | Secure but surpassed | **Not approved** | CPU-only — does not resist GPU attacks as effectively |
| PBKDF2 | Secure but surpassed | **Not approved** | Lacks memory-hardness; weaker against modern hardware |
| **Argon2id** | Current gold standard | **Approved** | No practical attacks known when properly configured — uses both CPU and memory |

> *Note: bcrypt and PBKDF2 remain secure for many use cases and are still recommended by OWASP as acceptable alternatives. However, Argon2id provides superior resistance against modern GPU-based and ASIC-based attacks due to its memory-hard design.*

#### How It Works (Simplified)

```text
User's Password: "MySecurePassword123!"
        |
   [Argon2id Algorithm]
   Memory: 19 MiB (makes brute-force expensive)
   Iterations: 2 passes (OWASP 2024 recommended)
   Salt: 16 random bytes (unique per password)
        |
Stored Hash: $argon2id$v=19$m=19456,t=2,p=1$[salt]$[hash]
```

**Why does memory matter?** Unlike simple algorithms that can run millions of times per second on GPUs, Argon2id requires 19 MiB of memory per attempt. An attacker trying to crack millions of passwords in parallel would need terabytes of RAM, making the attack economically impractical.

### Defense-in-Depth: Pepper & Breached-Password Screening

Beyond Argon2id, two optional layers can be enabled per environment:

- **Pepper** — a server-side secret mixed into *every* password hash and stored separately from the database (in the secret store, never in SQL). If the database alone is ever breached, the stolen hashes cannot be brute-forced without the pepper. Existing hashes are upgraded transparently on each user's next login.
- **Breached-Password Screening** — passwords are checked against the *Have I Been Pwned* Pwned Passwords dataset using k-anonymity (only the first 5 characters of the SHA-1 hash ever leave the server; the password never does). Breached passwords can be rejected (`Enforce`) or flagged with a warning (`Warn`).

Both are disabled by default and add no external dependency until enabled.

### Token-Based Authentication

Instead of checking credentials with every request (like showing ID at every door), we use **tokens** — like a concert wristband that proves you paid at the entrance.

#### JWT (JSON Web Token) Structure

| Component | Contains | Lifetime |
|-----------|----------|----------|
| **Access token** | A signed statement of the user's identifier, email, name, session identifier, preferred language, time zone and theme, every role code, every permission code, and every organization-scoped permission | **15 minutes** |
| **Refresh token** | Nothing readable — it is 64 random bytes. Only a keyed hash of it is stored server-side, so a database leak does not yield usable tokens | **7 days** |

A refresh token is rotated every time it is used: the old one is marked as replaced. Presenting an already-replaced token is treated as theft — every token that user holds is revoked at once and they are emailed about it.

#### Why RS256 Asymmetric Encryption?

```text
+----------------------------------------------------------+
|                 SYMMETRIC (HS256)                         |
|  Same key to sign AND verify = Security risk             |
|  If server B needs to verify, it needs the secret key    |
+----------------------------------------------------------+

+----------------------------------------------------------+
|                ASYMMETRIC (RS256)                         |
|  Private key: Signs tokens (kept secret)                 |
|  Public key: Verifies tokens (can be shared safely)      |
|  Other services can verify without knowing the secret    |
+----------------------------------------------------------+
```

### Rate Limiting

Rate limiting happens in **two separate processes with two separate sets of numbers**, and they behave differently when a caller goes over. Treating them as one thing is the most common mistake made about this system, so both are given in full.

**In the API process**, there are exactly two named policies and **no default bucket at all**. An endpoint that is not on the list below is not rate-limited by the API. That is deliberate, not an oversight — the edge is where general throttling belongs.

| Policy | Limit | Window | Applies to |
|--------|-------|--------|------------|
| `login` | **20 requests** | 60 seconds, per client address | Sign-in, register, external sign-in, token exchange, forgot password, the three email-verification endpoints, two-factor verification, the four account-deletion endpoints, the two self-service deletion endpoints, invitation preview and invitation registration, and the two secrets-challenge endpoints |
| `password-reset` | **10 requests** | 60 seconds, per client address | The password-reset redemption endpoint |

**At the gateway**, there is a global limit plus three named policies. These are the numbers that protect the platform as a whole.

| Policy | Limit | Window | Queue | Applies to |
|--------|-------|--------|-------|------------|
| Global | **1,000 requests** | 60 seconds | 100 | Everything passing through the gateway |
| `auth` | **20 requests** | 60 seconds | 0 | The `/auth/` routes |
| `api` | **100 requests** | 60 seconds | 10 | The 19 general management routes |
| `admin` | **120 requests** | 60 seconds | 0 | The `/admin/` routes |

**When a limit is exceeded, the two processes do not answer the same way.** Both return HTTP 429 (Too Many Requests), but:

- The **gateway** returns a standards-shaped problem document with `type`, `title`, `status`, `detail` and `retryAfter` in whole seconds, **and** sets the `Retry-After` response header.
- The **API** returns a smaller body of just `{ "error": ..., "retryAfter": ... }`, where `retryAfter` is a fractional number of seconds, and sets **no** `Retry-After` header.

A client that talks to the API directly must therefore read `retryAfter` from the body and must not rely on the header. A client that goes through the gateway — which is how the system is meant to be reached — gets the header.

The gateway partitions its limits by the network address it actually sees. The API partitions by the first entry in the `X-Forwarded-For` header, falling back to the connection address. That is the correct choice for a process sitting behind a proxy, and it depends on the API not being directly reachable from the internet.

### Security Headers

Shared middleware, used by both the API and the gateway, sets the following on every response.

| Header | Value | Protects against |
|--------|-------|------------------|
| `X-Frame-Options` | `DENY` | Clickjacking |
| `X-Content-Type-Options` | `nosniff` | MIME sniffing |
| `X-XSS-Protection` | `1; mode=block` | Legacy cross-site scripting filters |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Leaking the full page address to third parties |
| `Content-Security-Policy` | `default-src 'self'; frame-ancestors 'none'` | Script injection and framing |
| `Permissions-Policy` | `geolocation=(), microphone=(), camera=()` | Silent access to device hardware |
| `Server`, `X-Powered-By` | **removed** | Advertising the runtime and its version to an attacker |

Two details matter. The Content-Security-Policy above is a **baseline that is applied only when the endpoint did not already set one** — the published privacy-policy pages, for instance, set their own far stricter policy. And HSTS (HTTP Strict Transport Security, the header that tells a browser never to speak plain HTTP to this host again) is **registered only outside Development**, with the value `max-age=31536000; includeSubDomains; preload`. In local development it is absent by design.

> *Clickjacking: An attack where a hidden page element tricks users into clicking something different from what they see. You think you are clicking "Play Video" but you are actually clicking "Transfer Money".*
>
> *MIME sniffing: A browser behaviour where it guesses a file's type instead of trusting what the server said. Attackers exploit it to have a malicious file executed as script.*
>
> *Cross-site scripting: Malicious script injected into a trusted site, able to steal a signed-in session or personal data.*

### Vulnerabilities Mitigated

| Vulnerability | How AuthSystem addresses it |
|---------------|-----------------------------|
| **SQL injection** | Every query is parameterised through Dapper — user input is never concatenated into SQL |
| **Cross-site scripting** | A Content-Security-Policy header, plus HTML-encoding of every variable rendered into an email body |
| **Cross-site request forgery** | The API authenticates with bearer tokens, not cookies, so classic request forgery does not apply to it. The one cookie the system sets is the identity-provider session cookie used by the sign-on flow: `HttpOnly`, `Secure`, `SameSite=Lax`, host-only, never scoped to a parent domain. **There is no antiforgery-token mechanism in this system** — do not expect one, and do not describe one to a security reviewer |
| **Brute force** | Two independent layers, described immediately below |
| **Session hijacking** | Refresh tokens are rotated on every use, and presenting an already-rotated token revokes every token the user holds and emails them about it |
| **Downgrade to plain HTTP** | HTTPS redirection always, and HSTS outside Development |

**Brute force gets two layers because one is not enough.** Per-address throttling stops one machine spraying many accounts: 20 requests per 60 seconds on the interactive sign-in endpoints. Per-account lockout stops many machines grinding down one account: **5 failed passwords locks that account for 15 minutes** — for strangers. A client address or a device the account has recently signed in from may still sign in and, on success, clears the lock; every single address is also refused after its own five wrong passwords. An administrator's lock is never relaxed this way. Neither number is the other; the older documentation conflated them.

### Account Protection

These are the shipped defaults. Every one of them is editable from the console's System settings screen, within the bounds shown in that screen.

| Protection | Default |
|------------|---------|
| Failed sign-ins before the account locks | **5** |
| Lockout duration | **15 minutes** |
| Password history (how many old passwords cannot be reused) | **3**, plus the current one |
| Minimum password length | **8 characters** |
| Password must contain an uppercase letter, a lowercase letter, a digit, and a special character | **All four required** |
| Common-password blocklist | Built in — passwords containing `password`, `123456`, `qwerty`, `admin` and similar strings are rejected |
| Access-token lifetime | **15 minutes** |
| Session lifetime | **7 days** — a session lives exactly as long as its refresh token |
| Idle timeout | **None.** A configuration key called `Session:IdleTimeoutMinutes` exists, but nothing in the code reads it. Do not plan around it |
| Maximum concurrent sessions per user | **Off by default** (`Session:MaxConcurrentSessions` ships as `0`, meaning unlimited). Set it above zero to enable |
| What happens at the session cap | With the shipped setting, reaching the cap ends the **oldest** session and lets the new sign-in through. Change `Session:TerminateOldestOnMax` to `false` to refuse the new sign-in instead |
| Two-factor authentication | TOTP from any authenticator app, plus **10 single-use recovery codes** issued once at enrolment. The shared secret is encrypted at rest under a key unique to that user |
| Server-side pepper mixed into every password hash | **Off by default.** When switched on, existing hashes are upgraded silently at each user's next sign-in |
| Breached-password screening against Have I Been Pwned | **Off by default.** When switched on, only the first five characters of the password's hash ever leave the server |

One field deserves a warning. Each registered application carries its own `MaxConcurrentSessions` value. It is stored, validated, returned by the API and sortable in the console — and **it is enforced by nothing**. Only the global setting is applied. The same is true of that application's `SessionTimeoutMinutes` and `RequireEmailVerification` fields.

`AllowSelfRegistration` was in that list and no longer pretends otherwise. It could never have worked as written: sign-up carries no application identity, and giving it one would not help, because the caller would be the party naming the application. **Whether strangers may create accounts is a property of the server**, and it now has a switch that works — System settings → *Who may create an account*. Two switches, in fact, because there are two doors: the public sign-up endpoint, and the first sign-in of a Google or Apple identity that matches no account here, which creates one. Both ship open, which is what every deployment had before they existed. Closing only the first leaves the second wide open. The application field remains on the API contract so integrations do not break, and the console no longer offers it as a control.

---

## 4. Performance: The Design Choices, and What Has Not Been Measured

**This system has no published benchmarks.** There is no load-test script, no benchmark project, and no recorded latency or throughput figure anywhere in the repository. What follows is why it is built the way it is — not how fast it runs. If throughput matters to your decision, measure it yourself against your own hardware and data volume.

### 1. Dapper: Direct SQL Execution

A full object-relational mapper writes your SQL for you. Dapper does not — you write the query, and Dapper only maps the resulting rows onto objects. The trade is control for convenience.

| Aspect | Full ORM (for example, Entity Framework) | Dapper (micro-ORM) |
|--------|------------------------------------------|--------------------|
| Who writes the SQL | The library | You do |
| Query shape | Generated; can be inefficient in ways that are hard to see | Exactly what you wrote |
| Memory | Higher — the library tracks every loaded object for changes | Lower — nothing is tracked |
| Debugging | You reason about the library's translation layer | You read the query |

Entity Framework appears here as the comparison only. It is not a dependency of this system.

### 2. Asynchronous Database Calls

Every database call is asynchronous and accepts a cancellation token, so a thread waiting on the database is released to serve another request instead of sitting idle, and an abandoned request stops doing work.

### 3. Connection Pooling

Database connections are reused from a pool rather than opened fresh for each request, which removes the connection handshake from the cost of every call.

### 4. In-Process Caching

Five things are cached inside the process. All five are per-process — nothing is shared between instances, which matters and is covered in Section 14.

| What | How it is cached |
|------|------------------|
| The revoked-token list | Held in memory and consulted on every authenticated request. Revocations are also written to a database table and reloaded from it when the process starts |
| Notification templates | Cached for up to 15 minutes, including "there is no such template", and evicted immediately when an administrator publishes a change |
| Parsed email templates | Cached by their source text, up to 1,024 entries |
| Published privacy-policy documents | Held with no expiry at all and replaced only when a new revision is published |
| Per-user encryption keys | Cached for 15 minutes after each use, then re-derived |

Configuration is **not** simply loaded at startup. File settings are the baseline; database overrides are layered on top of them and re-read live. Most settings take effect without a restart, and the ones that cannot are explicitly labelled "restart required" in the console. This is covered in Section 13.

---

## 5. User Management

### User Lifecycle

A user account moves through the states below. What matters to an evaluator is the right-hand side: there are **four different ways an account ends**, and they are not interchangeable.

```text
+--------------+    +--------------+    +--------------+
|   Created    | -> |   Active     | -> |  Inactive    |
| (pending     |    | (full        |    | (deactivated |
|  verification)|   |  access)     |    |  by an admin)|
+--------------+    +--------------+    +--------------+
                          |
                          v
                   +--------------+
                   |   Locked     |
                   | (too many    |
                   |  failed      |
                   |  passwords)  |
                   +--------------+

Four terminal paths:
  1. Soft delete by an administrator   -> row kept, hidden from lists
  2. Permanent purge by an administrator -> row destroyed
  3. Administrative deletion             -> 30-day recoverable window, then destroyed
  4. Self-service deletion by the user   -> 30-day recoverable window, then destroyed
```

**Soft delete** marks the row as deleted and keeps it. A deleted user becomes visible again only by asking for them explicitly, and only if you hold the `users:manage` permission. Be clear-eyed about what this does and does not give you: **there is no restore endpoint.** Undoing a soft delete is a database operation performed by whoever administers the database, not a button in the console.

**Permanent purge** is a separate endpoint that destroys an already-soft-deleted row outright and releases the email address for reuse.

**Administrative and self-service deletion** are the full data-protection pipeline, with a 30-day recoverable window and staged destruction afterwards. That pipeline is described in Section 12.

### What a User Can Manage About Themselves

Everything in this table is reachable by the user themselves, in the accounts application, with no administrator involved.

| Feature | Detail |
|---------|--------|
| **Email address** | The unique identifier for the account |
| **Name and profile image** | Uploaded through the shared image pipeline described in Section 13 |
| **Phone number** | Stored encrypted at rest under a key unique to that user |
| **Preferred language** | One of the seven; it also decides the language of the emails they receive |
| **Time zone** | Used everywhere a date is shown. "UTC" means "follow my browser"; a user who genuinely wants UTC picks `Etc/UTC` |
| **Theme** | Light, dark, or follow the operating system |
| **Password** | Change, or reset by emailed link |
| **Two-factor authentication** | Enrol by scanning a code, receive 10 recovery codes, or disable |
| **Active sessions** | Every session listed and individually revocable, grouped by browser |
| **Known devices** | Every device that has signed in, individually removable, with an optional email alert the first time a new one appears |
| **Sign-in history** | Recent attempts, successful and failed |
| **Linked external accounts** | Which Google or Apple identity is attached |
| **Display preferences** | Per-user interface settings such as table column choices, stored server-side so they follow the user between browsers |
| **Account deletion** | Self-service, described in Section 12 |

### Designed for Scale

| Choice | What it gives you |
|--------|-------------------|
| Indexed queries | Lookups stay fast as the table grows |
| Paged list endpoints | Every list endpoint takes a page number and a page size, capped at 100 rows per page |
| Allow-listed sorting | Every list endpoint accepts a sort field and direction, but only from a fixed list — an unrecognised value is rejected with a 400 rather than passed into SQL |
| Soft delete | The row survives an accidental delete, though restoring it is a database operation |

There is **no bulk or batch endpoint** anywhere in this system. Every create, update and delete acts on one record. If you need to move ten thousand users in, you make ten thousand calls or you write to the database directly.

---

## 6. Single Sign-On and the Authorization-Code Flow

This is how your own applications let a person sign in through AuthSystem. It shipped, and it is the mechanism the accounts application itself uses.

### What Happens, In Order

Your application never sees the user's password. It hands the browser off, and gets back a token.

1. Your application sends the browser to `GET /api/v1/auth/authorize`, carrying its `client_id`, the exact `redirect_uri` you registered, `response_type=code`, a `code_challenge`, `code_challenge_method=S256`, and a `state` value of your own choosing (up to 512 characters).
2. If the browser has no valid identity-provider session, AuthSystem redirects it to the accounts application's sign-in page, remembering where to return.
3. The user signs in — with a password, with Google, with Apple — and clears two-factor verification if it is enabled.
4. AuthSystem checks that this user is entitled to this application, then redirects back to your `redirect_uri` with a one-time `code` and your `state`.
5. Your application posts that code to `POST /api/v1/auth/token` together with the `code_verifier` matching the challenge from step 1.
6. You receive an access token and a refresh token. The access token's audience is your application's code, not a shared platform value.

**PKCE (Proof Key for Code Exchange) is mandatory, not optional.** PKCE is the mechanism that stops a stolen authorization code from being useful: your application invents a random secret, sends only its hash up front, and must produce the original secret to redeem the code. Only the `S256` hashing method is accepted, and the secret must be 43 to 128 characters from the URL-safe alphabet. There is no client secret and no client authentication — every client is treated as public, which is exactly why PKCE is required.

### The Rules the Server Enforces

| Rule | Value |
|------|-------|
| Authorization code lifetime | **60 seconds** |
| Code reuse | The code is consumed atomically. A second attempt is refused and logged as a replay |
| `redirect_uri` at redemption | Must match the one bound to the code exactly, character for character |
| `redirect_uri` registration | Must already be registered against that application, or the request is refused with a 400 and no redirect |
| `response_type` | `code` only |
| Grant types at the token endpoint | `authorization_code` and `refresh_token`; anything else is refused |
| Re-authentication | An application can require the user to prove themselves again if their session is older than a configured age, and `prompt=login` always forces it |
| Entitlement | Checked when the code is issued **and** again when it is redeemed |

### The Discovery Document

Other software can read three unauthenticated endpoints to verify your tokens without any coordination with you.

| Address | Contents |
|---------|----------|
| `/.well-known/openid-configuration` | The machine-readable description of this identity provider |
| `/.well-known/jwks.json` | The public signing key in JSON Web Key format |
| `/.well-known/public-key.pem` | The same public key in PEM text form |

The discovery document advertises exactly what is true and nothing more: `response_types_supported: ["code"]`, `grant_types_supported: ["authorization_code", "refresh_token"]`, `code_challenge_methods_supported: ["S256"]`, and `token_endpoint_auth_methods_supported: ["none"]`. It deliberately **omits** `id_token_signing_alg_values_supported` and `scopes_supported`, because this system does not yet issue OpenID Connect `id_token`s. That omission is the honest signal to a client library that it should not expect one.

### Two Standard Token Endpoints

- `POST /api/v1/auth/revoke` — RFC 7009 token revocation. Anonymous.
- `POST /api/v1/auth/introspect` — RFC 7662 token introspection: hand it a token, learn whether it is still active. This endpoint **requires an authenticated caller**.

### External Identity Providers

| Provider | Status |
|----------|--------|
| **Google** | Shipped and **on by default**. Identity tokens are validated with Google's own library, and the audience is pinned to your configured client id |
| **Apple** | Shipped and **off by default**. Turning it on requires an Apple Services ID, a verified domain, and a `.p8` private key from Apple. The issuer is pinned and Apple's key set is cached for 24 hours |

An account whose email address the provider has not verified is rejected. Adding a third provider means implementing one interface and registering it; the system resolves providers by name from a registry rather than switching on a hard-coded list. Importing the user's avatar from the provider is on by default.

---

## 7. Multi-Application and Organization Support

### The Shopping Mall Analogy

Think of AuthSystem as a **shopping mall security system**:
- **The Mall** = Your organization
- **Stores** = Your different applications
- **Security Badge** = User credentials (works in all stores)
- **Store Access Cards** = Application-specific permissions

### Multi-Application Support

```text
                    +---------------------+
                    |     AuthSystem      |
                    |  (Central Identity) |
                    +----------+----------+
                               |
         +---------------------+---------------------+
         |                     |                     |
         v                     v                     v
+-----------------+  +-----------------+  +-----------------+
|   CRM App       |  |   HR Portal     |  |   Finance App   |
| (Application 1) |  | (Application 2) |  | (Application 3) |
+-----------------+  +-----------------+  +-----------------+
```

**Benefits:**
- Single sign-on across all applications — SSO means a user proves who they are once, and every application accepts that proof
- Centralized user management
- Application-specific roles and permissions
- Shared audit logging

Each registered application carries its own client identifier, its own list of allowed redirect addresses, its own access mode — open to every user, or restricted to an invitation list — and its own optional re-authentication age. Deactivating an application immediately revokes the sessions and tokens issued for it.

### Organization Support (Multi-Tenancy)

```text
+-----------------------------------------------------------+
|                      AuthSystem                           |
+-----------------------------------------------------------+
|  +--------------+  +--------------+  +--------------+     |
|  |  Org: ABC    |  |  Org: XYZ    |  |  Org: 123    |     |
|  |  Company     |  |  Company     |  |  Company     |     |
|  +--------------+  +--------------+  +--------------+     |
|  | Users: 50    |  | Users: 200   |  | Users: 25    |     |
|  | Roles: 5     |  | Roles: 10    |  | Roles: 3     |     |
|  | Apps: 3      |  | Apps: 5      |  | Apps: 2      |     |
|  +--------------+  +--------------+  +--------------+     |
+-----------------------------------------------------------+
```

### The Membership Lifecycle

An organization is not just a label on a user. It has a full lifecycle, and every step of it ships.

1. **Create.** Any signed-in user can create an organization. Whoever creates it becomes its owner. No platform permission is needed for this — it is deliberately self-service.
2. **Invite.** An owner or administrator invites someone by email address. The invitation carries a token in the emailed link. An invitation can be **resent**, which reissues the token.
3. **Accept — two branches.** The person opening the invitation link may or may not already have an account. If they do, they accept as themselves. If they do not, they register through the invitation, and their email address is treated as already confirmed because the invitation reached it. Both branches are anonymous-accessible; that is what makes the link work from an email client.
4. **Grant.** Inside the organization, a member holds an organization role — `org-owner`, `org-admin` or `org-member` — and can additionally be granted individual application roles and individual permissions scoped to that organization.
5. **Subscribe applications.** An organization can be given access to specific applications, and that subscription can be updated or withdrawn.
6. **Transfer ownership.** The current owner starts a transfer, which emails a code to the prospective owner. The transfer completes only when that code is supplied. A platform administrator holding the right permission may complete a transfer without the code.
7. **Remove.** A member can be removed, or the whole organization deleted by its owner.

### How Organization Permissions Work

Organization permissions are carried in the access token as claims of the form `{organizationId}:{permissionCode}`, so the same person can be an owner of one organization and an ordinary member of another with no ambiguity. The permission codes in use are:

```text
org:update              org:members:read      org:members:manage
org:members:invite      org:apps:read         org:apps:manage
org:permissions:read    org:permissions:manage
```

There is one deliberate exception to reading permissions from the token. If a user creates an organization during their current session, their existing token cannot possibly carry a claim for it. When a request needs an `org:` permission and the token carries **no** claims at all for that organization, the system performs one live membership lookup against the database. If the token carries some claims for that organization but not the one required, no lookup happens — the answer is simply no.

One gap worth knowing: an individual permission can be **granted** to an organization member, but the API offers no endpoint to list those grants or to take one back. Application roles inside an organization do have all three — read, assign and remove — so the missing pair is specific to individually granted permissions. Undoing one is a database operation.

---

## 8. Roles and Permissions

### Role-Based Access Control

RBAC (role-based access control) means a user is not granted abilities one at a time. Abilities are bundled into a named role, and the user is given the role. A role belongs to one application, so "Manager" in your CRM and "Manager" in your HR portal are two independent things.

A user's effective permissions are the union of three sources: the permissions of every role they hold, any permissions granted to them directly, and any permissions they hold through an organization membership.

### Permission Format

A permission is a text code, colon-separated, read as `resource:action`:

```text
users:read              Read any user
users:create            Create users
users:update            Update users
users:delete            Soft-delete a user
users:manage            Lock, unlock, activate, deactivate, permanently purge
users:manage-roles      Assign and remove a user's roles
users:manage-permissions Grant and revoke a user's individual permissions
users:*                 All of the above
*                       Everything
```

### The 50 Permission Codes This System Actually Checks

An evaluator planning role design needs the real list, not an illustration. These are the exact codes enforced on endpoints, grouped by what they govern.

| Area | Codes |
|------|-------|
| Users | `users:read`, `users:create`, `users:update`, `users:delete`, `users:manage`, `users:manage-roles`, `users:manage-permissions` |
| Roles | `roles:read`, `roles:create`, `roles:update`, `roles:delete` |
| Permissions | `permissions:read`, `permissions:create`, `permissions:update`, `permissions:delete`, `permissions:manage` |
| Applications | `applications:read`, `applications:create`, `applications:update`, `applications:delete` |
| Organizations (platform-wide) | `organizations:read` |
| Organizations (scoped to one organization) | `org:update`, `org:members:read`, `org:members:manage`, `org:members:invite`, `org:apps:read`, `org:apps:manage`, `org:permissions:read`, `org:permissions:manage` |
| API keys | `apikeys:read`, `apikeys:create`, `apikeys:revoke`, `apikeys:rotate`, `apikeys:validate` |
| Webhook keys | `webhookkeys:read`, `webhookkeys:create`, `webhookkeys:revoke`, `webhookkeys:rotate`, `webhookkeys:validate` |
| Audit logs | `auditlogs:read`, `auditlogs:export` |
| Notifications | `notification-templates:read`, `notification-templates:manage`, `notification-templates:publish`, `notification-layouts:manage` |
| Privacy policy | `privacy-policy:read`, `privacy-policy:manage` |
| Platform branding | `platform-settings:manage` |
| System settings | `system-settings:manage` |
| Secrets vault | `secrets.manage` — note the **dot**; it is the only code in the system that does not use a colon |

### Wildcard Matching Is Prefix Matching

This is the detail people get wrong. A wildcard grant matches by **prefix**, not by pattern.

| Permission granted | Grants access to | Does **not** grant |
|--------------------|------------------|--------------------|
| `*` | Everything | — |
| `users:*` | `users:read`, `users:create`, `users:update`, `users:delete`, and `users` itself | `usersx:read` |
| `crm:*` | `crm:leads:read` and anything else starting `crm:` | `crmx:read` |

A wildcard in the middle of a code, such as `a:*:c`, is not supported.

### The Seeding Gap — Read This Before You Plan Roles

**On a freshly published database, 34 of the 50 codes above do not exist as rows.** They are checked by the API, but nothing creates them, so nothing can grant them. The affected codes are every `users:*`, `roles:*`, `permissions:*`, `applications:*`, `apikeys:*`, `webhookkeys:*` and `auditlogs:*` code, plus `org:permissions:manage` and `secrets.manage`.

The publish script seeds **8 roles** — `super-admin`, `admin`, `user-manager`, `auditor`, `user`, `org-owner`, `org-admin`, `org-member` — and **45 permission rows**. The trouble is *which* 45. Fifteen of them sit in a different namespace, prefixed `auth:`, and since wildcards match by prefix, holding `auth:users:*` does **not** satisfy a check for `users:read`. Several of the organization codes miss in the same way: the seed creates `org:permissions:grant` and `org:permissions:revoke`, while the code checks `org:permissions:manage`.

Follow that through to what each seeded role can actually do, because the names are misleading. `admin` is granted exactly one code, `auth:*`. `user-manager` is granted `auth:users:*`. `auditor` is granted `auth:audit:read` and `auth:users:read`. **No endpoint checks any of those codes**, so all three roles reach nothing. On a clean install the only account that can use the administrative endpoints is `super-admin`, which holds the global `*`.

A file named `08_AdditionalPermissions.sql` exists in the repository and would create 28 of the 34 missing codes, but the post-deployment script never includes it, so publishing the database does not run it. **Six codes exist in no SQL file anywhere**: `apikeys:validate` and all five `webhookkeys:*` codes. Those six can only ever be reached through a global `*` grant.

This is a known gap in the shipped seed data, not a mistake in your configuration. Anyone standing this system up should plan to run that file by hand or grant `*` to their first administrator.

### Time-Limited Assignments

A role assignment and a direct permission grant can both carry an expiry date, which is what you want for a contractor or a temporary escalation.

```text
Role assignment:
  User:     a contractor
  Role:     Developer
  Assigned: 2026-01-01
  Expires:  2026-06-30   <- stops applying after this date
```

---

## 9. Email and Notifications

Most identity systems bury their email content in resource files or in code, so changing a sentence means a developer, a build and a deployment. **This system keeps every message in the database.** An administrator edits the content, previews it, publishes it, and the next email sent uses the new wording. Nothing is redeployed and nothing restarts.

### What Ships

| Thing | Count seeded |
|-------|--------------|
| Notification types (the events that can produce a message) | **16** |
| Templates (the actual content) | **15** |
| Language translations of those templates | **105** — every template in all 7 languages |
| Shared layouts (the frame wrapped around every message) | **1** |

The one type without a template is `welcome-email`. It is seeded as a type and no template exists for it, which means **no welcome email is sent after registration**. That is the current state, stated plainly rather than implied. It is also the only one of the 16 marked as non-system, so the startup check does not flag it as a fault.

The other fifteen cover: email verification, password reset, organization invitation, ownership transfer code, ownership transferred, the four account-deletion stages, privacy-policy updated, new-device sign-in, account deleted by an administrator, sessions revoked after token reuse, session limit enforced, and the secret-operation confirmation code.

### Versioning: Draft, Publish, Roll Back

A template is not a single blob of text. It has a version history, and two pointers into it: one for the version that is live, one for the draft being worked on.

- **Editing** any language creates a draft automatically, cloned from whatever is currently published.
- **Saving a draft** rejects broken template syntax before it is stored, and refuses the save if someone else edited the template since you loaded it.
- **Publishing** is one atomic move of the "live" pointer. Every language of a version goes live together — you cannot ship English while Arabic is half-finished.
- **The publish gate** renders every language of the draft against the type's sample data with unknown-variable detection switched on. A typo in one language blocks the publish for all of them, and the error names the failing language.
- **Rolling back** moves the live pointer to an earlier version. No content is copied and no history is deleted.
- **Restoring** an old version as a new draft is a separate action, so you can edit history forward rather than reverting to it.

Layouts version more simply: draft columns and published columns, with publish copying one to the other.

### Rendering

Templates are written in Liquid, a small and widely-used templating language, rendered by a sandboxed engine. The sandbox is genuinely a sandbox:

- **No reflection.** A template can only see the variables handed to it, never the objects behind them.
- **A step limit.** Execution stops after 5,000 steps, so a runaway loop cannot hang the process.
- **HTML encoding by default** in every HTML context. Hostile input is escaped. The `raw` filter is the deliberate opt-out, used by the layout to place the body inside itself.
- **No custom filters, no file access.** Nothing beyond the engine's own built-ins is registered.

Every template receives the platform name, the platform logo, an email-safe rendition of that logo, the current year, and the calling application's name, code and address — plus whatever variables the triggering event supplies.

### Delivery: Queued, Retried, Dead-Lettered

By default a send does not go out inside the request that triggered it. The finished message is written to an outbox table and a background worker delivers it. This means a slow or unreachable mail server cannot make a user's sign-in slow or fail.

| Behaviour | Value |
|-----------|-------|
| Delivery mode | Queued by default; can be switched to send inline |
| Wake-up | Immediately when a message is queued, and otherwise every 30 seconds |
| Batch size | 20 messages per cycle |
| Attempts before giving up | 5 |
| Retry spacing | 1, 4, 16, 64, then 256 minutes |
| Orphaned messages | A message claimed by a worker that then died is reclaimed after 5 minutes |
| Manual requeue | An administrator can requeue a failed or dead message from the console's delivery log |

The outbox stores the **already rendered** message, so editing a template never changes mail that is already queued.

Six message types are marked as carrying live secrets — the ones containing a one-time code or a tokenised link. **Their bodies are overwritten with a placeholder the moment delivery succeeds**, so the delivery log cannot be used to read back somebody's verification code.

Two behaviours to be aware of. First, when email is switched off in configuration, a send **reports success** and the outbox row reads "Sent" even though nothing left the building; in development the code or link is written to the log instead. Second, a malformed recipient address is retried on the transient-failure schedule rather than failed immediately, so it consumes the full retry budget before being dead-lettered.

**Email is the only delivery channel that ships.** The database has room for SMS and push, and the code will refuse a message whose channel has no registered handler, but no such handler exists.

---

## 10. Multilingual Support

### Supported Languages

Seven languages ship, and the front end and the back end agree on exactly the same seven.

| Code | Language | Direction | Native name |
|------|----------|-----------|-------------|
| `en` | English | Left-to-right | English |
| `ar` | Arabic | **Right-to-left** | العربية |
| `tr` | Turkish | Left-to-right | Türkçe |
| `fr` | French | Left-to-right | Français |
| `zh` | Chinese | Left-to-right | 中文 |
| `ur` | Urdu | **Right-to-left** | اردو |
| `fa` | Persian | **Right-to-left** | فارسی |

### What Is Translated

Four things, not one. This is broader than most identity products offer.

1. **Both web applications** — every label, button, message and empty state in the console and the accounts application.
2. **Every email the system sends** — all 15 seeded templates ship with all 7 translations, 105 rows in total.
3. **The published privacy policy** — each revision carries a document per language, and the public address includes the language.
4. **API error and validation messages** — returned in the caller's language, from four families of resource files.

### Right-to-Left Support

Three of the seven languages read right-to-left: Arabic, Urdu and Persian. **Both** applications mirror for them, not just the administrative one. Each language declares its own direction, and the applications set the page direction from that declaration, so navigation, forms and tables flip without a separate build or a separate stylesheet.

### How It Works — Front End

The seven translation files are TypeScript modules. English is bundled and available immediately; the other six are fetched on demand the first time they are needed, so a user reading English never downloads the other six. The chosen language is stored in the browser's local storage under the key `auth.language` — **not a cookie** — and is sent to the API on every request in the standard `Accept-Language` header, which is how the user's interface language and the language of their error messages stay in agreement. Switching language does not reload the page.

An automated test enforces that every one of the seven files contains exactly the same set of keys as English, with the same placeholders, and that no application code refers to a key that does not exist. A missing translation is a failed build, not a blank screen.

### How It Works — Back End

Message text lives in 28 resource files: 4 families (general messages, domain errors, middleware messages, validation messages) times 7 languages. The API picks the language from four sources, in this order:

1. A query-string value
2. A cookie
3. The standard `Accept-Language` header
4. A custom `X-Language` header

If none of them names a supported language, the API answers in English. An unsupported value is ignored rather than causing an error.

---

## 11. Audit Logging and Compliance

### What the Audit Log Records

The audit log answers four questions about every recorded action: **who did it, what they did, when, and from where.** It is a building's visitor book, not its security cameras — it tells you that a change was made and by whom, with the before and after values, but it does not tell you whether an attempt failed.

That last point is important enough to state on its own, because the wrong assumption here has consequences for a compliance review:

> **There is no success or failure flag on an audit row, and there is no correlation identifier.** Those columns do not exist in the table. Every row the API returns reports `isSuccess: true` because the value is filled in by the code at read time, not stored. Failed sign-ins are recorded separately, in a different table, and are read through `GET /auth/login-history` and through the dashboard's sign-in statistics — not through the audit log.

### What Gets Logged

Audit rows are written by event handlers reacting to things that happened, which means coverage follows the events rather than being applied by a blanket rule.

| Category | Actions |
|----------|---------|
| **Authentication** | Sign-in, sign-out, password change, two-factor enable and disable |
| **User management** | Create, update, delete, permanent purge, lock, unlock, activate, deactivate |
| **Roles and permissions** | Create, update, delete, assign, revoke |
| **API keys** | Create, rotate, revoke |
| **Sessions** | Create, terminate |
| **Organizations** | Create, invite, accept, member changes, ownership transfer |
| **Secrets** | Key generation, import, and the confirmation ceremony around them |
| **Notifications** | Template publishing and rollback |
| **Privacy policy** | Version creation and publication |
| **System settings** | Section updates and resets |
| **Account deletion** | Every stage of the lifecycle |

**Coverage is not uniform, and one gap is known:** creating or revoking a **webhook key writes no audit row**. The events are raised but nothing handles them. Creating an API key, by contrast, does write a row.

### Audit Log Entry Structure

These are the fourteen columns that actually exist. Nothing is shown here that the table does not store.

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "userId": "the user who performed the action",
  "applicationId": "which registered application it happened in",
  "sessionId": "which session it happened in",
  "action": "UserCreated",
  "entityType": "User",
  "entityId": "the record that was affected",
  "oldValues": null,
  "newValues": {
    "email": "newuser@example.com",
    "firstName": "John"
  },
  "ipAddress": "192.168.1.100",
  "userAgent": "Mozilla/5.0...",
  "details": "free-text context for the action",
  "timestamp": "2026-01-15T10:30:00Z",
  "performedBy": "the acting account, for attribution after deletion"
}
```

### Compliance Coverage

The audit log is raw material an auditor can be handed. It is not compliance, and nothing in this repository certifies the system against any framework below. Read the right-hand column as the whole of what you get, limits included.

| Standard | What it regulates | How AuthSystem helps, and where it stops |
|----------|-------------------|---------------------|
| **GDPR** | EU law governing privacy and protection of personal data | Records who **changed** a user record, when, and from which address, alongside the erasure pipeline in Section 12. It does **not** record who merely *read* personal data — reads are never audited |
| **SOC 2** | Auditing standard that measures customer data protection | Shows that access controls exist and that administrative changes are recorded. Monitoring is not included: nothing in this repository raises an alert about anything |
| **HIPAA** | US law protecting sensitive patient health information | Shows that authentication controls exist and that sign-ins and administrative changes are recorded. Access to the clinical records themselves happens in your application, not here |
| **PCI-DSS** | Security requirements for credit card transactions | Supplies an administrative-change trail for the identity system. It is **not** a complete access trail: failed attempts are absent, reads are absent, and webhook-key changes are recorded nowhere |
| **Internal audit** | Organization's own governance and risk management policies | Entries can be filtered by user, application, date range and action name, and exported. There is no free-text search across the log |

### Reading the Audit Log

There are four ways to read it and one way to export it.

| Address | What it returns | Permission |
|---------|-----------------|------------|
| `GET /api/v1/audit-logs` | The paged log, 50 rows per page by default | `auditlogs:read` |
| `GET /api/v1/audit-logs/{id}` | One entry | `auditlogs:read` |
| `GET /api/v1/audit-logs/users/{userId}` | Everything one user did | `auditlogs:read` |
| `GET /api/v1/audit-logs/entities/{entityType}/{entityId}` | Everything that happened to one record | `auditlogs:read` |
| `POST /api/v1/audit-logs/export` | A downloadable file in **CSV or JSON**, up to 10,000 rows by default | `auditlogs:export` |

**Filters that work:** user, application, date range, and action name as a substring match.

**Filters that do not work.** The API accepts `actionType` and `isSuccess` as query parameters on both the list and the export, and **silently ignores both**. Neither column exists. Do not build a monitoring process around either of them.

**Searching that does not exist.** There is no search across the `details` field, and no full-text index on the table. The only text filter is the substring match on the action name.

---

## 12. Privacy, Consent and Account Deletion

This is the section a data-protection reviewer will ask about. Everything described below ships and runs; what it does not amount to is stated at the end of the section.

### Versioned, Translated, Published Privacy Policies

A privacy policy here is not a static page someone remembers to update. It is a versioned object with an explicit publication step.

| Capability | Detail |
|------------|--------|
| Revisions | Each is named `YYYY.MM`, carries an effective date and a change note |
| Translation | Each revision holds one document per language |
| Publishing | An explicit action makes one revision the published one; until then it is a draft nobody sees |
| Public reading | `/privacy` redirects to the reader's preferred language chosen from their browser's `Accept-Language` header; `/privacy/{language}` serves that language |
| Permanent addresses | Superseded revisions stay readable forever at `/privacy/v{version}/{language}`, so a link in an old email never breaks |
| Notifying users | One action emails every active, confirmed user that the policy changed, through the notification pipeline in Section 9 |
| Disclosure block | The published policy is served together with the live values of the configured data-controller identity and retention windows, so the document cannot silently drift from the system's actual behaviour |

The public policy pages set their own, far stricter Content-Security-Policy, are cached with an `ETag`, and answer a conditional request with a 304. They are served as complete HTML by the API rather than by the React application, so they remain readable even if that application fails.

Permissions: `privacy-policy:read` to view revisions, `privacy-policy:manage` to create, edit, publish and notify.

### Deleting an Account

Deletion is a staged pipeline, not a `DELETE` statement. There are two entry points and one destination.

**A signed-in user** requests deletion from the Danger Zone on their profile. They must re-prove possession of their email address with a code sent to it.

**A person who cannot sign in** — because they lost their password, or never had one — uses the public wizard at `/delete-account` in the accounts application. They enter an email address and confirm with an emailed code. The system's response never reveals whether that address has an account.

**An administrator** can also initiate deletion of a user's account, which sends its own notification.

Whichever door was used, the same thing happens next:

1. The account is scheduled for deletion and the user is told the exact date.
2. **A 30-day recoverable grace window opens.** During it, the user can cancel by following the recovery path, and the account comes back intact.
3. When the window closes, a background worker performs the staged irreversible destruction, in batches, with retries and a dead-letter state if it cannot complete.
4. A **tombstone** row survives. It records that an account existed and was destroyed, without holding the personal data — this is what lets you answer "was this person's data deleted, and when?" without keeping the person's data to answer it.
5. The destroyed email address is **reserved** so it cannot silently be reused to create a fresh account that inherits history.

### Retention Windows

These are the shipped defaults, all editable from the console within bounds.

| Data | Retained for |
|------|--------------|
| Grace period before destruction | **30 days** |
| Sign-in attempt records | **365 days** |
| Delivered email records in the outbox | **180 days** |
| Audit log rows | **1,095 days** (three years). The console will not let this be set lower, because it is treated as a published commitment |
| Identifier reservation after destruction | **1,095 days** |

### What This Section Does Not Claim

Nothing in this repository certifies the system against GDPR, SOC 2, HIPAA or PCI-DSS. There is no external audit, no attestation, and no certificate. What exists is the machinery an auditor asks for — an audit trail, versioned published policies, a documented erasure pipeline, and stated retention windows. Whether that satisfies your regulator is a question for your regulator.

---

## 13. Configuration and Operations

### Changing Behaviour Without a Redeploy: System Settings

Most of this system's behaviour is a setting, and most settings can be changed from the console by someone holding `system-settings:manage`.

The mechanism has three layers. The configuration files that ship are the baseline. A database table holds **only the values an administrator has changed** — the overrides, not a copy of everything. A provider layers the overrides on top of the files and re-reads them live.

| Property | Behaviour |
|----------|-----------|
| What is editable | Password policy, lockout, token lifetimes, session rules, both sets of rate limits, sign-on settings, external providers, email and SMTP settings, notification delivery, image limits, retention windows, CORS origins, gateway settings |
| Hot versus restart | Most values apply immediately. The ones that cannot — signing-key identity, the token issuer, Argon2id cost parameters, the breached-password check — are explicitly labelled restart-required in the console |
| Bounds | Every numeric field declares a minimum and a maximum, enforced server-side; the console shows them next to the field |
| Read-only fields | A few values are shown but cannot be edited, because something else owns them |
| Sensitive fields | Values held in the secrets vault are never read or written through this screen; the console links to the secrets screen instead |
| Resetting | A whole section can be reset to its file values in one action |
| Concurrency | Saving carries a row version, so two administrators editing at once get a conflict rather than a silent overwrite |
| Failure behaviour | If the database layer cannot be read at startup, the system falls back to the file values rather than refusing to start |
| Escape hatch | An environment variable disables the database layer entirely, for recovery |

The gateway is a separate process with no database access, so it cannot read these overrides itself. Instead it **pulls its settings from the API** over an internal endpoint that requires the shared gateway token, which is why an administrator can change the gateway's rate limits from the same console screen as everything else.

There is also a **Platform settings** screen, separate from the above, holding the platform's own name, its light and dark logos, and its favicon. Those values appear on the sign-in pages and inside outgoing email.

### Where the Keys Live: The Secrets Vault

The signing key, the refresh-token HMAC key, the gateway token, the SMTP password and the database connection string are not kept in configuration files. They live in a secret store with three modes, and **only** three:

| Mode | What it means |
|------|---------------|
| `PlainText` | Values are written into the environment's own settings file. This is the class default, and it is what the shipped Development configuration explicitly uses |
| `Certificate` | Values are encrypted with a certificate you supply. This is what the base configuration sets. Outside Development, the system **refuses to start** if it cannot load the certificate |
| `Dpapi` | Values are encrypted with the Windows Data Protection API, tied to the machine |

**There is no external key vault integration of any kind.** Not Azure Key Vault, not HashiCorp Vault, not AWS Secrets Manager. If your policy requires one, this is a gap you would have to close yourself.

On first start with no secrets file, the required keys are generated automatically: a 2048-bit RSA signing key, a 256-bit HMAC key, and a 256-bit gateway token. The system refuses to start if a required secret is missing afterwards, and it refuses to start in Production if a plaintext secret is found sitting in the Production configuration file.

**Every destructive secret operation requires a challenge first.** Rotating or importing a key is a two-step ceremony:

1. The administrator raises a challenge. A six-digit code is emailed to them, and the response tells them the **blast radius** — what will stop working when this key changes.
2. They answer with the code. The approval is valid for **5 minutes**, is bound to that administrator, that specific operation, and for an import, a digest of the exact key material being imported.

The code expires, allows 5 attempts, and is single-use. Attempt counting happens before the hash is verified, so slow hashing cannot be used to buy extra guesses. Every failure mode returns the same message, so an attacker learns nothing from the difference between "wrong code" and "no such challenge".

### Images and Branding

One authenticated upload endpoint serves every image in the system: profile pictures, organization logos, application logos, and the platform's own branding. The caller uploads a file, receives a storage key and a public address, and then saves that key against the user, organization or application.

| Limit | Value |
|-------|-------|
| Maximum file size | 4 MB |
| Maximum pixel count | 50 megapixels, checked from the file header before decoding, so a decompression bomb is rejected without being expanded |
| Maximum edge length | 1,024 pixels; larger images are downscaled |
| Accepted types | PNG, JPEG, WebP and GIF. **SVG is deliberately excluded**, because an SVG can carry script |
| Output | Re-encoded to WebP at quality 90, with metadata stripped |

Uploaded files are served from a physical folder outside the deployed application, with `nosniff` and cache headers. **The application writes a probe file into that folder at startup** and reports loudly if it cannot, because a web server's application-pool identity frequently lacks permission to write there and the failure would otherwise appear only when a user tried to upload.

Because some email clients cannot display WebP — and one of them flattens transparency onto black — the system additionally builds opaque PNG renditions of the platform logo specifically for use inside email, in both a light and a dark variant.

### The Dashboard

The console's home screen is backed by six endpoints, each with its own permission, and the screen hides any panel the viewer cannot read.

| Panel | Covers | Permission |
|-------|--------|------------|
| User statistics | Totals, new signups, activation funnel, dormancy | `users:read` |
| Sign-in statistics | Outcomes, daily active users, failure reasons, lockouts, most-failing addresses | `auditlogs:read` |
| Audit statistics | Totals, daily series, breakdown by action | `auditlogs:read` |
| Session statistics | Session and refresh-token hygiene | `auditlogs:read` |
| Application activity | Per-application activity and organization enablements | `applications:read` |
| Credential expiry | API keys and webhook keys approaching expiry, over a configurable horizon | None — it returns only the families the caller is allowed to read |

Most panels cover a trailing 30-day window by default, adjustable up to 90 days, rendered in the viewer's own time zone.

### Machine Credentials: API Keys and Webhook Keys

**API keys** let another program call the API without a human signing in. A key is issued with a visible prefix identifying its environment, and only its hash is stored — the full value is shown once, at creation, and never again. Keys carry scopes, an optional expiry, rotation with a grace period during which both old and new work, and revocation.

One honest limitation: an API key carries `RateLimitPerMinute` and `RateLimitPerDay` fields. They are stored, validated, returned when the key is validated, and sortable in the console — and **nothing in this system enforces them**. They are metadata for a consuming application to act on, not a throttle.

**Webhook keys** are signing keys, so a system receiving a callback can verify it genuinely came from you. They are created, listed, rotated, revoked and validated the same way.

Two things to know before relying on webhook keys. First, as noted in Section 11, creating or revoking one **writes no audit row**. Second, none of the five `webhookkeys:*` permission codes, nor `apikeys:validate`, exists in any database seed file, so on a standard install they can only be reached by an administrator holding the global `*` grant.

---

## 14. Scalability and Maintainability

### Horizontal Scalability, and Its One Real Limit

```text
Current:                              Scaled:
+--------------+                      +--------------+
|  AuthSystem  |                      | AuthSystem 1 |
|  instance    |                      +--------------+
+------+-------+                      | AuthSystem 2 |
       |                              +--------------+
       v                              | AuthSystem 3 |
+--------------+                      +------+-------+
|  Database    |                             | Load balancer
+--------------+                             v
                                      +--------------+
                                      |  Database    |
                                      +--------------+

  Caveat: token revocation is NOT shared between instances.
  See the paragraph below before deploying more than one.
```

| Property | What it gives you |
|----------|-------------------|
| **Self-contained tokens** | An access token carries everything needed to validate it, so any instance can accept a token issued by any other instance without a shared session store |
| **Database indexing** | Query performance holds as the tables grow |
| **Asynchronous operations** | More concurrent requests per server |
| **Edge rate limiting** | The gateway absorbs traffic spikes before they reach the API |

**Now the limit, stated bluntly, because it decides your deployment topology.** Revocation is *not* shared between instances. The list of revoked tokens lives in each process's own memory and is consulted on every authenticated request. Revocations are written to a database table asynchronously and read back **only when a process starts**. A sign-out handled by instance 1 therefore does not stop that token working on instance 2 until instance 2 restarts — for up to the token's full 15-minute lifetime.

Running more than one instance today means accepting that window. Closing it needs either a shared cache or a short polling loop against the revocation table. **Neither ships.** No distributed cache of any kind is present in this system.

### What Makes It Maintainable

| Property | What it gives you |
|----------|-------------------|
| **Clear layer separation** | The domain layer depends on nothing; infrastructure never leaks inward |
| **One handler per request** | Every operation has exactly one place it lives, found by its name |
| **Typed errors instead of exceptions** | A business rule violation is a returned value, not a thrown exception, so it cannot be swallowed by accident |
| **Dependency injection throughout** | Components are swapped by registration, not by editing call sites |
| **Structured logging** | Logs are queryable data, not sentences to grep |
| **A consistent error body** | Failures come back as a standard problem document, so a client writes one error handler |

> For detailed architecture and design principles, see [03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md](03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md)

---

## 15. What This System Does Not Include

An evaluator who discovers these in week two will stop trusting the rest of this document. So here they are in week one. Every line was verified against the repository, not assumed.

| Not included | What that means for you |
|--------------|-------------------------|
| **No continuous integration or deployment pipeline** | The workflow folder exists and is empty. Nothing builds, tests or deploys automatically. Every deployment is a manual act |
| **No Docker, no container images, no Kubernetes** | There is no Dockerfile, no compose file and no manifest anywhere. Deployment targets IIS on Windows |
| **No Redis and no distributed cache** | All caching is inside each process. This is what produces the revocation limit in Section 14 |
| **No message broker** | The interface for publishing events outside the process exists, and its only implementation deliberately discards them. Nothing leaves the process |
| **No outbound webhook delivery** | Webhook signing *keys* are fully managed, but no event is ever delivered to an external system |
| **No real-time channel** | There is no WebSocket or server-push mechanism. The applications poll |
| **No external key vault** | Three secret modes only, described in Section 13 |
| **No Swagger user interface** | A machine-readable OpenAPI document is generated, but only when running in Development. **Production exposes no API document and no interactive explorer at all** |
| **No `id_token` issuance** | The authorization-code flow is OAuth 2.0. It is not yet a complete OpenID Connect provider, and the discovery document says so |
| **No bulk endpoints** | Every write acts on one record |
| **No restore endpoint for a soft-deleted user** | Undoing a soft delete is a database operation |
| **No SMS or push notifications** | Email is the only delivery channel implemented |
| **No SQL Server Always Encrypted** | No column in the schema uses it. Field-level protection is done in the application, with AES-256-GCM under a per-user key |
| **No production configuration in the repository** | Every committed value is a placeholder. A clean clone has no production settings, deliberately — production secrets are never committed |

---

## 16. How It Is Tested

These are static counts, taken by reading the test source. They tell you how much test code exists, not what fraction of the system it exercises.

| Measure | Count |
|---------|-------|
| Backend test project | **1** |
| Backend test files | **176**, of which **171** contain test cases |
| `[Fact]` test cases | **1,412** |
| `[Theory]` test cases | **68**, fed by **241** inline data rows and 2 data sources |
| Front-end unit test files | **40** |
| Front-end unit test cases | **276** |
| End-to-end browser test files | **5** |

Two honest caveats belong here.

**No coverage percentage is published, by anyone, including this document.** A coverage collector is referenced by the test project, but there is no threshold, no report task, and nothing that consumes its output. Any coverage figure you are quoted about this system is unsupported.

**Nothing runs the suite automatically.** There is no pipeline. The tests run when a developer runs them.

Some tests exist specifically to stop known classes of mistake recurring. One asserts that every gateway route covers a real controller prefix, so adding an endpoint without a route fails the build. One asserts that all seven translation files hold identical key sets. One asserts the database's foreign-key behaviour on permanent user deletion.

---

## 17. Comparison with ASP.NET Core Identity

### Honest Comparison

ASP.NET Core Identity is a well-maintained library backed by a large community and extensive official documentation. It integrates natively with the Microsoft ecosystem and is an excellent choice for straightforward authentication in a single application.

The difference is scope, not quality. ASP.NET Core Identity gives you authentication primitives and leaves the product around them to you — the administrator interface, the self-service interface, the organization model, the audit trail, the email content management. AuthSystem ships those. If you do not need them, the extra surface is a cost, not a benefit.

| Aspect | ASP.NET Core Identity | AuthSystem |
|--------|-----------------------|------------|
| **Password algorithm** | PBKDF2 by default — the exact settings change between ASP.NET Core versions, so check the version you are on — and the hasher is pluggable | Argon2id at `m=19456, t=2, p=1`, with an optional server-side pepper and optional breached-password screening |
| **Permission model** | Claims-based and flat | Hierarchical codes with prefix wildcards, and per-organization scoping |
| **Multi-application** | Set up separately per application | One authorization-code flow serving every registered application |
| **Organizations** | Not included; you build it | Included, with invitations, per-member roles and ownership transfer |
| **Audit logging** | You build it | Included, automatic for the covered actions — with the gap in Section 11 stated |
| **API gateway** | Separate component you choose and wire up | Included, with 24 routes and its own rate limits |
| **Rate limiting** | Available in ASP.NET Core; you configure it | Configured, in two tiers, editable from the console |
| **Two-factor authentication** | Supported | TOTP plus 10 single-use recovery codes, with the secret encrypted per user |
| **Session management** | Limited visibility for the end user | Every session and device listed and individually revocable by the user; a concurrent-session cap that is off by default |
| **Email content** | Resource files or code; changing wording needs a deployment | Stored in the database, edited and published from the console in 7 languages |
| **Administrator interface** | None; you build it | A React console shipped with the product |
| **End-user self-service interface** | None; you build it | A React accounts application shipped with the product |
| **Right-to-left interface support** | You build it | Three of the seven shipped languages, in both applications |
| **Community and documentation** | Very large community, extensive official documentation | This documentation set, and the source |
| **Azure integration** | Native | Only through standard protocols |

### When to Choose AuthSystem

AuthSystem is the better choice when your organization needs:

1. **Multiple applications** sharing one identity system
2. **Organizations or tenants** as a first-class concept
3. **Hierarchical permissions** beyond flat roles
4. **An audit trail and an erasure pipeline** without building them
5. **An administrator console and an end-user self-service application** without building them
6. **Seven languages, including right-to-left, in the interface and in the emails**

### When ASP.NET Core Identity May Suffice

ASP.NET Core Identity may be enough when:

1. You have a single application with simple role-based access
2. You do not need organizations or hierarchical permissions
3. You are already building your own administrator and account screens
4. Your audit requirements are minimal
5. You want the Microsoft ecosystem's community and support surface

### Migration Path

```text
Phase 1: Deploy AuthSystem alongside the existing system
Phase 2: Move the user records across, and give every user a new password
Phase 3: Point applications at AuthSystem one at a time
Phase 4: Decommission the old identity system
```

**Phase 2 is the expensive one, and it is worth being blunt about why.** This system verifies Argon2id hashes and nothing else. A password hash exported from ASP.NET Core Identity — or from any other product — will not verify here, and no import path exists that would let it: there is no reader for a foreign hash format anywhere in the code. A migration therefore cannot quietly convert people as they sign in. Every migrated user has to go through a password reset, or set a password on first sign-in, or arrive through Google or Apple instead. Budget for that message going out to your whole user base.

The re-hashing behaviour that *does* exist is much narrower, and it is easy to mistake for the one above. An AuthSystem hash is recomputed at that user's next successful sign-in when the Argon2id cost settings change, or when the server-side pepper is switched on. That keeps this system's own hashes current. It does not accept anybody else's.

---

## 18. Roadmap

### Shipped — Do Not Confuse These With Plans

Older versions of this document listed several of the items below as planned. They are not planned; they exist today, and each has a section above.

| Capability | Where it is described |
|------------|----------------------|
| OAuth 2.0 authorization-code flow with mandatory PKCE, plus token introspection, revocation, and a public discovery document with a published key set | Section 6 |
| Sign-in with Google (on by default) and sign-in with Apple (shipped, off until you supply Apple's credentials) | Section 6 |
| Database-backed notification templates with versioning, publishing, rollback and 7 languages | Section 9 |
| Versioned, translated, published privacy policies with a "notify every user" action | Section 12 |
| Self-service and administrative account deletion with a 30-day recoverable window | Section 12 |
| Database-backed system settings, editable from the console | Section 13 |
| An encrypted secrets vault with challenge-confirmed key operations | Section 13 |
| Image upload and platform branding | Section 13 |
| A dashboard with six statistics panels | Section 13 |
| Organization invitations, per-member roles, application subscriptions and ownership transfer | Section 7 |
| API keys and webhook signing keys | Section 13 |

### Genuinely Not Built

Each of these was checked against the source. None of them exists in any form.

| Capability | What is missing, precisely | Priority |
|------------|---------------------------|----------|
| **OpenID Connect `id_token` issuance** | The authorization-code flow works, but no `id_token` is issued. The discovery document deliberately omits the fields that would advertise one | High |
| **LDAP / Active Directory synchronisation** | Nothing reads a corporate directory | High |
| **Microsoft Entra ID and other external providers** | Only Google and Apple are implemented. Adding another means implementing one interface and registering it — that is the extension point | Medium |
| **SAML 2.0 federation** | No SAML implementation of any kind | Medium |
| **Outbound webhook delivery** | Signing keys are fully managed today, but **no event is delivered to any external system**. The publisher interface exists and its only implementation deliberately discards what it is given. There is no broker and no outbound HTTP sender | Medium |
| **Trend and anomaly analytics** | The dashboard shows six live panels over a trailing window. What is missing is history beyond that window and any risk or anomaly scoring | Future |
| **Passwordless authentication (WebAuthn / FIDO2, passkeys)** | Nothing in the repository touches these standards | Future |
| **Shared token revocation across instances** | Described in Section 14. Needs a shared cache or a polling loop; neither exists | High, if you intend to run more than one instance |

### Design Principles for Future Development

- Security over convenience
- Correctness over speed
- Full audit coverage for every new feature — including closing the webhook-key gap in Section 11
- Backward-compatible API versioning

---

## 19. Conclusion

### Summary of Key Benefits

| Benefit | What it means for you |
|---------|-----------------------|
| **Argon2id password storage** | Memory-hard hashing, plus an optional pepper and optional breached-password screening you can switch on per environment |
| **Standards-based single sign-on** | An OAuth 2.0 authorization-code flow with mandatory PKCE, a public discovery document, and a published signing key, so other software can verify your tokens without asking you |
| **Two interfaces you do not have to build** | An administrator console and an end-user accounts application, both shipped, both in 7 languages, three of which read right-to-left |
| **Content you can change without a deployment** | Email templates, privacy policies and most server settings are edited and published from the console |
| **An erasure pipeline with a grace window** | Self-service and administrative deletion, 30 days recoverable, then staged destruction with a tombstone and an identifier reservation |
| **An audit trail** | Who did what, to which record, when, and from where — with the coverage gap and the absent success flag stated in Section 11 rather than hidden |
| **Honest limits** | Section 15 lists everything absent. Section 14 states the multi-instance revocation window. Section 8 states the permission-seeding gap. You will not discover these in week two |

### Your Current Identity System: A Quick Assessment

If your organization answers "No" to three or more of these, this platform is worth evaluating:

1. Does your system use a memory-hard password hashing algorithm?
2. Can you show who accessed which record, when, and from where?
3. Do your users sign in once and reach every one of your applications?
4. Can you express permissions hierarchically rather than as flat roles?
5. Can you delete a user's data on request, on a defined schedule, and prove afterwards that you did?
6. Can a non-developer change the wording of a verification email, in every language you support, without a deployment?
7. Do your users have a self-service screen where they can see and end their own sessions?

### Next Steps

| Action | Resource |
|--------|----------|
| **One-page overview for decision makers** | [Executive Summary](01_AUTH_SYSTEM_EXECUTIVE_SUMMARY_EN.md) |
| **Architecture and implementation details** | [Technical Deep Dive](03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md) |
| **Connecting your own application to this platform** | [Application Integration Guide](APPLICATION_INTEGRATION_GUIDE.md) |
| **Setting up a development machine** | [Developer Guide](DEVELOPER_GUIDE.md) |
| **Deploying to a server** | [Production Deployment Guide](PRODUCTION_DEPLOYMENT_GUIDE.md) |

### Final Word

This document was written to be checked. Every count in it was counted, every default was read from the shipped configuration, and everything that does not work is named as not working — the permission-seeding gap, the ignored audit filters, the unenforced per-application session cap, the undelivered webhooks, the per-instance revocation window. A platform you can verify is worth more than one you have to trust.

---

*Verified against branch `main` at commit `58744f9`.*
