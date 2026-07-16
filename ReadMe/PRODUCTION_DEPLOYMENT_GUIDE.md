# Production Deployment Guide

Deploy this authentication system to production, start to finish. Written for developers who are
new to *this* codebase (you know what a connection string, env var, and CLI are — we explain the
project-specific parts).

**Read it in two passes:** **Part 1** is the few things you decide up front. **Part 2** is the
ordered install (**Phase 1 → 8**) — do it top to bottom.

Generic names used throughout — substitute your own:

| Placeholder | Meaning | Example |
|---|---|---|
| `<yourdomain>.com` | Your domain | `mycompany.com` |
| `<webspace>` | Your hosting folder | `mysite.package` |
| `<AppName>` | One of *your* apps that will use this auth | `Portal`, `Shop` |

---

# Part 1 — Understand & Decide

## 1. What you actually deploy

The solution has several projects, but **only two are websites**:

| Project | What it is | Gets a domain? |
|---|---|---|
| **Auth_API** | The authentication server (REST API). The brain. | ✅ Web app |
| **API_Gateway** | Public reverse proxy in front of Auth_API (rate limiting, security). Optional but recommended. | ✅ Web app |
| **Auth_DB** | SQL Server database (tables, procedures, seed). | ➡️ To a SQL Server, not a domain |
| **Auth_Localization** | Translations (7 languages). Compiled *into* Auth_API. | ❌ Ships inside Auth_API |
| **Auth.Sdk** | NuGet package your *other* apps install to trust tokens. | ❌ NuGet feed (optional) |
| **Auth_Setup** | One-time CLI to create the admin password hash. | ❌ Run once locally |

**Request flow** (with the optional Gateway):

```
Client ──▶ API Gateway (public)  ──▶  Auth API (private)  ──▶  SQL Server
           auth.<yourdomain>.com      adds X-Gateway-Token       database
           rate limit + headers       checks token, runs logic
```

You can skip the Gateway and let clients hit the Auth API directly — both layouts are shown below.

## 2. Two decisions to make now

### Decision A — Your domains

| Component | Recommended subdomain | Notes |
|---|---|---|
| API Gateway (public) | `auth.<yourdomain>.com` | The name everyone uses |
| Auth API (private) | `auth-api.<yourdomain>.com` | Behind the Gateway |
| Your frontend(s) | `app.<yourdomain>.com` | Used for CORS later |

* **Layout A (simple, no Gateway):** clients hit `auth.<yourdomain>.com` → Auth API directly.
* **Layout B (recommended):** clients hit `auth.<yourdomain>.com` (Gateway) → `auth-api.<yourdomain>.com` (Auth API).

Write these down — they go into `Issuer`, `Audience`, and CORS in Phase 3.

### Decision B — How secrets are stored (`SecretManagement:StorageMode`)

The system needs a few secrets: the **RSA signing key** (JWT), an **HMAC key** (refresh tokens),
and a **gateway token**. One setting decides how they're protected at rest:

| Mode | Where keys live | Protected by | Portable to another server? | Best for |
|---|---|---|---|---|
| **`PlainText`** | `appsettings.Production.json` (readable) | File permissions only | ✅ Copy the file | Quick start; you trust the file system |
| **`Certificate`** | Encrypted `secrets.dpapi` | An X.509 cert **you own** | ✅ Carry `.pfx` + key ring + file | **Shared hosting**; servers that may move |
| **`Dpapi`** | Encrypted `secrets.dpapi` | Windows DPAPI (this machine) | ❌ Breaks if the host moves you | A Windows box you fully control |

