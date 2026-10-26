# Production Deployment Guide

Deploy this authentication system to production, start to finish. Written for developers who are
new to *this* codebase (you know what a connection string, an environment variable, and a command
line are — we explain the project-specific parts).

**Read it in two passes:** **Part 1** is the few things you decide up front. **Part 2** is the
ordered install (**Phase 0 → 10**) — do it top to bottom.

Acronyms are expanded once here so nothing surprises you later. **API** application programming
interface. **SPA** single-page application — a website whose pages are drawn by JavaScript in the
browser. **IIS** Internet Information Services, the Windows web server. **JWT** JSON Web Token, the
signed token this system issues. **CORS** Cross-Origin Resource Sharing, the browser rule that
decides which websites may call the API. **CSP** Content Security Policy, a browser rule limiting
what a page may load. **HSTS** HTTP Strict Transport Security, a response header telling browsers to
use HTTPS only. **DACPAC** Data-tier Application Package, the compiled output of the database
project. **SSDT** SQL Server Data Tools, the Visual Studio component that builds and publishes it.
**SMTP** Simple Mail Transfer Protocol, how the server sends email. **YARP** Yet Another Reverse
Proxy, the library behind the API Gateway. **IdP** identity provider — this system in its role of
signing users in. **PKCE** Proof Key for Code Exchange, the browser sign-in flow the accounts
application uses. **TLS** Transport Layer Security, the encryption behind HTTPS. **RSA** and
**HMAC** are the two key types the system signs with.

Generic names used throughout — substitute your own:

| Placeholder | Meaning | Example |
|---|---|---|
| `<yourdomain>.com` | Your domain | `mycompany.com` |
| `<webspace>` | Your hosting folder | `mysite.package` |
| `<AppName>` | One of *your* apps that will use this auth | `Portal`, `Shop` |

---

# Part 1 — Understand & Decide

## 1. What you actually deploy

**Four things get their own web address, not two.** This table is the complete list of what leaves
your machine.

