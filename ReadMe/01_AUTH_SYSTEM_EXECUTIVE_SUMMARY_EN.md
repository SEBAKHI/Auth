# AuthSystem — Executive Summary

## Identity platform for organisations running several applications and several tenants

---

### What is AuthSystem?

AuthSystem is one central place that answers three questions for all of your applications: **who is this person** (authentication), **what are they allowed to do** (authorization), and **what did they do** (audit logging).

You are not buying a library that your developers wire into each application. You are getting a running system with four parts: a backend service that every application calls, a gateway that sits in front of it and absorbs traffic, an **admin console** your staff work in, and a separate **account portal** your end users sign in to and manage themselves. Both of those last two are ordinary web applications that open in a browser — there is nothing to install on a desktop.

---

### What it is, in numbers

Every figure below was counted in the repository, not estimated.

| Measure | Count |
|---|---|
| Backend endpoints (HTTP actions the applications can call) | **199** across 25 controllers |
| Business operations behind them (request handlers) | **190** |
| Database tables | **52** |
| Automated backend test cases | **1,412** fixed cases plus 68 parameterised ones |
| Display languages, including right-to-left scripts | **7** |
| Message types the system can email, with editable content | **16** types, **15** with a template, each in all 7 languages |

There is no measured test-coverage percentage and no automatic build pipeline; see "What this system does not do yet".

---

### What you need to run it

- **A Windows server running IIS** (Internet Information Services, the Windows web server). IIS is the only hosting arrangement the product is configured for, and one of its three secret-storage modes is Windows-only, so this is a Windows product in practice.
- **A Microsoft SQL Server database.** The schema targets SQL Server 2019 or later.
- **An SMTP mail server** (Simple Mail Transfer Protocol — the standard way software sends email), because verification codes, invitations and security alerts go out by email.
- **A TLS certificate** (Transport Layer Security — what puts the padlock in the browser) for each public address.

There is no container platform, no message broker, no separate caching server and no cloud key-vault service to buy or operate.

---

### What you get

Each row states its limit in the same cell. Nothing here is a plan; it all runs today.

| Capability | What it means for you, and where it stops |
|---|---|
| **Two web applications, not one** | An admin console where your staff manage users, roles, applications, organisations, keys, audit history, email content and platform settings — and a separate account portal where your own users sign in, edit their profile, turn on two-step sign-in, review their sessions and delete their account. Both are built as modern browser applications. |
| **Single sign-on across your applications** | A person signs in once and is returned to whichever application sent them, using the same standard handshake that Google and Microsoft sign-in use: an authorization code with PKCE (proof key for code exchange). An application that already speaks that standard can be pointed at this system. Each application declares in advance the exact addresses it may be returned to. |
| **Password security at the current recommended setting** | Passwords are hashed with Argon2id at 19 MiB of memory, 2 iterations, 1 thread — the setting OWASP (the Open Worldwide Application Security Project) currently recommends. An account locks itself after 5 failed attempts for 15 minutes. Two further defences ship **switched off**: a server-held secret mixed into every hash, and a check of new passwords against public breach lists. Turning the server-held secret on creates a backup obligation — lose it and every user is locked out. |
| **Two-step sign-in with an authenticator app** | A user can require a 6-digit code from an authenticator app in addition to their password, and gets 10 single-use recovery codes for a lost phone. Codes by text message or email are not supported, and recovery codes are issued once at setup — there is no regenerate button. |
| **Sign in with Google or Apple** | Users can reuse an account they already have instead of creating another password. Google works once you supply a client identifier. Apple needs an Apple Developer services identifier, a verified domain and a signing key before it can be switched on, and ships disabled. |
| **You can see and end sessions** | An administrator, and the user themselves, can list active sign-ins and revoke any of them. Signing in from an unrecognised device sends the user an email. A stolen refresh token is detected the moment it is reused, and every token that account holds is revoked at once. The limit on concurrent sessions is set once for the entire platform — the per-application limit field on each registered application is stored but enforced nowhere. |
| **Several organisations on one platform** | Create an organisation, invite people to it by email, give each member one of three roles inside it, choose which applications that organisation may use, and hand ownership to someone else through a confirmation code. Permissions can be held for one organisation rather than for the whole platform. |
| **Permissions in groups, not just roles** | Permissions are grouped with a wildcard, so one grant such as `org:*` covers everything inside an organisation. A grant can be given an expiry date, and the database stops honouring it on its own when that date passes. **Know this before you plan roles:** the code checks 50 different permission codes, but a clean database install creates only 45 permission rows and 34 of the 50 checked codes get no row at all. A one-time script in the repository would create 28 of the 34, but it is not wired into the installer, and 6 of the 34 exist in no script at all. Until you fix that by hand, only the built-in super-admin role reaches most administrative endpoints. |
| **A record of sensitive operations** | The system writes a timestamped entry for the operations it is wired to watch — sign-in, user and role changes, key creation, policy publication and similar — each carrying who did it, the address they came from, the browser they used and the record they touched. Entries are searchable in the console and exportable as a spreadsheet file. **Two limits to know before you plan an audit programme: the log records what succeeded, not what failed, and before-and-after values are captured for only a handful of administrative changes.** |
| **Every email the system sends is yours to edit** | Verification codes, password resets, invitations, security alerts and policy notices are stored as templates in the database in all 7 languages, edited in the admin console, previewed and published without a code release. Messages queue and retry if the mail server is down, and the delivery log shows what went out. One gap: the welcome-email type has no template, so no welcome email is sent. |
| **Keys for programs, not people** | Another system can call this platform with an API key (application programming interface key) instead of a human sign-in, and outbound webhooks are signed so the receiver can prove the call came from you. Keys rotate with an overlap window so nothing breaks mid-swap. The per-key request quotas are recorded but enforced by nothing in this system, and the permissions that guard webhook keys have no database row, so only the super-admin role can use them. |
| **Seven languages, including right-to-left** | English, Arabic, Turkish, French, Chinese, Urdu and Persian, in both web applications and in the messages the backend sends. Arabic, Urdu and Persian render right to left throughout. |
| **Rate limiting and security headers** | Every response carries the browser-hardening headers (frame blocking, content-type pinning, referrer policy, permissions policy, content security policy). Rate limiting happens in two places: sign-in and password reset are throttled inside the backend itself, and everything else is throttled at the gateway in front of it. **The gateway is therefore not optional — publish the backend straight to the internet and everything except sign-in and password reset is unthrottled.** |