Setup steps for each mode are in [Reference §A](#a-storage-mode-setup). Pick one now; you can't switch
painlessly later (switching regenerates keys and logs everyone out).

> **First startup auto-generates the keys** for *all three* modes (when `AutoGenerateKeys: true`)
>You never run a key-gen command. PlainText writes them into
> `appsettings.Production.json`; Certificate/Dpapi write the encrypted `secrets.dpapi`.
> Prefer to control the key material yourself (e.g. for painless server migration)? You can generate
> or import your own keys instead — see [Reference §G](#g-provision-your-own-keys-byok--painless-migration).

## 3. How config and secrets are read

ASP.NET Core loads config in layers; a later layer overrides only the keys it sets:

```
appsettings.json          base — every setting, with default values
  └─ appsettings.Production.json   only what CHANGES in production (you edit this)
       └─ environment variables    optional overrides (good for secrets)
            └─ secrets.dpapi        Certificate/Dpapi only — wins for the secrets it holds
```

`appsettings.Production.json` is intentionally small — anything you don't list is inherited from
the base. The app uses Production settings whenever `ASPNETCORE_ENVIRONMENT=Production` **or the
variable is unset** (Production is the default).

### Three kinds of "value" in the files — don't confuse them

| You see | What it is | What to do |
|---|---|---|
| `"{{GOOGLE_CLIENT_ID}}"` | A **fill-me-in placeholder**. *No code resolves `{{ }}`.* | Replace it with the real value in `appsettings.Production.json`. If left, the literal text is used → broken. |
| `"PasswordEnvironmentVariable": "AUTH_DP_CERT_PASSWORD"` | The **name of an OS env var** the code reads. | Create an env var with exactly that name. |
| *(absent)* `ConnectionStrings__AuthDb` | A **generic ASP.NET Core override** — any setting, with `:` → `__`. | Create the env var on the host to keep a secret out of files. |

**Which to use:** put **non-secrets** (URLs, CORS origins, Google Client ID) straight in
`appsettings.Production.json`. Keep **secrets** out of files with `__` env vars
(`ConnectionStrings__AuthDb`, `Email__Password`). The **certificate password** is the one special
case wired to a named variable (`AUTH_DP_CERT_PASSWORD`).

### Where each secret can live

| Secret | PlainText mode | Certificate / Dpapi mode |
|---|---|---|
| RSA / HMAC / gateway token | Auto-generated into `appsettings.Production.json` | Auto-generated into `secrets.dpapi` (encrypted) |
| Connection string | `appsettings.Production.json` or `ConnectionStrings__AuthDb` env var | Same options as PlainText (file or `__` env var) — **plus** you can encrypt it in `secrets.dpapi` ([Reference §B](#b-advanced-encrypt-the-connection-string--smtp-password)) |
| SMTP password | `Email__Password` env var | Same option as PlainText (`Email__Password`) — **plus** you can encrypt it in `secrets.dpapi` ([Reference §B](#b-advanced-encrypt-the-connection-string--smtp-password)) |

---

# Part 2 — Deploy (in order)

**Prerequisites:** .NET 10 SDK (build machine), .NET 10 Hosting Runtime (server), SQL Server, a SQL
client (SSMS / Azure Data Studio), a Windows host for DPAPI/Certificate modes.

## Phase 1 — Build

```bash
git clone <repository-url>
cd AuthSystem
dotnet build Auth/Auth.sln -c Release
```

If it fails, it's almost always a missing .NET 10 SDK. Fix the build before continuing.

## Phase 2 — Database

The Auth API can't start without a database. Do this first.

1. **Create an empty database** and a least-privilege SQL login (read/write on that DB, **not** `sa`).
   Note the server, DB name, user, password.
2. **Create the schema + seed data.** Publish the `Auth_DB` project to *your* database (not
   `master`): in Visual Studio, right-click **Auth_DB** → **Publish**, set the target connection to
   your database, and Publish. This deploys every table and the seed data (permissions, roles, the
   default admin) from the project itself, so it is always current.

   The `Auth_DB` project is the single source of truth for the schema — publishing is the only
   supported path. Publish profiles under `Auth_DB/PublishLocations/` are per-environment and
   gitignored; yours is created on first publish. Leave `CreateNewDatabase` off (step 1 already
   created the database) — publishing only needs read/write on that one database, not `sa`.
3. **Set a real admin password** (the seed ships a placeholder that can't log in):
   ```bash
   dotnet run --project Auth/Auth_Setup -c Release
   ```
   It prints an `UPDATE [dbo].[Users] SET [PasswordHash] = N'...' WHERE [Email] = 'admin@company.com';`
   — run that against your DB. Default password is `Admin@123!` (edit `Auth_Setup/Program.cs` to change).
   You'll be forced to change it on first login.

## Phase 3 — Configure the Auth API

Edit `Auth/Auth_API/appsettings.Production.json` (never put production secrets in the base
`appsettings.json`):

```json
{
  "ConnectionStrings": {
    "AuthDb": "Data Source=<SQL_SERVER>;Initial Catalog=<DB>;User Id=<USER>;Password=<PWD>;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Issuer": "https://auth.<yourdomain>.com",
    "Audience": "https://auth.<yourdomain>.com"
  },
  "SecretManagement": { "StorageMode": "PlainText", "AutoGenerateKeys": true, "EnableAdminApi": false },
  "Cors": { "AllowedOrigins": [ "https://app.<yourdomain>.com" ], "AllowCredentials": true },
  "Email": { "Enabled": false }
}
```

* **Issuer/Audience** = your public auth URL (the Gateway's, in Layout B). Your apps/SDK must use
  these exact values. Same URL for both is fine.
* **CORS** is required in production — list real origins; `*` or empty makes the app refuse to start.
* **StorageMode** — keep `PlainText` for the simplest path, or follow [Reference §A](#a-storage-mode-setup)
  for Certificate/Dpapi. In PlainText, give the app **write permission** to
  `appsettings.Production.json` so it can save the generated keys.
* **`DataProtection:KeyPath`** — set this to a writable folder **outside the public web root** on
  IIS / shared hosting, even in PlainText mode. ASP.NET Core's Data Protection key ring is created at
  startup regardless of StorageMode; if `KeyPath` is empty it defaults to `%ProgramData%\AuthSystem\Keys`,
  and under an IIS app-pool identity with no loaded user profile it falls back further to
  `C:\Windows\System32\config\systemprofile\AppData\Local`, which the process **cannot write** —
  producing `An error occurred while reading the key ring / Access to the path ... is denied` and a
  failed startup. Point the **Auth API and the API Gateway at the SAME folder** so they share one ring
  (e.g. a `secrets` folder next to the app). Grant the app-pool identity *Modify* on that folder.
* **Email** — set `Enabled: true` and SMTP host/port/sender when you need password reset; keep the
  password out of the file (`Email__Password` env var, or [Reference §B](#b-advanced-encrypt-the-connection-string--smtp-password)).
* **Gateway** — Layout B: leave `Gateway:ValidationEnabled: true`. Layout A: set it to `false`, or
  the API rejects every request that lacks the gateway token.

## Phase 4 — Publish and run the Auth API

```bash
dotnet publish Auth/Auth_API/Auth_API.csproj -c Release -o ./publish/auth-api
```

This produces the DLLs, the `appsettings*.json` files, the translations, and a **`web.config`**.

**Set the environment to Production** in `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\Auth_API.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <!-- add ConnectionStrings__AuthDb / Email__Password / AUTH_DP_CERT_PASSWORD here if you use them -->
  </environmentVariables>
</aspNetCore>
```

> **Make sure** `web.config` is a **source file in the project** (kept out of source control — it
> holds secrets) rather than something you hand-edit on the server after every deploy. The publish
> keeps your `<environmentVariables>` and only rewrites `processPath`/`arguments`. If you edit only
> the *deployed* copy, the next publish overwrites it and the app loses its env vars.
>
> **Attention (Certificate/Dpapi mode):** **each** app needs `AUTH_DP_CERT_PASSWORD` in **its own**
> `web.config` — the Gateway loads the same `.pfx` as the API. A missing variable on the Gateway side
> is the classic cause of a Gateway-only `HTTP 500.30` while the API runs fine.


**Deploy:**
* **Shared hosting (Plesk/IIS):** create the site/subdomain, upload everything from
  `./publish/auth-api`. **It auto-starts** — IIS's ASP.NET Core Module launches it on the first
  request. No CLI, no "start" button.
* **A server you control:** `dotnet Auth_API.dll` (with `ASPNETCORE_ENVIRONMENT=Production`), behind
  IIS or as a Windows Service for HTTPS and auto-restart.

**On first run** the app auto-generates the secret keys. Check the log for *"Generated plain-text
secrets"* (PlainText) or *"auto-generating cryptographic keys"* (Certificate/Dpapi), plus the
public key (safe to share). A permission/path error means the write target isn't writable — fix it
and restart ([Reference §A](#a-storage-mode-setup)).

> ### ⚠️ CAUTION — don't let the second publish wipe your keys
> The keys are generated **on the server on first run** and (Certificate/Dpapi) live in the
> `secrets` folder, not in your repo. A careless **re-publish** can destroy them — invalidating every
> token, logging out all users, and desyncing the gateway token. The durable fix is architectural,
> not a checkbox you must remember: **keep the `secrets` folder OUTSIDE the publish destination**
> (a sibling of the site root, not under it). Then Visual Studio's *"Remove extra files in
> destination"* can stay **on** safely, and `secrets.dpapi` / the key ring are never in the wipe path.
> Set `AutoGenerateKeys` back to **`false`** after the first run so that, if secrets ever *do* go
> missing, the app **fails loudly instead of silently minting new keys**. Full procedure and the
> fallback for when `secrets` must live inside the deploy folder: [Reference §E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys).

## Phase 5 — (Optional) API Gateway

Skip if you chose Layout A (and set `Gateway:ValidationEnabled: false` on the API).

Edit `Auth/API_Gateway/appsettings.Production.json`:

```json
{
  "ReverseProxy": { "Clusters": { "auth-cluster": { "Destinations": {
    "auth-api": { "Address": "https://auth-api.<yourdomain>.com" } } } } },
  "Cors": { "AllowedOrigins": [ "https://app.<yourdomain>.com" ] },
  "AllowedHosts": "<yourdomain>.com;*.<yourdomain>.com"
}
```

The Gateway must send the same `X-Gateway-Token` the API expects, and use the **same StorageMode**:

* **PlainText:** copy the API's generated `Gateway:ExpectedToken` (from its
  `appsettings.Production.json`) into the Gateway's `Gateway:Token`.
* **Certificate / Dpapi:** run the Gateway on the **same machine** and point its
  `DataProtection:KeyPath`, `SecretManagement:SecretFilePath` (and certificate settings) at the
  **same** locations the API uses — both then read the token automatically.

> **Make sure (Certificate/Dpapi):** the Gateway needs its **own** `web.config` carrying
> `AUTH_DP_CERT_PASSWORD` (same value as the API's) — without it the Gateway can't open the `.pfx`
> and dies with `HTTP 500.30` even though the API is healthy. The API and Gateway can share **one**
> secrets folder: on IIS/Plesk the app-pool identity can read across sibling subdomain folders, so
> point both at a single folder (the API's) instead of keeping two copies — one source of truth, and
> it survives key-ring rotation. Grant the API **Modify** and the Gateway **Read** on that folder.

> **Note:** the Gateway's readiness probe derives from `Services:AuthApi:BaseUrl` (it appends
> `/ready`). Set `BaseUrl` to the API's real URL; leave `Services:AuthApi:ReadyUrl` empty unless
> `/ready` lives on a different host. A leftover `{{…}}` placeholder there is ignored (it used to
> crash startup with a `UriFormatException` → 500.30).

Publish and deploy as its own subdomain (`auth.<yourdomain>.com`), same as the API. **The same
first-publish vs. every-publish-after rules apply** ([Reference §E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys)):

```bash
dotnet publish Auth/API_Gateway/API_Gateway.csproj -c Release -o ./publish/gateway
```

## Phase 6 — Verify and log in

Use your **public** URL (Gateway in Layout B, else the API):

```bash
curl https://auth.<yourdomain>.com/health   # app is up
curl https://auth.<yourdomain>.com/ready    # up AND can reach the database
curl https://auth.<yourdomain>.com/.well-known/jwks.json   # signing keys loaded

curl -X POST https://auth.<yourdomain>.com/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "admin@company.com", "password": "Admin@123!" }'
```

* `/ready` failing (but `/health` OK) = wrong connection string — the #1 first-deploy error.
* Login returns tokens → it works. You'll get `requiresPasswordChange: true`; change it via
  `POST /api/v1/auth/change-password`. `User.InvalidCredentials` → you skipped the admin password in Phase 2.

## Phase 7 — Go-live checklist

- [ ] HTTPS with valid TLS on every domain (the apps force HTTPS + HSTS).
- [ ] Admin password changed from `Admin@123!`.
- [ ] SQL user is least-privilege (not `sa`).
- [ ] `Cors:AllowedOrigins` lists only real front-end domains.
- [ ] `SecretManagement:EnableAdminApi: false` (enable only briefly to rotate keys).
- [ ] `Gateway:ValidationEnabled` matches your layout (`true` with Gateway).
- [ ] **Secrets backed up:** PlainText → `appsettings.Production.json`; Certificate/Dpapi →
      `secrets.dpapi` **+** the key-ring folder (**+** the `.pfx` for Certificate). Losing them
      invalidates all tokens — and DPAPI can't be recovered on a new machine.
- [ ] Database backups scheduled.

## Phase 8 — (Optional) Connect other apps via the SDK

Only if you have *other* .NET apps that must trust this system. **Auth.Sdk** lets them validate
tokens/API-keys without sharing any private key. Quickest: reference the project
(`<ProjectReference Include="..\Auth.Sdk\Auth.Sdk.csproj" />`). Proper: publish to a NuGet feed
(see `SDK_PUBLISHING_GUIDE.md`). Consumer setup and full walkthrough are in `APPLICATION_INTEGRATION_GUIDE.md`.

---

# Reference

## A. Storage-mode setup

All modes auto-generate the keys on first start. They differ only in *where* keys live and *how*
they're protected. **The Gateway must use the same mode as the API.**

### PlainText

Keys are written into `appsettings.Production.json` on first start. No certificate, no extra files.

```json
"SecretManagement": { "StorageMode": "PlainText", "AutoGenerateKeys": true }
```

Requirement: the app's account needs **write permission** to `appsettings.Production.json`. After
the first run, set `"AutoGenerateKeys": false` so keys are never regenerated by accident.
Trade-off: the keys sit in readable text — lock down file permissions and never commit the file.

> **Expected, benign warning in this mode:** `No XML encryptor configured. Key … may be persisted to
> storage in unencrypted form.` Data Protection still creates a key ring at startup, but in PlainText
> mode that ring protects **none of your secrets** (the RSA/HMAC/gateway values live in
> `appsettings.Production.json`), so nothing sensitive is written unencrypted. Under an IIS app-pool
> identity with no loaded user profile the automatic DPAPI key encryption can't apply, which is why
> the warning appears. **Bottom line:** keep PlainText and ignore the warning, **or** switch to
> **Certificate** mode below to both silence it and encrypt your secrets at rest.

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

> ⚠️ Once `PasswordEnvironmentVariable` is set, the app reads **only** that variable — it does
> **not** fall back to the `Password` field. A missing/misspelled variable makes the password
> resolve to `null`, and startup fails with *"Failed to load the Data Protection certificate … the
> password is correct."* Fix the variable, or clear `PasswordEnvironmentVariable` to use `Password`.

> **Note — one shared folder, not two copies.** The API and Gateway must use the **same** cert, key
> ring, and `secrets.dpapi`. On IIS/Plesk the app-pool identity can read across sibling subdomain
> folders, so point **both** apps' `KeyPath` + `SecretFilePath` at a **single** folder (the API's) —
> don't copy the folder into each subdomain. One source of truth means key-ring rotation never
> desyncs them. The API needs **Modify** on that folder (it writes/rotates keys); the Gateway needs
> only **Read**. **Each app still needs `AUTH_DP_CERT_PASSWORD` in its own `web.config`.**

### Dpapi (Windows-only, zero setup)

Just leave `"StorageMode": "Dpapi"`. Secrets are encrypted with Windows DPAPI, bound to **this
machine + account**. If the host moves your site to another physical server, the file can't be
decrypted and keys regenerate (logging everyone out) — **unless you hold the key material yourself
and re-import it on the new machine** ([§G](#g-provision-your-own-keys-byok--painless-migration)).
Fine for a box you fully control.

## B. (Advanced) Encrypt the connection string & SMTP password

In Certificate/Dpapi mode the keys are encrypted automatically, but the **connection string** and
**SMTP password** are not auto-added. The encrypted `secrets.dpapi` *does* have slots for both, and
when present they override `ConnectionStrings:AuthDb` and `Email:Password` from appsettings. There
is **no HTTP endpoint or CLI** to set them — you write them with a tiny one-off program that reuses
the app's certificate-protected key ring.

> **Practicality check:** this requires running a console app **on the same machine, with the same
> certificate**. On locked-down shared hosting where you can't, keep these two as env vars
> (`ConnectionStrings__AuthDb`, `Email__Password`) instead — still out of any readable file.

**Order matters:** run this **after** the API's first start (so the file already holds the RSA/HMAC/
gateway keys), or call `GenerateMissingKeysAsync` first as below so a fresh file is complete.

```csharp
// One-off console app referencing Auth.Infrastructure, Auth.Application, Auth.Shared.
using Auth.Application.Configuration;
using Auth.Infrastructure.Security;
using Auth.Shared.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Point these at the SAME locations the Auth API uses:
var keyPath        = @"C:\inetpub\vhosts\<webspace>\secrets";
var secretFilePath = @"C:\inetpub\vhosts\<webspace>\secrets\secrets.dpapi";
var certSettings   = new DataProtectionCertificateSettings
{
    PfxPath = @"C:\inetpub\vhosts\<webspace>\secrets\dp-cert.pfx",
    PasswordEnvironmentVariable = "AUTH_DP_CERT_PASSWORD"   // must be set in this process
};

var provider = new ServiceCollection()
    .AddDataProtection()
    .SetApplicationName("AuthSystem")
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath))
    .ConfigureKeyProtection(SecretStorageMode.Certificate, certSettings)
    .Services.BuildServiceProvider()
    .GetRequiredService<IDataProtectionProvider>();

var settings = Options.Create(new SecretManagementSettings { SecretFilePath = secretFilePath });
var secrets  = new DpapiSecretService(provider, settings, NullLogger<DpapiSecretService>.Instance);

await secrets.GenerateMissingKeysAsync(CancellationToken.None);   // safe if keys already exist
await secrets.SetSecretAsync("ConnectionStrings.AuthDb",
    "Data Source=...;Initial Catalog=...;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=true",
    CancellationToken.None);
await secrets.SetSecretAsync("SmtpPassword", "<your-smtp-password>", CancellationToken.None);
Console.WriteLine("Wrote encrypted secrets to " + secretFilePath);
```

Then **remove** `ConnectionStrings:AuthDb` and `Email:Password` from `appsettings.Production.json`
and restart — the API now reads both from the encrypted file. (For Dpapi mode, use
`SecretStorageMode.Dpapi` and drop `certSettings`/`ConfigureKeyProtection`.)

## C. Environment variables on Plesk and cPanel

The app reads env vars two ways: generic settings use `__` (`ConnectionStrings__AuthDb`,
`Email__Password`); `AUTH_DP_CERT_PASSWORD` is read by its exact name. Both are created the same way.

* **Plesk #1 gotcha:** ignore the **ASP.NET Settings** / "Connection string manager" page — it's for
  old .NET Framework and this .NET 10 app never reads it.
* **web.config method (always works on IIS):** add `<environmentVariable name="…" value="…" />`
  lines inside `<aspNetCore>` (Phase 4). Names must match **exactly** — a wrong name is silently
  ignored. Browsers can't download `.config` files (404), but it's still plaintext on disk.
* **Host-native (keeps secrets out of deployed files):** Plesk with a *Dedicated IIS Application
  Pool*, or cPanel's **Setup .NET App → Environment variables**, lets you set the same names there
  instead. Use this for `AUTH_DP_CERT_PASSWORD` when you don't want it in `web.config`.
* **cPanel/Linux:** DPAPI doesn't exist — use PlainText or Certificate mode; paths use `/`.

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
| Gateway 500.30 with `UriFormatException` | `Services:AuthApi:ReadyUrl`/`BaseUrl` missing or left as a literal `{{…}}` placeholder | Set `Services:AuthApi:BaseUrl` to the API's real URL — `ReadyUrl` derives from it (leave `ReadyUrl` empty) |
| "An error occurred while reading the key ring" / "Access to … `systemprofile` … denied" | `DataProtection:KeyPath` empty → default key-ring folder not writable by the app-pool identity | Set `KeyPath` to a writable folder outside the web root (the **same** one for API + Gateway); grant the app pool *Modify* |
| `No XML encryptor configured. Key … unencrypted form` (warning) | PlainText/Dpapi key ring has no at-rest encryptor (no loaded profile under the app-pool identity) | **Benign in PlainText** — the ring protects none of your secrets. To encrypt at rest, switch to **Certificate** mode (§A) |
| "Connection string 'AuthDb' not found" | No connection string in active config | Add it to `appsettings.Production.json` or `ConnectionStrings__AuthDb` |
| `/ready` fails, `/health` works | Can't reach the database | Check server, DB name, user/password, firewall, `Encrypt` |
| Startup error about CORS | `Cors:AllowedOrigins` empty or `*` in production | List explicit `https://…` origins |
| "Failed to decrypt … different machine" | DPAPI file/keys from another machine | Keep API+Gateway on one machine, or delete the secret files to regenerate (logs everyone out) |
| "Failed to load the Data Protection certificate" | Wrong/missing `AUTH_DP_CERT_PASSWORD` or `.pfx` | Set the env var correctly; verify the `.pfx` path/password |
| Gateway 500.30 while the API is healthy (Certificate/Dpapi) | The **Gateway's own** `web.config` is missing `AUTH_DP_CERT_PASSWORD`, so it can't open the `.pfx` | Add the same `AUTH_DP_CERT_PASSWORD` to the Gateway's `web.config` (it's a separate file from the API's) |
| Tokens suddenly invalid / everyone logged out after a deploy | A re-publish wiped/overwrote `secrets.dpapi` (or the PlainText keys) and they regenerated | Don't wipe the `secrets` folder on republish; keep it outside the deploy target and `AutoGenerateKeys=false` ([§E](#e-first-publish-vs-every-publish-after-dont-wipe-your-keys)) |
| Login `User.InvalidCredentials` for admin | Admin hash still the placeholder | Redo Phase 2 step 3 |
| Generated keys not saved (PlainText/Cert) | Write target not writable | Fix write permission on the appsettings file / secrets folder, restart |
| Production using dev settings | `ASPNETCORE_ENVIRONMENT` not `Production` | Set it in `web.config` / host settings |

## E. First publish vs. every publish after (don't wipe your keys)

Two publish settings are **safe on the first deploy and dangerous on every one after**, because the
keys are generated **on the server**, not in your repo:

| Setting | Where | First (clean) publish | Every publish after |
|---|---|---|---|
| **Remove extra files in destination** (`<DeleteExistingFiles>` in the `.pubxml`) | VS publish profile | On — start from a clean folder | **Off** *(or keep on — see below)* |
| **`AutoGenerateKeys`** | `appsettings.Production.json` | `true` — mint the keys | **`false`** — never regenerate |

**Why it bites:** "Remove extra files" deletes anything in the destination not in the publish output.
If your `secrets` folder (key ring + `secrets.dpapi`) sits **inside** that destination it gets
deleted, and with `AutoGenerateKeys=true` the app then **silently mints new keys** on next start —
every existing token dies and the gateway token desyncs. In PlainText the same happens *without* the
wipe, because `appsettings.Production.json` is itself part of the publish and overwrites the server's
key-populated copy.

### The robust layout (recommended) — remove the foot-gun instead of documenting it

Put the `secrets` folder **OUTSIDE the publish destination** (a sibling of the site root, not under
it). Then:

* **Remove extra files can stay ON** every publish — it can't reach `secrets`, and you still get
  clean deploys (no orphaned DLLs).
* Set **`AutoGenerateKeys=false`** after the first run, permanently. It then acts as a **fail-loud
  fuse**: if secrets ever go missing the app refuses to start instead of quietly rotating keys.
* Nothing depends on a human remembering a checkbox months later.

### First clean publish — ordered, no skipping

1. **(Certificate mode only)** Create `dp-cert.pfx`, place it in the `secrets` folder, and set
   `AUTH_DP_CERT_PASSWORD` (§A) — the app **won't start** without it.
2. In `appsettings.Production.json`, set the secrets/key-ring paths (`SecretFilePath`,
   `DataProtection:KeyPath`) and `AutoGenerateKeys: true`.
3. Ensure `web.config` is a **source** file (gitignored) carrying the env vars — `ASPNETCORE_ENVIRONMENT`,
   `AUTH_DP_CERT_PASSWORD`, any `__` overrides — so publishing preserves them.
4. **Auth API:** enable *Remove extra files in destination*, publish.
5. Hit the API once; confirm the log shows keys generated, grab the public key, and **back up all
   three**: `.pfx`, key ring, `secrets.dpapi` (losing the `.pfx` is unrecoverable).
6. **PlainText only:** copy the API's generated `Gateway:ExpectedToken` into the Gateway's
   `Gateway:Token`. (Certificate/Dpapi: skip — the Gateway reads it from the shared `secrets.dpapi`.)
7. **Gateway:** point its paths at the **same** `secrets` folder, put `AUTH_DP_CERT_PASSWORD` in
   **its own** `web.config`, enable *Remove extra files*, publish.
8. Verify `/ready` on both, then do one real login through the Gateway.

**Then immediately:** set `AutoGenerateKeys` back to **`false`** (re-publish that one file). If you
could **not** move `secrets` outside the deploy target, also turn **off** *Remove extra files in
destination* in both publish profiles — that checkbox is then the only thing between a routine deploy
and a full key wipe.

---

## F. Password protection — pepper & breached-password check

Both features are **opt-in** (default `false`) and toggled per environment under `Password` in
`appsettings`. The Argon2id hashing itself (per-password salt, constant-time compare, rehash-on-login)
is always on and needs no configuration.

### F.1 Pepper (server-side secret key)

A pepper is a secret mixed into **every** password hash (Argon2id `KnownSecret`), stored in the
**secret store** (never in the database). It defends a **database-only breach** (SQL injection,
stolen backup, rogue DBA): without the pepper, the stolen hashes can't be brute-forced. On a fully
compromised host (DB + secret store both taken) it adds nothing — its value is the *separation*
between the DB and the secret store.

**Enable:**
```jsonc
"Password": { "Pepper": { "Enabled": true } }
```
On startup the app provisions a pepper if none exists and stores it in the active secret store
(PlainText → `appsettings.Production.json`; Certificate/Dpapi → `secrets.dpapi`) under
`Password:Pepper:Keys:{id}` + `Password:Pepper:CurrentKeyId`. Only `Enabled` belongs in appsettings;
the key material is secret-managed. The app **refuses to start** if peppering is enabled but the
pepper can't be persisted (an ephemeral pepper would lock everyone out on restart).

**Migration is automatic & safe.** Existing (unpeppered) hashes keep verifying and are transparently
upgraded to peppered ones (`keyid` added) on each user's next successful login — no mass reset, no
downtime. The seeded `admin` hash works the same way.

> [!CAUTION]
> **Losing the pepper locks out every peppered user — permanently and unrecoverably.** Back it up
> with exactly the same rigor as the JWT/HMAC keys: it lives in the same secret store, so the Phase 7
> "secrets backed up" item already covers it **as long as that store is truly backed up**. Treat
> enabling the pepper as a one-way decision unless you keep the key material.

**Rotation (advanced):** add a new pepper id, keep the previous id(s) in the store (so old hashes
still verify), and point `Password:Pepper:CurrentKeyId` at the new id. New and next-login hashes use
the new pepper; old ones migrate on login. Remove a retired id only once you're certain no hash still
uses it.

### F.2 Breached / weak password block (HIBP Pwned Passwords)

Rejects or warns on passwords found in known breaches, using the **free, keyless, unthrottled** HIBP
Pwned Passwords *range* API with k-anonymity (only the first 5 chars of the SHA-1 hash leave the
server; the plaintext never does). Checked on register / change / reset / admin-create.

```jsonc
"Password": {
  "BreachedPasswordCheck": {
    "Enabled": true,            // false = fully inert: no HttpClient, no external call
    "Mode": "Enforce",          // Enforce = reject; Warn = allow but flag
    "FailOpen": true,           // allow if HIBP is unreachable (logged); false = reject on outage
    "RejectThreshold": 1,       // min breach occurrences to treat as breached
    "TimeoutMs": 2000
  }
}
```

- **Enforce** → a breached password is rejected (`User.PasswordBreached`, HTTP 400).
- **Warn** → the operation succeeds but the response carries an **`X-Password-Warning`** header
  (and `X-Password-Warning-Code: User.PasswordBreached`) so the client can nudge the user. Works on
  204 No Content responses too.
- **FailOpen=true** (default) means an HIBP outage never blocks password changes — the event is
  logged. Set `false` only if you'd rather hard-fail than risk admitting an unchecked password.
- To remove the external dependency entirely, you can later host the HIBP dataset locally behind the
  same `IBreachedPasswordChecker` interface — no caller changes.

`Password:MinimumLength` is independent policy (currently 8); raising it to 12 is recommended for
passphrase-friendly strength and only affects new/changed passwords.

---

## G. Provision your own keys (BYOK) & painless migration

By default the app **mints the keys for you** on first start ([Part 1 §2](#decision-b--how-secrets-are-stored-secretmanagementstoragemode)). You don't have to.
Two alternatives let you control the key material — useful when you must move servers without
logging everyone out, or you simply don't want the app to decide your keys:

| You want… | Use | Third-party tool? | Portable across servers? |
|---|---|---|---|
| Strong keys, but generated by **this system on demand** (not on first-run) | Admin **`generate/*`** endpoints | No | Only via **Certificate** mode — the minted private values can't be read back |
| Keys **you generate and hold** yourself, encrypted into this system | Admin **`import/*`** endpoints (Certificate/Dpapi) **or** hand-edit appsettings (PlainText) | Your choice | ✅ Yes — re-import the same material on any server, even in **Dpapi** mode |

**The migration win:** if *you* hold the plaintext key material (in a vault/password manager), you
re-encrypt it on each server. The new machine produces **identical, still-valid tokens** — no mass
logout — and you never carry the machine-bound `secrets.dpapi` or the key ring. This is the only way
to make **Dpapi** mode portable (otherwise it "breaks if the host moves you", [§A](#dpapi-windows-only-zero-setup)).

### The admin secrets API

All key operations live under `…/api/v1/admin/secrets/` and are gated three ways:

* `SecretManagement:EnableAdminApi: true` — **off by default**; turn it on only while provisioning,
  then back off (Phase 7 checklist).
* A bearer token from a user with the **`secrets.manage`** permission (log in as admin first).
* HTTPS — these requests carry private keys; never send them over plain HTTP.

| Endpoint | Does | Returns |
|---|---|---|
| `GET  secrets/status` | Lists which secrets are set (never the values) | status only |
| `POST secrets/generate/rsa` \| `/hmac` \| `/gateway-token` | **System** mints a fresh random key | public key / token / message |
| `POST secrets/import/rsa` \| `/hmac` \| `/gateway-token` | Stores a **value you supply**, encrypted | derived public key / message |

> **`import/*` requires Certificate or Dpapi mode.** In PlainText mode it returns
> `409 Secret.ImportNotSupportedInPlainText` — there the keys live in `appsettings.Production.json`,
> so just paste them in (see *PlainText BYOK* below). Importing **replaces** the current key, so if
> the value differs from what's stored, existing tokens are invalidated — re-importing the *same*
> value is a safe no-op for live tokens.

### Generate the material yourself (formats the system expects)

| Secret | Format | Generate it |
|---|---|---|
| RSA private key | PKCS#8 (or PKCS#1) PEM, **≥ 2048-bit** | `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out jwt-private.pem` |
| HMAC key | Base64 of **≥ 32 bytes** (256-bit) | `openssl rand -base64 32` |
| Gateway token | Any string, **≥ 16 chars** (Base64 of 32 bytes recommended) | `openssl rand -base64 32` |

You supply only the **private** RSA key — the server derives and stores the matching public key and
returns it (use it for JWKS/SDK consumers). PowerShell equivalents:
`[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`.

### Import over HTTP (shared hosting — no console access needed)

Each request body is `{ "value": "<the key>" }`. For the RSA PEM, JSON-escape the newlines as `\n`.

```bash
# 1) Log in as an admin who has the secrets.manage permission, capture the access token.
TOKEN=$(curl -s -X POST https://auth.<yourdomain>.com/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{ "email": "admin@company.com", "password": "<admin-password>" }' | jq -r .accessToken)

# 2) Import each key (Certificate/Dpapi mode, EnableAdminApi=true).
curl -X POST https://auth.<yourdomain>.com/api/v1/admin/secrets/import/rsa \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d "{ \"value\": \"$(awk '{printf "%s\\n", $0}' jwt-private.pem)\" }"

curl -X POST https://auth.<yourdomain>.com/api/v1/admin/secrets/import/hmac \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "value": "<your-base64-hmac-key>" }'

curl -X POST https://auth.<yourdomain>.com/api/v1/admin/secrets/import/gateway-token \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{ "value": "<your-gateway-token>" }'
```

The connection string and SMTP password have **no** HTTP import — keep them as `__` env vars, or use
the console app in [§B](#b-advanced-encrypt-the-connection-string--smtp-password) on a machine you control.

### PlainText BYOK (no API needed)

In PlainText mode you "import" by editing the file: put your values under `Jwt:PrivateKeyPem`,
`Jwt:RefreshTokenHmacKeyPlain`, and `Gateway:ExpectedToken` in `appsettings.Production.json`. The app
only generates secrets that are **missing**, so pre-filled values are used verbatim and never
overwritten. Set `AutoGenerateKeys: false` as well.

### Migration procedure (move to a new server, keep everyone logged in)

1. **Once, on the old server (or offline):** capture the plaintext key material into your vault —
   either values you generated, or read them from the source (PlainText: from `appsettings`;
   Certificate/Dpapi: there's no read-back, so this only works if you imported/own them).
2. **On the new server:** set the same `StorageMode`, `AutoGenerateKeys: false`,
   `EnableAdminApi: true` (Certificate: also place the `.pfx` and set `AUTH_DP_CERT_PASSWORD`), deploy,
   and start. With `AutoGenerateKeys=false` and an empty store the app **fails loud** instead of
   minting new keys — that's expected until you import.
3. **Import** the three keys via the API above (or paste into appsettings for PlainText).
4. **Verify** `/.well-known/jwks.json` shows your public key and a pre-existing token still validates,
   then set `EnableAdminApi: false` again.

Tokens issued by the old server keep working because the signing/HMAC material is byte-for-byte the
same. The gateway token must match on both API and Gateway, exactly as in [Phase 5](#phase-5--optional-api-gateway).

---

**The whole flow:** build → database (schema + admin password) → edit
`Auth_API/appsettings.Production.json` → publish + upload Auth API (auto-starts, generates secrets)
→ (optional) Gateway on the same machine → verify `/ready` + log in → security checklist →
(optional) SDK. 🎉