| Unit | What it is | Gets a domain? |
|---|---|---|
| **Auth_API** | The authentication server. Every rule, every token, every database write. | ✅ Website |
| **API_Gateway** | Public reverse proxy in front of Auth_API: rate limiting, security headers, a shared secret header. Optional but strongly recommended. | ✅ Website |
| **Console application** | The administrator's web application — users, roles, applications, notification templates, system settings, secret keys. | ✅ Website (static files) |
| **Accounts application** | The end user's web application — sign-in, profile, sessions, two-factor, organizations, privacy policy. | ✅ Website (static files) |
| **Auth_DB** | The SQL Server database: 52 tables plus seed data. | ➡️ Published to a SQL Server, not a domain |
| **Auth_Localization** | Translations for 7 display languages. Compiled *into* Auth_API. | ❌ Ships inside Auth_API |
| **Auth.Sdk** | A .NET library your *other* applications can reference to validate tokens. Read [Phase 10](#phase-10--optional-connect-other-apps-via-the-sdk) before you use it — it has a known defect. | ❌ Not deployed here |
| **Auth_Setup** | A command-line utility that prints SQL for you to run: the statement that gives the seeded administrator your address and a password (`--email`), and the owner's emergency statement that removes an account's two-step verification (`--reset-two-factor`). Never deployed. | ❌ Run locally: once per new database, and in an emergency |

*In code:* `Auth/Auth_API`, `Auth/API_Gateway`, `Auth_UI/apps/console`, `Auth_UI/apps/accounts`,
`Auth/Auth_DB`, `Auth/Auth_Localization`, `Auth/Auth.Sdk`, `Auth/Auth_Setup`.

**The two web applications are the only thing a human being ever looks at.** Nobody signs in by
sending a request to the API by hand: an administrator opens the console, an end user opens the
accounts application, and both call the API in the background. If you deploy only the API and the
Gateway you have shipped a product with no interface — including no way to reach the console pages
that several later sections of this guide tell you to open. Both are React applications built into
a folder of static files; building and uploading them is [Phase 7](#phase-7--build-and-deploy-the-two-web-applications).

**Request flow** (with the optional Gateway):

```text
Browser ──▶ Console application    (console.<yourdomain>.com)  ─┐
Browser ──▶ Accounts application   (accounts.<yourdomain>.com) ─┤
                                                                ▼
                    API Gateway   (auth.<yourdomain>.com)
                    rate limits, adds the X-Gateway-Token header
                                                                ▼
                    Auth API      (auth-api.<yourdomain>.com)
                    checks that header, runs the logic
                                                                ▼
                    SQL Server    (your database)
```

You can skip the Gateway and let the two applications call the Auth API directly. Read the warning
under Decision A first: skipping the Gateway removes almost all rate limiting.

## 2. Two decisions to make now

### Decision A — Your domains

You need **four** names. Write them down now; they go into `Jwt:Issuer`, `Jwt:Audience`,
`IdentityProvider:PublicBaseUrl`, `IdentityProvider:AccountsBaseUrl`, `Cors:AllowedOrigins`, and the
two applications' build-time settings.

| Component | Recommended subdomain | Used for |
|---|---|---|
| API Gateway (public) | `auth.<yourdomain>.com` | The address the two applications and your own apps call |
| Auth API | `auth-api.<yourdomain>.com` | Sits behind the Gateway. **You must restrict it at the firewall or in IIS — the application does not restrict it for you** ([Reference §G](#g-network-topology--what-must-and-must-not-sit-in-front-of-what)) |
| Console application | `console.<yourdomain>.com` | Where administrators sign in |
| Accounts application | `accounts.<yourdomain>.com` | Where end users sign in; also where the API's `/auth/authorize` endpoint sends people |

* **Layout A (no Gateway):** the two applications call `auth.<yourdomain>.com`, which *is* the Auth API.
* **Layout B (recommended):** the two applications call `auth.<yourdomain>.com` (Gateway), which forwards to `auth-api.<yourdomain>.com` (Auth API).

> **Layout A leaves almost every endpoint unthrottled.** The Auth API defines exactly two named
> rate-limit policies — one for login, one for password reset — and deliberately registers no general
> limiter. Everything else has no rate limit at all when the Gateway is absent. The four fixed-window
> limiters (global, auth, api, admin) live only in the Gateway. Choose Layout A only if something
> else in front of your site throttles traffic.
> *In code:* `Auth/Auth_API/appsettings.json`, section `RateLimiting` — "There is no general/default
> bucket by design"; `Auth/API_Gateway/Program.cs` registers `GlobalLimiter` and the `auth`, `api`,
> `admin` policies.

### Decision B — How secrets are stored (`SecretManagement:StorageMode`)

The system needs a few secrets: the **RSA signing key** (it signs every JWT), an **HMAC key** (it
hashes refresh tokens), a **gateway token** (the shared header value between Gateway and API), and a
**permanent account-deletion identifier key**. One setting decides how they are protected at rest.

**In production you have two choices, not three.**

| Mode | Where the keys live | Protected by | Moves to another server? | Best for |
|---|---|---|---|---|
| **`Certificate`** | Encrypted file `secrets.dpapi` | An X.509 certificate **you own** | ✅ Carry the `.pfx`, the key ring and the file | **Shared hosting**; any server that might move |
| **`Dpapi`** | Encrypted file `secrets.dpapi` | Windows DPAPI, bound to this machine and account | ❌ Breaks if the host moves your site | A Windows box you fully control |

`Certificate` is what the shipped `appsettings.json` already sets, for both the API and the Gateway.
Setup steps for both modes are in [Reference §A](#a-storage-mode-setup). Pick one now; you cannot
switch painlessly later, because switching regenerates the keys and signs everyone out.

> ### ⚠️ `PlainText` is a Development-only mode — it cannot survive a restart in Production
> A third value exists, `PlainText`, which writes the keys as readable text into an appsettings
> file. **Never use it in Production.** A startup guard refuses to run in the Production environment
> whenever `Jwt:PrivateKeyPem`, `Jwt:RefreshTokenHmacKeyPlain`,
> `AccountDeletion:IdentifierHmacKeyPlain` or `ExternalAuth:Apple:PrivateKeyPem` holds a value. In
> PlainText mode the first boot generates exactly those values and saves them, so the **second** boot
> finds them and throws `Refusing to start: plaintext secret(s) [...] were found in the Production
> configuration`. A site that came up once and never came up again is the symptom. Nothing in this
> guide's production path uses PlainText.
> *In code:* `Auth/Auth_API/Common/ProductionSecretGuard.cs`, called from `Auth/Auth_API/Program.cs`
> before the encrypted-secrets provider is layered on.

> **First startup generates the keys for you** when `SecretManagement:AutoGenerateKeys` is `true`.
> There is no key-generation command to run. Be precise about *when*: generation happens only on the
> run where the secrets file does not exist yet. On every later start the file exists, the keys are
> loaded, and nothing is re-minted even if a value looks empty. That is deliberate — re-minting would
> invalidate every issued token and desynchronise the gateway token. The one exception is the
> account-deletion identifier key, which is topped up once if it is missing, and logs a Warning when
> it does.
> *In code:* `Auth/Auth_API/Program.cs` — `if (AutoGenerateKeys && !File.Exists(SecretFilePath))`.
>
> Prefer to hold the key material yourself, so you can move servers without signing everyone out?
> See [Reference §F](#f-provision-your-own-keys-byok--painless-migration).

## 3. How config and secrets are read

ASP.NET Core builds one settings dictionary by stacking sources in order. **The last layer wins**
for any key it sets; keys it does not set keep the value from the layer below.

Lowest priority first:

1. **`appsettings.json`** — the base file. Every setting, with a default. Committed to the repository; never edit it for production.
2. **`appsettings.Production.json`** — only the settings that *change* in production. **Not in the repository — you create it** ([Phase 0](#phase-0--create-the-files-the-repository-does-not-contain)).
3. **`appsettings.Production.local.json`** — a machine-local layer, also not in the repository. You normally leave this file absent in production.
4. **Environment variables** — `ConnectionStrings__AuthDb`, `Email__Password`, `AUTH_DP_CERT_PASSWORD`, and any other setting written with `:` replaced by `__`.
5. **Database-backed settings** — the values an administrator saves in the console. Skipped entirely when the environment variable `AUTH_DISABLE_DB_SETTINGS` is `true` ([Reference §B.6](#b6-recovery--a-bad-value-saved-in-the-console)). This layer deliberately never carries the secrets in step 6; they are filtered out on both read and write.
6. **The encrypted secrets file** (`secrets.dpapi`, Certificate and Dpapi modes only) — added after the environment variables, so for the handful of secrets it holds it beats even an environment variable. The database layer in step 5 is registered after it, but because that layer filters the secret-owned keys out of both its reads and its writes, nothing saved in the console can shadow a stored secret.

*In code:* `Auth/Auth_API/Program.cs` — `AddEnvironmentLocalJsonFile` inserts layer 3, the
`DbSettingsConfigurationSource` registration adds layer 5, and `AddDpapiSecrets` adds layer 6.

`appsettings.Production.json` is intentionally small — anything you do not list is inherited from
the base file. The application uses Production settings whenever `ASPNETCORE_ENVIRONMENT=Production`
**or the variable is unset**, because Production is the .NET default.

> **Arrays merge by index, and a higher layer cannot delete an entry.** If the base file supplies
> `Cors:AllowedOrigins:0` and `Cors:AllowedOrigins:1` and your Production file supplies only one
> entry, that entry replaces index 0 and index 1 survives untouched from the base file. The base
> file ships two placeholder origins, `{{FRONTEND_ORIGIN_1}}` and `{{FRONTEND_ORIGIN_2}}`, so a
> one-entry override leaves the literal text `{{FRONTEND_ORIGIN_2}}` in your live allow-list.
> **Always list at least as many entries as the base file does.**
> *In code:* rule stated at `Auth/Auth.Application/Configuration/SettingsArrayNormalizer.cs`; base
> values at `Auth/Auth_API/appsettings.json`, section `Cors`, and `Auth/API_Gateway/appsettings.json`,
> section `Cors`.

### Three kinds of "value" in the files — don't confuse them

| You see | What it is | What to do |
|---|---|---|
| `"{{GOOGLE_CLIENT_ID}}"` | A **fill-me-in placeholder**. *No code resolves `{{ }}`.* | Replace it with the real value in `appsettings.Production.json`. If left, the literal text is used → broken. |
| `"PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD"` | The **name of an OS env var** the code reads. | Create an env var with exactly that name. |
| *(absent)* `ConnectionStrings__AuthDb` | A **generic ASP.NET Core override** — any setting, with `:` → `__`. | Create the env var on the host to keep a secret out of files. |

**Which to use:** put **non-secrets** (URLs, CORS origins, the Google client id) straight in
`appsettings.Production.json`. Keep **secrets** in `secrets.dpapi` — including the connection string
and the SMTP password, both settable from the console
([Reference §B](#b-encrypt-the-connection-string--smtp-password)). The `__` environment variables
(`ConnectionStrings__AuthDb`, `Email__Password`) remain valid and are how you bootstrap a new server
before the encrypted file holds them. The **certificate password** is the one special case wired to a
named variable (`AUTH_DP_CERT_PASSWORD`), and the one secret that cannot be encrypted — it opens the
certificate that protects everything else.

### Where each secret lives

| Secret | Where it goes (Certificate / Dpapi mode) |
|---|---|
| RSA signing key, refresh-token HMAC key, gateway token, account-deletion identifier key | Generated on first start into `secrets.dpapi`, encrypted. You never type them anywhere. |
| Connection string | **Recommended:** encrypted in `secrets.dpapi`, set from the console ([Reference §B](#b-encrypt-the-connection-string--smtp-password)). The `ConnectionStrings__AuthDb` environment variable still works and is how you bootstrap a new server before the encrypted file holds it. |
| SMTP password | **Recommended:** encrypted in `secrets.dpapi`, set from the console ([Reference §B](#b-encrypt-the-connection-string--smtp-password)). The `Email__Password` environment variable still works. |
| Certificate password | `AUTH_DP_CERT_PASSWORD` environment variable only — it cannot live inside the file it unlocks. |

> **The rule that governs everything below:** when a secret is present in `secrets.dpapi`, the
> encrypted value **wins** and the environment variable is ignored. The secrets layer is added after
> environment variables, and before the connection string is read.

---

# Part 2 — Deploy (in order)

## Prerequisites — install these first, and verify each one

Do not start Phase 0 until every row below passes. Each row has a command you can run and an answer
you should see.

| # | You need | On which machine | Check it |
|---|---|---|---|
| 1 | **.NET 10 SDK** | Build machine | `dotnet --version` → a version starting with `10.` |
| 2 | **.NET 10 Hosting Bundle** | Server | It installs the `AspNetCoreModuleV2` IIS handler both applications require. In IIS Manager, the server's **Modules** list contains `AspNetCoreModuleV2`. Installing the SDK on the server is **not** a substitute. |
| 3 | **Visual Studio with SQL Server Data Tools (SSDT)**, *or* the standalone `SqlPackage` command-line tool | Build machine | `SqlPackage /version` prints a version, or Visual Studio's installer shows "SQL Server Data Tools" as installed. Phase 1 and Phase 2 both fail without one of them. |
| 4 | **SQL Server** and a client to run queries (SQL Server Management Studio or Azure Data Studio) | Server / anywhere | You can connect and run `SELECT @@VERSION;` |
| 5 | **Node.js** and **pnpm** | Build machine | `node --version` and `pnpm --version` both print a version. The repository pins neither — it declares no `engines` field, no `packageManager` field and no `.nvmrc`. The installed toolchain's real floor is Node `^20.19.0 \|\| ^22.13.0 \|\| >=24`, and the lockfile is `lockfileVersion: '9.0'`, so install a pnpm 9-compatible release. Needed only for [Phase 7](#phase-7--build-and-deploy-the-two-web-applications). |
| 6 | **IIS URL Rewrite module** | Server | Required by both web applications' `web.config`: each one contains a `<rewrite>` section, and IIS rejects a configuration section it does not recognise — so without the module the site returns 500 on *every* request, not only on deep links. In IIS Manager, opening a site shows a **URL Rewrite** icon. |
| 7 | An **IIS application pool set to "No Managed Code"** for each of the four sites | Server | .NET 10 runs out-of-band; the pool must not load the .NET Framework CLR. |
| 8 | A **Windows host** | Server | Required for the `Dpapi` storage mode, and for the `Certificate` mode instructions as written here. |

## Phase 0 — Create the files the repository does not contain

**A clean clone has no production configuration at all.** Four kinds of file that later phases talk
about are deliberately excluded from source control, because they hold secrets or machine-specific
paths. Every step below that mentions them says *create*, never *edit* — you are writing them for the
first time.

Verify this for yourself before you start hunting for files that are not there. From the repository
root:

```bash
git ls-files | grep -iE "appsettings|web\.config|publish\.xml|pubxml"
```

**What success looks like:** exactly six lines — `Auth/API_Gateway/appsettings.json`,
`Auth/API_Gateway/appsettings.Development.json`, `Auth/Auth_API/appsettings.json`,
`Auth/Auth_API/appsettings.Development.json`, and the two web applications' `public/web.config`
files. No `appsettings.Production.json`, no `web.config` for the API or the Gateway, no publish
profile of any kind.

These are the files you will create, and the phase that creates each:

| File you must create | Where it goes | Created in | Ignored by |
|---|---|---|---|
| `appsettings.Production.json` (Auth API) | `Auth/Auth_API/` | [Phase 3](#phase-3--configure-the-auth-api) | `Auth/.gitignore` pattern `*.Production.json` |
| `appsettings.Production.json` (Gateway) | `Auth/API_Gateway/` | [Phase 5](#phase-5--optional-api-gateway) | same pattern |
| `web.config` (Auth API) | `Auth/Auth_API/` | [Phase 4](#phase-4--publish-and-run-the-auth-api) | `.gitignore` |
| `web.config` (Gateway) | `Auth/API_Gateway/` | [Phase 5](#phase-5--optional-api-gateway) | `.gitignore` |
| A Visual Studio publish profile (`.pubxml`) per .NET application | `Properties/PublishProfiles/` in each | [Phase 4](#phase-4--publish-and-run-the-auth-api) and [Phase 5](#phase-5--optional-api-gateway) | `Auth/.gitignore` pattern `*.pubxml` |
| A database publish profile (`.publish.xml`) | `Auth/Auth_DB/PublishLocations/` | [Phase 2](#phase-2--database) | `Auth/.gitignore` pattern `*.[Pp]ublish.xml` |

> **`web.config` is git-ignored precisely because it carries secrets in plain text on disk.**
> Whatever you put in it sits there as a literal `<environmentVariable>` value that anyone who can
> read the file can read: always the Data Protection certificate password, and — while you are
> bootstrapping a new server — the connection string with its SQL password and the SMTP password
> too. Treat it like a key file:
> restrict its file permissions, never attach it to a ticket, and rotate anything that has ever been
> in it if the file leaks. Browsers cannot download `.config` files from IIS (they get a 404), but
> it is still readable to anyone with file access, and it is copied into `bin\` and into the publish
> staging folder on every build.

## Phase 1 — Build the code

Start in whatever folder you keep source code in. The first command creates the repository folder
inside it, so this one command is *not* run from the repository root — nothing exists yet.

```bash
git clone <repository-url>
```

```bash
cd AuthSystem
```

**You are now at the repository root:** the folder that contains the `Auth` and `Auth_UI` folders.
Every remaining command in this guide that says "from the repository root" means this folder. If
`git clone` created a folder under a different name, `cd` into that name instead.

```bash
dotnet build "Auth/Auth_API/Auth_API.csproj" -c Release
```

**What success looks like:** a final line reading `Build succeeded` with `0 Error(s)`.

```bash
dotnet build "Auth/API_Gateway/API_Gateway.csproj" -c Release
```

**What success looks like:** the same `Build succeeded` line. Skip this second command only if you
chose Layout A and are not deploying the Gateway.

> **Do not run `dotnet build Auth/Auth.sln`.** The solution contains the database project
> `Auth_DB.sqlproj`, which is a legacy SSDT project (`ToolsVersion="4.0"`, targeting the .NET
> Framework build system). The `dotnet` command line cannot build it and stops the whole solution
> build with `error MSB4278: The imported file "…\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets"
> does not exist and appears to be part of a Visual Studio component`. Installing the .NET 10 SDK
> does not fix this — it is not an SDK problem. Build the two web projects individually as above, and
> build and publish the database separately in Phase 2, from Visual Studio or with `SqlPackage`.
> *In code:* `Auth/Auth.sln` lists `Auth_DB\Auth_DB.sqlproj`; `Auth/Auth_DB/Auth_DB.sqlproj` declares
> `ProjectVersion 4.1` and `TargetFrameworkVersion v4.7.2`.

The two web applications are built separately, with a different toolchain, in
[Phase 7](#phase-7--build-and-deploy-the-two-web-applications).

## Phase 2 — Database

The Auth API cannot start usefully without a database, so this comes before any application deploy.

### Step 1 — Create an empty database and a login

Create the database yourself, empty, and create a SQL login that has read and write rights **on that
one database only**. Do not use `sa`. Write down four things: server address, database name, login
name, password. You will paste them into a connection string in Phase 3.

**What success looks like:** connecting with that login and running `SELECT DB_NAME();` returns your
database name.

### Step 2 — Create your own database publish profile

The database project is the single source of truth for the schema, and publishing it is the only
supported way to create the schema. **There is no canonical publish profile in the repository.** Four
exist on the original author's machine and all four are excluded from source control, so a clean
clone has none. You create yours, once, and reuse it.

In Visual Studio, right-click the **Auth_DB** project → **Publish…**, click **Edit…** next to the
target connection, and point it at the database you created in step 1. Then click **Advanced…** and
set these options before saving. They are what makes a publish safe against a shared-hosting database
you do not own outright:

| Option | Set it to | Why |
|---|---|---|
| `CreateNewDatabase` | **False** | Step 1 already created the database. True would drop and recreate it. |
| `BlockOnPossibleDataLoss` | **True** | Stops the publish rather than silently dropping a column with rows in it. |
| `BackupDatabaseBeforeChanges` | **True** | Takes a backup first. |
| `DropObjectsNotInSource` | **False** | Leaves anything the hosting provider added in place. |
| `IgnorePermissions` | **True** | Your login cannot manage server-level permissions on shared hosting. |
| `IgnoreRoleMembership` | **True** | Same reason. |
| `AllowIncompatiblePlatform` | **True** | Lets the publish proceed when the server's edition differs from the project's target. |

The names in that first column are the property names the profile file stores and `SqlPackage`
accepts. The **Advanced…** dialog shows each one as a plain-English checkbox instead (for example,
the checkbox for `BlockOnPossibleDataLoss` talks about blocking a deployment that might lose data),
so match them by meaning, then confirm the saved `.publish.xml` contains the names above.

Click **Save Profile As…** and save it under `Auth/Auth_DB/PublishLocations/` with a name that says
which environment it is, for example `myproject_prod.publish.xml`. It will not be committed — the
`*.[Pp]ublish.xml` pattern in `Auth/.gitignore` excludes it.

If you do not have Visual Studio, the command-line equivalent is `SqlPackage /Action:Publish` with
the same properties passed as `/p:` arguments.

### Step 3 — Publish

Click **Publish**.

**What success looks like:** the Data Tools Operations window ends with `Update complete.` and the
messages pane shows the post-deployment `PRINT` output, including `Created admin user`.

**On every later release, publish the database before the API.** A release that adds columns needs them
in place before any API built from it starts, or the requests that read them fail. The two-factor
`TwoFactorAuth.LastUsedTimeStep` column is one: on a database without it, an API that checks authenticator
codes once (replay protection) fails every sign-in and every account recovery with an authenticator code,
every two-factor setup and enable, and every switch-off with an authenticator code (recovery codes do not
read the column) — a switch fails whole, so nothing is left half written. The `TwoFactorBindCodes`
table is another: on a database without it, an API that has it fails every hard delete of a user, the
expired-data clean-up step for that table on every run, and — while `Email:Enabled` is true — every request for the code
emailed before a first second factor and every first switch-on that carries one. Before you click
Publish on an existing database, click **Generate Script** and read it: a release that only adds columns
or tables shows `ALTER TABLE … ADD` or `CREATE TABLE` lines and nothing that drops. A `DROP COLUMN` or a `tmp_ms_xx` table rebuild
means the project you built is older than the database — stop, and rebuild from the current code.

### Step 4 — Know what the seed created

A clean publish creates **8 roles**: `super-admin`, `admin`, `user-manager`, `auditor`, `user`,
`org-owner`, `org-admin`, `org-member`.

It also seeds the whole permission catalogue — the global `*`, an area wildcard per platform area
(`users:*`, `roles:*`, …), and a row for every permission code the API enforces — and grants the
built-in roles their permissions:

| Role | Holds | So it can |
|---|---|---|
| `super-admin` | `*` | Everything. It is the only seeded role holding `secrets.manage`, `system-settings:manage` and `privacy-policy:manage`, which are withheld from `admin` on purpose. |
| `admin` | The users, roles, permissions, applications, audit-log, API-key and webhook-key wildcards, plus organizations, platform settings, notification templates and layouts, and reading the privacy policy | Run the platform day to day. |
| `user-manager` | `users:*`, `roles:read` | Manage users, and pick their roles. |
| `auditor` | `auditlogs:read`, `users:read` | Read the audit log. Not export it. |
| `user` | nothing | Use its own profile, which only needs a signed-in user. |

Wildcard grants are prefix matches: `users:*` satisfies `users:read` at any depth. A role you create
in the console can be granted any of the seeded codes.

*In code:* enforced codes come from `[RequirePermission("…")]` attributes across `Auth/Auth_API`;
seeded rows come from `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql` plus the seed files
it includes with `:r` (the platform codes and role grants are in `18_PlatformPermissions.sql`).
`Auth/Auth_API.Tests/Infrastructure/PermissionSeedCoverageTests.cs` fails if an enforced code has no
seeded row or a seeded code is enforced nowhere. Wildcard matching is at
`Auth/Auth_API/Authorization/PermissionRequirementHandler.cs`.

Every seed insert is guarded by `IF NOT EXISTS`, so publishing again adds what is missing and
overwrites nothing. **If you are upgrading a database created from a version older than commit
`8ae40fbe`**, bring it up to date first with the procedure in `Auth/Auth_DB/README.md`.

### Step 5 — Give the seeded admin your address and a password. It has neither until you do.

The publish seeds a single administrator with **no password at all**, and with a **placeholder
address** at a domain that belongs to someone else:

| Field | Value |
|---|---|
| Id | `00000000-0000-0000-0000-000000000002` |
| Email | `admin@company.com` — a placeholder; this step replaces it with yours |
| Password | none — `PasswordHash` is `NULL` |
| Role | `super-admin` |
| Can sign in before you complete this step | No |

No deployment of this system ships a password anyone could look up. The account exists and holds
`super-admin`, but `LoginCommandHandler` rejects a null hash before it reaches the password
verifier, so it cannot authenticate until you set one here.

Run `Auth_Setup` with **your own email address**, type the password you have chosen when it asks,
then execute the statement it prints against the database you just published:

```
dotnet run --project Auth/Auth_Setup -- --email <a mailbox you read>
```

What the statement does, in one `UPDATE` keyed on the seeded **Id** (never on the address):

- sets `[Email]` to your address and `[NormalizedEmail]` to the same address in capitals (the form
  the sign-in lookup compares);
- marks the address **confirmed, on your word** — so it must be a mailbox you read. The code that
  turns on two-step verification for this account, and its security notices (a new sign-in, a
  change to its second factor), will be sent there;
- sets the password hash, and `[MustChangePassword] = 0`.

It is followed by a check: if the statement changed anything but exactly one row, it stops with
*"The seeded administrator is missing or deleted; nothing was changed."* Two failures to know:

- **That message.** The seeded row is gone or soft-deleted. Nothing changed. Do not restore a deleted
  administrator to run this; ask whoever deleted it.
- **Error 2627 on `UQ_Users_NormalizedEmail`.** Another account already holds that address (deleted
  accounts included: the unique key covers every row). Nothing changed. Choose another address.

The address is proved for real at the first two-step setup: binding the account's first second
factor needs a code emailed to it (while `Email:Enabled` is on). If that code does not arrive, run
this step again with the right address.

The tool refuses, with a usage line and exit code 1: no `--email`; the old form with two plain
arguments (`"<password>" "<email>"`, whose second argument used to *select* the row and would now
*set* the address); an unknown option or one given twice; a password that starts with `--`; and the
placeholder address itself. Give no password argument and it prompts instead, which keeps the
password out of your shell history (what you type is shown on screen; nobody should watch). It opens
no database connection and changes nothing on its own — you run the statement it prints.
*In code:* `Auth/Auth_Setup/Program.cs` (parsing, prompting, printing),
`Auth/Auth_Setup/SeededAdministratorBootstrap.cs` (the statement).

> **Why this is a step and not a convenience.** `MustChangePassword` is carried in the sign-in
> response and acted on by the browser; no server path reads it. A seeded password is therefore a
> live credential until a human changes it, not a temporary one the system retires. The null hash is
> the server-side gate that the flag never was.

## Phase 3 — Configure the Auth API

**Create the file `Auth/Auth_API/appsettings.Production.json`.** It does not exist in a clean clone;
you are writing it now (see [Phase 0](#phase-0--create-the-files-the-repository-does-not-contain)).
Never put production values in the base `appsettings.json` — that file is committed.

Start from this content and replace every `<…>` placeholder with a real value:

```json
{
  "AllowedHosts": "<yourdomain>.com;*.<yourdomain>.com",

  "ConnectionStrings": {
    "AuthDb": "Data Source=<SQL_SERVER>;Initial Catalog=<DB>;User Id=<USER>;Password=<PWD>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=true"
  },

  "Jwt": {
    "Issuer": "https://auth.<yourdomain>.com",
    "Audience": "https://auth.<yourdomain>.com"
  },

  "IdentityProvider": {
    "PublicBaseUrl": "https://auth.<yourdomain>.com",
    "AccountsBaseUrl": "https://accounts.<yourdomain>.com",
    "FirstPartySpaOrigins": [ "https://console.<yourdomain>.com", "https://accounts.<yourdomain>.com" ]
  },

  "SecretManagement": {
    "StorageMode": "Certificate",
    "SecretFilePath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\secrets.dpapi",
    "AutoGenerateKeys": true,
    "EnableAdminApi": false
  },

  "DataProtection": {
    "KeyPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets",
    "Certificate": {
      "PfxPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\dp-cert.pfx",
      "PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD"
    }
  },

  "Cors": {
    "AllowedOrigins": [
      "https://console.<yourdomain>.com",
      "https://accounts.<yourdomain>.com"
    ],
    "AllowCredentials": true
  },

  "Gateway": { "ValidationEnabled": true },

  "HealthChecks": { "ExposeErrorDetails": false },

  "Email": {
    "Enabled": true,
    "SmtpHost": "<smtp-host>",
    "SmtpPort": 587,
    "UseSsl": true,
    "Username": "<smtp-username>",
    "SenderEmail": "noreply@<yourdomain>.com",
    "SenderName": "<YourProduct>",
    "FrontendBaseUrl": "https://accounts.<yourdomain>.com"
  },

  "ImageStorage": {
    "PhysicalPath": "C:\\inetpub\\vhosts\\<webspace>\\persistent\\uploads\\images",
    "PublicBaseUrl": "https://auth.<yourdomain>.com/uploads/images"
  },

  "PrivacyPolicyPublication": {
    "PhysicalPath": "C:\\inetpub\\vhosts\\<webspace>\\persistent\\privacy"
  },

  "ExternalAuth": {
    "Google": { "Enabled": false, "ClientId": "" }
  }
}
```

Line by line, what each block is for:

* **`AllowedHosts`** — the base file ships `"*"`, which accepts any `Host` header. Set it to your own
  domain so a request with a forged host cannot be used to build links pointing elsewhere.

* **`ConnectionStrings:AuthDb`** — the four values from Phase 2 step 1. This is a secret; you put it
  here to get the first boot working, then move it into the encrypted store in
  [Reference §B.1](#b1-first-deployment--moving-off-webconfig).

* **`Jwt:Issuer` and `Jwt:Audience`** — your public authentication URL, which in Layout B is the
  Gateway's address. Your own applications and the SDK must use these exact strings. Using the same
  URL for both is normal and correct.

* **`IdentityProvider:PublicBaseUrl`** — this API's own public origin. **Required whenever the API
  sits behind a reverse proxy**, which Layout B always does: without it the API reads its own
  hostname from the incoming request, which is the internal address, and publishes that internal
  address in the OpenID discovery document and in every `/auth/authorize` redirect. Set it to the
  Gateway's public address.

* **`IdentityProvider:AccountsBaseUrl`** — the accounts application's public origin. Unauthenticated
  visitors to `/auth/authorize` are redirected to its `/login` page, carrying the application's code
  and a handle to the stored request (`AuthorizationRequestHandlesEnabled`, [§N](#n-pending-sign-in-requests-behind-a-handle-identityproviderauthorizationrequesthandlesenabled)).

* **`IdentityProvider:FirstPartySpaOrigins`** — the exact origins of your console and accounts
  applications, the accounts one included, each also listed in `Cors:AllowedOrigins`. **Filling it is
  the one step that moves their refresh token into an HttpOnly cookie**: `SpaRefreshCookieEnabled`
  ships on and stays inert while this list is empty. Once filled, it is also the only kind of browser
  origin allowed to sign in with a password. **Both applications must be on the API's site** —
  subdomains of the same registrable domain as the API. Otherwise the browser never sends the
  `SameSite=Strict` cookie and every session ends at its first refresh; only a boot-log warning
  (`boot.first-party-origins`) says so. Only these two origins; never a relying party's: an origin
  listed here may sign in with a password, spend the platform's refresh cookie and confirm the
  platform's sign-out, and an application's own server-side code needs none of that.

* **`SecretManagement`** — `Certificate` or `Dpapi`; never `PlainText` (Decision B). `SecretFilePath`
  and the `DataProtection:KeyPath` below must both point **outside the publish destination** — see
  the persistent-folders block below. `EnableAdminApi` is discussed under its own warning below.

* **`Cors:AllowedOrigins`** — the browser origins allowed to call this API. In practice: your console
  application, your accounts application, and any of your own front ends. **List at least two
  entries**, because the base file ships two placeholders and a shorter list leaves the second
  placeholder in place (see the array-merge warning in Part 1 §3). **Google sign-in adds nothing to
  this list.** The Google button hands the credential to your own page's JavaScript, which then
  calls the API from your own origin, so Google never calls the API itself.
  `https://accounts.google.com` belongs in the two web applications' Content Security Policy
  instead ([Phase 7 step 3](#phase-7--build-and-deploy-the-two-web-applications)), where it is
  already listed.

* **`Cors:AllowCredentials`** — must be `true` for the browser to send the identity-provider session
  cookie between the accounts application and the API.

* **`Gateway:ValidationEnabled`** — Layout B: leave it `true`, so the API rejects anything that did
  not come through the Gateway. Layout A: set it to `false`, or the API answers **403** to every
  request.

* **`HealthChecks:ExposeErrorDetails`** — keep it `false`. `/health` and `/ready` are reachable
  without the gateway token, so error details would be public. Full errors are always in the log.

* **`Email`** — see [Phase 6](#phase-6--email-and-notifications), which covers every key, the one
  that blocks startup, and what breaks when email is off.

* **`ExternalAuth:Google`** — **the base file ships `Enabled: false`** alongside the placeholder
  client id, so a deployment that forgets this key advertises nothing rather than advertising a
  Google sign-in whose audience is the literal text `{{GOOGLE_CLIENT_ID}}`. To use Google, set a real
  `ClientId` and turn `Enabled` on — either here or, without a restart, from **System settings →
  External authentication**, since this section is console-editable and read per request. If you do enable it, the
  same client id must also be built into the accounts application
  ([Phase 7](#phase-7--build-and-deploy-the-two-web-applications)). Apple sign-in ships disabled and
  needs an Apple Developer Services ID, a verified domain, a `.p8` signing key provisioned into the
  secrets file, and the seeded `apple` row in `ExternalAuthProviders` flipped to enabled — leave it
  off unless you are doing all four.

### Three folders must live outside the publish destination

The application writes three kinds of file that must survive a redeploy, and every one of them needs
a path you set yourself. The defaults fail in two different ways, so both are worth understanding.

**Uploaded images and published privacy policies** default to a path written *relative to the
application folder*, which resolves **inside** the deployment tree. A publish that cleans the
destination folder deletes them.

**The encrypted secrets file and the Data Protection key ring** default somewhere else entirely: two
per-machine Windows profile folders. Those are outside the deployment tree, so a publish cannot
delete them — the problem there is that an IIS application pool with no loaded user profile usually
cannot write to them at all, and the application fails to start.

Give all three an explicit path in a folder outside the site root — a sibling of it, never a
subfolder of it — before the first deploy.

| What | Setting | Default (change it) | What is lost if it is wiped |
|---|---|---|---|
| Encrypted secrets file and the Data Protection key ring | `SecretManagement:SecretFilePath` and `DataProtection:KeyPath` | `%LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi` and `%ProgramData%\AuthSystem\Keys` | Every issued token, plus the gateway token — everyone is signed out |
| Uploaded images: profile pictures, organization logos, the platform logo | `ImageStorage:PhysicalPath` | `App_Data/uploads/images` | Every uploaded image |
| Published privacy-policy documents | `PrivacyPolicyPublication:PhysicalPath` | `App_Data/privacy` | Every published policy version, including the one the accounts application serves at `/privacy` |

Do this for each of the three folders: create it outside the site root, grant the IIS application
pool identity **Modify** permission on it, set the matching key in
`appsettings.Production.json`, and add it to your backup schedule.

**Why the key-ring folder in particular must be set explicitly.** ASP.NET Core creates a Data
Protection key ring at startup no matter which storage mode you chose. If `DataProtection:KeyPath` is
empty it defaults to `%ProgramData%\AuthSystem\Keys` — a machine-wide folder that a locked-down IIS
or Plesk application pool identity frequently has no write access to. The result is the startup
error `An error occurred while reading the key ring` / `Access to the path ... is denied`. Point the
**Auth API and the API Gateway at the same folder** so they share one ring.

**`PrivacyPolicyPublication:PhysicalPath` blocks startup if it is empty.** The base file supplies a
value, so this only bites if you set it to an empty string. The message names the setting.
*In code:* validated with `ValidateOnStart()` in `Auth/Auth_API/Program.cs`.

> ### The secrets admin API is OFF in the shipped configuration
> `SecretManagement:EnableAdminApi` ships **`false`**, matching the settings class and the settings
> registry, so a deployment that never mentions the key runs with the secret-management endpoints
> refused. (It used to ship `true`, which meant the opposite: forgetting the key left them live.)
> Turn it on only for the minutes you are provisioning or rotating keys, then turn it off. The
> console pages under **System Settings → Secret management → Manage secrets** need it `true`, so
> expect to flip it twice during setup. **The console cannot flip it for you** — the field is
> read-only in the settings registry, so this is an edit to `appsettings.Production.json`,
> deliberately. **No restart is needed:** the switch is read on every request, and the file is
> watched, so the next request after you save sees the new value. Two rules make that true:
> 1. **Set it in `appsettings.Production.json` only.** An environment variable
>    (`SecretManagement__EnableAdminApi`), a `web.config` entry, a command-line value or an
>    `appsettings.Production.local.json` would shadow the file: a later edit of the file would then
>    change nothing, and a switch you turned off in the file could stay on.
> 2. **Validate the JSON before you save, and keep a copy of the last good file.** A save that does
>    not parse empties **every** value of `appsettings.Production.json`, not only this switch, for
>    every setting the API reads per request (token lifetimes, sign-in and email settings among
>    them): until a valid file is saved, those settings fall back to `appsettings.json`. The switch
>    closes, which is the safe direction, but much else changes with it. If the status check below
>    answers anything you did not expect, put the last good copy back at once.
>
> Check the effective state after every flip: `GET …/api/v1/admin/Secrets/status` with an administrator's
> token answers **200** while it is on and **403 `Secret.AdminApiDisabled`** while it is off.
> Development is unaffected: `appsettings.Development.json` sets it `true` for itself.
> *In code:* `Auth/Auth.Application/Configuration/SecretManagementSettings.cs` (class default);
> `Auth/Auth_API/appsettings.json`, section `SecretManagement` (shipped value); enforcement at
> `Auth/Auth_API/Modules/Administration/Filters/RequireAdminApiEnabledAttribute.cs`, which reads
> `IOptionsSnapshot` per request and answers 403 `Admin API Disabled`.

> ### CORS: what the startup guard does and does not catch
> The application refuses to start when `Cors:AllowedOrigins` is an **empty array** outside
> Development. That is the only case it catches. Anything else starts normally — including the value
> `["*"]`, which outside Development produces a policy with **no origins configured at all** and
> therefore silently denies every cross-origin request. And because the committed base file already
> contains two non-empty placeholder entries, the guard **cannot fire** on a deployment that simply
> forgets to configure CORS. Check the list yourself; the guard will not do it for you.
> *In code:* the fail-fast is in `Auth/Auth_API/Program.cs`; the `*` behaviour is in
> `Auth/Auth_API/Common/DynamicCorsPolicyProvider.cs`.

Two password-hardening features also belong in this file, under a `Password` section: a
server-side pepper mixed into every password hash (off by default), and a check of new passwords
against known breached passwords (**on** by default, and it needs outbound HTTPS to
`api.pwnedpasswords.com`). They have their own appendix —
[Reference §J](#j-password-protection--pepper-and-breached-password-check). Read it before you go
live, because enabling the pepper is close to a one-way decision.

## Phase 4 — Publish and run the Auth API

### Step 1 — Create `web.config` (this is the only section that owns it)

**A clean clone has no `web.config` for the Auth API.** You create it once, as a file inside the
project folder, and from then on every publish carries it along. Create
`Auth/Auth_API/web.config` with this content:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" arguments=".\Auth_API.dll" hostingModel="inprocess"
                  stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
          <environmentVariable name="AUTH_DP_CERT_PASSWORD" value="<the password for dp-cert.pfx>" />
          <!-- Optional bootstrap values, removed again in Reference §B.1 once the
               encrypted store holds them:
          <environmentVariable name="ConnectionStrings__AuthDb" value="…" />
          <environmentVariable name="Email__Password" value="…" />
          -->
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

Three things about this file, all of which bite people:

1. **It must be a file in the project, not something you edit on the server.** A publish rewrites
   only `processPath` and `arguments` and keeps everything else you wrote, including every
   `<environmentVariable>`. If instead you hand-edit the *deployed* copy, the next publish overwrites
   it and the application silently loses its environment variables.

2. **`AUTH_DP_CERT_PASSWORD` is per application, and the Gateway needs its own copy.** Both
   applications open the *same* certificate file, so both need the *same* password — but each
   process reads it from its own environment, which means either the `<environmentVariable>` line in
   its own `web.config` or the hosting panel's environment-variable page for that site
   ([Reference §C](#c-environment-variables-on-plesk-and-cpanel)). Setting it for the API does
   nothing for the Gateway. Forgetting it on the Gateway side is the classic cause of a
   Gateway-only `HTTP 500.30` while the API is perfectly healthy. This is the only place the rule is
   stated; everywhere else points here.

3. **Once `PasswordEnvironmentVariable` is set, the inline `Password` field is dead.** The shipped
   `appsettings.json` already sets `PasswordEnvironmentVariable` to `AUTH_DP_CERT_PASSWORD`, and the
   code reads only that variable with no fallback. A missing or misspelled variable makes the
   password resolve to null and startup fails with *"Failed to load the Data Protection certificate
   … the password is correct."* Fix the variable, or clear `PasswordEnvironmentVariable` if you
   really want to use the inline `Password`.
   *In code:* `Auth/Auth.Shared/Configuration/DataProtectionCertificateSettings.cs`, `ResolvePassword`.

### Step 2 — Publish

Run this from the repository root:

```bash
dotnet publish "Auth/Auth_API/Auth_API.csproj" -c Release -o ./publish/auth-api
```

**What success looks like:** the command ends with a line naming the output folder, and
`./publish/auth-api` now contains `Auth_API.dll`, the `appsettings*.json` files, a `web.config`
carrying your environment variables, and language subfolders such as `ar/` and `tr/` holding the
translations.

If you prefer Visual Studio's **Publish** dialog, create your own profile now — the repository
contains none, and `.pubxml` files are git-ignored. Whichever method you use, note the checkbox
labelled **"Remove extra files in destination"**: read
[Reference §E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys) before you tick it.

### Step 3 — Deploy

* **Shared hosting (Plesk / IIS):** create the site or subdomain, set its application pool to **No
  Managed Code**, and upload everything from `./publish/auth-api`. **It starts itself** — the
  ASP.NET Core Module launches the process on the first request. There is no command to run and no
  start button.
* **A server you control:** run `dotnet Auth_API.dll` with `ASPNETCORE_ENVIRONMENT=Production`,
  behind IIS or as a Windows Service so you get HTTPS and automatic restarts.

Either way, read [Reference §H](#h-iis-hosting-and-application-pool-settings) before you finish — the
default application-pool settings stop background work such as email delivery whenever the site is
idle.

### Step 4 — Check the first run

**On first run the application generates its secret keys.** Open the log and look for
`First startup detected - auto-generating cryptographic keys...`, followed by `Generated keys:` and
the JWT public key (which is safe to share — it is the public half). A permission or path error here
means the secrets folder is not writable by the application pool identity; fix the permission and
restart. Where to find the log is [Reference §I](#i-logs--where-they-are-and-how-to-find-them).

> ### ⚠️ CAUTION — do not let the second publish wipe your keys
> The keys are generated **on the server, on first run**, and live in the `secrets` folder — not in
> your repository. A careless re-publish can destroy them, which invalidates every token, signs out
> every user, and desynchronises the gateway token. The durable fix is structural rather than a
> checkbox you must remember for years: **keep the secrets folder outside the publish destination**,
> as a sibling of the site root rather than under it (Phase 3, persistent folders). Then Visual
> Studio's *"Remove extra files in destination"* can stay on safely, because it cannot reach the
> secrets. Also set `SecretManagement:AutoGenerateKeys` back to **`false`** after the first
> successful run, so that if the secrets ever do go missing the application **fails loudly instead
> of silently minting new keys**. Full procedure, and the fallback for when the secrets folder must
> live inside the deploy folder:
> [Reference §E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys).

## Phase 5 — (Optional) API Gateway

Skip this whole phase if you chose Layout A — and if you do skip it, set
`Gateway:ValidationEnabled: false` in the API's `appsettings.Production.json`, or the API answers
403 to everything.

> ### One secret, two names — read this before anything else
> The Gateway stamps a header called `X-Gateway-Token` on every request it forwards. The API
> compares that header against its own expected value and answers **403** on any mismatch. The two
> processes call the same secret by different names: the Gateway reads `Gateway:Token`, the API
> reads `Gateway:ExpectedToken`, and in Certificate or Dpapi mode both are filled automatically from
> the single `GatewayToken` entry in the shared `secrets.dpapi`. **If those two values ever differ,
> 100% of proxied traffic returns 403 while both applications report themselves healthy** — there is
> no health check that notices. Sharing one secrets file is what prevents it.
> *In code:* mapping at `Auth/Auth.Shared/Configuration/SecretConfigurationExtensions.cs`;
> comparison at `Auth/Auth_API/Common/Middleware/GatewayTokenValidationMiddleware.cs`.

### Step 1 — Create `Auth/API_Gateway/appsettings.Production.json`

This file does not exist in a clean clone either. Create it with this content:

```json
{
  "AllowedHosts": "<yourdomain>.com;*.<yourdomain>.com",

  "SecretManagement": {
    "StorageMode": "Certificate",
    "SecretFilePath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\secrets.dpapi"
  },

  "DataProtection": {
    "KeyPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets",
    "Certificate": {
      "PfxPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\dp-cert.pfx",
      "PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD"
    }
  },

  "ReverseProxy": {
    "Clusters": {
      "auth-cluster": {
        "Destinations": {
          "auth-api": { "Address": "https://auth-api.<yourdomain>.com" }
        }
      }
    }
  },

  "Services": {
    "AuthApi": {
      "BaseUrl": "https://auth-api.<yourdomain>.com",
      "ReadyUrl": ""
    }
  },

  "Cors": {
    "AllowedOrigins": [
      "https://console.<yourdomain>.com",
      "https://accounts.<yourdomain>.com"
    ]
  },

  "HealthChecks": { "ExposeErrorDetails": false }
}
```

**The three path settings must name the exact same folder and file the API uses.** Do not copy the
secrets folder into each site. On IIS and Plesk the application pool identity can read across sibling
subdomain folders, so point both applications at one folder — the API's. One source of truth means a
key-ring rotation can never desynchronise them. Grant the API **Modify** on that folder (it writes
and rotates keys) and the Gateway **Read**.

`Services:AuthApi:BaseUrl` does two jobs, not one, and both break quietly if it is wrong:

1. **Readiness.** The Gateway's `/ready` calls `{BaseUrl}/ready`. Leave `ReadyUrl` empty so it is
   derived; set it only if `/ready` genuinely lives on a different host. The readiness probe by
   itself survives a leftover `{{…}}` placeholder: a value that is not an absolute `http(s)` URL is
   ignored, a warning is logged, and the probe falls back to `http://localhost:5100/ready` — so the
   probe fails instead of the process. **That tolerance does not extend to the settings pull
   below.** It builds its address from `Services:AuthApi:BaseUrl` with no such check, so a
   placeholder left in that key throws a `UriFormatException` while the Gateway is starting, which
   IIS shows as an opaque `HTTP 500.30`. Put a real URL in `BaseUrl`.

2. **The settings pull.** Every 30 seconds the Gateway calls
   `GET {BaseUrl}/api/v1/internal/gateway-settings`, authenticating with the same `X-Gateway-Token`
   header, and replaces its own in-memory copy of four things: the allowed CORS origins, whether
   credentials are allowed, whether health-check error details are exposed, and the four rate limits.
   That is how an administrator changing a rate limit in the console reaches the Gateway. When the
   API is unreachable the Gateway keeps the previous values and logs **once per outage**, not once
   per attempt. **If `Gateway:Token` is empty the poller exits immediately and the Gateway stays on
   its own file-based values forever**, silently.
   *In code:* `Auth/API_Gateway/Configuration/GatewayRuntimeSettingsPoller.cs`.

There is also a `Services:AuthApi:HealthUrl` key in the shipped base file. **No code reads it.**
Ignore it; do not spend time setting it.

### Step 2 — Create the Gateway's own `web.config`

Copy the file from [Phase 4 step 1](#phase-4--publish-and-run-the-auth-api) to
`Auth/API_Gateway/web.config`, changing `arguments` to `.\API_Gateway.dll`. It needs its **own**
`AUTH_DP_CERT_PASSWORD` with the same value as the API's — that rule and its failure mode are
explained in Phase 4 and not repeated here.

### Step 3 — Publish and deploy

```bash
dotnet publish "Auth/API_Gateway/API_Gateway.csproj" -c Release -o ./publish/gateway
```

**What success looks like:** the command names the output folder and `./publish/gateway` contains
`API_Gateway.dll` and your `web.config`.

Deploy it as its own subdomain, `auth.<yourdomain>.com`, exactly as you deployed the API. **The same
first-publish versus every-publish-after rules apply** —
[Reference §E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys).

Before you go live, read [Reference §G](#g-network-topology--what-must-and-must-not-sit-in-front-of-what).
The Gateway assumes it is the outermost hop, and putting a content delivery network or another
reverse proxy in front of it collapses every client into a single rate-limit bucket without any
warning.

## Phase 6 — Email and notifications

**Email is not optional in practice.** Password reset, email verification, organization invitations,
ownership-transfer codes, new-device alerts, account-deletion confirmation codes, and the six-digit
codes that authorize secret-key operations are all delivered by email and by nothing else. Turning
email off does not disable those features — it makes them fail invisibly, which is worse.

**Email can also be the way in.** Every visitor may sign in with a code emailed to the address, and with
*Password at sign-up* off (System settings → Who may create an account) new accounts have no password at
all. Mail delay then becomes sign-in delay, and a mail outage keeps every account without a password out
unless a provider is linked to it. Watch delivery for a while before you switch that setting off. It cannot
be saved off while email sending is off, and switching email off asks first, with the number of accounts
that sign in only by code.

**Upgrading to the release with sign-in by emailed code** (OI-215), for each existing deployment, in this
order: publish the database package first (three notification types and their templates, and two columns
appended to `PendingRegistrations` with named defaults — `WrongCodeCount` and `WrongCodeWindowStartUtc`, the
per-address wrong-code ceiling; read the generated script first, it must add them in place, never rebuild the
table); then the API, which reads those columns in every start and every code check and answers 500 without
them; then the
accounts and console applications, with a short gap — an older
application on the new API keeps its screens, and a newer application is never deployed before its API.
Check on a test account that the sign-in page offers the code, that one message arrives, and that a platform
administrator with a password signing in by code is asked for the password. A platform administrator without
a password signs in with the mailbox and the authenticator app only, and without the app the mailbox alone
opens it. Four ways lead there: authority granted to an account with no password and no app; the first code
confirmation of an administrator-created account without the app; the owner removing the password (refused
until the app is bound) and then switching the app off, which is refused only while
`TwoFactor:EnforceForPlatformAdmins` is on; and another administrator resetting the account's app. Wherever
such administrators exist, keep `TwoFactor:EnforceForPlatformAdmins` on, give every such account the app
before the authority, and confirm who is asking, through a channel other than that mailbox, before resetting
such an administrator's app. API clients that call `registration/start` with email sending off now get
`403 User.EmailCodeSignInUnavailable` instead of 200. *Password at sign-up* and *Name at sign-up*
arrive on; turning the first off is a per-deployment choice, not part of the upgrade. To undo: turn
*Password at sign-up* back on before redeploying the previous API and applications; the seed rows and the two
columns stay, unused by the older build. On the older build, a session that holds the new sign-in bit reads as an emailed code only, so an
account without a password cannot manage its authenticator app until the new build returns, and signs in
through "forgot password" (which sets a first password) or a provider.

### What happens when `Email:Enabled` is false

**The send path reports success and delivers nothing.** It writes a single log line at Information
level — `Email sending disabled. Would have sent to {Email} …` — and returns success to whatever
asked for the message. With the delivery queue on (the default), the queued row is even marked
**Sent**. Under a production log level of Warning that Information line is not written at all, so
there is no trace anywhere.

Nothing in the system will tell you. A user who asks for a password reset sees a normal success
screen. An administrator who tries to rotate a signing key waits for a code that never arrives.
The sign-in and sign-up codes are the exception: both starts (`email-code/start` and
`registration/start`) refuse every address with `403 User.EmailCodeSignInUnavailable`, and the steps
that take a code refuse too, so no such code is minted while nothing can deliver it.
*In code:* `Auth/Auth.Infrastructure/Notifications/Channels/EmailNotificationChannel.cs`.

### The `Email` settings

All of these live under `"Email"` in `appsettings.Production.json`.

| Key | Default in the base file | What it does |
|---|---|---|
| `Enabled` | `true` | Master switch. On by default, so a deployment without mail fails visibly instead of silently: while it is on, `FrontendBaseUrl` must be an absolute URL or the API does not start. See the warning above. |
| `SmtpHost` | placeholder | Your mail server's hostname. |
| `SmtpPort` | `587` | See the port rule below. |
| `UseSsl` | `true` | On any port other than 465, this chooses STARTTLS. |
| `Username` | placeholder | Leave empty for a server that needs no authentication. |
| `Password` | the literal text `Email__Password` — a reminder of the environment variable's name, not a password | **Never put a real password here.** Use the `Email__Password` environment variable to bootstrap, then move it into the encrypted store ([Reference §B](#b-encrypt-the-connection-string--smtp-password)). |
| `SenderEmail` | placeholder | The `From` address. |
| `SenderName` | `Auth System` | The display name beside it. |
| `FrontendBaseUrl` | placeholder | **Blocks startup — see below.** |
| `OtpExpirationMinutes` | `5` | Lifetime of a one-time code. |
| `ResetTokenExpirationMinutes` | `30` | Lifetime of a password-reset link. |
| `RateLimitWindowSeconds` | `60` | Window for the per-user code request limit. |
| `MaxOtpRequestsPerWindow` | `3` | How many codes one user may request per window. |
| `MaxWrongCodesPerDay` | `20` | How many wrong sign-up or sign-in codes one address may enter in 24 hours, across every code and step; reached, its codes are refused until the window passes (the password, a provider and "forgot password" stay open). |

**`Email:FrontendBaseUrl` refuses to start the application when it is not an absolute URL — empty,
relative, or the base file's `{{FRONTEND_BASE_URL}}` placeholder — and `Email:Enabled` is `true`,
which it is by default.** The message is exactly `Email:FrontendBaseUrl must be an absolute URL when Email:Enabled is
true.` Set it to your accounts application's public origin, `https://accounts.<yourdomain>.com` —
every reset link and verification link in every email is built from it, and a relative value would
produce a dead link in every message.
*In code:* validated with `ValidateOnStart()` in `Auth/Auth_API/Program.cs`.

**Port 465 means implicit TLS; every other port means STARTTLS.** If your provider gives you port
465, set `SmtpPort: 465` and the client connects with TLS from the first byte. On 587 or 25 it
connects in the clear and upgrades. Getting this backwards produces a connection that hangs and then
times out.
*In code:* `Auth/Auth.Infrastructure/Notifications/Channels/SmtpEmailSender.cs`.

### Where the message content comes from

**Every email's subject and body live in the database, not in the code and not in resource files.**
The database publish in Phase 2 seeds **16 notification types**, **15 templates** — each with all
**7 languages**, so **105 translation rows** — and **1 shared layout** that wraps them.

One seeded type, `welcome-email`, has **no template**. That is deliberate: it is the only seeded type
not marked as a system type, and no code ever sends it. Do not go looking for the missing template.

Administrators edit these from the console at **Notifications → Templates**, with a draft/publish
model: edits accumulate in a draft, publishing moves a pointer, and rolling back moves it again. No
content is ever deleted by publishing.

**At startup the application checks that every system type has a published global email template**
and, if any is missing, logs at Error:

```text
Notification template seed is incomplete: no published global Email template for system type(s) {TypeCodes}.
Critical auth emails WILL FAIL until the templates are published
(re-run the Auth_DB post-deployment seed or publish from the console).
```

**This check never blocks startup.** The site comes up and the affected emails fail one by one. If
you see that line, the fix is to re-run the database publish or to publish the named templates from
the console.
*In code:* `Auth/Auth.Infrastructure/Notifications/NotificationTemplateStartupCheck.cs`.

### How delivery actually works — the outbox

By default, sending an email does not talk to your mail server during the user's request. The
finished message — already rendered, with the subject and both bodies baked in — is written as one
row in the `NotificationOutbox` table, and a background service delivers it. This is why a slow mail
server never slows down a sign-in, and why a later template edit never changes a message already
queued.

The background service wakes on an in-process signal the moment a row is written, and otherwise on a
timer. Both triggers exist because under IIS an idle application pool cannot be relied on to keep a
long-running timer alive.

| Setting (`Notifications` section) | Default | What it controls |
|---|---|---|
| `UseOutbox` | `true` | `false` sends inside the request instead, and a delivery failure then fails the user's operation |
| `PollIntervalSeconds` | `30` | Fallback wake-up when no signal arrives |
| `BatchSize` | `20` | How many messages are claimed per cycle |
| `MaxAttempts` | `5` | Attempts before a message is given up on |
| `StaleClaimMinutes` | `5` | How long a claimed-but-unfinished message waits before another worker reclaims it |
| `NewDeviceAlertEnabled` | `true` | Whether signing in from a new device emails the account owner |
| `NewDeviceAlertMinIntervalMinutes` | `60` | Shortest gap between two alerts for the same device |

**Retry schedule.** A failed attempt is retried after 1 minute, then 4, then 16, then 64, then 256 —
each wait is four times the last, capped there.

**Dead-lettering.** On the attempt that reaches `MaxAttempts`, the row's status is set to Dead and it
is never retried again. The log line is at Error level:
`Outbox message {MessageId} ({TypeCode}) dead-lettered after {Attempts} attempts: {Error}`. Earlier
failures log at Warning. An administrator can requeue a Retry or Dead row from the console at
**Notifications → Delivery Log** — the console's name for the outbox table; only those two statuses
qualify.

**One trap worth knowing:** a malformed or non-existent recipient address is caught by the same
handler as a network failure, so it is retried on the full schedule and only then dead-lettered. A
permanent fault consumes the entire retry budget.

**Delivered rows are purged** by the account-deletion sweep after `AccountDeletion:OutboxRetentionDays`
days, default 180. Messages whose content carries a live secret — verification codes, reset links,
invitations, transfer codes, deletion codes, secret-operation codes — have their bodies overwritten
with `[redacted]` the moment delivery succeeds, so the delivery log can never be used to re-read
someone's one-time code.

### When mail stops arriving — check these in order

1. **Is email switched on at all?** Look at the effective `Email:Enabled`. Remember it is also a hot
   setting an administrator can flip in the console, so the file is not the last word — check
   **System settings → Communication** too.
2. **Open the console at Notifications → Delivery Log.** That page shows the outbox table, one row
   per message, and it answers the next question immediately.
   * Rows with status **Sent** and no error: the system believes it delivered. Either email is
     disabled (step 1) or the problem is downstream at your mail provider.
   * Rows with status **Retry** or **Dead**: read the `LastError` column. It carries the SMTP
     failure verbatim.
   * **No rows at all** for the message you expected: the failure happened *before* queueing —
     almost always a missing template. Go to step 4.
3. **If `LastError` names authentication or TLS**, re-check `SmtpHost`, `SmtpPort` (the 465 rule
   above), `UseSsl`, `Username`, and the SMTP password. A username set with no password is treated
   as a configuration fault rather than a transient one: it is logged at Error naming `Email:Password`
   and no send is attempted.
4. **Search the log for `no published global Email template`.** That is the startup check reporting
   missing content. Republish the database or publish the template from the console.
5. **Search the log for `Reclaimed`** — `Reclaimed {Count} orphaned Processing outbox message(s) from
   a previous worker.` at Warning means the application pool is being recycled mid-delivery. Fix the
   pool settings in [Reference §H](#h-iis-hosting-and-application-pool-settings).
6. **Confirm the server can reach your mail host at all.** Shared hosting frequently blocks outbound
   SMTP ports. Test from the server itself, not from your laptop.

## Phase 7 — Build and deploy the two web applications

This is the phase that turns an API into a product. Both applications are React single-page
applications: you build each one into a folder of plain static files and upload that folder to an IIS
site. There is no runtime, no application pool process, and no server-side code involved.

> ### Nothing in this repository deploys these folders for you
> There is no publish profile, no script, and no continuous-integration workflow that targets
> `Auth_UI/apps/console/dist` or `Auth_UI/apps/accounts/dist`. The `.github/workflows/` folder exists
> and is empty. **Uploading the two folders is a manual step, every time.** If you want it automated,
> that is a decision for whoever owns this codebase — the repository expresses no opinion.

### Step 1 — Install the workspace dependencies

The frontend is one pnpm workspace containing the two applications and five shared packages. Install
everything once, from the `Auth_UI` folder:

```bash
cd Auth_UI
```

```bash
pnpm install
```

**What success looks like:** pnpm prints a dependency summary and ends without an error. A
`node_modules` folder now exists at `Auth_UI/node_modules` and inside each application and package.

### Step 2 — Set the build-time configuration for each application

**These settings are baked into the JavaScript at build time.** They are not read from a file on the
server, so you cannot change them after the fact by editing something in IIS — you rebuild.

Two files hold them, and **both are committed to the repository carrying a placeholder origin**,
`https://auth.example.com` / `https://accounts.example.com`. `example.com` is reserved for
documentation (RFC 2606) and resolves nowhere, so a build you forgot to edit fails on its first
request rather than quietly reaching somebody else's server. Nothing validates these values — the
fallback in `packages/api/src/env.ts` fires only when a key is *absent*, never when it is wrong.

**Edit `Auth_UI/apps/console/.env.production`:**

```ini
VITE_API_BASE_URL=https://auth.<yourdomain>.com
VITE_ACCOUNTS_URL=https://accounts.<yourdomain>.com
```

**Edit `Auth_UI/apps/accounts/.env.production`:**

```ini
VITE_API_BASE_URL=https://auth.<yourdomain>.com
VITE_ACCOUNTS_URL=https://accounts.<yourdomain>.com
VITE_GOOGLE_CLIENT_ID=
VITE_APPLE_SERVICES_ID=
```

| Variable | Set it to | Notes |
|---|---|---|
| `VITE_API_BASE_URL` | The address the browser calls — the **Gateway's** in Layout B, the API's in Layout A | Must match the CSP `connect-src` in step 3 and appear in the API's `Cors:AllowedOrigins` |
| `VITE_ACCOUNTS_URL` | The accounts application's own public origin | The console redirects password resets here; the accounts application keeps its privacy links on this origin |
| `VITE_GOOGLE_CLIENT_ID` | Your Google client id, or leave empty | Must be the **same** value as the API's `ExternalAuth:Google:ClientId`. Leave both empty and disabled if you are not using Google sign-in. |
| `VITE_APPLE_SERVICES_ID` | Your Apple Services ID, or leave empty | Only if you completed the Apple prerequisites in Phase 3 |

### Step 3 — Update the Content Security Policy in both `web.config` files

Each application ships an IIS configuration file that is copied into the build output. Unlike the
API's `web.config`, **these two are committed to the repository** — so, like the environment files
above, they carry the placeholder origin rather than a working one.

Open `Auth_UI/apps/console/public/web.config` and
`Auth_UI/apps/accounts/public/web.config`, find the `Content-Security-Policy` header in each, and
replace every occurrence of `https://auth.example.com` with your own API origin — the same value you
set for `VITE_API_BASE_URL` in step 2. The two directives that matter:

* **`connect-src`** — must list the origin in `VITE_API_BASE_URL`, or the browser blocks every API
  call and the application shows a blank screen with console errors.
* **`img-src`** — must list the same origin, because uploaded avatars and logos are served from the
  API's `/uploads/images` path.

The accounts application's policy additionally names three provider hosts: `https://accounts.google.com`
for Google, and `https://appleid.cdn-apple.com` (the script) plus `https://appleid.apple.com` (the
request it makes) for Apple. Leave them alone if you use those sign-in providers; remove all three
if you use neither, and remove the Apple pair if you use only Google.

Do not change the rest of either file. Between them they carry the single-page-application fallback
rule, the caching rules, and the security headers, each with a comment explaining why it is written
the way it is.

**Inline styles and the second CSP header.** Neither policy is meant to allow inline styles
(`'unsafe-inline'` in `style-src`): with it, anyone who manages to inject markup into a page can
restyle it — hide a warning, cover a button. The change ships in two stages:

* **Stage A:** the enforced `Content-Security-Policy` is unchanged, and a second header,
  `Content-Security-Policy-Report-Only`, carries the target policy. A browser reports — in the
  developer console — every inline style the target would refuse, and blocks nothing.
* **Stage B,** a later release: the target becomes the enforced policy and the Report-Only header goes.
  Before you deploy it, confirm your own deployment has shown zero such reports in Chrome and Firefox.

The `'sha256-…'` values in `style-src` are **generated, never typed by hand**. They are the hashes of
the few constant `<style>` texts the libraries inject (`csp/inline-styles.json` in each application,
written by `pnpm csp:collect`) and, for the accounts site, of every stylesheet the privacy-policy
renderer has published (`apps/accounts/csp/privacy-style-hashes.json`; `/privacy` is served under
that site's policy). The unit tests fail until `web.config` carries exactly those values, and the
build's seal step refuses a bundle that lacks one of the texts or creates a `<style>` somewhere new.
Change them only by re-running `pnpm csp:collect` after a dependency upgrade.

The console's file has one more policy, in `<location path="email-preview.html">`: the
notification preview's own document, framed by the console with an opaque origin. It must be
uploaded with the rest of `dist`; without it the preview shows an alert pointing to the Text tab.
The seal step recomputes the hash of its script from the built file and refuses a mismatch.

Before stage B, run this read-only query on the production database and confirm every value is
listed in `Auth_UI/apps/accounts/csp/privacy-style-hashes.json` (the file stores them with a
`sha256-` prefix):

```sql
SELECT DISTINCT [StyleHash] FROM [dbo].[PrivacyPolicyArtifacts];
```

Read a missing value before appending it. `gFmvl/t0qhw7O9MeRPLmybvcVXJqB1XQJl9O0cs4kJk=` is the
listed stylesheet hashed by an API built from a checkout with Windows (CRLF) line endings: a browser
never computes it - the HTML parser folds the line endings first, so every copy of that document
hashes to the listed value - and appending it would add nothing. Any **other** missing value is a
stylesheet this list does not know: append it. (Documents stored with the CRLF value are also served
unstyled by the API's own privacy endpoint, which names the stored hash in its policy; new
publications no longer produce it.)

Upload in this order so no request meets a new header with old files: the `assets` folder first,
then `email-preview.html` and `index.html`, then `web.config` last. Keep the previous `dist` of each
application; re-uploading it is the rollback.

### Step 4 — Build

From the `Auth_UI` folder:

```bash
pnpm -r build
```

That runs `tsc -b && vite build` in each application: a TypeScript compile that fails the build on a
type error, then the bundler.

**What success looks like:** two Vite summaries, one per application, each ending with a `built in …`
line, and two new folders on disk:

* `Auth_UI/apps/console/dist/`
* `Auth_UI/apps/accounts/dist/`

Each `dist` folder contains `index.html`, an `assets/` folder of fingerprinted JavaScript and CSS
files, the fonts and icons, **and a copy of `web.config`** — Vite copies everything in `public/`
into the build output, which is how the IIS configuration you edited in step 3 ends up beside the
files it configures. Confirm it is there before you upload; a missing `web.config` means every deep
link returns 404.

### Step 5 — Upload

Create two IIS sites and copy the **contents** of each `dist` folder into the corresponding site
root:

| Copy from | To the site | Serving |
|---|---|---|
| `Auth_UI/apps/console/dist/*` | `console.<yourdomain>.com` | The administrator application |
| `Auth_UI/apps/accounts/dist/*` | `accounts.<yourdomain>.com` | The end-user application |

Both sites need the **IIS URL Rewrite module** installed (prerequisite 6). Without it IIS rejects the
`<rewrite>` section in `web.config` and returns a 500 on every request.

**The accounts site needs one extra thing: a virtual directory named `privacy`**, mapped to the
folder you set as `PrivacyPolicyPublication:PhysicalPath` in Phase 3. Published privacy-policy
documents are written there by the API and served from there by this site, through the rewrite rules
already in its `web.config`. Without the virtual directory, `/privacy` returns 404 and the policy
links in the application and in emails all break.

### Step 6 — Let the API accept them

The browser will refuse to let either application call the API unless the API says it may. Go back to
`Auth/Auth_API/appsettings.Production.json` and confirm:

```json
"Cors": {
  "AllowedOrigins": [
    "https://console.<yourdomain>.com",
    "https://accounts.<yourdomain>.com"
  ],
  "AllowCredentials": true
}
```

* Both origins must be listed, exactly, with scheme and no trailing slash.
* `AllowCredentials` must be `true` — the accounts application relies on the identity-provider
  session cookie travelling with the request.
* Google sign-in adds no entry here. Only pages served from your own two origins call the API, so
  `https://accounts.google.com` belongs in the Content Security Policy of step 3, not in this list.
* Remember the array-merge rule from Part 1 §3: list at least as many entries as the base file's two,
  or the second placeholder survives into your live allow-list.
* In Layout B the Gateway also has a `Cors:AllowedOrigins` list, used until its first successful
  settings pull from the API. Set it to the same values so the two never disagree during a restart.

**What success looks like:** open `https://console.<yourdomain>.com` in a browser and sign in with
the address and the password you gave `Auth_Setup` in [Phase 2 Step 5](#step-5--give-the-seeded-admin-your-address-and-a-password-it-has-neither-until-you-do).
If you have not done that step yet, the sign-in is refused because the account has no password. A
blank page with `blocked by CORS policy` in the browser console means this step is wrong; a blank
page with a Content Security Policy error means step 3 is wrong.

> **A known gap, so it does not surprise you.** The console's `/organizations` route requires only
> that you are signed in — unlike `/users`, `/roles` and `/api-keys`, it carries no permission guard.
> Any authenticated administrator can open the organizations pages regardless of their permissions.
> The API still enforces its own permission checks on every call those pages make, so this exposes
> navigation rather than data, but it is a real inconsistency worth reporting to whoever owns the
> codebase.
> *In code:* `Auth_UI/apps/console/src/routes.tsx`.

## Phase 8 — Verify and sign in

Use your **public** address — the Gateway's in Layout B, the API's in Layout A. Run these from any
machine that can reach it.

```bash
curl https://auth.<yourdomain>.com/health
```

**What success looks like:** HTTP 200 and a JSON body whose `status` is `Healthy`. In Layout B this
proves the Gateway process is running and nothing more — it makes no call to the API.

```bash
curl https://auth.<yourdomain>.com/ready
```

**What success looks like:** HTTP 200 with `status: "Healthy"`. In Layout B the Gateway's readiness
check calls the API's own `/ready`, which in turn runs two checks: it opens a database connection
with a 5-second timeout, and it confirms the JWT signing key is loaded. A database failure reports
`Degraded`; a missing signing key reports `Unhealthy`.

```bash
curl https://auth.<yourdomain>.com/.well-known/jwks.json
```

**What success looks like:** a JSON document containing one key entry. An empty key list means the
signing key did not load.

```bash
curl -X POST https://auth.<yourdomain>.com/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "<the address you gave Auth_Setup in Phase 2 Step 5>", "password": "<the password you set there>" }'
```

**What success looks like:** HTTP 200 and a body shaped like this — note that the tokens are nested
under `token`, not at the top level:

```json
{
  "token": {
    "accessToken": "eyJ…",
    "refreshToken": "…",
    "tokenType": "Bearer",
    "expiresIn": 900,
    "refreshExpiresIn": 604800
  },
  "user": { "id": "…", "email": "<your address>" },
  "requiresPasswordChange": false,
  "requiresTwoFactor": false
}
```

Reading the results:

* **`/ready` fails while `/health` succeeds** — the process is up but cannot reach the database. This
  is the most common first-deployment error; check the connection string, the SQL login, and the
  firewall between the web server and the SQL server.
* **`requiresPasswordChange: false`** is expected: `Auth_Setup` set the password you chose and cleared
  `MustChangePassword`. To change the password later, sign in to the console
  ([Phase 7](#phase-7--build-and-deploy-the-two-web-applications)) and use the security tab of your
  profile, or call the change-password endpoint with the access token from the response above and a
  three-field body:

  ```bash
  curl -X POST https://auth.<yourdomain>.com/api/v1/auth/change-password \
    -H "Authorization: Bearer <accessToken from the login response>" \
    -H "Content-Type: application/json" \
    -d '{ "currentPassword": "<your current password>", "newPassword": "<your new password>", "confirmNewPassword": "<the same new password>" }'
  ```

  **What success looks like:** HTTP **204 No Content** with an empty body. The new password must
  satisfy the configured policy (`Password:MinimumLength`, 8 out of the box), and
  `confirmNewPassword` must match `newPassword` exactly.
* **`User.InvalidCredentials`** — the address or the password is not the one you gave `Auth_Setup`, or
  its statement was never run. Run [Phase 2 step 5](#phase-2--database) again with the same `--email`
  and the password you want; the statement only ever changes the seeded administrator's row.
* **HTTP 403 on every call** — the gateway token does not match. Re-read the "one secret, two names"
  box in [Phase 5](#phase-5--optional-api-gateway).

## Phase 9 — Go-live checklist

Grouped by what goes wrong, because "the site will not start" and "the site is quietly insecure" are
very different mornings.

### It will not start at all without these

- [ ] `ConnectionStrings:AuthDb` resolves to a real connection string — not the literal text `ConnectionStrings__AuthDb`, which a startup guard rejects by name.
- [ ] `Cors:AllowedOrigins` is not an empty array.
- [ ] `Email:FrontendBaseUrl` is an absolute URL whenever `Email:Enabled` is `true`.
- [ ] `PrivacyPolicyPublication:PhysicalPath` is not empty.
- [ ] `SecretManagement:StorageMode` is `Certificate` or `Dpapi` — never `PlainText`, which fails on the second boot in Production.
- [ ] In `Certificate` mode: the `.pfx` exists at `DataProtection:Certificate:PfxPath`, and `AUTH_DP_CERT_PASSWORD` is set for **each** application separately — in its own `web.config`, or on the hosting panel's environment-variable page for that site.
- [ ] Every required secret is present. With `AutoGenerateKeys: false` and an empty store, the application refuses to start rather than minting new keys — that refusal is the feature, not a fault.

### It will start, and be broken

- [ ] `Cors:AllowedOrigins` lists **both** web applications' origins and at least as many entries as the base file's two, so no `{{FRONTEND_ORIGIN_2}}` placeholder survives.
- [ ] `Cors:AllowCredentials` is `true`.
- [ ] `Jwt:Issuer` and `Jwt:Audience` are your real public address.
- [ ] `IdentityProvider:PublicBaseUrl` and `IdentityProvider:AccountsBaseUrl` are set — without the first, every authorize redirect and the discovery document publish the internal hostname.
- [ ] `Gateway:ValidationEnabled` matches your layout: `true` with a Gateway, `false` without one.
- [ ] `Services:AuthApi:BaseUrl` on the Gateway is the API's real address, so both the readiness probe and the settings pull work.
- [ ] Both applications were rebuilt after you edited their `.env.production` — the values are baked in at build time and cannot be corrected on the server.
- [ ] Both applications' `web.config` Content Security Policies name your API origin, and both `dist` folders actually contain a `web.config`.
- [ ] The accounts site has a `privacy` virtual directory pointing at `PrivacyPolicyPublication:PhysicalPath`.
- [ ] `ExternalAuth:Google` is either fully configured on both sides or left at the shipped `Enabled: false`.
- [ ] The data-controller fields are filled in at **System settings → Data controller**. They ship empty on purpose and a privacy policy cannot be published until they are set.
- [ ] Every system notification type has a published template — no `no published global Email template` line in the startup log.
- [ ] A test email actually arrives ([Phase 6](#phase-6--email-and-notifications)).
- [ ] The application pool is set to **AlwaysRunning** with idle timeout `0` ([Reference §H](#h-iis-hosting-and-application-pool-settings)) — otherwise email delivery and account-deletion work stall whenever the site is idle.

### It will work, and be unsafe

- [ ] HTTPS with a valid certificate on all four domains. **Note what does and does not send HSTS:** the Auth API sends it (365 days, including subdomains, with preload) in every non-Development environment; the **Gateway does not send it at all** — it only redirects HTTP to HTTPS; both web application sites send their own from their `web.config`. If the Gateway is your public origin, add the header at the IIS level.
- [ ] The seeded admin has **your** address and a password you chose, set through Phase 2 Step 5. Run, on the production database: `SELECT [Email], [IsEmailConfirmed], [IsTwoFactorEnabled], CASE WHEN [PasswordHash] IS NULL THEN 'no password' ELSE 'set' END AS [Password] FROM [dbo].[Users] WHERE [Id] = '00000000-0000-0000-0000-000000000002';` Expected: your own address (never `admin@company.com`), `1`, `1`, `set`. `admin@company.com` or `no password`: Phase 2 Step 5 was not run — run it. `IsEmailConfirmed` `0`: the row was changed by hand — run Phase 2 Step 5 again. `IsTwoFactorEnabled` `0`: sign in as this account and turn on two-step verification (the line below). No row at all: the seeded administrator was deleted — do not go live until a platform administrator exists.
- [ ] The SQL login is least-privilege — read and write on one database, not `sa`.
- [ ] `SecretManagement:EnableAdminApi` is `false` — it now ships that way, so confirm nothing in your Production file turns it back on. Enable it only while provisioning keys.
- [ ] `HealthChecks:ExposeErrorDetails` is `false` on both applications. `/health` and `/ready` bypass gateway-token validation, so they are publicly reachable.
- [ ] `AllowedHosts` is your own domain, not the shipped `"*"`.
- [ ] `SecretManagement:AutoGenerateKeys` is back to `false` after the first successful run.
- [ ] `IdentityProvider:FirstPartySpaOrigins` lists the console and accounts origins, both on the API's site. The boot log shows `boot.first-party-origins: first-party app origins are …` with no "does not look same-site" line.
- [ ] Nothing to set for replay protection: an authenticator code is accepted once only, and no setting turns that off. An incident in which genuine users are refused is handled by deploying the previous API build, not by a switch. **But a previous build reads the two retired switches again** — `TwoFactor:RejectReusedCodes` and `TwoFactor:RequireEmailCodeForFirstFactor` — so after a rollback each comes back as this deployment had it before: on by default, off wherever a stored override, a configuration file or an environment variable (`TwoFactor__RejectReusedCodes`, `TwoFactor__RequireEmailCodeForFirstFactor`) still sets it off. This release ignores those values and no longer shows them. So right after deploying it, and before you rely on a rollback: at **System settings → Two-factor authentication**, change one field (for example the re-authentication window by one minute), save, then set it back and save again — the Save bar appears only for a changed card, and each save replaces the stored row with the remaining fields only (do not use *Reset to defaults*: it drops every override of the section); and remove either key from every configuration file and environment variable.
- [ ] `Email:Enabled` is `true`. It makes an account that turns on its first second factor also type a code emailed to its confirmed address, so somebody who holds only a stolen password cannot bind an authenticator app of their own and lock the owner out; no other setting turns this off. With email off, the password alone is enough to bind one — and no mail goes out at all. **And every account that holds `system-settings:manage` has its own second factor:** such an account can switch email off itself, so its password alone would otherwise still be enough. The seeded administrator is such an account, and it can bind its factor only once its address is yours: Phase 2 Step 5, and the check two lines above.
- [ ] `TwoFactor:EnforceForPlatformAdmins` is a decision, not a default: it ships `false`. **No account gets a platform role or permission in production before it has its own second factor** — while it is off, that manual rule is the only thing standing in for the switch; while it is on, the product refuses such a grant itself (`TwoFactor.RequiredForPlatformGrant`). Switch it on only after the five preconditions — deploy 2, the refresh fix OI-101, a clean inventory and two quiet days, the drill, and your own word that production is stable — in [Reference §K](#k-two-factor-authentication-for-platform-administrators-twofactorenforceforplatformadmins).
- [ ] `TwoFactor:ReauthenticationMaxAgeMinutes` is the shipped 15 (it accepts 5 to 60) — how recent a sign-in must be before a session may set up, switch on or switch off two-factor. A longer window lets an older session — one left open on a shared computer, or a stolen token — change the second factor. No value turns the check off.
- [ ] **The Auth API host is restricted at the firewall or in IIS to the Gateway's address.** The application does not do this for you, and the consequences are in [Reference §G](#g-network-topology--what-must-and-must-not-sit-in-front-of-what).
- [ ] Nothing — no content delivery network, no second reverse proxy — sits in front of the Gateway ([Reference §G](#g-network-topology--what-must-and-must-not-sit-in-front-of-what)).
- [ ] **Secrets backed up:** `secrets.dpapi` **and** the key-ring folder **and** the `.pfx`. All three, or the set restores nothing. Losing them invalidates every token, and a Dpapi-mode file cannot be recovered on a different machine at all.
- [ ] The three persistent folders — secrets, uploaded images, published privacy policies — are outside the publish destination and in the backup schedule.
- [ ] Database backups are scheduled.

## Phase 10 — (Optional) Connect other apps via the SDK

Only relevant if you have *other* .NET applications that must trust this system. `Auth.Sdk` lets them
validate tokens and API keys without ever holding a private key.

> ### ⚠️ Known defect — read before you use it
> **The SDK sends the `X-Gateway-Token` header twice.** It is added once when the HTTP client is
> registered and again on the resolved client, and a two-value header can never equal the single
> value the API compares against. **Every SDK call through a Gateway with
> `Gateway:ValidationEnabled: true` is rejected with 403.** Separately, the SDK attaches no
> `Authorization` header when calling the API-key and webhook-key `validate` endpoints, which require
> both authentication and a permission — so those calls cannot succeed either.
> *In code:* `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs` and
> `Auth/Auth.Sdk/AuthSystemClient.cs`.
>
> Do not point the SDK at the Gateway until this is fixed. Whether it is worth fixing is an open
> question for the codebase owner: the SDK is in the solution, is referenced by no project, has no
> package definition and no publishing pipeline, so it is unknown whether any application anywhere
> consumes it.

If you proceed anyway, the quickest wiring is a direct project reference
(`<ProjectReference Include="..\Auth.Sdk\Auth.Sdk.csproj" />`). Consumer setup and the full
walkthrough are in [`APPLICATION_INTEGRATION_GUIDE.md`](./APPLICATION_INTEGRATION_GUIDE.md); packaging
options are in [`SDK_PUBLISHING_GUIDE.md`](./SDK_PUBLISHING_GUIDE.md).

---

# Reference

## A. Storage-mode setup

Both production modes generate the keys on the first start where the secrets file does not yet
exist. They differ only in *what* protects the file. **The Gateway must use the same mode as the
API, the same certificate, the same key-ring folder, and the same secrets file.**

`PlainText` is not covered here. It is a Development-only mode and cannot survive a restart in
Production — see [Decision B](#decision-b--how-secrets-are-stored-secretmanagementstoragemode).

### Certificate (portable; recommended for shared hosting)

Secrets stay encrypted in `secrets.dpapi`; the key ring that encrypts it is protected by a cert you
own, so the set is portable: `.pfx` → key ring → `secrets.dpapi`.

1. **Create a long-lived cert** (once, any Windows machine):
   ```powershell
   $cert = New-SelfSignedCertificate -Subject "CN=<AppName>-Auth-DataProtection" `
     -KeyExportPolicy Exportable -KeySpec KeyExchange -NotAfter (Get-Date).AddYears(20) -KeyLength 2048
   $pwd = Read-Host "PFX password" -AsSecureString
   Export-PfxCertificate -Cert $cert -FilePath dp-cert.pfx -Password $pwd
   ```
   It's a private encryption cert, not a TLS cert — a 20-year life is fine.
2. **Upload `dp-cert.pfx`** into a secrets folder outside the web root, e.g.
   `C:\inetpub\vhosts\<webspace>\secrets\`.
3. **Put the password in an env var** named `AUTH_DP_CERT_PASSWORD` (see
   [§C](#c-environment-variables-on-plesk-and-cpanel) — you do **not** have to use `web.config`).
4. **Configure and switch mode:**
   ```json
   "SecretManagement": {
     "StorageMode": "Certificate",
     "SecretFilePath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\secrets.dpapi",
     "AutoGenerateKeys": true
   },
   "DataProtection": {
     "KeyPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets",
     "Certificate": {
       "PfxPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\dp-cert.pfx",
       "PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD"
     }
   }
   ```
5. **Start the app.** It writes the encrypted `secrets.dpapi`. **Back up all three:** `.pfx`, the
   key-ring folder, and `secrets.dpapi`. The `.pfx` alone restores nothing — keep all three. (An
   *expired* cert still decrypts; only losing the `.pfx` locks you out.)

To rotate the cert, set the new one as `PfxPath` and list the old under
`Certificate:AdditionalPfxPaths` until the key ring rolls over.

Two rules from earlier phases apply here and are not repeated in full: the certificate password is
read only from `AUTH_DP_CERT_PASSWORD` and each application needs it in its own `web.config`
([Phase 4 step 1](#phase-4--publish-and-run-the-auth-api)); and both applications point at one
shared folder rather than a copy each, with the API granted Modify and the Gateway granted Read
([Phase 5 step 1](#phase-5--optional-api-gateway)).

### Dpapi (Windows-only, machine-bound)

Set `"StorageMode": "Dpapi"` in both applications. Secrets are encrypted with Windows DPAPI, bound to
**this machine and this account**. If the host moves your site to another physical server the file
cannot be decrypted, and with `AutoGenerateKeys: true` the application quietly mints new keys and
signs everyone out — **unless you hold the key material yourself and re-import it on the new
machine** ([§F](#f-provision-your-own-keys-byok--painless-migration)).

**"Zero setup" is only true on a desktop.** Under IIS this mode needs exactly the same two path
settings the Certificate mode does, for exactly the same reason:

```json
"SecretManagement": {
  "StorageMode": "Dpapi",
  "SecretFilePath": "C:\\inetpub\\vhosts\\<webspace>\\secrets\\secrets.dpapi"
},
"DataProtection": {
  "KeyPath": "C:\\inetpub\\vhosts\\<webspace>\\secrets"
}
```

Left empty, `SecretFilePath` defaults to `%LOCALAPPDATA%\AuthSystem\Secrets\secrets.dpapi`. Under an
IIS application pool with no loaded user profile, `%LOCALAPPDATA%` resolves to
`C:\Windows\System32\config\systemprofile\AppData\Local`, which the process cannot write. That is the
same failure the key-ring path produces, and this mode hits it twice.
*In code:* `Auth/Auth.Shared/Configuration/SecretConfigurationExtensions.cs` (secrets-file default);
`Auth/Auth.Shared/Configuration/AuthDataProtectionExtensions.cs` (key-ring default).

Dpapi is a reasonable choice for a machine you fully control and will not migrate. On shared hosting,
prefer Certificate.

## B. Encrypt the connection string & SMTP password

The signing keys are encrypted automatically. The **connection string** and the **SMTP password**
are not auto-generated — nothing can invent them — so they start out wherever you first put them,
usually a `__` environment variable in `web.config`. Both have slots in `secrets.dpapi`, and both
are set from the console:

**Console → System Settings → Secret management → Manage secrets.** That button opens the **Secret
keys** page. On it, click **Edit** on the `ConnectionStrings.AuthDb` row or the `SmtpPassword` row.

Two prerequisites, and both catch people out:

1. **The console must exist.** These are pages in the administrator application, so
   [Phase 7](#phase-7--build-and-deploy-the-two-web-applications) has to be done first.
2. **`SecretManagement:EnableAdminApi` must be `true`**, and you must be signed in as someone holding
   the **`secrets.manage`** permission. The seed grants it to no role but `super-admin`'s global `*`
   (Phase 2 step 4), so on a clean database the **only** account that reaches these pages is the
   seeded `super-admin`.
   Turn `EnableAdminApi` back to `false` when you have finished.

> **Why the console badge says "Not configured" while everything works.** The badge reads the
> encrypted file, not the effective configuration. A value supplied by `ConnectionStrings__AuthDb`
> never touches that file, so the row is red however healthy the database connection is. Red means
> *"not in the vault"*, not *"not working"*.

> **"Set custom secret" cannot do this.** That dialog namespaces every key under `Custom:`, so
> typing `ConnectionStrings.AuthDb` there stores a value under `Secrets:Custom:ConnectionStrings.AuthDb`
> — which nothing reads. It returns success and adds a second, green row while the real row stays
> red. Use the **Edit** buttons above.

Both values take effect on the **next API restart**: the connection string is captured once at
startup into the connection factory, and the process keeps its startup configuration until it
restarts.

### B.1 First deployment — moving off web.config

The console cannot be used without a working database, so start from the file and migrate inward:

1. Leave `ConnectionStrings__AuthDb` in `web.config` as it is. Start the API — it boots from the
   environment variable.
2. Console → System Settings → Secret management → Manage secrets → **Edit** both rows. The badges
   turn **Configured**.
3. Restart the application. Sign in again, then send a test email to prove that both values are now
   coming from the encrypted file. Over HTTP that is:

   ```bash
   curl -i -X POST https://auth.<yourdomain>.com/api/v1/admin/system-settings/email/test \
     -H "Authorization: Bearer <accessToken from a sign-in>"
   ```

   **The request has no body, and you cannot choose the recipient.** The test message always goes to
   the email address of the account whose access token you used, so sign in as an administrator whose
   mailbox you can actually open. The caller needs the **`system-settings:manage`** permission, which
   is a different permission from the `secrets.manage` one the secret pages need. The seed withholds
   it from `admin` too, so on a clean database the only account that holds it is still the seeded
   `super-admin` with its global `*`.

   **What success looks like:** HTTP **204 No Content**, an empty body, *and* the message arriving in
   that inbox. Read the two failure answers as follows:

   * **HTTP 400 with the code `SystemSettings.EmailSendingDisabled`** — `Email:Enabled` is false, so
     nothing was attempted ([Phase 6](#phase-6--email-and-notifications)). This endpoint is the one
     place that says so out loud; the ordinary send path stays silent.
   * **HTTP 500 with the code `SystemSettings.TestEmailFailed`** — the SMTP transport itself failed.
     The response stays deliberately vague because SMTP errors can quote hostnames and credentials;
     the real cause is in the log as `Failed to send email to {Email}: {Subject}` at Error level,
     with the exception attached.

   A 204 and no email means your mail server accepted the message and something after that dropped
   it: check the recipient's spam folder and your provider's own delivery log.

4. Delete `ConnectionStrings__AuthDb` and `Email__Password` from `web.config`, keeping
   `AUTH_DP_CERT_PASSWORD`. Restart and repeat step 3.

This is the only time you touch `web.config` for these two.

> Rotate the credentials afterwards if they were ever plaintext on disk. `web.config` is copied into
> `bin\` and `obj\...\PubTmp\Out\` on every publish; moving a value into the vault does not
> retroactively un-expose it.

### B.2 Rotating the database password

Three steps, and `web.config` is not involved:

1. Console → **Edit** `ConnectionStrings.AuthDb` → enter the string carrying the **new** password →
   the connect test fails (the password is not live yet) → confirm **Save anyway**.
2. Change the password at SQL Server.
3. Restart the API.

Run steps 1 and 2 back to back: between them the vault holds a value that does not work yet, so an
unplanned restart in that window leaves every database-backed request failing until step 2 lands.

> The connect test is a warning, not a gate, precisely for this. If it refused, password rotation
> would have no valid order: changing it at the server first takes the console down with the
> database, and storing the new string first can never pass a test against a credential that is not
> active yet. A malformed string is still refused outright — that one can never start working.

### B.3 Rotating the SMTP password

No forced save and no escape hatch needed — a wrong SMTP password stops email, not the API:

1. Change it at the mail provider.
2. Console → **Edit** `SmtpPassword`.
3. Restart, **then** send a test email. Testing before the restart exercises the old password and
   gives a misleading result.

### B.4 Recovery — a stored connection string that no longer works

If the stored value goes stale (database host renamed, password changed at the server, site
migrated), **the process still starts** — nothing at startup opens a database connection — but every
database-backed request fails, sign-in included, so the console that would fix it is unusable.

> Expect this symptom, not a startup failure: IIS shows a running site, `/health` returns 200, and
> `/ready` plus every real endpoint fail. Looking for an HTTP 500.30 "failed to start" page will send
> you hunting in the wrong place. See the [§D troubleshooting table](#d-troubleshooting) row for
> "/ready fails, /health works".

Break the loop:

1. In `web.config`, add a working `ConnectionStrings__AuthDb` **and**:

   ```xml
   <environmentVariable name="AUTH_IGNORE_SECRET_CONNECTIONSTRING" value="true" />
   ```

   The secrets layer then skips the connection string only — every other secret still loads, so the
   signing keys are unaffected and the startup guard stays satisfied.
2. Restart. The API boots on the `web.config` value.
3. Console → **Edit** `ConnectionStrings.AuthDb` → store the correct string.
4. Remove both lines from `web.config` and restart.

Exercise this once deliberately, before you need it.

> If instead the whole file fails to decrypt, the startup log says so explicitly and names
> `secrets.dpapi`. On a freshly migrated server that means the Data Protection certificate or key
> ring did not travel with it — see
> [§F Migration procedure](#migration-procedure-move-to-a-new-server-keep-everyone-signed-in). The
> escape hatch will not help there: the signing keys are gone too.

### B.5 Moving to another server

Nothing special to do for these two — they travel **inside** `secrets.dpapi`. Copy the three
artefacts (`secrets.dpapi`, the `key-*.xml` ring, and `dp-cert.pfx`) and set `AUTH_DP_CERT_PASSWORD`
in the new `web.config`, as described in
[§F Migration procedure](#migration-procedure-move-to-a-new-server-keep-everyone-signed-in).

The one thing that can still need attention is a **different database host** on the new server: the
stored connection string is then stale, so the site comes up but no request that touches the database
succeeds. Use [§B.4](#b4-recovery--a-stored-connection-string-that-no-longer-works) to get a usable
console, then store the correct string from it.

### B.6 Recovery — a bad value saved in the console

The console can change far more than these two secrets: rate limits, CORS origins, log levels,
notification settings. Those values are stored in the database and layered **above** the files, so a
mistake there survives a restart and cannot be undone by editing a file.

There is a second escape hatch, the sibling of the one in §B.4. In `web.config`, add:

```xml
<environmentVariable name="AUTH_DISABLE_DB_SETTINGS" value="true" />
```

Restart. The database settings layer is skipped entirely for that run, so every setting falls back to
whatever the files and environment variables say, and the log carries the warning
`AUTH_DISABLE_DB_SETTINGS=true - database system-settings overrides are DISABLED for this run.`
Fix or clear the bad value from the console — note that with the layer disabled the console shows the
file values — then remove the line and restart again.

**Exercise this once deliberately, before you need it**, exactly as with §B.4. The two hatches are
independent: `AUTH_IGNORE_SECRET_CONNECTIONSTRING` skips one secret, `AUTH_DISABLE_DB_SETTINGS` skips
one whole configuration layer.
*In code:* `Auth/Auth_API/Program.cs`, around the `DbSettingsConfigurationSource` registration.

## C. Environment variables on Plesk and cPanel

The application reads environment variables two ways: generic settings use `__` in place of `:`
(`ConnectionStrings__AuthDb`, `Email__Password`), while `AUTH_DP_CERT_PASSWORD` is read by its exact
name. Both are created the same way.

* **Plesk gotcha number one:** ignore the **ASP.NET Settings** and "Connection string manager" pages.
  They are for the old .NET Framework and this .NET 10 application never reads them.
* **The `web.config` method (always works on IIS):** add `<environmentVariable name="…" value="…" />`
  lines inside `<aspNetCore>`, as shown in
  [Phase 4 step 1](#phase-4--publish-and-run-the-auth-api). Names must match **exactly** — a wrong
  name is silently ignored, with no error anywhere. Browsers cannot download `.config` files from
  IIS, but the value is still plain text on disk.
* **Host-native (keeps secrets out of deployed files):** Plesk with a *Dedicated IIS Application
  Pool*, or cPanel's **Setup .NET App → Environment variables**, lets you set the same names there
  instead. Use this for `AUTH_DP_CERT_PASSWORD` when you would rather it were not in `web.config`.
* **cPanel or Linux hosting:** Windows DPAPI does not exist there, so `Dpapi` mode is unavailable —
  use `Certificate`. Paths use `/` instead of `\`.

## D. Troubleshooting

> **Behind an opaque `HTTP Error 500.30 - ASP.NET Core app failed to start` there is always a real
> exception** — IIS just hides it. Surface it by enabling the ASP.NET Core Module **stdout log** in
> `web.config`: on the `<aspNetCore>` element set `stdoutLogEnabled="true"` and
> `stdoutLogFile=".\logs\stdout"` (create the `logs` folder first), reproduce, then read the newest
> file in that folder. The app's own Serilog file (`Logs/…` for the API, `logs/…` for the Gateway)
> usually captures it too. Fix the exception, then set `stdoutLogEnabled="false"` again.

| Symptom | Cause | Fix |
|---|---|---|
| `HTTP Error 500.30 … failed to start` | A startup exception (IIS hides the detail) | Enable the stdout log (note above), read the real exception, fix that |
| `Refusing to start: plaintext secret(s) […] were found in the Production configuration` — and it started fine the first time | `SecretManagement:StorageMode` is `PlainText` in Production. The first boot generated the keys into a file; the second boot found them and refused | Switch to `Certificate` or `Dpapi` ([Decision B](#decision-b--how-secrets-are-stored-secretmanagementstoragemode)) and remove the generated plaintext values |
| Gateway 500.30 with `UriFormatException` | `Services:AuthApi:BaseUrl` missing or left as a literal `{{…}}` placeholder | Set `Services:AuthApi:BaseUrl` to the API's real URL — `ReadyUrl` derives from it, so leave `ReadyUrl` empty |
| "An error occurred while reading the key ring" / "Access to … `systemprofile` … denied" | A default path the application pool identity cannot write: `DataProtection:KeyPath` left empty falls back to `%ProgramData%\AuthSystem\Keys`, and `SecretManagement:SecretFilePath` left empty falls back under `%LOCALAPPDATA%`, which for a pool with no loaded user profile is `…\systemprofile\AppData\Local` | Set **both** keys to a writable folder outside the web root — the **same** folder for API and Gateway — and grant the application pool *Modify* |
| `No XML encryptor configured. Key … unencrypted form` (warning) | The Data Protection key ring has no at-rest encryptor because no user profile is loaded under the application pool identity | Expected in `Dpapi` mode on some hosts. Switching to `Certificate` mode encrypts the ring and silences it (§A) |
| "Connection string 'AuthDb' not found", or a guard naming the literal text `ConnectionStrings__AuthDb` | No connection string in the active configuration — the base file ships an environment-variable *name*, not a value | Add it to `appsettings.Production.json`, or set the `ConnectionStrings__AuthDb` environment variable |
| `/ready` fails, `/health` works | The process is up but cannot reach the database | Check the server address, database name, login and password, the firewall, and the `Encrypt` setting in the connection string |
| Startup error about CORS | `Cors:AllowedOrigins` is an **empty array** outside Development | List explicit `https://…` origins. Note that `*` does **not** trigger this error — it starts and silently denies everything |
| Every cross-origin request is blocked, but the application started fine | `Cors:AllowedOrigins` is `["*"]`, or still holds a `{{FRONTEND_ORIGIN_…}}` placeholder | List real origins; list at least as many entries as the base file's two |
| "Failed to decrypt … different machine" | A Dpapi-mode secrets file or key ring from another machine | Keep API and Gateway on one machine, or hold the key material yourself and re-import it ([§F](#f-provision-your-own-keys-byok--painless-migration)) |
| "Failed to load the Data Protection certificate" | Wrong or missing `AUTH_DP_CERT_PASSWORD`, or a wrong `.pfx` path | Set the variable correctly and verify the `.pfx` path and password ([Phase 4 step 1](#phase-4--publish-and-run-the-auth-api)) |
| Gateway 500.30 while the API is healthy | The **Gateway's own** `web.config` is missing `AUTH_DP_CERT_PASSWORD`, so it cannot open the `.pfx` | Add the same value to the Gateway's `web.config` — it is a separate file from the API's |
| Every proxied request returns 403, both applications report healthy | The Gateway's `Gateway:Token` and the API's `Gateway:ExpectedToken` have drifted apart | Point both at one shared secrets file ([Phase 5](#phase-5--optional-api-gateway)) |
| Tokens suddenly invalid, everyone signed out after a deploy | A re-publish wiped or overwrote `secrets.dpapi` and the keys regenerated | Keep the secrets folder outside the deploy target and set `AutoGenerateKeys: false` ([§E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys)) |
| Login returns `User.InvalidCredentials` for the seeded admin | The address or the password is not the one given to `Auth_Setup`, or the statement was never run | Run `Auth_Setup -- --email <your address>` again and run the statement it prints; it must report one row ([Phase 2 Step 5](#step-5--give-the-seeded-admin-your-address-and-a-password-it-has-neither-until-you-do)). If it throws "missing or deleted", the seeded row is gone: do not recreate it by hand |
| Generated keys not saved | The secrets folder is not writable by the application pool identity | Fix the folder permission and restart |
| A permission you granted still returns 403 | The person's access token predates the grant — permissions travel inside the token | Wait for their next token refresh (at most `Jwt:AccessTokenLifetimeMinutes`), or have them sign in again |
| No email arrives, and nothing is logged | `Email:Enabled` is false — the send path reports success and discards the message | [Phase 6](#phase-6--email-and-notifications) |
| Both web applications show a blank page | Either the API rejected the request (CORS) or the browser blocked it (Content Security Policy). The browser console says which | [Phase 7 steps 3 and 6](#phase-7--build-and-deploy-the-two-web-applications) |
| A deep link in a web application returns 404 or 500 | The IIS URL Rewrite module is missing, or `web.config` was not copied into the site root | [Phase 7 steps 4 and 5](#phase-7--build-and-deploy-the-two-web-applications) |
| Production is using development settings | `ASPNETCORE_ENVIRONMENT` is not `Production` | Set it in `web.config` or the host's settings |

## E. First publish vs. every publish after (don't wipe your keys)

Two publish settings are **safe on the first deploy and dangerous on every one after**, because the
keys are generated **on the server**, not in your repository:

| Setting | Where | First (clean) publish | Every publish after |
|---|---|---|---|
| The checkbox labelled **"Remove extra files in destination"** | Your Visual Studio publish profile | On — start from a clean folder | **Off** *(or keep it on — see below)* |
| **`SecretManagement:AutoGenerateKeys`** | `appsettings.Production.json` | `true` — mint the keys | **`false`** — never regenerate |

The checkbox is described here by its label on purpose. The underlying property name differs between
the two publish methods — a file-system publish and a Web Deploy publish spell it differently, and
one of the two spellings inverts the meaning. Since the repository contains no publish profile for
you to compare against, use the label in the dialog rather than trusting a property name.

**Why it bites.** "Remove extra files in destination" deletes anything in the destination that is not
in the publish output. If the secrets folder — the key ring plus `secrets.dpapi` — sits **inside**
that destination, it is deleted; and with `AutoGenerateKeys: true` the application then **silently
mints new keys** on the next start. Every existing token dies, every user is signed out, and the
gateway token desynchronises so every proxied request returns 403.

**The robust layout, which removes the trap rather than documenting it:** put the secrets folder
**outside** the publish destination, as a sibling of the site root rather than under it. Then "Remove
extra files in destination" can stay on for every publish — it cannot reach the secrets, and you
still get clean deploys with no orphaned assemblies. Set `AutoGenerateKeys: false` after the first
successful run, permanently; it then acts as a fail-loud fuse, refusing to start if the secrets ever
go missing instead of quietly rotating them. Nothing depends on a person remembering a checkbox
months later. The same reasoning applies to the uploaded-images and privacy-policy folders from
[Phase 3](#phase-3--configure-the-auth-api) — all three belong outside the deploy tree.

### First clean publish — ordered, no skipping

1. **(Certificate mode only)** Create `dp-cert.pfx`, place it in the secrets folder, and set
   `AUTH_DP_CERT_PASSWORD` in `web.config` (§A and
   [Phase 4 step 1](#phase-4--publish-and-run-the-auth-api)). The application **will not start**
   without it.
2. In `appsettings.Production.json`, set `SecretManagement:SecretFilePath`,
   `DataProtection:KeyPath`, `ImageStorage:PhysicalPath` and
   `PrivacyPolicyPublication:PhysicalPath` to folders outside the publish destination, and set
   `SecretManagement:AutoGenerateKeys: true`.
3. Confirm `web.config` is a file **in the project** carrying `ASPNETCORE_ENVIRONMENT`,
   `AUTH_DP_CERT_PASSWORD`, and any `__` overrides, so publishing preserves them.
4. **Auth API:** tick "Remove extra files in destination", publish.
5. Make one request to the API so IIS starts the process. Confirm the log shows
   `First startup detected - auto-generating cryptographic keys...`, copy the public key it prints,
   and **back up all three artefacts**: the `.pfx`, the key-ring folder, and `secrets.dpapi`. Losing
   the `.pfx` is unrecoverable; the other two alone restore nothing.
6. **Gateway:** point its `SecretFilePath`, `DataProtection:KeyPath` and certificate settings at the
   **same** folder the API uses, put `AUTH_DP_CERT_PASSWORD` in **its own** `web.config`, tick
   "Remove extra files in destination", publish. There is nothing to copy by hand — the gateway token
   comes out of the shared `secrets.dpapi`.
7. Call `/ready` on both applications, then perform one real sign-in through the Gateway.

**Then immediately:** set `SecretManagement:AutoGenerateKeys` back to `false` and re-publish. If you
could **not** move the secrets folder outside the deploy target, also untick "Remove extra files in
destination" in both publish profiles — that checkbox is then the only thing standing between a
routine deploy and a full key wipe.

---

## F. Provision your own keys (BYOK) & painless migration

By default the application **mints the keys for you** on first start
([Decision B](#decision-b--how-secrets-are-stored-secretmanagementstoragemode)). You do not have to
let it. Two alternatives let you control the key material — useful when you must move servers
without signing everyone out, or when you simply do not want the application choosing your keys.

| You want… | Use | Portable across servers? |
|---|---|---|
| Strong keys, generated by **this system on demand** rather than on first run | The `generate/*` endpoints | Only in **Certificate** mode — the minted private values cannot be read back out |
| Keys **you generate and hold** yourself, encrypted into this system | The `import/*` endpoints | ✅ Yes — re-import the same material on any server, even in **Dpapi** mode |

**The migration win:** if *you* hold the key material, in a vault or a password manager, you
re-encrypt it on each server. The new machine then produces **identical, still-valid tokens**, so
nobody is signed out, and you never have to carry the machine-bound `secrets.dpapi` or the key ring.
This is the only way to make **Dpapi** mode portable — see
[§A](#dpapi-windows-only-machine-bound). **The HMAC key is the exception** since the keyring (S11):
it moves with the secrets file, not by import — see
[The HMAC key moves with the secrets file](#the-hmac-key-moves-with-the-secrets-file-not-by-import).

### The admin secrets API

All key operations live under `…/api/v1/admin/secrets/` and are gated four ways:

* **`SecretManagement:EnableAdminApi` must be `true`.** It ships **`false`**, matching the settings
  class, so a deployment that never mentions the key has these endpoints refused. Turn it on in your
  Production file for the minutes you are provisioning, then off again (Phase 9 checklist). Set it
  in `appsettings.Production.json` only (an environment variable or any other override would
  shadow the file) and validate the JSON before saving: a save that does not parse empties every
  value of that file for the settings read per request, not only this one. The edit applies to the
  next request, with no restart. The console cannot change it. After each flip, `GET secrets/status` answers 200 (on)
  or 403 (off).
* **A bearer token from a user holding the `secrets.manage` permission.** On a clean database that
  means the seeded `super-admin` and nobody else (Phase 2 step 4).
* **HTTPS.** These requests carry private keys. Never send them over plain HTTP.
* **A verified confirmation code, per operation.** This is the part people miss — see below.

| Endpoint | What it does | Returns |
|---|---|---|
| `GET  secrets/status` | Lists which secrets are set. Never the values. | Status only |
| `POST secrets/challenges` | Raises a confirmation and emails a six-digit code | The challenge id |
| `POST secrets/challenges/{challengeId}/verify` | Answers the confirmation with the code | The blast radius of the pending operation |
| `POST secrets/generate/rsa` \| `/hmac` \| `/gateway-token` | The system mints a fresh random key | Public key, token, or a message |
| `POST secrets/import/rsa` \| `/hmac` \| `/gateway-token` | Stores a value **you** supply, encrypted | Derived public key, or a message |
| `GET  secrets/keyring` | The HMAC keyring: each key's state, dates and webhook keys. Never the material. | The keyring |
| `POST secrets/keyring/hmac/stage` | Adds a new HMAC key, generated or supplied, that verifies and issues nothing | The keyring |
| `POST secrets/keyring/hmac/promote` | Makes the staged key the one that issues (`emergency: true`: a fresh key at once, every older key refused) | The keyring |
| `POST secrets/keyring/hmac/discard-next` | Removes the staged key | The keyring |
| `POST secrets/keyring/hmac/reinstate/{id}` | Makes an older key issue again: the rollback of a promote | The keyring |
| `POST secrets/keyring/hmac/retire/{id}` | Removes an older key from the file and from its backup copy | The keyring |

`generate/hmac` and `import/hmac` remain for scripts and for consoles loaded before the keyring:
each is now an **emergency rotation** (every user signs in again). The routine rotation is the
keyring's stage and promote — see
[Rotating the HMAC key without signing anyone out](#rotating-the-hmac-key-without-signing-anyone-out).

> ### ⚠️ Every generate and import needs a confirmation code emailed to you — so email must work
> You cannot simply post a key. Each `generate/*` and `import/*` call requires a `challengeId` in its
> body, and that id only becomes usable after you have answered a **six-digit code emailed to the
> requesting administrator**. The consequences, in order of how often they bite:
>
> 1. **With `Email:Enabled` false in Production, key import and rotation are impossible.** The code
>    is written to the log instead of being emailed **only** when email is disabled *and* the
>    environment is Development. In Production it is emailed or it does not exist. Configure email
>    ([Phase 6](#phase-6--email-and-notifications)) before you attempt any of this.
> 2. **The requesting administrator needs a confirmed email address.** If their account has no
>    address, or the address is unconfirmed, the call returns `ChallengeRecipientUnavailable` and no
>    code is sent.
> 3. **An approval is bound to one operation, and for imports to the exact bytes.** You cannot get a
>    confirmation for rotating the gateway token — which invalidates nothing — and spend it on
>    rotating the refresh-token HMAC key, which signs everybody out. For imports, the approval is
>    bound to a digest of the key material, so you must supply the value when you raise the
>    confirmation as well as when you spend it.
> 4. **Requesting codes is rate-limited** per administrator, by `Email:RateLimitWindowSeconds` and
>    `Email:MaxOtpRequestsPerWindow`, and a fresh code invalidates every outstanding one.
>
> *In code:* `Auth/Auth_API/Modules/Administration/Controllers/SecretsController.cs`;
> `Auth/Auth.Application/Features/Secrets/Common/SecretOperationChallengeService.cs`;
> `Auth/Auth.Domain/Enums/SecretOperation.cs`.

The `operation` value in the confirmation request is one of these names:

| Name | Rotates |
|---|---|
| `GenerateRsaKey` | The RSA key pair that signs access tokens |
| `GenerateHmacKey` | The HMAC key that fingerprints refresh tokens, SSO sessions, links, emailed codes and webhook keys. With `keyringAction`, the keyring change it names; without, an emergency rotation |
| `GenerateGatewayToken` | The shared gateway token |
| `ImportRsaKey` | Same as above, with key material you supply |
| `ImportHmacKey` | With `keyringAction: "StageImport"`, stages the key you supply; without, an emergency rotation to it |
| `ImportGatewayToken` | Same |

A keyring change also names itself: the confirmation request carries `keyringAction` (`Stage`,
`StageImport`, `Promote`, `PromoteEmergency`, `DiscardNext`, `Reinstate` or `Retire`), and `keyId`
for `Reinstate` and `Retire`; the verify request repeats them, with `value` for `StageImport`. The
approval is bound to the action and to the key the secrets file names for it, so an approval to
promote the staged key cannot be spent on an emergency, or on a key staged after it.

**Importing replaces the current key.** For the RSA key and the gateway token: if the value differs
from what is stored, every existing token signed with the old one is invalidated, and re-importing
the *same* value is a safe no-op. **The HMAC key is different since the keyring (S11):**
`import/hmac` makes the supplied key current at once *as an emergency* — every older key stops
verifying, and everyone signs in again — and a value the keyring already holds is refused with
`Secret.InvalidKeyMaterial` before your code is spent. To bring your own HMAC key without signing
anyone out, stage it (`keyring/hmac/stage` with `value`) and promote it, as below.

### Rotating the HMAC key without signing anyone out

One HMAC key fingerprints every opaque credential the system stores for a lookup: refresh tokens,
SSO sessions, parked sign-in requests, authorization codes, password-reset and invitation links,
pending two-factor sign-ins, pending registrations, emailed codes and webhook keys. Rotating it the
old way (`generate/hmac`) signs every user out. The **keyring** rotates it in two steps instead:

* **Stage** a new key. It verifies from that moment and issues nothing. Nothing anyone holds
  changes.
* **Promote** it. It fingerprints everything new from then on, and the old key keeps verifying
  through its **overlap window**: as long as the longest refresh token or SSO session alive at that
  moment still has to run, at least 7 days and **at most 30 days**, plus five minutes. Webhook keys
  keep 90 days, and each is re-fingerprinted under the new key the next time it is used.

The original key is key 1. Until the first promote, it is the only key and it fingerprints in its
original form, so a deployment that never rotates stores exactly what it always stored.

**Before you start:** email works (every change takes an emailed code, see above), you hold
`secrets.manage`, and you have a fresh backup of `secrets.dpapi`, the DataProtection key folder (and
its `.pfx` in Certificate mode), and the database.

**The routine rotation.** In the console: **System Settings → Secret management → Manage secrets**,
the **HMAC keyring** panel. Each change asks for the code emailed to you, shows its cost, and runs
once you type your address.

1. Set `SecretManagement:EnableAdminApi` to `true` in `appsettings.Production.json` (there only;
   validate the JSON before saving, and keep the last good copy). No restart:
   `GET …/api/v1/admin/Secrets/status` answers **200** on the next request.
2. **Stage a new key** (generate one, or import your own). Then **back up `secrets.dpapi`.**
3. Check the staged key's row says **Loaded in this process: Yes**. If several API processes share
   the file (a web garden, more than one server), recycle the others first: a promote is refused,
   with that reason shown beside the button, while the process you talk to has not loaded the
   staged key.
4. **Promote the staged key.** Then **back up `secrets.dpapi` again.**
5. Check: a session signed in before the promote still refreshes; an invitation or reset link sent
   before it still opens; a webhook key issued before it still validates, and is re-fingerprinted
   under the new key as it does. A webhook key not used since is marked on the Webhook keys page with
   the date by which it must be used or re-issued.
6. Set `EnableAdminApi` back to `false`; `GET …/admin/Secrets/status` answers **403**. That save
   reloads the configuration, so the posture line `keyring.legacy-derivation` is written now, with
   the days left on the original key's window (a keyring change by itself reloads nothing; a recycle
   of the API also writes it).

**Who signs in again.** With the shipped lifetimes (refresh tokens and SSO sessions of 7 days),
nobody: the old key keeps verifying until the last refresh token and SSO session alive at the
promote has expired. The window stops at 30 days, so only a deployment whose
`Jwt:RefreshTokenLifetimeDays` or `IdentityProvider:IdpSessionLifetimeDays` is above 30 has sessions
that outlive it, and those end when it closes, used or not: an SSO session keeps the fingerprint it
was issued with for life, and a refresh token is re-fingerprinted only when it is renewed with
`Jwt:RotateRefreshTokens` on. The promote's preview counts them before you confirm.

**More than one API process.** Every change takes effect at once in the process that made it.
Another process that shares the secrets file (a web garden, a second server) keeps the keyring it
loaded until it is recycled, and logs the posture line `keyring.process-ring` at Critical at its next
configuration reload. Recycle the others after every change, and **at once after an emergency**:
until then they still accept the leaked keys. The file is not yet locked across processes: make
every keyring change from one API process, and run no other secret change (the SMTP password, the
connection string, the gateway token) on another process at the same time, or one of two saves can
be lost.

**Rolling back.** Before the promote: **Discard the staged key**. After it: **Reinstate** the
previous key (row menu → *Reinstate (roll back to this key)*), while its window is open. It issues
again, and the key you promoted keeps verifying through its own window, so a rollback signs nobody
out either. A key marked compromised can never be reinstated.

**A leaked key: the emergency rotation.** **Emergency rotation** makes a fresh key current at once
and marks every key the ring held, the staged one included, as compromised: none of them verifies
from that moment, so everyone signs in again and every pending link and emailed code stops working.
Webhook keys are the exception: they keep validating for 90 days and are re-fingerprinted on their
next use, because a leaked HMAC key cannot forge a random webhook key. `generate/hmac` and
`import/hmac` do exactly this. Recycle every API process that shares the file, then back up
`secrets.dpapi`. The emergency overwrites `secrets.dpapi.bak` as well, so the backup copy no
longer holds the ring in which the leaked keys are healthy; every backup you took before still
does: destroy or quarantine those, as for a retired key. If the emergency is refused because the
file no longer holds a key this process verifies under (another process changed the ring, or the
file was replaced), restart the API first, then run the emergency.

**Retiring an old key.** Once an older key's *Verifies until* date has passed and no webhook key
still validates under it, **Retire this key** removes it from `secrets.dpapi` and from
`secrets.dpapi.bak`. While webhook keys stored under it are in their grace, the panel says how many
and until when, and the retire is refused. A retired key is still in every backup you took before;
if it leaked, destroy those copies. The keyring forgets a retired key entirely, compromised or not,
so **never stage or import a key that was retired**: nothing would recognize it as leaked.

**Backups.** Keep a backup of `secrets.dpapi` from after each change until every *Verifies until*
date in the panel has passed: an older copy may lack the key that credentials issued since are
fingerprinted under. A promote, an emergency, a discard and a retire overwrite `secrets.dpapi.bak`
too, so it never holds the ring as it was before them; if that second write fails, the change still
stands, and the API log says at Error which `.bak` to overwrite or delete by hand.

> ### ⚠️ Once a key has been staged, never run a build older than S11 PR A
> A build from before S11 PR A (merge `e1e28e22`) does not read the keyring. After a promote it would
> fingerprint under the wrong key, so nothing it issues would be found again, and its first save of
> the secrets file erases the ring; the next start of a current build then refuses to boot and names
> the backup to restore. Roll the API back to S11 PR A's build or later. The keyring needs no
> database change, so the database is never the reason to roll back past it.
>
> *In code:* `Auth/Auth.Application/Security/Keyring/HmacKeyringTransitions.cs` (the changes);
> `Auth/Auth.Application/Features/Secrets/Common/HmacKeyringChangeService.cs` (confirmation, save,
> then the process swap); `Auth/Auth.Application/Security/Keyring/CredentialOverlapRule.cs` (the
> windows); `Auth/Auth.Infrastructure/Security/KeyringPostureContributor.cs` (the posture lines).

### Generate the material yourself — the formats the system expects

| Secret | Format | Generate it |
|---|---|---|
| RSA private key | PKCS#8 or PKCS#1 PEM, **2048 bits or more** | `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-private.pem` |
| HMAC key | Base64 of **32 bytes or more** | `openssl rand -base64 32` |
| Gateway token | Any string of **16 characters or more**; Base64 of 32 bytes is a good default | `openssl rand -base64 32` |

You supply only the **private** RSA key. The server derives the matching public key, stores it, and
returns it — that is the value you publish to anything validating tokens. On a machine without
`openssl`, the PowerShell equivalent for the two random values is
`[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`.

### Import over HTTP — the full four-step flow

Run these from a machine that can reach your API, with `curl` and `jq` installed. `jq` is a
command-line JSON reader; if you do not have it, read the values out of the responses by eye instead.

```bash
# 1) Sign in as an administrator holding the secrets.manage permission, and capture the token.
#    Note the nesting: the access token is at .token.accessToken, NOT at the top level.
TOKEN=$(curl -s -X POST https://auth.<yourdomain>.com/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "<your administrator address>", "password": "<admin-password>" }' | jq -r .token.accessToken)
```

**What success looks like:** `echo $TOKEN` prints a long string beginning `eyJ`. If it prints `null`,
the sign-in failed or you used the wrong JSON path.

```bash
# 2) Raise a confirmation for the exact operation, supplying the key material for an import.
#    The RSA PEM must have its newlines JSON-escaped as \n.
#    Note the field name: the challenge id comes back as .challengeId, not .id.
CHALLENGE=$(curl -s -X POST https://auth.<yourdomain>.com/api/v1/admin/secrets/challenges \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{ \"operation\": \"ImportRsaKey\", \"value\": \"$(awk '{printf "%s\\n", $0}' jwt-private.pem)\" }" \
  | jq -r .challengeId)
```

**What success looks like:** HTTP 200, `echo $CHALLENGE` prints a GUID (not `null`), and a six-digit
code arrives in the administrator's inbox within a few seconds. The response also carries an
`expiresAt` timestamp and the masked address the code went to. A 409 means either the account has no
confirmed email address or the storage mode does not support the operation.

```bash
# 3) Answer the confirmation with the emailed code.
curl -X POST "https://auth.<yourdomain>.com/api/v1/admin/secrets/challenges/$CHALLENGE/verify" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "code": "<the six digits from the email>" }'
```

**What success looks like:** HTTP 200 with a body describing what this rotation will invalidate.
Read it before you continue — it is the live count, not an estimate.

```bash
# 4) Perform the import, quoting the same challenge id and the identical key material.
curl -X POST https://auth.<yourdomain>.com/api/v1/admin/secrets/import/rsa \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{ \"value\": \"$(awk '{printf "%s\\n", $0}' jwt-private.pem)\", \"challengeId\": \"$CHALLENGE\" }"
```

**What success looks like:** HTTP 200 with the derived public key. Repeat steps 2 to 4 for
`ImportGatewayToken` against `…/import/gateway-token`. **Not for the HMAC key:** `import/hmac` is an
emergency rotation now, which signs everyone out; bring your own HMAC key through the keyring instead
(stage it with `value`, then promote it) — see
[Rotating the HMAC key without signing anyone out](#rotating-the-hmac-key-without-signing-anyone-out).
The `generate/*` endpoints take the same body minus `value`: just `{ "challengeId": "…" }`.

### The connection string and the SMTP password are different

Neither is key material, so neither needs a confirmation code — nothing is invalidated by changing
them. Normally you set them from the console
([§B](#b-encrypt-the-connection-string--smtp-password)). Over HTTP, reusing `$TOKEN` from step 1:

```bash
curl -X PUT https://auth.<yourdomain>.com/api/v1/admin/Secrets/smtp-password \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "value": "<your-smtp-password>" }'
```

```bash
curl -X PUT https://auth.<yourdomain>.com/api/v1/admin/Secrets/connection-string \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "value": "<your-connection-string>", "forceSave": false }'
```

`forceSave: true` stores a connection string that failed the connect test. See
[§B.2](#b2-rotating-the-database-password) for the one situation where that is the correct thing to
do.

### Migration procedure (move to a new server, keep everyone signed in)

1. **Once, before you need it:** capture the key material into your vault. Either you generated it
   yourself, or you imported it. **There is no read-back** — a key the system minted for itself
   cannot be exported, so this procedure only works if you owned the material from the start.
2. **On the new server:** set the same `SecretManagement:StorageMode`, set `AutoGenerateKeys: false`
   and `EnableAdminApi: true`, place the `.pfx` and set `AUTH_DP_CERT_PASSWORD` if you are in
   Certificate mode, then deploy and start. With `AutoGenerateKeys: false` and an empty store the
   application **fails loudly** instead of minting new keys. That failure is expected and correct
   until step 3 completes.
3. **Import the RSA key and the gateway token** with the four-step flow above.
4. **Verify.** `GET /.well-known/jwks.json` shows your public key, and a token issued by the old
   server still validates. Then set `EnableAdminApi: false` again.

Tokens issued by the old server keep working because the signing material is byte-for-byte
identical. The gateway token must match on both the API and the Gateway, exactly as described in
[Phase 5](#phase-5--optional-api-gateway).

> ### The HMAC key moves with the secrets file, not by import
> Since the keyring (S11), `import/hmac` is an emergency rotation, which signs everyone out: the
> opposite of what a migration wants. The keyring itself — every key, its dates, and which one
> issues — lives in `secrets.dpapi`. So in **Certificate** mode, carry the file to the new server
> with its DataProtection key folder and the `.pfx` (and set the same password variable): the new
> server reads the same ring, and every refresh token, SSO session, link and webhook key carries
> over. A **Dpapi** file cannot be read on another machine, so moving a Dpapi deployment starts a
> new HMAC key there, and everyone signs in once.

---

## G. Network topology — what must and must not sit in front of what

Two rules, both structural, both invisible until they bite.

### The Auth API host must be restricted to the Gateway

Giving the Auth API its own public hostname is convenient and, on its own, unsafe. **Restrict that
hostname at the firewall or in IIS so only the Gateway's address can reach it.** The application does
not do this for you, and two specific things go wrong if you skip it.

**Anyone who can reach the API directly can forge the client IP address it records.** For audit
logging and for per-IP rate-limit buckets, the API takes the first entry of the `X-Forwarded-For`
header unconditionally — because behind the Gateway that entry is the real client. Reached directly,
that header is whatever the caller typed. Audit rows then name an address of the attacker's choosing,
and per-IP buckets can be sidestepped by rotating the header.
*In code:* `Auth/Auth_API/Common/ClientIpResolver.cs`; `UseForwardedHeaders` is configured in
`Auth/Auth_API/Program.cs` with no known-proxy list.

**Six path prefixes bypass gateway-token validation entirely**, by design, and are therefore
reachable on the API host without the shared header: `/.well-known/`, `/health`, `/ready`,
`/swagger`, `/openapi`, and `/uploads/`. That exposes the health surface and the public signing keys.
Keeping `HealthChecks:ExposeErrorDetails` at `false` limits what the health endpoints reveal, but the
real control is network-level.
*In code:* `Auth/Auth_API/appsettings.json`, `Gateway:ExemptPaths`.

Two of those six serve nothing in production, so do not go looking for them. `/swagger` is a leftover
entry — no such endpoint exists in this application, in any environment. And the API document at
`/openapi/v1.json` is published **only in the Development environment**, so in production that path
returns 404 as well.
*In code:* `MapOpenApi()` is wrapped in an `IsDevelopment()` check in `Auth/Auth_API/Program.cs`.

### Nothing may sit in front of the Gateway

The Gateway is designed to be the outermost hop. Its rate limiter partitions clients by the actual
connection address and **deliberately ignores `X-Forwarded-For`**, because at the edge that header is
attacker-controlled and would let any caller claim a fresh, empty rate-limit bucket on every request.

The consequence is the mirror image: **put a content delivery network or a second reverse proxy in
front of the Gateway and every client in the world collapses into a single bucket**, because the
Gateway now sees only the proxy's address. The first burst of legitimate traffic then rate-limits
everybody. Nothing logs a warning; the limits simply stop meaning what you think they mean.
*In code:* `Auth/API_Gateway/Program.cs`, with the reasoning written out above the partition-key
definition.

### The four Gateway limiters and their starting values

These are the values the Gateway uses at startup and falls back to whenever it cannot reach the API.
After the first successful settings pull they are replaced by whatever an administrator has saved in
the console, so **change them there, not in the file**, once you are running.

| Limiter | Applies to | Permitted requests | Window | Queue |
|---|---|---|---|---|
| Global | Everything | 1000 | 60 s | 100 |
| `auth` | `/api/v{n}/auth/…` | 20 | 60 s | 0 |
| `api` | The 19 general feature routes | 100 | 60 s | 10 |
| `admin` | `/api/v{n}/admin/…` | 120 | 60 s | 0 |

Three route groups declare no named policy and are covered by the global limiter only:
`/.well-known/…`, `/openapi/…` and `/uploads/…`.

The same numbers are mirrored in the Auth API's own `GatewayRateLimiting` section — a different
section name on purpose, so they are never confused with the API's two login-related limits. A test
enforces that the two copies stay equal, so if you change one, change both.
*In code:* `Auth/API_Gateway/appsettings.json`, section `RateLimiting`;
`Auth/Auth_API/appsettings.json`, section `GatewayRateLimiting`;
`Auth/Auth_API.Tests/Configuration/GatewayRateLimitingParityTests.cs`.

**The Gateway forwards a fixed allow-list of 24 route prefixes and nothing else.** Anything not on
that list returns 404 at the Gateway even though the API implements it. If you later add a new
feature area to the API, it needs a matching Gateway route or it is unreachable in production; a test
in the backend test project fails when that is forgotten.
*In code:* `Auth/API_Gateway/appsettings.json`, section `ReverseProxy:Routes`;
`Auth/Auth_API.Tests/Gateway/GatewayRouteCoverageTests.cs`.

### The token endpoint's own limit

`POST /api/v{n}/auth/token` and `POST /api/v{n}/auth/revoke` are not counted in the sign-in limit.
They have a `token` policy of their own in both processes, per client address, on two Gateway routes
of their own (`token-route` and `revoke-route`). A server-side application's sign-out from its server,
`POST /api/v{n}/auth/end-session/server`, spends the same budget, on its own route
(`end-session-server-route`):

| Setting | Process | Default |
|---|---|---|
| `RateLimiting:TokenPermitLimit` / `RateLimiting:TokenWindowSeconds` | Auth API | 300 / 60 s |
| `GatewayRateLimiting:TokenPermitLimit` / `GatewayRateLimiting:TokenWindowSeconds` | Gateway (pulled from the API; seeded from its own `RateLimiting` section) | 300 / 60 s |
| `RateLimiting:TokenApplicationPermitLimit` / `RateLimiting:TokenApplicationWindowSeconds` | Auth API: per application, once its client assertion verifies | 3000 / 60 s |
| `GatewayRateLimiting:TokenAssertionPermitLimit` / `GatewayRateLimiting:TokenAssertionWindowSeconds` | Gateway: token requests that carry a client assertion, per address, outside the global limit | 6000 / 60 s |

A server-side web application (a Backend-for-Frontend) or a fleet of machine clients renews every one
of its users' tokens from one address, so on the sign-in limit of 20 a minute about 300 people signed
in through one server were enough to have the rest refused and signed out. Nothing at these two
endpoints can be guessed, so the higher number costs no protection against password guessing, and the
sign-in limit keeps its 20. One formula sizes every token budget: limit ≥ (people signed in at once ÷
access-token minutes + sign-ins a minute) × 1.5 + the largest group that signs in at once. Raise **both**
limits together in the console (system settings, both rate-limit sections): the lower of the two is the
one a client meets. That server's whole traffic through the
Gateway also counts against the global per-address limit above, so check that one against its total.

**A server that authenticates (OI-178).** A server-side application in confidential mode, or a machine
client, sends a client assertion (`private_key_jwt`) with its token requests. The Gateway cannot check a
signature, so it counts every token request that carries an assertion in a per-address bucket of its own
(`TokenAssertionPermitLimit`, 6000 a minute), outside the global limit; the Auth API verifies the
assertion before its limiter and counts each verified request on its **application's** budget
(`TokenApplicationPermitLimit`, 3000 a minute), whatever its address. Requests without an assertion, and
assertions that do not verify, count per address as before. An address that keeps sending assertions that
do not reach an application's budget (they do not verify, they are copies of one already counted, or their
verification failed) is refused without further verification once it has spent the per-address token budget
(`TokenPermitLimit`) in the window; that number therefore also sizes how many such assertions one address
may have verified. The per-application limit is sized by the same formula (3000 serves about 20,000 people
signed in at once with 15-minute tokens, with room for a few hundred sign-ins a minute), counting the
application's machine tokens too: its machine key and its sign-in key share the one budget. Keep the
Gateway's assertion bucket at or above that, times the number of such servers behind one address. Raising
the per-application number costs no protection: only an assertion signed with the application's key can
spend it, each assertion once (a copy counts on its address).

The server-side sign-out counts per address only, in `TokenPermitLimit`, at both hosts, although it
carries an assertion: neither the Gateway's assertion bucket nor the application's budget applies to it.
It is one call per sign-out, far fewer than the renewals those two buckets are sized for.

Both hosts look for the assertion only in a form POST with a declared `Content-Length` of at most 16 KB (the
API: `application/x-www-form-urlencoded`, the one form the token endpoint accepts). A chunked or larger
request is counted per address, and at the Gateway in the global limit too. Counting is per process: each
Auth API instance keeps its own per-application budget and its own record of the assertions it has
counted, so with several instances each counts only the requests it receives, and a copy of an assertion
may count once on each. The formula's result divided by the number of instances is enough for each when
the load balancer spreads evenly; the full result is the safe setting when it does not.

**After deploying this version, where an interim was set.** If this deployment raised
`RateLimiting:TokenPermitLimit`, `GatewayRateLimiting:TokenPermitLimit` or
`GatewayRateLimiting:GlobalPermitLimit` as an interim for a server-side application, lower them back once
that application authenticates on this release: `TokenPermitLimit` also sizes the allowance of assertions
that do not verify. **Order:** the Auth API before the Gateway, as below; the same applies to this pair,
and if the two went out in the wrong order, save any setting once in the console after both are upgraded,
or restart the Gateway, so the buckets already in use take the new limits.

The console refuses a save of the Gateway section while the global rate (permits divided by window) is
below the token rate, and the refusal names `TokenPermitLimit` (on that field, or in the global field's
message when the save changed the global pair) even when the field you edited was another one: every request passes the global limit first, so a token limit above it could never be
reached. Lower the token pair or raise the global pair, then save again.

**Deploy the Auth API before the Gateway when upgrading to this limit.** A Gateway newer than its API
finds no token pair in the pulled settings and rejects the rate-limit block of that pull, and only that
block: the CORS origins, the health-check flag and the settings version still apply. It keeps the
limits it is **currently** running, which are its own file values only if it was restarted since the
last pull that carried usable limits (no outage, but limits saved in the console do not reach it until
the API catches up). A Gateway older than its API simply keeps the two endpoints on the sign-in limit
until it is upgraded. If the two went out in the wrong order, save any setting once in the console
after both are upgraded: the new settings version starts fresh partitions, so even the addresses that
stayed busy throughout take the new limits. A Gateway whose routes were replaced on the server must
also receive `token-route` and `revoke-route`, or both endpoints stay on the sign-in limit; the same
holds for `end-session-server-route` (without it, a server-side application's sign-outs count on the
sign-in limit, and the ones refused fall back to the end-session confirmation page).

**Checking the server-side sign-out after deploying.** This probe must answer 400 with
`"code": "Auth.InvalidClient"` (a 404 means the Auth API is older than the endpoint):

```bash
curl -s -X POST https://auth.<yourdomain>.com/api/v1/auth/end-session/server \
  -d refresh_token=x \
  -d client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer \
  -d client_assertion=junk
```

The answer is the same through an old Gateway, which forwards the path on its general `auth-route`, so it
does not show which Gateway runs. To check the Gateway, look for `end-session-server-route` in the
deployed Gateway's `appsettings.json`. Alternatively, send the probe 21 times within one minute from one
address: behind the new Gateway every answer is 400; behind an old one the 21st answers 429, because
there the path counts on the sign-in limit. That alternative spends the address's sign-in allowance for a
minute, so run it from an address nobody signs in from.
*In code:* the `token` policy in `Auth/Auth_API/Program.cs` and in `Auth/API_Gateway/Program.cs`;
`Auth/Auth_API.Tests/Configuration/TokenEndpointThrottleGuardTests.cs`.

### The application directory's own limit

The application directory (`GET /api/v{n}/app/organizations/…`: an application's server reading, with a
user's own access token, the organizations that user belongs to) has an `app-read` policy of its own in
both processes, and a Gateway route of its own, `app-route`:

| Setting | Process | Default |
|---|---|---|
| `RateLimiting:AppReadApplicationPermitLimit` / `RateLimiting:AppReadApplicationWindowSeconds` | Auth API: per application and per address, once the token verifies and carries `organizations.read` | 300 / 60 s |
| `RateLimiting:AppReadAddressPermitLimit` / `RateLimiting:AppReadAddressWindowSeconds` | Auth API: per address, for a token that does not verify or lacks the scope | 60 / 60 s |
| `GatewayRateLimiting:AppReadPermitLimit` / `GatewayRateLimiting:AppReadWindowSeconds` | Gateway (pulled from the API; seeded from its own `RateLimiting` section): per address, inside the global limit | 600 / 60 s |

One board view in an application is about three calls (the organization, its members, its
invitations), so 300 a minute is about a hundred views a minute for one application from one address.
Keep the Gateway's `AppReadPermitLimit` at or above the API's per-application limit times the
applications whose servers call from one address. At the shipped defaults, one server's traffic through
the Gateway (directory 600 + token 300 + general API 100) equals the global per-address limit of 1000 a
minute, so a server that needs more raises the global pair too.

**The console's global-ceiling rule names this pair.** Where a deployment runs a global Gateway limit below
600 a minute, every save of the Gateway section is refused, naming `AppReadPermitLimit`, until that pair
is lowered to the global rate or below: lower it in the same save.

**A Gateway whose routes were replaced on the server must also receive `app-route`**
(`/api/v{version:int}/app/{**catch-all}`, policy `app-read`): without it every directory call reaches no
route and is refused. Deploy the Auth API before the Gateway, as for the token pair: a Gateway newer than
its API finds no `AppRead` pair in the pulled settings and keeps the limits it is running.
*In code:* the `app-read` policy in `Auth/Auth_API/Program.cs` and in `Auth/API_Gateway/Program.cs`;
`Auth/Auth_API/Common/Middleware/ApplicationReadClassificationMiddleware.cs`;
`Auth/Auth_API.Tests/OrganizationManagement/ApplicationDirectory/ApplicationDirectoryBudgetTests.cs`.

---

## H. IIS hosting and application-pool settings

Both .NET applications run **in-process** inside the IIS worker process. That has one operational
consequence worth a section of its own: **when IIS stops the worker process, everything the
application does in the background stops with it.**

Seven background services run inside the Auth API, and one inside the Gateway:

| Background service | What stops when the pool sleeps |
|---|---|
| Notification outbox dispatcher | Queued emails are not delivered until the next request wakes the site |
| Account-deletion worker (polls every 15 minutes) | Scheduled deletions do not progress |
| Token-revocation cleanup | Expired revocation records accumulate |
| System-settings refresh | Console-saved settings take longer to apply |
| Notification template startup check, email-logo backfill, encryption migration | Run at startup only; a recycle re-runs them |
| Gateway settings poller | The Gateway keeps its last-known rate limits and CORS origins |

None of this loses data — the outbox dispatcher reclaims messages that were claimed by a worker that
vanished mid-delivery, logging `Reclaimed {Count} orphaned Processing outbox message(s) from a
previous worker.` at Warning. But delivery is delayed until something wakes the site, which on a
quiet installation can be hours.

**Configure each application pool like this:**

| Setting | Value | Why |
|---|---|---|
| .NET CLR version | **No Managed Code** | .NET 10 runs out-of-band; the pool must not load the old runtime |
| Start Mode | **AlwaysRunning** | Starts the process without waiting for a request |
| Idle Time-out (minutes) | **0** | Never shut down for inactivity |
| Regular Time Interval (recycling) | **0**, or a fixed quiet-hours schedule | Avoid recycling in the middle of a delivery batch |

On Plesk, these live under the site's **Dedicated IIS Application Pool** settings. Some shared hosts
do not expose Start Mode or Idle Time-out; if yours does not, expect background work to run only
while the site is receiving traffic, and say so in your own runbook.

---

## I. Logs — where they are and how to find them

Each application writes a rolling daily log file through Serilog. **The configured paths are
relative**, and the folder they resolve to is decided at runtime by the hosting environment, so this
guide cannot tell you an absolute path — it can only tell you how to find it.

| Application | Configured path | Rolling | Retention in the base configuration |
|---|---|---|---|
| Auth API | `Logs/auth-api-.log` | Daily | 30 files |
| API Gateway | `logs/gateway-.log` | Daily | 30 files |

**Note the capital letter.** The API writes to `Logs/` and the Gateway to `logs/`. On Windows that
makes no difference; if you ever grep a backup on a case-sensitive filesystem, it does.

**To find the folder on your server:** the base is the running application's content root, which
under IIS is the site's physical path — the folder containing `Auth_API.dll`. Look for `Logs` or
`logs` directly inside it. If it is not there, the application pool identity could not create it;
grant Modify on the site folder, or check the ASP.NET Core Module stdout log described at the top of
[§D](#d-troubleshooting).

**Log levels are set per environment** under `Serilog:MinimumLevel`. A production configuration
typically raises the default to `Warning`, which is worth remembering when a diagnostic you expected
is missing — several of the "quietly did nothing" behaviours in this guide announce themselves at
Information level and are therefore invisible at Warning. The API's levels are hot: an administrator
saving a new level in the console applies it immediately, with no restart. The Gateway's are read
once at startup.

---

## J. Password protection — pepper and breached-password check

Both are configured under `Password` in `appsettings.Production.json`. The pepper is **opt-in**
(default `false`); the breached-password check is **on by default** (`Enabled: true`). The Argon2id password hashing itself — a unique salt per password, a
constant-time comparison, and a re-hash on sign-in when the parameters change — is always on and
needs no configuration.

### J.1 Pepper (a server-side secret mixed into every hash)

A pepper is a secret mixed into **every** password hash and stored in the **secret store**, never in
the database. It defends against a **database-only breach** — an injection flaw, a stolen backup, a
dishonest database administrator: without the pepper, the stolen hashes cannot be brute-forced. On a
fully compromised host, where the attacker takes both the database and the secret store, it adds
nothing. Its entire value is the *separation* between the two.

**Enable it:**

```jsonc
"Password": { "Pepper": { "Enabled": true } }
```

On startup the application provisions a pepper if none exists and stores it in `secrets.dpapi` under
`Password:Pepper:Keys:{id}` and `Password:Pepper:CurrentKeyId`. Only `Enabled` belongs in
appsettings; the key material is managed by the secret store. The application **refuses to start** if
peppering is enabled but the pepper cannot be persisted, because a pepper that lived only in memory
would lock everyone out on the next restart.

**Migration is automatic and safe.** Existing unpeppered hashes keep verifying and are transparently
upgraded on each user's next successful sign-in. No mass reset, no downtime. The seeded admin hash
behaves the same way.

> [!CAUTION]
> **Losing the pepper locks out every peppered user, permanently and unrecoverably.** Back it up with
> exactly the rigour you give the signing keys — it lives in the same secret store, so the Phase 9
> "secrets backed up" item covers it **provided that store is genuinely being backed up**. Treat
> enabling the pepper as a one-way decision unless you keep the key material yourself.

**Rotation, for the advanced case:** add a new pepper id, keep the previous ids in the store so old
hashes still verify, and point `Password:Pepper:CurrentKeyId` at the new id. New hashes and
next-sign-in re-hashes use the new pepper; old ones migrate as people sign in. Remove a retired id
only when you are certain no hash still uses it.

### J.2 Breached-password check (Have I Been Pwned)

Rejects or warns on passwords found in known breaches, using the free, keyless Pwned Passwords range
API with k-anonymity: only the first five characters of the password's SHA-1 hash leave your server,
and the password itself never does. It is checked in all five flows that set a password: verify-first
registration, registration by invitation, change, reset, and administrator-create.

```jsonc
"Password": {
  "BreachedPasswordCheck": {
    "Enabled": true,            // the shipped default; false = no external call from a password change
    "Mode": "Enforce",          // Enforce = reject; Warn = allow but flag
    "FailOpen": true,           // allow if the service fails or times out (logged); false = reject
    "RejectThreshold": 1,       // how many breach occurrences count as breached
    "TimeoutMs": 2000           // per call, 100-30000
  }
}
```

**These shipped values are the ones to keep.** A deployment that already overrides the section (in
`appsettings.Production.json`, or in the database from the console) keeps its own values: the
override wins over the shipped default. Read both before you deploy, and remove any value weaker
than the block above unless you have written down why:

```sql
SELECT JSON_QUERY(OverridesJson, '$.BreachedPasswordCheck')
FROM dbo.SystemSettingsOverrides WHERE SectionKey = N'Password';
```

**Every field is hot.** Switching the check on or off, changing `Mode`, `FailOpen`,
`RejectThreshold` or `TimeoutMs` in the console applies to the next request; nothing needs a
restart. Saving any of them asks for confirmation first.

- **Enforce** rejects a breached password with `User.PasswordBreached` and HTTP 400.
- **Warn** lets the operation succeed but adds an `X-Password-Warning` response header, plus
  `X-Password-Warning-Code: User.PasswordBreached`, so the application can nudge the user. It works
  on 204 No Content responses too.
- **`FailOpen: true`**, the default, means an outage at the breach service never blocks a password
  change; the event is logged. Set it to `false` only if you would rather hard-fail than risk
  admitting an unchecked password; the request is then refused with
  `User.PasswordBreachCheckUnavailable` (HTTP 500).
- **A timeout is an outage.** When the service answers slower than `TimeoutMs`, the request follows
  `FailOpen` exactly like any other failure. The log line says which it was:
  `Breached-password check failed (Timeout)` or `Breached-password check failed (Error)`, at Warning
  when failing open and at Error when failing closed. A client that disconnects while the breach check
  is in flight is not an outage: the request stops before the password is written, and no such line is
  logged.
- The check requires **outbound HTTPS from your server to `api.pwnedpasswords.com`**. On
  locked-down hosting, confirm that first: without it every new password waits `TimeoutMs` and is
  then accepted unchecked (`FailOpen`).
- **The log states the posture.** After startup and after every settings change, when something
  changed:
  - `Breached-password posture: Enabled=True, Mode=Enforce, FailOpen=True, RejectThreshold=1, TimeoutMs=2000`
    (Information) — the values in force;
  - `HIBP probe call: reachable in {ElapsedMs} ms` (Information) — a lookup of a value HIBP is known
    to list answered from this server;
  - `HIBP probe call failed ({FailureKind}) after {ElapsedMs} ms` — `Timeout`, `Error`, or
    `NotListed` (something answered, but not with HIBP's list: a proxy, a captive portal). Warning in
    Production;
  - `Breached-password screening weakened in Production: {Reasons}` (Warning) — `Enabled=False`,
    `Mode=Warn`, `TimeoutMs` below 500, or `RejectThreshold` above 1.

  After a deploy, look for the posture line and `HIBP probe call: reachable`; a day later, count
  `Breached-password check failed` to see how often HIBP was slow or unreachable.

`Password:MinimumLength` is separate policy, currently 8. Raising it to 12 is a reasonable hardening
step and affects only new and changed passwords.

## K. Two-factor authentication for platform administrators (`TwoFactor:EnforceForPlatformAdmins`)

**MFA** (multi-factor authentication) means proving two different things at sign-in: the password,
and a code from an authenticator app (or one of the recovery codes). A **platform administrator** is
any account that holds a platform permission — a role or a permission granted with no application.
Those accounts can manage every user, role and setting, so a stolen password alone must not be enough
for them.

**What the switch does.** While `TwoFactor:EnforceForPlatformAdmins` is `true`, a console or accounts
token for a platform administrator whose session has **not** proved a second factor carries **no
permissions and no roles**. The API answers such a token `403` with the code
`TwoFactor.RequiredByPolicy`, and both web applications send the person to `/two-factor/required`,
which offers exactly one step:

| The page says | Why | What the person does |
|---|---|---|
| Set up two-factor | The account has no second factor | Sets up an authenticator app (with the emailed code, while `Email:Enabled` is on) and saves the recovery codes |
| Enter a code | The session signed in with the password alone | Types a code from the authenticator app or a recovery code |
| Sign in again | This session's sign-in does not count as a first factor: it was opened before this version was deployed, it began with an emailed code (the sign-in that confirms an email address), or its record is gone | Signs out and back in with the password and the code |

After the first two steps they return to the page they were going to; signing in again starts from the sign-in page. Nothing else changes: tokens issued to
**applications** (with an application id), organization permissions, and every account without a
platform permission are never affected. While the switch is on, a platform administrator cannot turn
their own two-factor off (`403 TwoFactor.RequiredByPolicy`).

**It ships `false`, and stays `false` until all of these hold:**

1. this version is deployed, with its seed row first ("deploy 2", [K.5](#k5-deploy-2-and-the-drill));
2. the refresh fix that keeps a session's record alive while its tokens are renewed is merged and
   deployed (follow-up OI-101, which carries OI-103). Without it, an administrator signed in for more
   than `Jwt:RefreshTokenLifetimeDays` is asked to sign in again;
3. every platform administrator has a second factor (the inventory below is clean);
4. the drill of [K.5](#k5-deploy-2-and-the-drill) — an administrator's reset, then the emergency
   script — was run on a development database, and each time the next sign-in offered "Set up
   two-factor";
5. you, the owner, have confirmed production is stable.

**Until the switch is on, the rule is manual:** in production, no account gets a platform role or a
platform permission before it has its own second factor.

**While the switch is on, the product enforces that rule itself.** Giving an account a platform role
or a platform permission — or adding a permission to a platform role one of whose holders has no
second factor — is refused with `409 TwoFactor.RequiredForPlatformGrant`: the account sets up two-step
verification first, then the grant is made. The reason: an account promoted without a factor is
offered "Set up two-factor", and whoever enrols first owns the factor — with the emailed code that
means whoever holds the mailbox (a password reset by link is enough), or a linked sign-in provider and
its mailbox; without it (`Email:Enabled` off) whoever
holds the password alone. Grants scoped to an application or an organization are never refused.
*In code:* `Auth/Auth.Application/Common/PlatformGrantFactorGuard.cs`.

### K.1 Before you switch it on

1. **Deploy, with the switch off.** Every new sign-in now records how it was proven. Check one: a new
   console token carries `amr` and `auth_time` claims and no `mfa_req` claim, and the newest console
   and accounts rows of `UserSessions` (`ApplicationId IS NULL`) have `AuthMethods` set. Rows of
   sign-ins into other applications stay `NULL` by design.
2. **Run the inventory (read only).** It lists every account the switch would apply to, with the two
   places that say whether it has a second factor — accounts waiting for deletion included, because
   deletion keeps the grants and recovering the account signs it straight back in:

   ```sql
   WITH H AS (
     SELECT ur.[UserId] FROM [dbo].[UserRoles] ur
     JOIN [dbo].[Roles] r ON r.[Id] = ur.[RoleId]
     JOIN [dbo].[RolePermissions] rp ON rp.[RoleId] = ur.[RoleId]
     JOIN [dbo].[Permissions] p ON p.[Id] = rp.[PermissionId]
     WHERE ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL AND ur.[IsActive] = 1 AND r.[IsActive] = 1
       AND p.[IsActive] = 1 AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
     UNION
     SELECT up.[UserId] FROM [dbo].[UserPermissions] up JOIN [dbo].[Permissions] p ON p.[Id] = up.[PermissionId]
     WHERE up.[ApplicationId] IS NULL AND up.[IsActive] = 1 AND p.[IsActive] = 1
       AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE()))
   SELECT u.[Id], u.[Email], u.[LastLoginUtc], u.[IsDeleted], u.[IsTwoFactorEnabled], t.[IsEnabled] AS [FactorRowEnabled]
   FROM H JOIN [dbo].[Users] u ON u.[Id] = H.[UserId] LEFT JOIN [dbo].[TwoFactorAuth] t ON t.[UserId] = u.[Id]
   ORDER BY u.[IsTwoFactorEnabled], u.[Email];
   ```

   **Clean** means every row has `FactorRowEnabled = 1`, and `IsTwoFactorEnabled` equals
   `FactorRowEnabled` on every row — the rows with `IsDeleted = 1` too: enrol them, or remove their
   platform grant. The switch reads `FactorRowEnabled`, not `IsTwoFactorEnabled`.
3. **Every listed person signs out, signs back in, and sets up a second factor** (Profile → Security),
   saving the recovery codes somewhere other than the device that holds the authenticator app.
4. **Every listed account that no person signs in to** — a script, a shared or service account — either
   loses the platform grant, or gets a second factor of its own. Under the switch, a password alone no
   longer gives it any permission.
5. **Watch the API log for two days** ([Reference §I](#i-logs--where-they-are-and-how-to-find-them)).
   While the switch is off, every sign-in or refresh that the switch *would* strip logs a warning
   starting `PlatformMfa.WouldRequire`, naming the user and the step (`enroll`, `step_up` or
   `reauthenticate`). A `reauthenticate` line with `methods 0` comes from a session opened before the
   deploy, and stops once that person signs in again; `methods 4` or `12` is a session that began
   with an emailed code. Switch on only when the other lines are gone.

### K.2 Switching it on, and back off

**On:** **System settings → Two-factor authentication → Require two-factor authentication for platform
administrators**. The console asks for confirmation before it saves. It takes effect without a
restart, at each person's next sign-in or token refresh: a token issued before the switch keeps its
permissions until it expires (`Jwt:AccessTokenLifetimeMinutes`, 15 by default).

**Also put it in the server's `appsettings.Production.json`** (`"TwoFactor": { "EnforceForPlatformAdmins": true }`)
once you have decided. The console stores its value in the database. The switch fails closed: an API
that started and could not read the database settings yet enforces, whatever the files say, until its
first successful read — normally the next periodic refresh, at most five minutes later, but
indefinitely while the cause lasts (for example the API deployed before its database project, or a
login without `SELECT` on `SystemSettingsOverrides`). It logs one warning starting
`PlatformMfa.EnforcedSettingsUnavailable` when the window opens. Before you switch on, that window
behaves as if the switch were on: an administrator without a factor sees the "Set up two-factor" page;
a platform administrator whose session did not prove two factors (signed in with the password alone,
or before this version) is asked to step up or sign in again at the next token refresh; a platform
administrator cannot switch two-factor off; and a platform role or permission for an account without a
factor is refused (`TwoFactor.RequiredForPlatformGrant`). A refresh that fails
later keeps the values it last read, so it changes nothing. With `AUTH_DISABLE_DB_SETTINGS` set
([Reference §B.6](#b6-recovery--a-bad-value-saved-in-the-console)) the files are the whole
configuration, and the file's value applies. The console can still switch it off: its value wins over
the file.

**Check it:** create a temporary account holding only `auditlogs:read`, with no second factor, and sign
in to the console with it. It must land on the two-factor page, and a direct
`GET /api/v1/audit-logs` with its token must answer `403` with the code `TwoFactor.RequiredByPolicy`.
Delete the account afterwards.

**Off:** the same switch. Also instant — the next refresh gives the permissions back. Keep that undo
within reach for the **window after switching on**: until the inventory shows every platform
administrator with an enabled factor **and** at least `Jwt:RefreshTokenLifetimeDays` (7 by default)
have passed without anyone locked out. Only after that window is the switch planned to become
permanent.

**Off when no administrator can sign in** — the last resort. First try another administrator's
reset ([K.3](#k3-recovery-new-codes-a-new-authenticator-an-administrators-reset)), then the owner's
emergency script ([K.4](#k4-the-owners-emergency-script-break-glass)), which needs neither the console
nor the switch. If neither is possible, every step below runs on the server; none needs the console.
In order:

1. In the server's `appsettings.Production.json`, set `"TwoFactor": { "EnforceForPlatformAdmins": false }`.
2. Remove the value the console stored, so the file's `false` applies. Look first, then remove that one
   key and nothing else in the section:

   ```sql
   SELECT [OverridesJson] FROM [dbo].[SystemSettingsOverrides] WHERE [SectionKey] = N'TwoFactor';

   UPDATE [dbo].[SystemSettingsOverrides]
   SET [OverridesJson] = JSON_MODIFY([OverridesJson], '$.EnforceForPlatformAdmins', NULL),
       [Version] = [Version] + 1,
       [ModifiedAt] = SYSUTCDATETIME(),
       [ModifiedBy] = NULL
   WHERE [SectionKey] = N'TwoFactor';
   ```

   It applies at the next settings refresh, within five minutes; recycle the application pool to
   apply it at once. The next sign-in or refresh gives the permissions back.
3. **Only if the database cannot be reached:** set the environment variable `AUTH_DISABLE_DB_SETTINGS`
   to `true` ([Reference §B.6](#b6-recovery--a-bad-value-saved-in-the-console),
   [Reference §C](#c-environment-variables-on-plesk-and-cpanel)) with the file at `false`, and
   restart. Remove the variable once the database is back: while it is set, every value saved in
   the console is ignored.

**When someone cannot get past "Set up two-factor"** because the emailed code never arrives: fix
email first ([Phase 6](#phase-6--email-and-notifications)). If that cannot wait, switch this setting
off until mail works. Do **not** set `Email:Enabled` to `false` for it: enrolling then needs no emailed
code, so whoever holds an administrator's password alone can bind an authenticator of their own to an
account waiting on "Set up two-factor" — and the same goes for every other account.

### K.3 Recovery: new codes, a new authenticator, an administrator's reset

Four ways back for someone who loses part of their second factor, from the lightest:

| Lost | Way back | Who | Needs |
|---|---|---|---|
| The phone, codes kept | Sign in with a recovery code, then **Profile → Security → Replace authenticator app** | The person | A recovery code |
| Few codes left | **Profile → Security → Generate new codes** (the page warns at three or fewer, and once after a sign-in with a recovery code) | The person | A code from the app or a recovery code |
| The phone and every code | **Users → the account → Reset two-factor** in the console | Another administrator holding `users:reset-two-factor` (every holder of `users:*` or `*`) | Their own authority must cover the account's |
| Everything, and no other administrator | The emergency script ([K.4](#k4-the-owners-emergency-script-break-glass)) | The owner | The database and the server |

New codes and a new authenticator both need a sign-in from the last
`TwoFactor:ReauthenticationMaxAgeMinutes` (15 by default) **that proved two factors** — at sign-in or
by a step-up. A session opened before this version, or with the password alone, is asked to sign in
again first (`403 Auth.ReauthenticationRequired`). The old codes stop working as soon as the new ones
are created; a new authenticator keeps the old one working until a code from the new app confirms it
(within ten minutes, or the replacement starts again), and confirming it signs out every other session
and browser of the account (the one confirming stays). The owner is told by email each time.

**An administrator's reset** first asks the administrator for their own proof, whatever the switch
says: a sign-in from the last `TwoFactor:ReauthenticationMaxAgeMinutes` (15 by default) that proved
two factors — the rule for changing one's own factor. Otherwise it answers `403
Auth.ReauthenticationRequired` before the account is read, and the console offers to sign in again —
or, to an administrator with no second factor of their own, to set one up first (**Profile → Security**).
It is rate-limited like the sign-in. Then it signs the account out everywhere (sessions, refresh
tokens, single sign-on sessions, and every access token it holds), removes its second factor and
recovery codes, and emails its owner; the audit log records it as `twofactor.reset-by-administrator`,
with the administrator as the actor. In that order, a failure leaves the account signed out with its
factor intact, and the same reset, run again, completes. It is refused (`403
TwoFactor.ResetNotPermitted`) for the administrator's own account, for the internal system account, and
for an account holding platform permissions the administrator's own do not cover — a user manager
(`users:*`) cannot reset a super-administrator (`*`). A user manager **can** reset any account below
its authority: every application user and every organization owner included, whose grants are not
platform permissions.
The account's next sign-in offers "Set up two-factor": whoever sets it up first owns it (with the
emailed code first, while `Email:Enabled` is on).

### K.4 The owner's emergency script (break-glass)

For the case no administrator can fix in the console: the only administrator lost the authenticator
app and every recovery code. It works with one administrator, no second factor, and the switch on or
off. On a machine with the repository:

```
dotnet run --project Auth/Auth_Setup -- --reset-two-factor <the account's address>
```

It prints one transaction of SQL and opens no connection. **Back up the database**, then run the
statement against it in one batch. It removes the account's second factor and recovery codes, clears
its two-factor flag, revokes its refresh tokens and single sign-on sessions, ends its sessions, and
adds a revocation of every access token it holds. It stops with `No such user`, changing nothing, when
no live account has that address. **Then recycle the API's application pool:** the API reads the
revocation list when it starts, so until the recycle an access token issued before the script keeps
working, up to its lifetime. The recycle overlaps — the new worker takes over when the old one exits —
so it needs no downtime.

Know before you run it:

- **No audit row is written.** Keep your own record of when and why you ran it.
- The revocation entry lasts one hour. If you raised `Jwt:AccessTokenLifetimeMinutes` above 60, a
  token issued in the minutes before the script outlives it (follow-up OI-102).
- The next person to sign in to the account and set up two-step verification owns it (with the emailed
  code first, while email is on). Sign in and set it up yourself straight away.

*In code:* `Auth/Auth_Setup/BreakGlassScript.cs`; its exact text is pinned by
`Auth/Auth_API.Tests/Setup/BreakGlassScript.golden.sql`.

### K.5 Deploy 2, and the drill

**Deploy 2 — before the API of this version.** The administrator's reset is guarded by a new
permission row, `users:reset-two-factor` (Id `20000000-0000-0000-0000-000000000217`, under `users:*`).
Publish the database project **before** you deploy the API, with a backup first, and generate the
script first and read it before you run it. What it may contain depends on the release the database is
at:

- **at the previous release (the platform-appearance change, #58, already published):** the
  post-deployment seed and nothing else — no `CREATE`, `ALTER` or `DROP` statement. The new line in
  the seed is the `…217` row;
- **one release further back (#58 not published yet):** the same, plus exactly #58's three changes:
  `ALTER TABLE [dbo].[Applications] ADD [LogoUrlDark] NVARCHAR (500) NULL`,
  `ALTER TABLE [dbo].[PlatformSettings] ADD [Theme] NVARCHAR (1000) NULL`, and the check constraint
  `CK_PlatformSettings_ThemeIsJson` (added `WITH NOCHECK`, then checked at the end).

Anything else, and any `DROP` statement (the word also appears inside an email template's style
comment; that is not a statement): stop. Nothing has run, so there is nothing to restore. Check:

```sql
SELECT [Code], [IsActive] FROM [dbo].[Permissions] WHERE [Id] = '20000000-0000-0000-0000-000000000217';
```

Expected: `users:reset-two-factor`, `1`. Nothing needs undoing if you stop here: holders of `users:*`
and `*` already cover the code by prefix.

**After the API is deployed:** at least five minutes after it starts, its log must hold no new line
starting `PlatformMfa.EnforcedSettingsUnavailable`. One that stays means the API cannot read its
database settings: it enforces the switch until it can (K.2).

**The drill — on a development database, never on production.** Before the switch is ever turned
on, prove both ways back work. The switch must be **on** in the development database for the drill
to show anything: with it off, an account without a factor signs in normally.

0. Two test administrators each turn on two-step verification (**Profile → Security**). On the
   development console, switch on **System settings → Two-factor authentication → Require
   two-factor authentication for platform administrators**. The acting administrator signs in with
   their code (the reset asks for a recent two-step sign-in).
1. As the acting administrator, reset the other one's second factor from the console (**Users → the
   account → Reset two-factor**). Sign in as that account: the console must show "Set up two-factor",
   with the emailed code first when email is on. A failure looks like: the reset answers "sign in
   again" (the acting session is not a recent two-step one — sign in again with the code), or the
   account reaches the dashboard (the switch is off, or the reset did not run).
2. Run the emergency script of [K.4](#k4-the-owners-emergency-script-break-glass) for the same
   account (after it set its factor up again) on the development database, then restart the
   development API — the development equivalent of the application-pool recycle. Sign in again:
   "Set up two-factor" again. A failure looks like: `No such user` (the address is not that account's),
   or the old session still working after the restart (the restart did not happen).
3. Switch it **off** again, and confirm that an administrator without a factor reaches the dashboard.
   A failure looks like: the "Set up two-factor" page still shows after a sign-out and sign-in (the
   value did not save, or the API has not read its settings — see the warning above).

## L. Refresh tokens: the application replay grace and the session row

A refresh token is single-use. Presenting one that was already spent is treated as theft: every session
of the user ends, in every application and on the sign-in page, and the user gets a security alert.

### L.1 `Jwt:ApplicationRefreshReplayGraceSeconds`

When an application's refresh response is lost on its way back (a timeout, a dropped mobile
connection), the application still holds the old refresh token, and its retry would be that theft. With
this setting, the retry is answered **once** more instead, if it arrives within this many seconds of the
first refresh and carries a `client_id` naming the application the token was issued to. A retry without
`client_id`, after the window, or a second time, is still theft. A `client_id` naming another
application is refused with `Auth.InvalidClient`, and nothing about the token changes.

* **Values:** 30 seconds by default, in `appsettings.json`, in the settings class and in the console;
  `0` turns it off; `60` at most (a larger value in a file is read as 60). It is read at every refresh, so
  a change applies at once, with no restart. In the console: **System settings → Tokens (JWT) → How
  long tokens last** («إعدادات النظام» › «الرموز (JWT)» › «مدة صلاحية الرموز»), the row **Application
  refresh replay grace (seconds)** («مهلة إعادة تقديم رمز التحديث للتطبيقات (بالثواني)»), right after
  the cookie window.
* **What it costs:** inside the window, a thief of the previous refresh token is as good as a thief of
  the current one. The theft is detected only when the application refreshes again while the thief's
  chain is still live, which can be days for an idle application; and a thief who revokes its own token
  (`POST api/v1/auth/revoke`) ends the session without any alert. An application's `client_id` is
  public, so the check stops another application, not a thief of this one.
* **One answer changes with `client_id`:** a switched-off application that sends `client_id` now gets
  400 `Auth.InvalidClient`, because the client check runs first. It used to get a 403
  (`Auth.RefreshTokenRevoked`, since switching an application off revokes its tokens, or
  `Application.Inactive`), and still does without `client_id`.
* **Undo:** setting `0` removes the grace only. The `client_id` check (a wrong, unknown or switched-off
  `client_id` answers 400 `Auth.InvalidClient`) and the sliding session row (L.2) stay until the
  release is reverted, and a revert also brings back the old session write, under which a refresh
  racing a sign-out could undo the sign-out.
* **The console and accounts apps** have their own window, `Jwt:RefreshReplayGraceSeconds`, for the
  refresh cookie only. Neither window applies to the other's tokens.

### L.2 The session row follows its refresh token

Each refresh moves the session row's expiry (`UserSessions.ExpiresAt`) to the expiry of the refresh
token it hands out, and never backwards. A session that a sign-out ended stays ended. The write is
best-effort: if it fails, the refresh still succeeds and the next refresh slides the row. So a session
in daily use stays in the console's list of active sessions after `Jwt:RefreshTokenLifetimeDays`, the
daily expiry sweep ends the sessions nobody refreshed, and the platform-administrator rule of
[§K](#k-two-factor-authentication-for-platform-administrators-twofactorenforceforplatformadmins) keeps
reading what a long-lived session proved, instead of asking its administrator to sign in again every
`Jwt:RefreshTokenLifetimeDays`.

**Rows the sweep ended before this version stay ended, and their sessions do not.** Nothing in the
refresh reads a row's end, so the refresh chains behind those rows keep working. They do not appear in
the list of active sessions, and they survive a password change, "sign out other sessions" and
switching two-factor off, because those keep the current session and end only the sessions the list
shows. [L.3](#l3-end-the-sessions-the-sweep-left-behind-once-after-the-api-deploy) ends them once. A
chain whose row is missing, or ended by something other than the sweep, still survives those three
actions until follow-up OI-114 changes how they revoke.

**Check it (read only)**, after the API deploy and after a console page has refreshed its token (about
15 minutes after signing in). Each row refreshed since the deploy must carry exactly the expiry of its
newest live refresh token:

```sql
DECLARE @DeployedAtUtc DATETIME2 = '2026-01-01T00:00:00';  -- replace with the API deploy time, in UTC

SELECT TOP 10 s.[Id], s.[StartedAt], s.[LastActivityAt], s.[ExpiresAt],
       MAX(r.[ExpiresAt]) AS [NewestLiveTokenExpiresAt],
       DATEDIFF(SECOND, s.[ExpiresAt], MAX(r.[ExpiresAt])) AS [SecondsBehind]
FROM [dbo].[UserSessions] s
JOIN [dbo].[RefreshTokens] r
  ON r.[SessionId] = s.[Id] AND r.[RevokedAt] IS NULL AND r.[ExpiresAt] > SYSUTCDATETIME()
WHERE s.[EndedAt] IS NULL
  AND s.[LastActivityAt] > @DeployedAtUtc
  AND s.[LastActivityAt] > DATEADD(MINUTE, 10, s.[StartedAt])
GROUP BY s.[Id], s.[StartedAt], s.[LastActivityAt], s.[ExpiresAt]
ORDER BY s.[LastActivityAt] DESC;
```

Expect at least one row, and `SecondsBehind` = 0 on every row: the refresh writes the same value to
the token and to the row. A positive `SecondsBehind` means the row did not follow its chain; under the
old behaviour it equals roughly the time between the sign-in and the last refresh. To see that failure
once, run the query before the deploy with `@DeployedAtUtc` an hour earlier: rows the old API refreshed
show it.

### L.3 End the sessions the sweep left behind (once, after the API deploy)

Run this once, on the database, **after** the API deploy (so no new row of this kind appears in
between). It revokes the still-live refresh tokens of every session whose row is already ended. The
reason it writes, `Token revocation requested`, is an existing one
(`TokenRevocationReasons.RevocationRequested`, what the revocation endpoint writes). Any reason other
than `Rotated` marks the
token as ended with its session, so its next presentation is answered 403 `Auth.RefreshTokenRevoked`
("session ended"): no sign-out of anything else, no security alert. Those users sign in once; their
access tokens stop within `Jwt:AccessTokenLifetimeMinutes`. There is no undo, and none is needed.

1. Count (read only). Write the number down:

   ```sql
   SELECT COUNT(*) AS [LiveTokensOfEndedSessions]
   FROM [dbo].[RefreshTokens] rt
   INNER JOIN [dbo].[UserSessions] s ON s.[Id] = rt.[SessionId]
   WHERE s.[EndedAt] IS NOT NULL
     AND rt.[RevokedAt] IS NULL
     AND rt.[ExpiresAt] > SYSUTCDATETIME();
   ```

2. Dry run: the revocation inside a transaction that is rolled back. `[WouldRevoke]` should equal the
   count (a token that expired in between can make it a little lower):

   ```sql
   BEGIN TRAN;
   UPDATE rt
   SET rt.[RevokedAt] = SYSUTCDATETIME(),
       rt.[RevokedBy] = NULL,
       rt.[ReasonRevoked] = N'Token revocation requested'
   FROM [dbo].[RefreshTokens] rt
   INNER JOIN [dbo].[UserSessions] s ON s.[Id] = rt.[SessionId]
   WHERE s.[EndedAt] IS NOT NULL
     AND rt.[RevokedAt] IS NULL
     AND rt.[ExpiresAt] > SYSUTCDATETIME();
   SELECT @@ROWCOUNT AS [WouldRevoke];
   ROLLBACK;
   ```

3. The revocation: the same batch with the last line removed. Run it, read `[WouldRevoke]`, then run
   `COMMIT;` alone in the same window if it matches step 2, or `ROLLBACK;` if it does not. Do not leave
   the transaction open: refreshes of those users wait for it.
4. Count again with step 1's query. It must read **0**.

## M. The password hard lock (`Password:ConsecutiveFailureLimit`)

Before this change every 5th wrong password locked the account for 15 minutes, and the first attempt
after the window wiped the count. A guesser who simply waited got about 480 tries a day for ever. Now
the count is kept when a timed lock ends, and after **100 consecutive wrong passwords** the account
stays locked with **no expiry** (the *hard lock*, NIST SP 800-63B-4 §3.2.2). The sign-in page then
answers `User.AccountLockedResetRequired`: "reset your password to unlock it".

What lifts the hard lock and restarts the count: a completed password reset, a successful password
change from an open session, a sign-in with the password from an address the account signed in from
in the last 30 days or a device with a live session (until the count passes the limit by a further 5),
or an administrator's **Unlock**, **Lock** or **Activate**. The lock and the per-address limit are
judged again after a correct password, so a correct guess inside a burst of wrong ones neither signs in
nor lifts the lock. A sign-in with an external provider is refused under the hard lock. Sessions that already exist keep renewing while the account is locked; only an
administrator's lock signs the owner out. An account pending deletion is never locked. The second
factor has its own counter and its own cap, with the same limit: section M.4.

* **Settings:** `Password:ConsecutiveFailureLimit` (10–100, default 100, no "off") and
  `Password:ConsecutiveFailureMode` (`Enforce`, the default, or `Legacy`). Both are read at every
  sign-in, so a change applies at once. In the console: **System settings → Password policy → After
  repeated failed sign-ins** («إعدادات النظام» › «سياسة كلمات المرور» › «بعد محاولات دخول فاشلة
  متكرّرة»), the rows **Consecutive failures before a hard lock** («الإخفاقات المتتالية قبل القفل
  الدائم») and **Consecutive-failure limit** («حدّ الإخفاقات المتتالية»). Saving either asks for
  confirmation.
* **No database publish.** The hard lock uses the existing `Users` columns.

### M.1 Before the deploy: three read-only counts

Run them against the production database, with `sqlcmd` or SSMS, **while the old build is still
running**. They change nothing. Write down when you ran them (UTC): M.2 (4) needs that time. Record each
count and the ids it lists. (The plan's step LK9.4 also counts second factors at the limit; that count
is M.4.1, run before the second part's deploy.)

The console controls named below are on **المستخدمون** › the user › the actions menu: **«قفل»** (Lock,
with the field «مدة القفل (دقائق)», empty for no expiry) and **«إلغاء القفل»** (Unlock, shown while the
account is locked).

1. Accounts already at the limit. Expected **0**:

   ```sql
   SELECT COUNT(*) AS [AtTheLimit]
   FROM [dbo].[Users]
   WHERE [IsDeleted] = 0 AND [FailedLoginAttempts] >= 100;
   ```

   If it is not 0, list them (`SELECT [Id], [Status] …` with the same WHERE). A locked one: press
   «إلغاء القفل», which sets its count to 0. An active one has no console control that sets the count
   without signing it out; its count returns to 0 at its next successful password sign-in, and until then
   its next wrong password locks it with no expiry and the sign-in page tells its owner to reset the
   password.

2. Locks with no expiry and a count of 0 that no administrator placed since the last sign-in. The old
   sign-in left these behind: a success cleared the count and the expiry but kept the status. Each one
   reads as an administrator's lock and nobody can sign in:

   ```sql
   SELECT u.[Id], u.[LastLoginUtc]
   FROM [dbo].[Users] u
   WHERE u.[IsDeleted] = 0 AND u.[Status] = 3 AND u.[FailedLoginAttempts] = 0 AND u.[LockoutEndUtc] IS NULL
     AND NOT EXISTS (
       SELECT 1 FROM [dbo].[AuditLogs] a
       WHERE a.[UserId] = u.[Id] AND a.[Action] = N'user.locked'
         AND (u.[LastLoginUtc] IS NULL OR a.[Timestamp] > u.[LastLoginUtc]));
   ```

   For each id, check who locked it (the user's audit tab). If no administrator did, press «إلغاء القفل».

3. Locks with a count above 0 that are not expired, with the latest administrator lock and the latest
   wrong password of each:

   ```sql
   SELECT u.[Id], u.[FailedLoginAttempts], u.[LockoutEndUtc],
     (SELECT MAX(a.[Timestamp]) FROM [dbo].[AuditLogs] a
       WHERE a.[UserId] = u.[Id] AND a.[Action] = N'user.locked') AS [LastAdministratorLock],
     (SELECT MAX(l.[AttemptedAt]) FROM [dbo].[LoginAttempts] l
       WHERE l.[UserId] = u.[Id] AND l.[FailureReason] = N'Invalid password') AS [LastWrongPassword]
   FROM [dbo].[Users] u
   WHERE u.[IsDeleted] = 0 AND u.[Status] = 3 AND u.[FailedLoginAttempts] > 0
     AND (u.[LockoutEndUtc] IS NULL OR u.[LockoutEndUtc] > SYSUTCDATETIME());
   ```

   A row whose `LastAdministratorLock` is newer than its `LastWrongPassword`, or whose expiry is empty,
   is an administrator's lock written before Lock started setting the count to 0 (`7c6a0c52`). The
   "expiry is empty" reading holds **only now, while the old build runs**: the old build never writes a
   lock with a count and no expiry, so that shape can only be an administrator's. After the deploy the
   same shape is the hard lock (M.2 (4)). Left as it is, such a row would read as an automatic lock after
   the deploy, which a familiar device may pass. So press «قفل» on it again, with «مدة القفل (دقائق)» =
   the minutes from now to its old `LockoutEndUtc` (empty when it had none): Lock starts it at a count of
   0. Like every administrator's lock, it also signs the user out of every device and revokes their
   tokens. Leave every other row alone.

**Undo** for an «إلغاء القفل» pressed here: «قفل» with the original duration, which also revokes the
user's credentials.

### M.2 The deploy

1. Back up the database. Tell the teams whose applications sign in through this server.
2. Publish and upload the Auth API. Then build and upload the console and the accounts app. No database
   publish, no gateway change. Write down when the new API started (UTC).
3. In the console, check that the two rows above show **Enforce** and **100**.
4. Run count 2 again, as it is, and handle its rows the same way.

   Do **not** run count 3 again as it is. After the deploy, a locked row with a count above 0 and no
   expiry **is the hard lock: never Lock it again.** «قفل» would turn it into an administrator's lock,
   which a password reset no longer lifts, and would sign its owner out: the owner who was told "reset
   your password" would reset it and stay locked out. The hard lock's exits are a password reset, a
   sign-in with the password from an address or device the account used recently, and «إلغاء القفل».
   Instead, run this query with the two times filled in. It lists only rows of count 3 that an
   administrator locked between M.1 and the deploy; press «قفل» again on those, as in M.1 (3):

   ```sql
   DECLARE @CountedAt DATETIME2 = '2026-01-01T00:00:00';  -- when M.1 ran (UTC)
   DECLARE @DeployedAt DATETIME2 = '2026-01-01T00:00:00'; -- when the new API started (UTC)
   SELECT u.[Id], u.[FailedLoginAttempts], u.[LockoutEndUtc]
   FROM [dbo].[Users] u
   WHERE u.[IsDeleted] = 0 AND u.[Status] = 3 AND u.[FailedLoginAttempts] > 0
     AND (u.[LockoutEndUtc] IS NULL OR u.[LockoutEndUtc] > SYSUTCDATETIME())
     AND EXISTS (
       SELECT 1 FROM [dbo].[AuditLogs] a
       WHERE a.[UserId] = u.[Id] AND a.[Action] = N'user.locked'
         AND a.[Timestamp] >= @CountedAt AND a.[Timestamp] < @DeployedAt);
   ```

**Effective when:** merged, deployed, and steps M.1 and M.2 done.

### Rollback

**`Legacy` is a switch, not a rollback.** Setting `Password:ConsecutiveFailureMode` to `Legacy` (no
restart) changes exactly three things: no new hard lock is written, a lock whose time has passed starts
the count again, and an account pending deletion has no per-address limit on its sign-in and recovery.
It undoes nothing else: an existing hard lock keeps its exits, sessions still renew under an automatic
lock, a provider sign-in or an emailed code still lifts no lock, Activate still sets the count to 0, and
profile edits still never write the lock.

To revert the code, in this order:

1. Set `Legacy` first, so that no new hard lock appears.
2. List the hard locks, including accounts pending deletion (the console cannot unlock a deleted
   account):

   ```sql
   SELECT u.[Id], u.[IsDeleted]
   FROM [dbo].[Users] u
   WHERE u.[Status] = 3 AND u.[LockoutEndUtc] IS NULL AND u.[FailedLoginAttempts] > 0
     AND (u.[IsDeleted] = 0
          OR EXISTS (SELECT 1 FROM [dbo].[AccountDeletionRequests] r
                     WHERE r.[UserId] = u.[Id] AND r.[Status] = 1));  -- 1 = pending, recoverable
   ```

3. Press «إلغاء القفل» on each row with `IsDeleted = 0`. For each row with `IsDeleted = 1`, run this
   statement, one id at a time; it changes nothing if the row is no longer a hard lock:

   ```sql
   DECLARE @Id UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
   UPDATE [dbo].[Users]
   SET [Status] = 1, [FailedLoginAttempts] = 0, [LockoutEndUtc] = NULL, [ModifiedAt] = SYSUTCDATETIME()
   WHERE [Id] = @Id AND [Status] = 3 AND [LockoutEndUtc] IS NULL AND [FailedLoginAttempts] > 0;
   ```

4. Know what a hard lock left in place does after the revert: the old code reads it as an administrator's
   lock, refuses its renewal and revokes every refresh token of the account at its next refresh, so its
   owner is signed out of every device and cannot sign back in.
5. Active accounts keep their count. Under the old code, an account whose count is already 5 or more locks
   again at its next wrong password, until a successful sign-in. To start them all at 0 (optional):

   ```sql
   UPDATE [dbo].[Users] SET [FailedLoginAttempts] = 0
   WHERE [IsDeleted] = 0 AND [Status] = 1 AND [FailedLoginAttempts] > 0;
   ```

6. Deploy the previous build of the Auth API, then of the console and the accounts app.

### M.3 Day 16: the review, then the clean-up

From the 16th day after the deploy:

1. Count the hard locks with the query in Rollback (2), and the log lines `account.hard-locked` and
   `account.unlocked` in the Auth API's Serilog files (section I).
2. Check that nobody switched to Legacy:

   ```sql
   SELECT [Timestamp], [PerformedBy] FROM [dbo].[AuditLogs]
   WHERE [Action] = N'system-settings.updated'
     AND [NewValues] LIKE N'%ConsecutiveFailureMode%Legacy%';
   ```

   And check that the console shows **Enforce** as the value in effect for «حدّ الإخفاقات المتتالية»: a
   Legacy set by a file or an environment variable leaves no audit row.
3. With the numbers recorded, the follow-up change SUNSET(X12) removes `Legacy` and the setting.

### M.4 The second-factor cap (the second part of X12)

Before this part, wrong second-factor codes locked the factor for 15 minutes after the 5th, and every
later wrong code locked it again, with no ceiling: someone who already had the password got about 96
code guesses a day, about 35,000 a year, recovery codes included. Now the second factor has the same
ceiling as the password: after **`Password:ConsecutiveFailureLimit` (100 by default) consecutive wrong codes**, from
the authenticator app and recovery codes together, it accepts **no code at all**, and waiting does not
end it. Sign-in, step-up, and every change to the factor answer `TwoFactor.LockedAdminResetRequired`:
"reset your password from the sign-in page, then ask an administrator to reset two-factor
authentication". The accounts app shows that sentence with a link to the forgotten-password page. A
pending factor at the limit (one being set up) refuses a new setup with the same code, so its secret is
not replaced by one that could never be enabled.

* **Who is told.** At the moment the limit is reached, the account's owner and up to 10 administrators who
  could reset the factor for that account get an email (type `two-factor-hard-locked`), the owner's first.
  An administrator qualifies when they hold `users:reset-two-factor` and `users:read` (directly or
  through `users:*` or `*`; the email's link opens the account's page, which asks `users:read`),
  their own platform permissions cover the account's, they have two-factor authentication of their own
  (the reset asks their recent two-factor sign-in), their account is active with a confirmed address, and
  they are not the account itself. The API's log says how many were told; at **Warning** when none was.
  None told does not mean no way out: an administrator who holds the permission but has no two-factor
  authentication of their own sets it up first (Profile › Security › Enable two-factor, «الملف الشخصي» ›
  «الأمان» › «تفعيل المصادقة الثنائية»), signs in again with the code, and then resets (M.4.4). The
  emergency script is the exit only when no administrator holds the permission. Step 0 of M.4.1 counts
  the administrators who can act today. The emails say "whoever entered the
  codes passed the first sign-in step": the password, or a sign-in provider linked to the account (Google
  and the like), since a provider sign-in opens the same second-factor step.
* **Accounts waiting for deletion: no cap at the recovery of a deletion the owner asked for while
  signed in** (owner decisions, 2026-10-12, deviation rows "X12 PR B pending deletion" (b) and its
  re-opening, D-66-1 (a)). No way out of the hard lock reaches an account waiting for deletion (the
  forgotten-password page, an administrator's reset and the emergency script all read live accounts
  only), so a cap there would cost its owner the account when the grace period ends. A deletion asked
  from a signed-in session (in the console or the accounts app; whoever asked passed the sign-in, the
  second factor included) therefore recovers (`deletion/recover`) under the timed lock alone: five wrong
  codes lock the factor for 15 minutes, as before, and reaching 100 there is no lock, no line and no
  email. A deletion asked through the public no-login page proved the mailbox alone, so it recovers
  under the cap like every other door. **What stays open:** (1) someone who holds the password of an
  account whose owner asked for its deletion while signed in gets about 96 code guesses a day there,
  about 2,885 in the default 30-day grace period (ARR-0633); (2) an owner who used the public page, and
  whose factor a password holder then drives to the limit, cannot recover during the grace period, and
  the account is deleted at its end. Anyone with the mailbox could already delete the account through
  that page.
* **The way out (the rebind).** A password reset alone does **not** unlock the factor. The owner first
  resets the password by email; then an administrator resets two-factor authentication (or the server
  owner runs the emergency script); the owner then sets a new factor up. The administrator's reset is
  refused with `TwoFactor.PasswordResetRequiredFirst` (403) until the owner's password reset is **later**
  than the failure that locked the factor, and nothing is revoked when it is refused. A password change
  from a signed-in session counts too. That is weaker than it sounds: changing the password asks only the
  current password and a signed-in session, and a session opened before the factor was set up never
  proved it. So the gate shows that the password moved, not who moved it; confirming the person outside
  email (M.4.4, step 1) is what tells the owner from an intruder. A sign-in that only re-hashes the same
  password (after an Argon2 or pepper change) does **not** count. Nor does the reset remove sign-in
  providers linked to the account: the owner accepted that as a residual (deviation row "X12 PR B
  provider", option (c)); the email tells the owner to secure the provider account.
* **Settings.** No new setting. The cap reads the same two rows as section M, at every attempt.
  `Legacy` also turns the cap off: a factor already at or past the limit then accepts codes again on the
  five-then-fifteen-minutes rhythm, because its count is its whole state. Switching back to `Enforce`
  hard-locks again, at once and with **no email**, every factor at or past the limit by then: run M.4.1's
  count before switching back.
* **Database publish: yes.** It adds the notification type `two-factor-hard-locked` and its template
  (inserts only), and drops three stored procedures that nothing calls (`sp_CheckAccountLockout`,
  `sp_RecordLoginAttempt`, `sp_ValidateCredentials`) through the post-deployment step
  `Upgrades/2026-10-12_DropDeadLockProcedures.sql`. No table, column, index or constraint changes.

#### M.4.1 Before the deploy: one read-only count

Run it against the production database while the old build is still running. It changes nothing. Write
down when you ran it (UTC) and the ids it lists. Run the second count below too. `100` is the default of «الإخفاقات المتتالية قبل القفل
الدائم» (**Consecutive failures before a hard lock**); if the setting holds another value, put that value
in its place.

```sql
SELECT t.[UserId], t.[FailedAttempts], t.[LockedUntil], t.[ModifiedAt], u.[IsDeleted], u.[LastPasswordChangeUtc]
FROM [dbo].[TwoFactorAuth] t
JOIN [dbo].[Users] u ON u.[Id] = t.[UserId]
WHERE t.[FailedAttempts] >= 100;
```

Expected **0**. Each row it lists is hard-locked from the deploy on, with **no email** (the email is sent
only at the moment the limit is reached), and stays locked. Do nothing to those rows now; when an owner
asks, follow M.4.4.

The administrators who could reset a hard-locked factor today (active, confirmed, on the platform, holding
`users:reset-two-factor`, `users:*` or `*`, with an enabled factor of their own):

```sql
SELECT COUNT(DISTINCT u.[Id]) AS [AdministratorsWhoCanReset]
FROM [dbo].[Users] u
JOIN [dbo].[TwoFactorAuth] t ON t.[UserId] = u.[Id] AND t.[IsEnabled] = 1
WHERE u.[IsDeleted] = 0 AND u.[Status] = 1 AND u.[IsEmailConfirmed] = 1
  AND u.[Id] IN (
    SELECT ur.[UserId]
    FROM [dbo].[UserRoles] ur
    JOIN [dbo].[Roles] r ON r.[Id] = ur.[RoleId]
    JOIN [dbo].[RolePermissions] rp ON rp.[RoleId] = r.[Id]
    JOIN [dbo].[Permissions] p ON p.[Id] = rp.[PermissionId]
    WHERE ur.[ApplicationId] IS NULL AND r.[ApplicationId] IS NULL AND ur.[IsActive] = 1 AND r.[IsActive] = 1
      AND p.[IsActive] = 1 AND (ur.[ExpiresAt] IS NULL OR ur.[ExpiresAt] > GETUTCDATE())
      AND p.[Code] IN (N'users:reset-two-factor', N'users:*', N'*')
    UNION
    SELECT up.[UserId]
    FROM [dbo].[UserPermissions] up
    JOIN [dbo].[Permissions] p ON p.[Id] = up.[PermissionId]
    WHERE up.[ApplicationId] IS NULL AND up.[IsActive] = 1 AND p.[IsActive] = 1
      AND (up.[ExpiresAt] IS NULL OR up.[ExpiresAt] > GETUTCDATE())
      AND p.[Code] IN (N'users:reset-two-factor', N'users:*', N'*'));
```

Expected **1 or more**. With **0**, set up your own two-factor authentication first (Profile › Security ›
Enable two-factor, «الملف الشخصي» › «الأمان» › «تفعيل المصادقة الثنائية») before you rely on the notice;
otherwise no administrator is told and the only exit is the emergency script. (It does not apply the
no-amplification rule account by account: an administrator it counts may still be refused for an account
that holds permissions they do not.)

Before any later **lowering** of «الإخفاقات المتتالية قبل القفل الدائم», run the same count with the new
value in place of 100: every factor it lists is hard-locked the moment you save, with no email.

#### M.4.2 The deploy

1. Back up the database. Tell the teams whose applications sign in through this server.
2. **If the deploy of section M has not run yet,** together with the database publish of the
   machine-credential change (the `client_credentials` grant and its two tables, follow-up OI-131), make
   it one deploy: one DACPAC that carries those two tables and this part's seed and drop, then the Auth
   API, then the console and the accounts app.
3. **Otherwise,** a deploy of its own:
   - Build the DACPAC (Release), use **Generate Script** against the production database, and read the
     script. Schema operations allowed: only the nine known CK constraint pairs (dropped and re-created
     with the same values; follow-up OI-36), and the machine-credential tables if their publish has not
     happened. The post-deployment part runs every seed step, as on every publish; what is new there is
     the guarded insert of the notification type `40000000-0000-0000-0000-000000000023` and its template,
     and Step 14's three `DROP PROCEDURE IF EXISTS` lines. **Anything else: stop.**
   - Publish the DACPAC, then publish and upload the Auth API, then build and upload the console and the
     accounts app.

#### M.4.3 Checks after the deploy

1. The notification type:

   ```sql
   SELECT [Code] FROM [dbo].[NotificationTypes] WHERE [Id] = '40000000-0000-0000-0000-000000000023';
   ```

   It reads `two-factor-hard-locked`.
2. The dead procedures are gone:

   ```sql
   SELECT OBJECT_ID(N'dbo.sp_CheckAccountLockout'), OBJECT_ID(N'dbo.sp_RecordLoginAttempt'), OBJECT_ID(N'dbo.sp_ValidateCredentials');
   ```

   It reads NULL three times.
3. In the console, **System settings → Password policy** («إعدادات النظام» › «سياسة كلمات المرور»): the hint
   under **Consecutive failures before a hard lock** («الإخفاقات المتتالية قبل القفل الدائم») now names the
   second factor.

**Effective when:** merged; the DACPAC, the Auth API and both apps deployed; M.4.1 done.

#### M.4.4 Unlocking a hard-locked second factor (on the owner's request)

For an account listed by M.4.1, or one you were emailed about:

1. Confirm it is really the account's owner, **outside email** (whoever drove the factor to the limit
   knew the password, and may control the mailbox too): by phone, in person, or through a channel you
   already trust.
2. The owner resets the password by email, from **Forgot password?** («هل نسيت كلمة المرور؟») on the
   sign-in page.
3. In the console: **Users** › the user › in the details, the **Reset two-factor** button beside
   **Two-factor** («المستخدمون» › the user › the «إعادة ضبط المصادقة الثنائية» button beside «المصادقة
   الثنائية»). It shows only when you hold `users:reset-two-factor`, and it needs your own recent sign-in
   with two-factor authentication. A 403 `TwoFactor.PasswordResetRequiredFirst` means step 2 has not
   happened yet: the gate is working.
4. The owner signs in and sets up two-factor authentication again.

With no administrator who can do step 3 (one who holds the permission but has no two-factor authentication
of their own can enable it first, as in M.4.1, then do step 3), the server owner runs the emergency script
(`dotnet run --project Auth/Auth_Setup -- --reset-two-factor <address>`). It is not gated: its first
lines are comments with the same rule and a read-only check of `FailedAttempts`, `ModifiedAt` and
`LastPasswordChangeUtc` for the address. For a factor at the limit, run the script only after step 2,
and only if `LastPasswordChangeUtc` is later than `ModifiedAt`. A factor at the limit whose
`ModifiedAt` is NULL never satisfies the gate (it fails closed): the script is its only exit, after the
same two steps.

#### M.4.5 Rollback and day 16

* **Switch:** `Legacy` (no restart) turns the cap off; factors at the limit accept codes again on the old
  rhythm. It does not touch the password's hard lock exits (section M, Rollback).
* **Code revert:** deploy the previous Auth API and apps. The notification type and template stay
  (harmless: nothing sends them); the dropped procedures stay dropped (nothing ever called them).
* **Day 16** (with M.3): also count the log lines `two-factor.hard-locked` in the Auth API's Serilog files,
  and the factors at or past the limit:

  ```sql
  SELECT COUNT(*) AS [SecondFactorsAtTheLimit] FROM [dbo].[TwoFactorAuth] WHERE [FailedAttempts] >= 100;
  ```

  (`100`, or the value «الإخفاقات المتتالية قبل القفل الدائم» holds.)

  SUNSET(X12) counts its 16 days from the **later** of the two deploys.

## N. Pending sign-in requests behind a handle (`IdentityProvider:AuthorizationRequestHandlesEnabled`)

When the authorize endpoint finds no session, it sends the browser to an accounts page (sign-in,
registration, organization creation). That page used to carry the application's whole authorize URL
in `returnTo`, and a web application firewall (WAF) commonly reads a parameter holding an off-site
`https://` address as a remote file inclusion and answers **403**. Now the API stores the
request in `dbo.AuthorizationRequests` and the page carries
`?client_id=<application code>&auth_request=<64 lowercase hex characters>`; the accounts application
resumes it at `/api/v1/auth/authorize/resume`, where every check runs again.

* **Settings** (System settings → Access → Identity provider (SSO), «إعدادات النظام» › «الوصول» ›
  «موفر الهوية (SSO)»; hot, no restart):
  * `AuthorizationRequestHandlesEnabled` — **on by default**, in `appsettings.json`, the settings class
    and the console alike, under the label **Short sign-in links (request handle)** («روابط دخول قصيرة
    (مرجع الطلب)»). It only gates **creating** handles: a handle already issued still resumes when it is
    off. It is the rollback lever, not a step.
  * `AuthorizationRequestLifetimeMinutes` — 60 by default, from 5 to 1440 (a file value outside the
    range is held to it). How long a stored request stays resumable; fixed when it is stored, never
    extended. The daily retention sweep deletes the expired ones.
* **Database:** the DACPAC creates the table `AuthorizationRequests` and nothing else. Check the
  generated script before publishing: only `CREATE TABLE [dbo].[AuthorizationRequests]`, its two
  indexes, its defaults and its foreign key. Without the table every sign-in that needs an accounts page
  answers 500, so the DACPAC always goes before the API. `/ready` must read **Healthy**. It does not stop
  an API deployed too early: without the table it reads **Degraded**, still with HTTP 200, and its public
  description only says "Database schema is behind this build"; the API log names the missing object
  ("AuthorizationRequests table is missing").
* **If the API went before the DACPAC:** switch «روابط دخول قصيرة (مرجع الطلب)» off (sign-ins carry
  `returnTo` again and never touch the table), publish the DACPAC, then switch it back on.

### N.1 The one-time order, for an existing deployment upgrading across this change

An accounts application older than this change cannot read `auth_request`. If the API is upgraded
first, it hands out handles that the old pages ignore, and relying-party sign-ins land on the accounts
application instead of returning to the application. So, **once**, at the upgrade that crosses this
change:

1. **First, the accounts application AND the console** (`packages/auth` ships in both), with their usual
   `.env.production.local` and the Content Security Policy seal of `dist/web.config`
   ([Phase 7](#phase-7--build-and-deploy-the-two-web-applications)). The new applications read both
   shapes, so they work against the old API.
2. **Check before going on:** against the old API, a relying party's sign-in still lands on
   `accounts…/login?returnTo=…` and completes.
3. **The DACPAC**, after grepping its generated script as above.
4. **Last, the Auth API.** From this moment handles are issued. `/ready` must read **Healthy** (see
   *Database* above: Degraded with HTTP 200 means the table is missing).

A **first install** has no order constraint: no old accounts build exists. After the crossing, later
deploys go in the usual order again.

**Check after the deploy:** the sign-in page's address shows `client_id=…&auth_request=<64 hex>` and no
`https%3A`; sign-in, registration and organization creation from a relying party complete; replacing
one character of `auth_request` in the address bar with another hex digit (0-9 or a-f; any other
character makes the page ignore the handle) ends, after sign-in, on «Sign-in request expired»
(«انتهت صلاحية طلب الدخول»); a tab opened before the upgrade, still on `?returnTo=`, completes; and each
relying party's «الرابط الأساسي» (Base URL, in the console's application edit form) holds its `https://`
address. The expired page offers «العودة إلى …» only for an `https` Base URL of an application with at least
one registered redirect URI; otherwise it is text only, and the user must find the way back alone.

### N.2 Rollback

* **Hot:** switch `AuthorizationRequestHandlesEnabled` off. New sign-ins carry `returnTo` again, and
  handles already issued still resume.
* **Full:** switch it off **FIRST**. Then wait until this read-only count reads **0**:

  ```sql
  SELECT COUNT(*) FROM dbo.AuthorizationRequests WHERE ExpiresAt > SYSUTCDATETIME();
  ```

  Only then restore the previous accounts application, console and API. Each row keeps the lifetime in
  force when it was stored, so the current `AuthorizationRequestLifetimeMinutes` alone is not a safe
  wait. An accounts build older than this change must never serve while the switch is on, nor while a
  handle issued before the switch-off can still resume: a resume that needs an accounts page again keeps
  its handle.
* **When you later upgrade across this change again:** after the API is up, check that «روابط دخول قصيرة
  (مرجع الطلب)» reads on, and switch it on if not. The switch-off is a database override; an older build
  keeps it, but a save of this section from an older console drops it, so the next upgrade may come up
  either way. Off means the accounts pages carry `returnTo` again, which a web application firewall may refuse.
* The table can stay; nothing reads it with the switch off once the issued handles expire, and the sweep
  empties it. Each insert also removes up to two expired rows, so the table stays near the requests that
  can still resume even when sign-ins without a session outpace the daily sweep's per-run ceiling.

## O. Stay signed in, and the browser session (`IdentityProvider:IdpBrowserSession*`)

The sign-in page offers **Stay signed in** («ابقَ مسجَّلًا»), unticked on every visit and never
remembered by the browser. Without it, a sign-in is a **browser session**: the single sign-on (SSO)
cookie `auth_idp`, the one that lets an application sign the person in without a password, and the
platform application's own refresh cookie carry no expiry, so the browser drops both when it closes.
The SSO session also ends after a period without use and after a maximum, whichever comes first. With
the box ticked, both cookies last their lifetimes and the SSO session has no inactivity limit, as
before, with one change: a ticked console or accounts-application sign-in now ends at the SSO session's
fixed lifetime (`IdentityProvider:IdpSessionLifetimeDays`, counted from the sign-in and never extended by
use) instead of sliding with each token refresh, which also caps a longer `Jwt:RefreshTokenLifetimeDays`.
A new sign-in in a browser ends the SSO session that browser held before, so the
previous person's session does not outlive its browser in the database; when it is the same person
signing in again, their applications' refreshes move to the new session and keep counting as its use.

The console's and the accounts application's own sign-in lasts as long as the SSO session it began
with: once that session is signed out, replaced by another person's sign-in in the same browser, past
its maximum, or, without the box, idle for the limit below, the application's next token refresh is
refused (`Auth.RefreshTokenRevoked`) and it shows its sign-in page. These applications count a use only
when they refresh their access token, so with the defaults (access token 15 minutes, idle limit 30) an
administrator who does not tick the box is signed out of the console between 15 and 30 minutes after
their last action, and after the browser-session maximum at the latest. Only a sign-in made from a
browser page is tied this way (the sign-in request carried an `Origin` header): a sign-in without one, by
a server or the SDK, is not tied to an SSO session and keeps its refresh behaviour, and an absolute limit
for such refresh chains is planned separately. Other applications' sign-ins end with their SSO session
the same way (OI-155 PR C, the tree model): see «Application sessions end with their SSO session» below.

**Single logout (OI-155).** An SSO session ended on purpose ends every sign-in that began from it: a
sign-out in that browser (the console, the accounts application, the end-session confirmation page, or a
server-side application's sign-out from its server), or another person signing in on that browser. Each
application session and platform-application session whose refresh token came from that SSO session has
its refresh tokens revoked, its row ended, and its session id blacklisted, so this API refuses its access
tokens at once (the console and the accounts application see it at once; introspection, a platform
endpoint, also reports a terminated session's platform token inactive). An application validates its
access tokens itself and learns at its next refresh, which is refused, at most one access-token lifetime
later; introspection does not help it, since it answers inactive for every application token. Idle and lifetime ends run no
cascade (the next paragraph but one ends those sign-ins at their next refresh). Each cascade that ended something writes one sign-out row to the audit log
(`additionalData.signedOutBy = "singleLogout"`, with the trigger and the count). The switch is
`IdentityProvider:SingleLogoutEnabled`, **on by default** («الخروج من كل التطبيقات معًا (Single logout)»
in the settings section below; hot, no restart). **Rollback:** turn it off in the console; from the next
sign-out on, a sign-out ends only the SSO session at once, and nothing needs redeploying. Sessions
already ended stay ended. While `ApplicationSessionsEndWithSsoSession` (OI-155 PR C, below) is on, each
application born from that SSO session still ends at its next refresh, within one access-token
lifetime: the behaviour before OI-155 needs both switches off.

**Single logout also reaches an SSO session that already ended idle (OI-155 PR B1).** On a shared
computer left unused past the idle limit, the SSO session usually ends as idle before anyone signs
out: the next request that reads it ends it, most often the sign-in step of the next application
someone opens, or the console's own token refresh, which is then refused and deletes the console's
refresh cookie. With single logout on, the first person's application sessions born from that session
still end when, in that browser:

* another person signs in (the same person signing in again ends nothing at once; with
  `ApplicationSessionsEndWithSsoSession` on, below, nothing moves to their new session and those
  application sessions are refused at their next refresh; with it off, they move to the new session);
* the person signs out of the console or the accounts application: that sign-out arrives without a
  refresh cookie, and ends the SSO session its cookie names only when the console's session id belongs
  to the same person, so a tab someone else left open ends nothing of the person who signed in since;
* the end-session confirmation page is confirmed, if it was open before the session ended.

This holds while the ended session's row is kept (`DataRetention:IdpSessionDays`, 30 days by default).
And an application sign-in that finishes after the sign-out is refused: the token endpoint refuses a
code whose SSO session has ended (`Auth.AuthorizationCodeInvalid`), checks again once the sign-in is
written, and then ends what it issued; that refusal holds whatever the switch.

* **Database:** the DACPAC drops and recreates one index, `IX_IdpSessions_TokenHash`, without its
  filter. Click **Generate Script** against the deployment's database and read it before publishing.
  Against a database that already has every earlier merged DACPAC change, its schema part must contain
  only `DROP INDEX [IX_IdpSessions_TokenHash] ON [dbo].[IdpSessions]` and
  `CREATE NONCLUSTERED INDEX [IX_IdpSessions_TokenHash] ON [dbo].[IdpSessions]([TokenHash] ASC)`, then the
  post-deployment seeds that run on every publish. If it shows anything else, the database is missing
  an earlier change: publish that first, by its own section's steps, then generate the script again. No
  data changes; the table is not rebuilt. The index is built offline: new sign-ins and recorded
  SSO-session uses wait while it builds, so on a large `IdpSessions` table publish when few people sign
  in.
* **Order, for each existing deployment: the DACPAC, then the Auth API, always in that order.** Nothing
  else (no console, no gateway). An API published before the DACPAC still answers correctly, but with
  single logout on, every sign-out or sign-in that presents a cookie naming no live SSO session (an ended
  one, or a made-up value) reads the whole `IdpSessions` table once, and two endpoints accept such a
  request without a signed-in caller: the end-session confirmation page, within the sign-in rate limit,
  and the cookie sign-out (`POST auth/logout/cookie`), within the gateway's limit on `auth/` paths.
  Publishing the DACPAC removes that; if the API went first, publish the DACPAC at once. An older API on
  the new index works as before.
* **Rollback:** single logout off in the console undoes the reach, with no redeploy. The refusal of a
  late code is undone only by redeploying the previous API. The index needs no rollback: the previous API
  seeks on it as well.

**Application sessions end with their SSO session (OI-155 PR C, the tree model).** An application's
sign-in now ends with the SSO session it began from, as the console's and the accounts application's
already did: once that session is signed out, idle, past its maximum, or its record is gone, the
application's next token refresh is refused (`Auth.RefreshTokenRevoked`), and its session ends as at a
sign-out (refresh tokens revoked, row ended, session id blacklisted). Each accepted refresh of the
application counts as a use of that SSO session, so a person at work in an application keeps it alive.
What people feel with the defaults (access token 15 minutes, idle limit 30, browser-session maximum 10
hours): without «Stay signed in», an application signs them out between 15 and 30 minutes after their
last use of any application or the console, and after 10 hours from the sign-in even while working; with
it, after `IdpSessionLifetimeDays`. Before this change an application in use kept refreshing for as long
as it was used. A sign-in with no SSO session (issued before OI-154, or made by a server or the SDK
without an `Origin`) refreshes as before. `refresh_expires_in` keeps counting
`Jwt:RefreshTokenLifetimeDays` and is an upper bound.

* **Order, for each existing deployment:** first check the pair below (idle limit 0, or at least twice
  the access-token lifetime) in the console and in the settings files; then deploy the Auth API, then the
  console (its new label and hints). No DACPAC. An API deployed without the console works; the console
  then shows the new switch under its raw name until it is deployed. At the deploy, every application
  sign-in whose SSO session has already ended (for example a browser-session sign-in older than the
  browser-session maximum) is refused at its first refresh: those people sign in again once.
* **Rollback:** `ApplicationSessionsEndWithSsoSession` off in the console, with no redeploy: from the next
  refresh on, an application's refresh is no longer refused for its SSO session. Sessions already ended
  stay ended. Full rollback: the previous API. While the pair below is broken, every save of the
  Identity provider section, this switch included, is refused until the same save also corrects the idle
  limit (the message names the value to use; 0 always passes), and so is every save of the JWT section,
  an Issuer change included, until the pair is corrected in one of the two sections.
* **Check after the deploy, one person on one browser:** in the console, set the access-token lifetime
  to 1 minute first, then the idle limit to 2 minutes (the other order is refused). Then open a private
  window, or sign out of the console in this browser, so the application's sign-in shows the sign-in
  page; sign in to the application with «Stay signed in» unticked, and touch nothing else in that
  browser during the wait (the console counts as a use). Leave the application untouched for 3
  minutes, then use it: it asks for the sign-in. Controls: using it every minute for 5 minutes keeps it
  signed in; and with `ApplicationSessionsEndWithSsoSession` off, the 3-minute wait does not sign it
  out. Restore the idle limit to its old value first, then the access-token lifetime (the other order
  is refused). The application must renew its access token once it expires; an application whose token
  validation allows a long clock skew renews later, so wait past that skew.

* **Settings** (System settings → Access → Identity provider (SSO), «إعدادات النظام» › «الوصول» ›
  «موفر الهوية (SSO)»; hot, no restart):
  * `IdpSessionLifetimeDays` — 7, unchanged; now the lifetime of a session signed in with «Stay signed
    in».
  * `IdpBrowserSessionLifetimeHours` — 10 by default, from 1 to 24 («الحدّ الأقصى لجلسة المتصفّح
    (ساعات)»). Fixed when the person signs in, never extended, and never longer than the line above. A
    change applies to new sign-ins only.
  * `IdpBrowserSessionIdleMinutes` — 30 by default, from 0 to 1440, **0 turns the limit off** («حدّ عدم
    الاستعمال لجلسة المتصفّح (دقائق)»). Read on every use, so a change applies to the sessions that
    exist. A use is a sign-in to an application through the authorize endpoint (a silent `prompt=none`
    included), the organization step, or an accepted refresh of a token that a sign-in through that
    session issued; the console and the accounts application count a use when they refresh their access
    token (see the last bullet of this section). A session is ended as idle only when something reads it,
    so one nobody used since
    it went idle is still on record: a change **saved in the console** (or a reset of the section)
    first ends every browser session already idle under the limit in force, so raising the limit or
    setting 0 brings none of them back. A change made **in a settings file**, applied at the next
    restart, does not: raising the limit or setting 0 there brings back the sessions nobody used since,
    including, in the seven days after this deploy, sessions signed in before it.
  * `StaySignedInMode` — who decides whether a sign-in outlives the browser («من يقرّر بقاء الدخول بعد
    إغلاق المتصفّح»). Three values: `PersonChooses`, the default («الشخص (تظهر «ابقَ مسجَّلًا»)»): the
    box is shown, as described above; `AlwaysBrowserSession` («جلسة متصفّح دائمًا»): the box is hidden
    and every sign-in is a browser session, whatever an older page still open in a tab sends (a
    browser set to restore its previous session keeps browser-session cookies, so there the inactivity
    limit and the maximum above are what end the session; see check 2 in O.1); and
    `AlwaysStaySignedIn` («البقاء مسجَّلًا دائمًا»): the box is hidden and every sign-in, registration
    and account recovery included, lasts `IdpSessionLifetimeDays` and survives closing the browser.
    **Do not choose `AlwaysStaySignedIn` where several people share computers.** The value is read only
    when a session is created, so a change applies to new sign-ins only: a session keeps what it was
    given until it ends, and a save ends or converts nothing. It is deployment-wide, so it also covers
    the deployment's administrators. Set it from the console, which validates the value. A value outside
    the three, a number or a name, in a settings file or in the database, reads as `PersonChooses`: the
    box shows, and nothing else breaks. This setting needs no DACPAC: the
    sign-in page learns whether to show the box from the public branding call
    (`GET /api/v1/Platform/branding`, field `staySignedInOffered`), and the API enforces the value
    whatever the page shows.
  * `SingleLogoutEnabled` (OI-155) — on by default («الخروج من كل التطبيقات معًا (Single logout)»): a
    sign-out in a browser, or another person's sign-in there, ends every sign-in that began from that
    browser's SSO session (see «Single logout» above). Read on every sign-out; off is the rollback, with
    no redeploy and no DACPAC: a sign-out then ends no application at once, but while
    `ApplicationSessionsEndWithSsoSession` is on each application still ends at its next refresh. The
    console shows its label from the build that carries this change, so deploy the API, then the console.
  * `ApplicationSessionsEndWithSsoSession` (OI-155 PR C) — on by default («دخول التطبيقات ينتهي بانتهاء
    جلسة SSO»): an application's sign-in ends with the SSO session it began from (see «Application
    sessions end with their SSO session» above). Read on every refresh and on every sign-in that replaces
    a browser's session; off is the rollback, with no redeploy and no DACPAC. Independent of
    `SingleLogoutEnabled`.
* **Database:** the DACPAC adds four nullable columns and one procedure parameter. Click **Generate
  Script** and read it before publishing ([Phase 2, Step 3](#step-3--publish)). It must contain only:
  * `ALTER TABLE [dbo].[IdpSessions] ADD [IsPersistent] BIT NULL, [LastActivityAt] DATETIME2 NULL`
    (one statement or two);
  * `ALTER TABLE [dbo].[AuthorizationCodes] ADD [IdpSessionId] UNIQUEIDENTIFIER NULL`;
  * `ALTER TABLE [dbo].[RefreshTokens] ADD [IdpSessionId] UNIQUEIDENTIFIER NULL`;
  * `ALTER PROCEDURE [dbo].[sp_CreateRefreshToken]`, whose new last parameter `@IdpSessionId`
    defaults to NULL.

  No `DROP`, no `tmp_ms_xx` table rebuild, no foreign key. An API published before the DACPAC cannot
  read or create SSO sessions, authorization codes or refresh tokens: every sign-in and every token
  refresh fails, and `/ready` reads **Degraded** with HTTP 200 while the API log names the missing column
  ("IdpSessions.IsPersistent column is missing").
* **Order, for each existing deployment:**
  1. **The DACPAC**, after reading its generated script as above.
  2. **The Auth API.** `/ready` must read **Healthy**.
  3. **Both web applications** (console and accounts). An application published before the API sends a
     field the older API ignores, and nothing breaks.

  Inside a batch that also crosses [section N](#n1-the-one-time-order-for-an-existing-deployment-upgrading-across-this-change),
  N.1's one-time order still holds: it already keeps the DACPAC before the API.
* **What the deploy does to the people signed in:** a session signed in before it reads as a browser
  session with no recorded use, so its idleness counts from its sign-in: anyone who signed in more than
  the idle limit before the deploy signs in again at their next use, whatever they did since. A session
  signed in within the limit before the deploy keeps its seven-day expiry and its persistent cookie while
  it is used at least once per idle limit: it survives closing the browser for up to seven days, and the
  browser-session maximum does not apply to it. The console's and the accounts application's sign-ins from before the deploy carry no
  link to an SSO session, so they are not ended with one. The platform applications' own refresh cookies
  issued before the deploy also keep their seven-day expiry until their first refresh after it: a browser
  left closed across the deploy reopens still signed in to the console or the accounts application, at
  most for the refresh-token lifetime (`Jwt:RefreshTokenLifetimeDays`).
* **The applications' own sign-in follows the box only in cookie mode:** with
  `IdentityProvider:FirstPartySpaOrigins` filled and `SpaRefreshCookieEnabled` on. With the list empty,
  the console and the accounts application keep their refresh token in the browser's storage, which no
  browser closing clears; the box then governs the SSO session alone.
* **Keep the idle limit 0 or at least twice `Jwt:AccessTokenLifetimeMinutes`.** A relying party and the
  platform applications refresh about once per access-token lifetime, and each accepted refresh counts as
  a use. With an idle limit at or below the access-token lifetime, every user who did not tick the box,
  the administrator included, is signed out at each token expiry however busy: of the console and the
  accounts application, and, since OI-155 PR C, of every relying party too, whose sign-in now ends with
  its SSO session. The console refuses a save, or a reset, of either section that leaves the idle limit
  above 0 and below twice the access-token lifetime (`SystemSettings.InvalidFieldValue`, naming the other
  setting and its value). Settings files are not checked at startup: check the pair there yourself before
  you deploy.

### O.1 Checks after the deploy

Use a relying party that does not send `prompt=login`.

1. Close every application tab first (an open relying-party tab that refreshes its tokens counts as
   use). Sign in without ticking «Stay signed in», wait 31 minutes without signing in to anything, then
   start a sign-in from the relying party: **the password is asked.**
2. Sign in without ticking it, close every window of the browser, reopen it, start a sign-in: the
   password is asked. A browser set to restore the previous session ("continue where you left off")
   keeps browser-session cookies; check 1 is the guarantee there.
3. Sign in WITH it ticked, close the browser, reopen it: the relying party signs in with no password, as
   before.
4. In the browser's developer tools (Application, Cookies) on the API host: `auth_idp` and the
   application's `__Host-` refresh cookie show «Session» as their expiry when unticked, and a date seven
   days ahead when ticked. The `__Host-` cookie exists only in cookie mode (`FirstPartySpaOrigins`
   filled, see above).
5. Two people, one browser: A signs in, then B signs in on the same browser. A's newest row has
   `RevokedAt` set (replace the address with A's):

   ```sql
   SELECT TOP (1) [CreatedAt], [RevokedAt], [IsPersistent], [LastActivityAt]
   FROM [dbo].[IdpSessions]
   WHERE [UserId] = (SELECT [Id] FROM [dbo].[Users] WHERE [Email] = N'a.tester@example.com')
   ORDER BY [CreatedAt] DESC;
   ```

   If A had the console open, A's next action there lands on its sign-in page once A's access token
   has expired (15 minutes by default): the console's sign-in is refused at its next token refresh, not
   before. Wait that long before checking.
6. An application's refresh counts as use: sign in to a relying party without ticking, note your newest
   session's `LastActivityAt` with the query in check 5 (with your own address), let the relying party
   refresh its tokens once, and run it again: `LastActivityAt` has moved, to within seconds of a NEW
   application refresh token created after your first reading (an authorize, a silent one included, also
   moves it, so the new token is what shows the refresh did):

   ```sql
   SELECT TOP (2) [CreatedAt], [IdpSessionId]
   FROM [dbo].[RefreshTokens]
   WHERE [ApplicationId] IS NOT NULL
     AND [UserId] = (SELECT [Id] FROM [dbo].[Users] WHERE [Email] = N'a.tester@example.com')
   ORDER BY [CreatedAt] DESC;
   ```

   The newest row is that refresh, and its `IdpSessionId` is not NULL.
7. The console follows its SSO session: sign in to the console without ticking, leave it untouched for 31
   minutes with no other application open, then click anything that loads data: the console shows its
   sign-in page, with no error message.
8. `StaySignedInMode` (in the same console page):
   * Untouched: the sign-in page shows «Stay signed in», as before.
   * Before the next step, sign in on a second browser WITH the box ticked.
   * Set «جلسة متصفّح دائمًا», save, and open the sign-in page in a new private window: no box. Sign in;
     in the developer tools (Application, Cookies) on the API host, `auth_idp` and the application's
     `__Host-` refresh cookie show «Session» as their expiry.
   * Close and reopen the second browser: it is still signed in (a change applies to new sign-ins only).
   * Set «البقاء مسجَّلًا دائمًا»: no box; after a sign-in `auth_idp` shows a date
     `IdpSessionLifetimeDays` ahead (7 days by default), and the application's `__Host-` refresh
     cookie a date `Jwt:RefreshTokenLifetimeDays` ahead (its own lifetime).
   * Set it back to «الشخص (تظهر «ابقَ مسجَّلًا»)» unless the deployment wants another value.

### O.2 Rollback

* **Lighter, no redeploy:** set «حدّ عدم الاستعمال لجلسة المتصفّح (دقائق)» to 0 **in the console**.
  Browser sessions then end only with the browser and at their maximum, and the console and the accounts
  application stop signing out after a period without use. The save first ends every session already
  idle under the old limit, so none of them comes back. Do not do this in a settings file: there, 0
  brings back every idle session nobody used since, including, in the seven days after this deploy, the
  sessions signed in before it, whose cookies are still persistent. This lever does not undo the rest:
  cookies stay browser-session cookies, and a sign-in still ends the browser's previous session.
* **`StaySignedInMode`, no redeploy:** set it back to «الشخص (تظهر «ابقَ مسجَّلًا»)» in the console; it
  applies to the next sign-in. Sessions signed in under another value keep what they were given until they
  end; to end them sooner, sign them out. Before a full rollback, set it back in the console: a value
  saved there stays in the database, and the next upgrade would apply it again. (Resetting the section
  would also return every other «موفر الهوية (SSO)» value to its file value; setting this one field back
  is enough.)
* **Full:** redeploy the previous API and both web applications. The four columns stay: the previous API
  ignores them, and its shorter call to `sp_CreateRefreshToken` still works against the new procedure.
  Cookies already issued as browser-session cookies stay so until the browser closes.
* **When you later upgrade across this change again:** rows written by the previous API read as browser
  sessions, so they idle out like the first time (one extra sign-in).

## P. Back to the application after sign-out (`post_logout_redirect_uri`)

A relying party that sends the browser to `/api/v1/auth/end-session` to end the single sign-on session
can now name where the browser comes back to (`post_logout_redirect_uri`, OpenID Connect RP-Initiated
Logout). Each application holds its own list of such addresses, matched exactly, edited in the console
under **Post-logout redirect URIs** («عناوين العودة بعد تسجيل الخروج»). The browser goes back only once no
session is left: straight away when there was none, or after the person confirmed on the accounts sign-out
page and the session was revoked. «Keep me signed in on this browser» on that page never goes back. Details for application
developers: `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`, "Signing the user out of AuthSystem too".

* **Database:** the DACPAC creates the table `ApplicationPostLogoutRedirectUris` and nothing else. Check
  the generated script before publishing: only `CREATE TABLE [dbo].[ApplicationPostLogoutRedirectUris]`,
  its index `IX_ApplicationPostLogoutRedirectUris_ApplicationId`, its two defaults and its foreign key to
  `Applications` (and the closing `WITH CHECK CHECK CONSTRAINT` on that key). No `DROP`, no `ALTER` of an
  existing object. Every single-application read loads the list: authorize, the code exchange, every
  relying-party token refresh, end-session, the public branding of the sign-in pages and the console's
  application pages. An API published before the table answers all of them with 500, so relying parties
  can neither sign people in nor keep them signed in; the DACPAC always goes first. `/ready` must read **Healthy**: without the table it reads **Degraded**, still with HTTP
  200, and the API log names the missing object ("ApplicationPostLogoutRedirectUris table").
* **Order, for each existing deployment:** the DACPAC, then the Auth API, then the console, then the
  accounts application. An older accounts application ignores the hash its URL carries and ends on the
  signed-out page as before; an older console does not show the list, and its saves leave the list
  unchanged.
* **A web application firewall in front of the API** sees a whole address in the query of
  `/api/v1/auth/end-session`, as it sees `redirect_uri` on `/api/v1/auth/authorize`: the protocol puts it
  there, from the relying party. If the firewall refuses these requests (commonly a 403 for a suspected
  remote file inclusion), exclude `redirect_uri` on GET `/api/v1/auth/authorize` and
  `post_logout_redirect_uri` on GET `/api/v1/auth/end-session` from that rule, on the API host only. Keep
  the rule everywhere else, and keep the accounts host fully covered: no accounts URL carries the address,
  and the accounts sign-out page receives only its hash, in `plr`.
* **CORS is not involved.** `Cors:AllowedOrigins` takes bare origins only (`scheme://host[:port]`); an
  entry with a path, such as a return address, matches nothing. If one was added for this purpose, remove
  it from the settings file and from the database override (System settings).

**Checks after the deploy,** with an application whose return address is registered:

1. The browser sent to end-session with its `client_id`, the registered `post_logout_redirect_uri` and a
   `state` shows the sign-out page; its address carries `client_id`, `state` and `plr=<64 hex>`, and no
   `https%3A`. «End sign-in on this browser» lands back on the return address, with `state`.
2. The same, but «Keep me signed in on this browser»: the accounts profile, never the return address.
3. With no single sign-on session in the browser: straight back to the return address, with `state`.
4. An address that is not registered: the «signed out» page, not the address.

**Rollback:** redeploy the previous API, console and accounts application, all three. A console newer
than the API shows the list, accepts edits and reports success while the previous API stores nothing. The
table can stay; the previous code does not read it.

## Q. Server-side applications: sign-in keys and server-side redirect URIs

A server-side web application (a Backend-for-Frontend) can authenticate itself at the token endpoint
with an application key of purpose **sign-in** (`private_key_jwt`), and its refresh tokens are then bound
to that key. The relying party's side is described in `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`,
"Confidential mode"; the design in `docs/adr/0003-confidential-server-side-clients.md`. **Nothing to switch
on:** until an application has a sign-in key and a server-side redirect URI, every flow is as before.

* **The schema:** one new table, `ApplicationConfidentialRedirectUris`, and three columns appended last:
  `ApplicationMachineKeys.Purpose` (`TINYINT NOT NULL`, named default 1, so every existing key stays a
  machine key), and `RequiresClientAuthentication` (`BIT NULL`) on `AuthorizationCodes` and
  `RefreshTokens`; `sp_CreateRefreshToken` gains a last parameter with a default. Read the generated
  script before publishing: it must show those `ALTER TABLE … ADD` statements, the `CREATE TABLE`, and the
  procedure change, with no `tmp_ms_xx` and no `DROP` other than the known CHECK constraint drop and
  re-create pairs.
* **Order, for each existing deployment:** the DACPAC, then the Auth API, then the console. An API
  published before the schema reads **Degraded** at `/ready` and names what is missing (for example
  "ApplicationConfidentialRedirectUris table" or "RefreshTokens.RequiresClientAuthentication column").
* **Checks after the deploy:** `/ready` reads **Healthy**; an application's page shows the
  **Server-side redirect URIs** list and the keys' **Purpose** column (the section is now called
  **Application keys**); a public sign-in of an existing application works as before; a code exchange sent
  with a junk `client_assertion` answers `Auth.InvalidClient`.

**Rollback:** the database needs nothing: an older API ignores the new table and columns, and never reads
the server-side list, so it refuses those addresses at authorize. **Before deploying an older API, revoke
every sign-in key** (console, Application keys): an older API does not read `Purpose`, so it would accept a
sign-in key at `client_credentials` as a machine key. When the console cannot do it (or to be sure), revoke
them in SQL, then start the older API only once the check reads 0:

```sql
UPDATE [dbo].[ApplicationMachineKeys]
SET [RevokedAt] = SYSUTCDATETIME(), [RevokeReason] = N'API rollback'
WHERE [Purpose] = 2 AND [RevokedAt] IS NULL;

SELECT COUNT(*) FROM [dbo].[ApplicationMachineKeys]
WHERE [Purpose] = 2 AND [RevokedAt] IS NULL;  -- must read 0
```

Roll the console back together with the API, or create no key while the two differ: the newer console
against an older API creates a **machine** key when "Sign-in" is chosen, and its server-side redirect list
is silently not saved. Under the older API, bound refresh tokens refresh without the key, and every token it
rotates is written unbound; after the upgrade is deployed again, such a chain stays unbound until the
server's next authenticated refresh rebinds it. Recreate the sign-in keys after the upgrade.

### Q.1 A server-side application's sign-ins describe the person's browser

When a server-side application redeems a code with its sign-in key, the session it opens, the person's
sign-in history, the location, the user's "last sign-in address" and the new-device e-mail are recorded from
the person's browser as the authorize step saw it, not from the server that called the token endpoint. A
public client's sign-ins are recorded as before. **Nothing to switch on.**

* **The schema:** three nullable columns appended last, with no default: `AuthorizationCodes.UserAgent`
  (`NVARCHAR(500)`), `AuthorizationCodes.DeviceId` (`NVARCHAR(64)`) and `IdpSessions.DeviceId`
  (`NVARCHAR(64)`). The generated script must show two `ALTER TABLE … ADD` statements (one per table, three
  columns in all), with no `tmp_ms_xx` and no `DROP COLUMN`.
* **Order:** the DACPAC, then the Auth API. Nothing else. `/ready` does not stop an API published before
  the schema: it reads **Degraded**, still with HTTP 200, and its public description only says "Database
  schema is behind this build"; the API log names the first missing column ("AuthorizationCodes.UserAgent
  column"). Until the DACPAC is published, sign-ins start no single sign-on session, every authorize that
  carries a sign-in cookie and every code issued or redeemed answers 500 (so no application can sign
  anyone in), and console renewals tied to a sign-in session answer 500, which signs console users out.
  Applications' own renewals keep working.
* **If the API went before the DACPAC:** publish the DACPAC. No restart is needed: the statements are
  plain text and the next request uses the new columns.
* **Checks after the deploy:** `/ready` reads **Healthy**. Sign in from a browser to an application in
  confidential mode, then open the profile's sessions: the new session shows that browser's address,
  browser and location, not the application server's. A public application's sign-in shows what it
  showed before.
* **During the switch:** a code issued by the previous API in the minute around the deploy is recorded as
  before. A sign-in session started before the deploy carries no device identifier, so the first
  server-side sign-in from it may send one extra "new device" e-mail.

**Rollback:** redeploy the previous API. The three columns can stay: the previous API does not name them.

## R. The machine directory (`organizations.read.all`)

An application's own server may read, with its machine key and no user, the directory of every
organization that enabled the application (the organization, the members who hold one of its roles, its
invitations). The consent is one switch per application, **on by default for every application,
existing ones included**. Integration details: `ReadMe/APPLICATION_INTEGRATION_GUIDE.md`, "Reading
organizations as your server: the machine directory".

* **The schema:** one column appended last, `Applications.AllowMachineOrganizationsRead`
  (`BIT NOT NULL`, named default `DF_Applications_AllowMachineOrganizationsRead` of 1). The generated
  script must show one `ALTER TABLE … ADD` with that named default, with no `tmp_ms_xx` and no
  `DROP COLUMN`. Every existing application reads consented after the publish.
* **Order, for each existing deployment: the DACPAC first, then the Auth API, then the console.** No
  gateway change: the gateway's `/app/` route already forwards the new paths. Every application read
  selects the new column, so an API published before the DACPAC fails every sign-in, every code exchange
  and the console's application pages with 500; `/ready` reads **Degraded** and the API log names
  "Applications.AllowMachineOrganizationsRead column". If that happens, publish the DACPAC at once; no
  restart is needed.
* **After the deploy:** review the applications that hold a machine key of purpose Machine, and switch
  the consent off for any that should not read organizations (the application's page, tab **Application
  keys**, switch **Organization directory for the application's server**; Arabic «مفاتيح التطبيق» and
  «دليل المنظمات لخادم التطبيق»). An application's server that should read them needs a machine key of
  purpose Machine and asks for `scope=organizations.read.all` explicitly at the token endpoint.
* **Checks:** with a test organization that enabled a test application and a test member holding one of
  its roles: the three machine reads answer 200; a member with no role in the application is absent from
  the members list; an organization that did not enable it answers 404; after the switch is turned off,
  the next read answers 403 and a new token request for the scope answers `Auth.InvalidScope`.

**Rollback:** switch the consent off (effective at the next read), or revoke the machine key (no new
token; the last one lasts five minutes at most), or redeploy the previous API (the column stays, unused).

---

**The whole flow:** install the prerequisites → create the files the repository does not ship →
build the two .NET projects → publish the database and give the seeded admin a password → write
`appsettings.Production.json` → publish and upload the Auth API → publish and upload the Gateway →
configure email → build and upload the two web applications → verify `/ready` and sign in → work the
go-live checklist → optionally connect other applications. 🎉