---

### What an auditor gets

Two mechanisms in the product speak directly to privacy regulation such as GDPR (the European General Data Protection Regulation):

- **Users can delete their own account.** They can do it signed in, or from a public page if they have lost access. The account stays recoverable for a grace period, then is destroyed, leaving only a non-identifying record that a deletion happened.
- **Your privacy policy is part of the product.** Draft it, translate it per language, publish a dated version, and the system emails every user that it changed. Previous versions stay on record.

Alongside those, an auditor can be handed the activity log described above, with its two stated limits, and each user's own sign-in history.

**What you are not getting:** there is no control mapping, no evidence pack and no certification for SOC 2, HIPAA or PCI-DSS anywhere in this product. Meeting those frameworks remains your organisation's work; this system supplies raw material for it, not compliance itself.

---

### Technology foundation

| Part | What it is |
|---|---|
| Backend service | .NET 10 and C#. |
| Database | Microsoft SQL Server, schema targeted at SQL Server 2019. Data access uses Dapper, a lightweight library — the system writes its own SQL rather than generating it. |
| Admin console (for your staff) | A browser application built with React 19 and TypeScript, bundled by Vite. |
| Account portal (for your users) | A second browser application on the same technology, built and deployed separately from the console, at its own web address. |
| Gateway | YARP (Yet Another Reverse Proxy), Microsoft's open-source reverse proxy, carrying 24 routes and most of the rate limiting. |
| Security | Argon2id password hashing; access tokens issued as JSON Web Tokens signed with RS256, a public-key signature so other systems can verify a token without holding a secret. |
| Logging | Serilog, writing plain-text lines to rolling daily files, one new file per day, the last 30 files kept. Nothing in the product ships a machine-readable log format or a connection to a log-search server; adding one is your work. |
| Hosting | Windows Server with IIS. |

---

### What this system does not do yet

Stated plainly so nothing surprises you after a decision.

- **Nothing builds, tests or deploys automatically.** There is no continuous-integration pipeline in the repository. Every release is performed by hand.
- **Nothing is broadcast to other systems.** The component meant to publish events outward is a stub that does nothing, so no other system can subscribe to what happens here.
- **Test coverage is not measured.** The 1,412 automated test cases are real; no tool reports what share of the code they touch, and nothing blocks a change that breaks them.
- **Looking up a sign-in's city from its network address ships switched off**, with no data file included.
- **The .NET client library in the repository should not be planned around.** It sends its gateway credential twice, which a gateway that checks that credential rejects. It has no known consumer.
- **Some settings on each registered application are stored and never applied** — its own session limit, its session timeout, and its require-email-verification flag. Only the platform-wide equivalents take effect.
- **Two audit-log filters are accepted and ignored** — filtering by action type or by success or failure changes nothing, because neither value is stored.

---

### Where to go next

| Action | Where |
|---|---|
| **Read the full feature overview** | [02_AUTH_SYSTEM_DOCUMENTATION_EN.md](02_AUTH_SYSTEM_DOCUMENTATION_EN.md) |
| **Read the architecture and implementation detail** | [03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md](03_AUTH_SYSTEM_TECHNICAL_DEEP_DIVE_EN.md) |
| **Judge your current setup** | Use the checklist below |

---

### Quick self-assessment

Every question below asks about something this platform genuinely does today. Each box you cannot tick is a gap it closes. There is no score — read the list against your own risk.

- [ ] Are your passwords hashed with a memory-hard algorithm, one that stays slow to crack even on modern graphics hardware?
- [ ] Does an account lock itself for a period after repeated wrong passwords?
- [ ] Can a user switch on an authenticator app themselves, and keep printed recovery codes for a lost phone?
- [ ] Can a person sign in once and be admitted to every one of your applications, without a second password?
- [ ] Can your users sign in with a Google or Apple account they already have?
- [ ] Can you list every active sign-in for a user and end any one of them from a screen?
- [ ] Are your sign-in and password-reset endpoints throttled, and is everything else throttled at a gateway in front of them?
- [ ] Can you grant someone a permission that expires by itself on a set date?
- [ ] Can a user delete their own account without emailing your support desk?
- [ ] Can you change the wording of a verification email, in seven languages, without a code release?
- [ ] Can you administer several separate companies or departments from one place, granting permissions inside one without granting them everywhere?
- [ ] Does every response your applications return carry the standard browser-hardening headers?

---

*Verified against the codebase on 15 August 2026, at commit `58744f9`.*
