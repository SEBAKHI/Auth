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
| **`PlainText`** *(default)* | `appsettings.Production.json` (readable) | File permissions only | ✅ Copy the file | Quick start; you trust the file system |
| **`Certificate`** | Encrypted `secrets.dpapi` | An X.509 cert **you own** | ✅ Carry `.pfx` + key ring + file | **Shared hosting**; servers that may move |
| **`Dpapi`** | Encrypted `secrets.dpapi` | Windows DPAPI (this machine) | ❌ Breaks if the host moves you | A Windows box you fully control |

Setup steps for each mode are in [Reference §A](#a-storage-mode-setup). Pick one now; you can't switch
painlessly later (switching regenerates keys and logs everyone out).

> **First startup auto-generates the keys** for *all three* modes (when `AutoGenerateKeys: true`,
> the default). You never run a key-gen command. PlainText writes them into
> `appsettings.Production.json`; Certificate/Dpapi write the encrypted `secrets.dpapi`.

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
2. **Create the schema + seed data.** Connect to *your* database (not `master`) and run:
   ```
   Auth/Auth_DB/PublishLocations/deploy_shared_hosting.sql
   ```
   (Or, on a server you control with Visual Studio: right-click **Auth_DB** → **Publish**.)
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
* **Email** — set `Enabled: true` and SMTP host/port/sender when you need password reset; keep the
  password out of the file (`Email__Password` env var, or [Reference §B](#b-advanced-encrypt-the-connection-string--smtp-password)).
* **Gateway** — Layout B: leave `Gateway:ValidationEnabled: true`. Layout A: set it to `false`, or
  the API rejects every request that lacks the gateway token.

## Phase 4 — Publish and run the Auth API

```bash
dotnet publish Auth/Auth_API/Auth_API.csproj -c Release -o ./publish/auth-api
```

This produces the DLLs, the `appsettings*.json` files, the translations, and a **`web.config`**.

**Set the environment to Production** in the published `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\Auth_API.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <!-- add ConnectionStrings__AuthDb / Email__Password / AUTH_DP_CERT_PASSWORD here if you use them -->
  </environmentVariables>
</aspNetCore>
```

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

Publish and deploy as its own subdomain (`auth.<yourdomain>.com`), same as the API:

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
(see `SDK_PUBLISHING_GUIDE.md`). Consumer setup and full walkthrough are in `CMS_INTEGRATION_GUIDE.md`.

---

# Reference

## A. Storage-mode setup

All modes auto-generate the keys on first start. They differ only in *where* keys live and *how*
they're protected. **The Gateway must use the same mode as the API.**

### PlainText (default)

Keys are written into `appsettings.Production.json` on first start. No certificate, no extra files.

```json
"SecretManagement": { "StorageMode": "PlainText", "AutoGenerateKeys": true }
```

Requirement: the app's account needs **write permission** to `appsettings.Production.json`. After
the first run, set `"AutoGenerateKeys": false` so keys are never regenerated by accident.
Trade-off: the keys sit in readable text — lock down file permissions and never commit the file.

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

### Dpapi (Windows-only, zero setup)

Just leave `"StorageMode": "Dpapi"`. Secrets are encrypted with Windows DPAPI, bound to **this
machine + account**. If the host moves your site to another physical server, the file can't be
decrypted and keys regenerate (logging everyone out). Fine for a box you fully control.

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

| Symptom | Cause | Fix |
|---|---|---|
| "Connection string 'AuthDb' not found" | No connection string in active config | Add it to `appsettings.Production.json` or `ConnectionStrings__AuthDb` |
| `/ready` fails, `/health` works | Can't reach the database | Check server, DB name, user/password, firewall, `Encrypt` |
| Startup error about CORS | `Cors:AllowedOrigins` empty or `*` in production | List explicit `https://…` origins |
| "Failed to decrypt … different machine" | DPAPI file/keys from another machine | Keep API+Gateway on one machine, or delete the secret files to regenerate (logs everyone out) |
| "Failed to load the Data Protection certificate" | Wrong/missing `AUTH_DP_CERT_PASSWORD` or `.pfx` | Set the env var correctly; verify the `.pfx` path/password |
| Login `User.InvalidCredentials` for admin | Admin hash still the placeholder | Redo Phase 2 step 3 |
| Generated keys not saved (PlainText/Cert) | Write target not writable | Fix write permission on the appsettings file / secrets folder, restart |
| Production using dev settings | `ASPNETCORE_ENVIRONMENT` not `Production` | Set it in `web.config` / host settings |

---

**The whole flow:** build → database (schema + admin password) → edit
`Auth_API/appsettings.Production.json` → publish + upload Auth API (auto-starts, generates secrets)
→ (optional) Gateway on the same machine → verify `/ready` + log in → security checklist →
(optional) SDK. 🎉
