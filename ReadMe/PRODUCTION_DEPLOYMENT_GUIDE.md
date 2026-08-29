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
| **Auth_Setup** | A one-shot command-line utility that prints a password hash. Never deployed. | ❌ Run locally only if you lock yourself out |

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

### Step 4 — Know what the seed did and did not create

A clean publish creates **8 roles** and **45 permission rows**.

The 8 roles: `super-admin`, `admin`, `user-manager`, `auditor`, `user`, `org-owner`, `org-admin`,
`org-member`.

> ### ⚠️ Read this before you create an administrator role — it will save you a day
> **The API enforces 50 distinct permission codes. Only 16 of them have a matching row in the
> database after a clean publish.** The other **34 codes exist in the code and in no table**, so
> nobody can be granted them.
>
> What that means in practice: you open the console, create a role called "Support", tick the
> permissions you want, and discover you cannot even find `users:read` in the list — because there
> is no row for it. If you insert one by hand and grant it, the endpoint still works, but the seeded
> `admin`, `user-manager` and `auditor` roles do **not**: they hold codes prefixed `auth:`, which no
> endpoint checks. Wildcard grants are prefix matches, so `auth:users:*` does **not** satisfy
> `users:read`.
>
> **On a clean database the only thing that reaches those 34 endpoints is the `super-admin` role's
> global `*` grant, which the seeded admin account holds.** Every one of this guide's console
> procedures — secret keys, system settings, notification templates — depends on that.
>
> **What to do about it.** The repository contains a script that would create 28 of the 34 missing
> codes, but it is never included by the post-deployment script, so a publish does not run it. If you
> need granular roles, run it by hand against your database after the publish:
> `Auth/Auth_DB/dbo/Scripts/SeedData/08_AdditionalPermissions.sql`. Read it first — it inserts
> permission rows only; it grants nothing to anybody.
>
> **Six codes cannot be granted at all**, because no SQL file in the repository creates them:
> `apikeys:validate`, `webhookkeys:create`, `webhookkeys:read`, `webhookkeys:revoke`,
> `webhookkeys:rotate`, `webhookkeys:validate`. Until someone adds them, only `super-admin` reaches
> those endpoints. **Flag this to whoever owns the codebase** — it is a gap in the seed data, not a
> configuration choice you can make.
>
> *In code:* enforced codes come from `[RequirePermission("…")]` attributes across `Auth/Auth_API`;
> seeded rows come from `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql` plus the seed
> files it includes with `:r`. Wildcard matching is at
> `Auth/Auth_API/Authorization/PermissionRequirementHandler.cs`.

Six of the sixteen seed scripts on disk are never included by the post-deployment script and
therefore never run on a publish. Four of the nine upgrade scripts are likewise not included. If you
are upgrading an existing database rather than creating a new one, read
`Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql` and compare its `:r` list against the
folders — the repository keeps no migration-history table, so nothing tells you which scripts a given
database has seen.

### Step 5 — Give the seeded admin a password. It has none until you do.

The publish seeds a single administrator with **no password at all**:

| Field | Value |
|---|---|
| Email | `admin@company.com` |
| Password | none — `PasswordHash` is `NULL` |
| Role | `super-admin` |
| Can sign in before you complete this step | No |

No deployment of this system ships a password anyone could look up. The account exists and holds
`super-admin`, but `LoginCommandHandler` rejects a null hash before it reaches the password
verifier, so it cannot authenticate until you set one here.

Run `Auth_Setup` with the password you have chosen, then execute the `UPDATE` it prints against the
database you just published:

```
dotnet run --project Auth/Auth_Setup -- "<the password you chose>"
```

Give it no argument and it prompts instead, which keeps the password out of your shell history. It
opens no database connection and changes nothing on its own — you run the statement it prints.
*In code:* `Auth/Auth_Setup/Program.cs`.

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
    "AccountsBaseUrl": "https://accounts.<yourdomain>.com"
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
  visitors to `/auth/authorize` are redirected to its `/login` page.

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
| Published privacy-policy documents | `PrivacyPolicyPublication:PhysicalPath` | `App_Data/SEBAKHI/sandbox/privacy` | Every published policy version, including the one the accounts application serves at `/privacy` |

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
> read-only in the settings registry, so this is a file or environment-variable edit and a restart,
> deliberately. Development is unaffected: `appsettings.Development.json` sets it `true` for itself.
> *In code:* `Auth/Auth.Application/Configuration/SecretManagementSettings.cs` (class default);
> `Auth/Auth_API/appsettings.json`, section `SecretManagement` (shipped value); enforcement at
> `Auth/Auth_API/Modules/Administration/Filters/RequireAdminApiEnabledAttribute.cs`, which answers
> 403 `Admin API Disabled`.

> ### CORS: what the startup guard does and does not catch
> The application refuses to start when `Cors:AllowedOrigins` is an **empty array** outside
> Development. That is the only case it catches. Anything else starts normally — including the value
> `["*"]`, which outside Development produces a policy with **no origins configured at all** and
> therefore silently denies every cross-origin request. And because the committed base file already
> contains two non-empty placeholder entries, the guard **cannot fire** on a deployment that simply
> forgets to configure CORS. Check the list yourself; the guard will not do it for you.
> *In code:* the fail-fast is in `Auth/Auth_API/Program.cs`; the `*` behaviour is in
> `Auth/Auth_API/Common/DynamicCorsPolicyProvider.cs`.

Two optional password-hardening features also belong in this file, under a `Password` section: a
server-side pepper mixed into every password hash, and a check against known breached passwords.
Both are off by default and neither is needed to get running, so they have their own appendix —
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

### What happens when `Email:Enabled` is false

**The send path reports success and delivers nothing.** It writes a single log line at Information
level — `Email sending disabled. Would have sent to {Email} …` — and returns success to whatever
asked for the message. With the delivery queue on (the default), the queued row is even marked
**Sent**. Under a production log level of Warning that Information line is not written at all, so
there is no trace anywhere.

Nothing in the system will tell you. A user who asks for a password reset sees a normal success
screen. An administrator who tries to rotate a signing key waits for a code that never arrives.
*In code:* `Auth/Auth.Infrastructure/Notifications/Channels/EmailNotificationChannel.cs`.

### The `Email` settings

All of these live under `"Email"` in `appsettings.Production.json`.

| Key | Default in the base file | What it does |
|---|---|---|
| `Enabled` | `false` | Master switch. See the warning above. |
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

**`Email:FrontendBaseUrl` refuses to start the application when it is empty and `Email:Enabled` is
`true`.** The message is exactly `Email:FrontendBaseUrl must be an absolute URL when Email:Enabled is
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
`admin@company.com` and the password you set in [Phase 2 Step 5](#step-5--give-the-seeded-admin-a-password-it-has-none-until-you-do).
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
  -d '{ "email": "admin@company.com", "password": "<the password you set in Phase 2 Step 5>" }'
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
  "user": { "id": "…", "email": "admin@company.com" },
  "requiresPasswordChange": true,
  "requiresTwoFactor": false
}
```

Reading the results:

* **`/ready` fails while `/health` succeeds** — the process is up but cannot reach the database. This
  is the most common first-deployment error; check the connection string, the SQL login, and the
  firewall between the web server and the SQL server.
* **`requiresPasswordChange: true`** is expected: the seeded admin must change its password. Do that
  by signing in to the console ([Phase 7](#phase-7--build-and-deploy-the-two-web-applications)),
  which walks you through it. To do it over HTTP instead, call the change-password endpoint with the
  access token from the response above and a three-field body:

  ```bash
  curl -X POST https://auth.<yourdomain>.com/api/v1/auth/change-password \
    -H "Authorization: Bearer <accessToken from the login response>" \
    -H "Content-Type: application/json" \
    -d '{ "currentPassword": "<your current password>", "newPassword": "<your new password>", "confirmNewPassword": "<the same new password>" }'
  ```

  **What success looks like:** HTTP **204 No Content** with an empty body. The new password must
  satisfy the configured policy (`Password:MinimumLength`, 8 out of the box), and
  `confirmNewPassword` must match `newPassword` exactly.
* **`User.InvalidCredentials`** — the seeded admin row is missing or its hash was changed. Republish
  the database, or use `Auth_Setup` as described in [Phase 2 step 5](#phase-2--database).
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
- [ ] The seeded admin has a password you chose, set through Phase 2 Step 5. Confirm the seed left it empty and your `UPDATE` filled it: `SELECT CASE WHEN [PasswordHash] IS NULL THEN 'no password - sign-in refused' ELSE 'set' END FROM [dbo].[Users] WHERE [Email] = 'admin@company.com';`
- [ ] The SQL login is least-privilege — read and write on one database, not `sa`.
- [ ] `SecretManagement:EnableAdminApi` is `false` — it now ships that way, so confirm nothing in your Production file turns it back on. Enable it only while provisioning keys.
- [ ] `HealthChecks:ExposeErrorDetails` is `false` on both applications. `/health` and `/ready` bypass gateway-token validation, so they are publicly reachable.
- [ ] `AllowedHosts` is your own domain, not the shipped `"*"`.
- [ ] `SecretManagement:AutoGenerateKeys` is back to `false` after the first successful run.
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
> both authentication and a permission — so those calls cannot succeed either, and two of the
> permissions they need (`apikeys:validate`, `webhookkeys:validate`) cannot be granted to anyone
> because no SQL file creates them.
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
   the **`secrets.manage`** permission. On a clean database that permission has no row of its own
   (Phase 2 step 4), so the **only** account that reaches these pages is the seeded `super-admin`.
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
   is a different permission from the `secrets.manage` one the secret pages need. It is one of the 16
   codes the publish does create a row for, but the seed grants it to no role, so on a clean database
   the only account that holds it is still the seeded `super-admin` with its global `*`.

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
| Login returns `User.InvalidCredentials` for the seeded admin | The admin row is missing, or its hash was changed | Republish the database, or reset the hash with `Auth_Setup` ([Phase 2 step 5](#phase-2--database)) |
| Generated keys not saved | The secrets folder is not writable by the application pool identity | Fix the folder permission and restart |
| A permission you granted still returns 403 | The code is one of the 34 the API enforces but the database does not seed | [Phase 2 step 4](#phase-2--database) |
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
[§A](#dpapi-windows-only-machine-bound).

### The admin secrets API

All key operations live under `…/api/v1/admin/secrets/` and are gated four ways:

* **`SecretManagement:EnableAdminApi` must be `true`.** It ships **`false`**, matching the settings
  class, so a deployment that never mentions the key has these endpoints refused. Turn it on in your
  Production file for the minutes you are provisioning, then off again (Phase 9 checklist). It needs
  a restart both ways — the console cannot change it.
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
| `GenerateHmacKey` | The HMAC key that hashes refresh tokens |
| `GenerateGatewayToken` | The shared gateway token |
| `ImportRsaKey` | Same as above, with key material you supply |
| `ImportHmacKey` | Same |
| `ImportGatewayToken` | Same |

**Importing replaces the current key.** If the value differs from what is stored, every existing
token signed with the old one is invalidated. Re-importing the *same* value is a safe no-op — which
is exactly what makes the migration procedure below work.

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
  -d '{ "email": "admin@company.com", "password": "<admin-password>" }' | jq -r .token.accessToken)
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
`ImportHmacKey` against `…/import/hmac` and `ImportGatewayToken` against `…/import/gateway-token`.
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
3. **Import the three keys** with the four-step flow above.
4. **Verify.** `GET /.well-known/jwks.json` shows your public key, and a token issued by the old
   server still validates. Then set `EnableAdminApi: false` again.

Tokens issued by the old server keep working because the signing and HMAC material is byte-for-byte
identical. The gateway token must match on both the API and the Gateway, exactly as described in
[Phase 5](#phase-5--optional-api-gateway).

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

Both features are **opt-in**, both default to `false`, and both are configured under `Password` in
`appsettings.Production.json`. The Argon2id password hashing itself — a unique salt per password, a
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
and the password itself never does. It is checked on register, change, reset, and administrator-create.

```jsonc
"Password": {
  "BreachedPasswordCheck": {
    "Enabled": true,            // false = fully inert: no HTTP client, no external call
    "Mode": "Enforce",          // Enforce = reject; Warn = allow but flag
    "FailOpen": true,           // allow if the service is unreachable (logged); false = reject
    "RejectThreshold": 1,       // how many breach occurrences count as breached
    "TimeoutMs": 2000
  }
}
```

- **Enforce** rejects a breached password with `User.PasswordBreached` and HTTP 400.
- **Warn** lets the operation succeed but adds an `X-Password-Warning` response header, plus
  `X-Password-Warning-Code: User.PasswordBreached`, so the application can nudge the user. It works
  on 204 No Content responses too.
- **`FailOpen: true`**, the default, means an outage at the breach service never blocks a password
  change; the event is logged. Set it to `false` only if you would rather hard-fail than risk
  admitting an unchecked password.
- Enabling this requires **outbound HTTPS from your server**. On locked-down hosting, confirm that
  first.

`Password:MinimumLength` is separate policy, currently 8. Raising it to 12 is a reasonable hardening
step and affects only new and changed passwords.

---

**The whole flow:** install the prerequisites → create the files the repository does not ship →
build the two .NET projects → publish the database and read the permission warning → write
`appsettings.Production.json` → publish and upload the Auth API → publish and upload the Gateway →
configure email → build and upload the two web applications → verify `/ready` and sign in → work the
go-live checklist → optionally connect other applications. 🎉
