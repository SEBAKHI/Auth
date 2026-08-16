# Auth.Sdk — packaging and publishing guide

This guide is for the person who has to turn the `Auth.Sdk` project into a shippable package and put it
somewhere other applications can install it from.

**Read this first: nothing has been published yet, and no pipeline exists.** There is no automated build,
no release tag, no package feed, and no application anywhere in this repository that uses it. This
guide is not "here is how the release process works". It is "here is how to publish it, starting from
where the project actually is today". Section 1 describes that starting point exactly, section 2 lists
what must be fixed before the first publish, and everything after that is the procedure.

**Terms used throughout, expanded once here.** *SDK* means Software Development Kit — a library another
team installs into their own application. *NuGet* is the package format and package manager that .NET
uses; a *package* is a single `.nupkg` file, and a *feed* is a server that stores packages so other
projects can install them. *JWT* means JSON Web Token, the signed token this system issues to signed-in
users. *API* means Application Programming Interface.

---

## 1. Current state — what exists today

### 1.1 What the SDK is

`Auth.Sdk` is a .NET class library that lets a *separate* .NET web application accept credentials issued
by this Auth system. It plugs three authentication schemes into the consuming application — JWT bearer
tokens, API keys, and webhook keys — and adds a `[RequirePermission("...")]` attribute for checking
permissions on an endpoint. It is a server-side library. It is not used by a browser.

*In code:* the project lives at `Auth/Auth.Sdk/`. It contains 19 files — one project file
(`Auth/Auth.Sdk/Auth.Sdk.csproj`) and 18 C# source files. Its entire public registration surface is one
method, `AddAuthSystemAuthentication`, at `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:23-25`.

### 1.2 The project file as it stands right now

This is the complete, unmodified contents of `Auth/Auth.Sdk/Auth.Sdk.csproj`. Every publishing
instruction later in this guide is expressed as a change to this file, so it is reproduced here in full
rather than described.

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <!-- Package Identity -->
    <PackageId>AuthSystem.Sdk</PackageId>
    <Version>1.0.0</Version>
    <Authors>AuthSystem Contributors</Authors>
    <Company>AuthSystem</Company>
    <Description>SDK for integrating external .NET applications with the AuthSystem. Provides JWT, API Key, and Webhook Key authentication handlers with permission-based authorization.</Description>

    <!-- Package Metadata -->
    <PackageTags>authentication;authorization;jwt;apikey;webhook;sdk</PackageTags>
    <RepositoryType>git</RepositoryType>

    <!-- Build Settings -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <!-- Microsoft.Extensions.Http and Microsoft.Extensions.Caching.Memory are deliberately
         NOT referenced: they ship inside the Microsoft.AspNetCore.App shared framework
         referenced above, so an explicit PackageReference is redundant (NU1510).
         JwtBearer is not part of that framework, so it stays. -->
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.10" />
  </ItemGroup>

</Project>
```

**Package metadata that is present today.** `PackageId` is `AuthSystem.Sdk`, `Version` is `1.0.0`,
`Authors` is `AuthSystem Contributors`, `Company` is `AuthSystem`, and there is a `Description`,
`PackageTags`, and `RepositoryType`. `GenerateDocumentationFile` is on, which is what puts the
IntelliSense documentation file into the package.

**Package metadata that is missing today.** None of these properties exists in the file:
`PackageLicenseExpression`, `PackageProjectUrl`, `RepositoryUrl`, `PackageReadmeFile`, `PackageIcon`,
`IsPackable`, `GeneratePackageOnBuild`, `IncludeSymbols`, `Copyright`, `AssemblyVersion`, `FileVersion`.
Section 4 covers which of them you must add and which you should leave out.

**Two identities are not declared and only default.** The namespace consumers write in their `using`
line and the name of the compiled file are both derived by the build from the project file's name; they
are not properties you can find in the file. Renaming `Auth.Sdk.csproj` changes both.

*Measured:* `dotnet msbuild Auth.Sdk.csproj -getProperty:RootNamespace -getProperty:AssemblyName` returns
`Auth.Sdk` for each.

### 1.3 What `dotnet pack` produces today

Packing already works. It is packing *well* that does not.

From the repository root, change into the SDK project folder and pack it:

```bash
cd "Auth/Auth.Sdk"
```

```bash
dotnet pack -c Release
```

**What success looks like.** The last two lines of the output are a warning and a success line:

```text
The package AuthSystem.Sdk.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
Successfully created package 'D:\...\Auth\Auth.Sdk\bin\Release\AuthSystem.Sdk.1.0.0.nupkg'.
```

The readme warning is expected on the current project and stays until you complete step 4.2. The path in
the success line is absolute and will start with wherever you cloned the repository.

**What is inside that package today**, listed by unzipping it (a `.nupkg` file is a zip archive):

```text
_rels/.rels
AuthSystem.Sdk.nuspec
lib/net10.0/Auth.Sdk.dll
lib/net10.0/Auth.Sdk.xml
[Content_Types].xml
package/services/metadata/core-properties/nuget.psmdcp
```

Six entries. `Auth.Sdk.dll` is the library, `Auth.Sdk.xml` is the IntelliSense documentation produced by
`GenerateDocumentationFile`, and `AuthSystem.Sdk.nuspec` is the metadata generated from the project file.
There is no readme and no license file in it, because neither exists yet.

**Packing on demand works; packing on every build does not.** `dotnet pack` succeeds, but an ordinary
`dotnet build` produces no package. That is the correct default and this guide does not change it.

*Measured:* `dotnet msbuild Auth.Sdk.csproj -getProperty:IsPackable -getProperty:GeneratePackageOnBuild`
returns `true` and `false`.

### 1.4 What does not exist

Every line in this table was checked in this repository. Each one is a thing a reader of the previous
version of this guide would reasonably have assumed was already in place.

| Thing | State | How it was checked |
|---|---|---|
| Any project that uses the SDK | **None.** `Auth.Sdk` appears exactly once outside its own folder — as a solution entry at `Auth/Auth.sln:40` | repository-wide search of every `.csproj`, `.sln`, `.yml`, `.ps1` and `.json` for `Auth.Sdk` |
| Any test that exercises the SDK | **None.** The single backend test project does not touch an SDK type | `Auth/Auth_API.Tests/` is the only test project in the repository |
| A release tag | **None ever created** | `git tag --list` returns zero lines |
| An automated build or publish pipeline | **None.** `.github/workflows/` exists and is empty | `ls -la .github/workflows` shows only `.` and `..` |
| A `LICENSE` file | **None anywhere in the repository** | search for `LICENSE*` from the repository root |
| A `nuget.config` (feed configuration) | **None anywhere** | repository-wide search |
| A `global.json` (pinned .NET SDK version) | **None anywhere** | repository-wide search |
| A hand-written `.nuspec` | **None.** The only `.nuspec` on disk is generated build output under `obj/` | repository-wide search |
| A package readme or icon inside the project | **Neither.** `Auth/Auth.Sdk/` holds 1 project file and 18 C# files, nothing else | file listing of `Auth/Auth.Sdk/`, excluding `bin/` and `obj/` |

**What this means in one sentence.** `dotnet build` and `dotnet pack` succeeding tells you the SDK
compiles — it tells you nothing about whether the SDK works, because nothing in this repository has ever
run it against the API.

### 1.5 The two web applications do not use this package

This system ships two React web applications, and neither one consumes the SDK — they cannot, because
they run in a browser and this is a .NET library. They talk to the API directly over HTTP using a
TypeScript client generated from the API's OpenAPI document. **Publishing, versioning, or unpublishing
this package has no effect on either of them.**

| | Administration console | Accounts application |
|---|---|---|
| Folder | `Auth_UI/apps/console` | `Auth_UI/apps/accounts` |
| Workspace name | `@authsystem/console` | `@authsystem/accounts` |
| Who uses it | an administrator running the platform | an end user managing their own account |
| Development address | `https://localhost:5173` | `https://localhost:5174` |

Both are single-page applications (SPAs — the whole application is one HTML page that rewrites itself as
you navigate). Their shared HTTP client is the workspace package `@authsystem/api`, built on
`openapi-fetch`, and its types are generated from `http://localhost:5100/openapi/v1.json` by the
`pnpm gen:api` script. If you need to change how a browser application authenticates, this guide is the
wrong document — see [APPLICATION_INTEGRATION_GUIDE.md](APPLICATION_INTEGRATION_GUIDE.md).

---

## 2. Fix these first — defects a consumer would hit

**Do not publish until these are fixed.** A package on a public feed cannot be edited or deleted after
it is pushed (section 7.4 explains exactly what you can and cannot undo), so a version published with
these defects is a permanently broken version bearing your name. Each defect below was confirmed by
reading the cited source file.

**Defect 1 — the SDK sends its gateway token twice, so the server rejects every call.**
The SDK adds the `X-Gateway-Token` header once when it registers its HTTP client, and adds it a second
time on the client it hands out. HTTP allows a header to carry two values, and that is what goes over the
wire. The API compares the incoming header against the expected token as a single value, byte for byte,
so a doubled value can never match and the request is refused with HTTP 403. Because the SDK converts
every failure into "not valid", the consumer sees an invalid key rather than a rejected call.
*In code:* added at `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:50-52`, added again at
`Auth/Auth.Sdk/AuthSystemClient.cs:221`; compared at
`Auth/Auth_API/Common/Middleware/GatewayTokenValidationMiddleware.cs:62,66-70`; the check is on by
default via `Gateway:ValidationEnabled` at `Auth/Auth_API/appsettings.json:95`.
*Affected calls:* `ValidateApiKeyAsync`, `ValidateWebhookKeyAsync`, `IntrospectTokenAsync`, `LoginAsync`.

**Defect 2 — the SDK calls three endpoints that require a signed-in caller, and never sends one.**
The two key-validation endpoints require an authenticated request **and** a specific permission
(`apikeys:validate`, `webhookkeys:validate`). The token-introspection endpoint requires only that the
caller is authenticated — it carries a plain `[Authorize]` and no permission attribute. The SDK attaches
only the gateway token to all three calls. An `Authorization` header goes out only when a previous
`LoginAsync` or `SetTokensAsync` has filled the SDK's token store, and that store starts empty. Out of the
box the server answers 401 and the SDK reports the key as invalid.
*In code:* the SDK builds those requests at `Auth/Auth.Sdk/AuthSystemClient.cs:217-223`; the server side
is `Auth/Auth_API/Modules/ApiKeyManagement/Controllers/ApiKeysController.cs:24,123`,
`Auth/Auth_API/Modules/WebhookKeyManagement/Controllers/WebhookKeysController.cs:24,95`, and
`Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:671`.

**Defect 3 — the permissions those endpoints demand have no row anyone can be granted.**
`apikeys:validate` and every `webhookkeys:*` permission code are required by the controllers above, but
no database script in this repository creates them. A permission that has no row cannot be attached to a
role, so no narrowly-scoped service account can be built for the SDK. On a clean database publish the only
thing that reaches those endpoints is the global `*` permission held by the `super-admin` role — which is
far too much authority to hand to an integrating application.
*Verified:* a search for the permission-code prefix `webhookkeys:` across every `.sql` file under
`Auth/Auth_DB` returns nothing, and so does a search for `apikeys:validate`. (The word `WebhookKeys` does
appear — it is the name of a table — but no row anywhere creates a permission code for it.) Note also that
`08_AdditionalPermissions.sql` — the seed file that would supply many other missing permission codes — is
never included by the deployment script, so it does not run on a clean database publish either. Running it
by hand does not close this gap: it creates no `apikeys:validate` row and no `webhookkeys:` row, though it
does create the wildcard `apikeys:*`, which the server's wildcard rule accepts in place of
`apikeys:validate`.

**What "fixed" has to mean here.** All three defects share one root cause: nothing has ever run the SDK
against the API. Fixing the header and adding an `Authorization` header without a test leaves you in the
same position — believing it works. Before publishing, at least one test must call the SDK against a
running API and assert a successful key validation.

### 2.1 Three decisions only the owner can make

These are not defects, but each one blocks a step later in this guide. Get an answer before you start.

1. **What license does the package carry?** There is no `LICENSE` file in this repository. A public
   package must state a license, and the license you stamp on a published package is a public,
   irrevocable grant. Do not guess one.
2. **Which feed does the package go to, and should it be public at all?** No feed is configured anywhere
   in this repository. Section 7 covers the options; choosing between them is not a technical decision.
3. **What repository URL and project URL should the package advertise?** The repository's remote is
   `https://github.com/SEBAKHI/Auth`, but whether that address is public and whether it is the address the
   package should point at are both unknown from the code.

---

## 3. Before you start

Everything in this guide runs on your own machine. You do not need SQL Server, a running API, or either
web application to pack the SDK — though you do need a running API to test the fixes from section 2.

1. **Install the .NET 10 SDK.** Check what you have by running, from any directory:

   ```bash
   dotnet --version
   ```

   You should see a version starting with `10.` — for example `10.0.400`. If the command is not found,
   the .NET SDK is not installed.

2. **Know that nothing pins the SDK version for you.** There is no `global.json` in this repository, so
   whatever .NET SDK is installed on the machine is the one that builds the package. Every project in the
   solution targets `net10.0`, including this one (`Auth/Auth.Sdk/Auth.Sdk.csproj:4`).

3. **Clone the repository** if you have not already, and note the path to its root. Every directory
   instruction in this guide is written relative to that root.

4. **Expect stricter-than-normal build failures.** This repository turns nine nullable-reference
   warnings into build errors for every C# project, the SDK included. A change that is only a warning in
   another codebase fails the build here. If `dotnet pack` stops with an error code starting `CS86`, that
   is why — fix the nullability, do not suppress it.
   *In code:* `Auth/Directory.Build.props:18`.

---

## 4. Prepare the project for publishing

Do these four steps in this order. Steps 4.2 and 4.3 must come before 4.4, because the properties added
in 4.4 refer to files that do not exist yet — and packing fails hard, not softly, if they are missing.

### 4.1 Confirm the package name

The name is already chosen and already in the project file. Nothing here needs changing; this section
exists so you know what the values mean before you publish them.

| Property | Value today | What it is |
|---|---|---|
| `PackageId` | `AuthSystem.Sdk` | The name other projects install by. It must be unique on the feed you publish to |
| Namespace | `Auth.Sdk` | What a consumer writes in their `using` line — `using Auth.Sdk.Extensions;` |
| Assembly name | `Auth.Sdk` | The name of the compiled file, `Auth.Sdk.dll` |

**On a public feed, package names are first-come and global.** Prefixing the name with the organization
(`AuthSystem.Sdk` rather than `Auth.Sdk`) is what keeps it from colliding with an unrelated package.

**Prefix reservation is optional and needs authority you may not have.** On nuget.org you can reserve a
name prefix so that only your account may publish packages starting with it, which puts a verification
mark on the package page. Doing so requires an existing nuget.org account with organization
rights, and it commits the organization name publicly. Whether `AuthSystem` is the right public identity
for this project is an owner decision (section 2.1, item 3) — do not reserve a prefix on your own
initiative. The reservation form lives under Account Settings on nuget.org.

### 4.2 Create the package readme

The readme is the page a developer sees on the package's listing. There is no readme in the project
today, which is what produces the warning in section 1.3.

**Create the file at `Auth/Auth.Sdk/README.md`.** It must be inside the SDK project folder — not this
`ReadMe/` folder, which is documentation for the repository and is never packaged.

The content below describes the SDK's real surface: one registration method, three authentication scheme
names, eight options, one authorization attribute, and the limits a consumer must know about. Every claim
in it was read from the SDK source.

~~~markdown
# AuthSystem.Sdk

Adds AuthSystem authentication to an ASP.NET Core application: JWT bearer tokens, API keys, and
webhook keys, plus permission-based authorization.

Requires .NET 10 and an ASP.NET Core project — the package carries a framework reference on
`Microsoft.AspNetCore.App`.

## What it registers

One extension method does all of it:

```csharp
services.AddAuthSystemAuthentication(options => { /* see Options below */ });
```

It registers three authentication schemes:

| Scheme name | Credential it reads | Where it reads it from |
|---|---|---|
| `Bearer` (the default scheme) | a JWT access token | the `Authorization` header |
| `ApiKey` | an API key | the `X-Api-Key` request header |
| `WebhookKey` | a webhook key | the `whk` query-string parameter |

JWT bearer tokens are validated locally against the server's published signing keys. The key set is
fetched from the server's discovery document on first use and cached, so steady-state validation
makes no network call. API keys and webhook keys are different: each one is validated by calling the
server, and a successful result is cached for the duration you configure.

## Minimum wiring

```csharp
using Auth.Sdk.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthSystemAuthentication(options =>
{
    // The AuthSystem server's base address.
    options.BaseUrl      = "<your AuthSystem base URL, e.g. https://localhost:5101>";

    // Issuer must match the server's Jwt:Issuer exactly, or every token is rejected.
    options.Issuer       = "<the server's Jwt:Issuer, e.g. https://localhost:5101>";

    // Audience must match the "aud" claim of the tokens YOUR callers arrive with.
    // A token from a direct /auth/login carries the server's Jwt:Audience (a URL).
    // A token from the browser authorization-code flow carries your application's
    // Code instead (e.g. CRM-WEB). Only one value can be set here.
    options.Audience     = "<the aud your tokens carry, e.g. https://localhost:5101 or CRM-WEB>";

    // Required when the server has gateway-token validation enabled (its default).
    options.GatewayToken = "<the server's Gateway:ExpectedToken>";
});

// Required. The SDK does NOT call this for you.
builder.Services.AddAuthorization();

builder.Services.AddControllers();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

## Protecting an endpoint

```csharp
using Auth.Sdk;
using Auth.Sdk.Authorization;
using Microsoft.AspNetCore.Authorization;

[HttpGet]
[RequirePermission("crm:leads:read")]
public IActionResult List() => Ok();

[HttpPost("import")]
[Authorize(AuthenticationSchemes = AuthSystemConstants.ApiKeyScheme)]
[RequirePermission("crm:leads:write")]
public IActionResult Import() => Ok();
```

A held permission of `*` grants everything; a held permission ending in `:*` grants everything
beneath that prefix.

## Options

| Property | Type | Default | Required |
|---|---|---|---|
| `BaseUrl` | `string` | empty | Yes |
| `Issuer` | `string` | empty | Yes |
| `Audience` | `string` | empty | Yes |
| `GatewayToken` | `string` | empty | Yes, when the server validates gateway tokens |
| `ApiKeyCacheDuration` | `TimeSpan` | 60 seconds | No |
| `WebhookKeyCacheDuration` | `TimeSpan` | 5 minutes | No |
| `EnableAutoRefresh` | `bool` | `true` | No |
| `RefreshBufferSeconds` | `int` | `120` | No — `0` turns the early refresh off, leaving refresh-on-expiry and refresh-after-401 |

## Known limits

- **You must call `services.AddAuthorization()` yourself.** The SDK registers a policy provider and a
  permission handler but never calls `AddAuthorization()`. Without it, `UseAuthorization()` fails.
- **Register the SDK once, and change JWT settings through options.** Calling
  `AddAuthSystemAuthentication` a second time, or chaining `AddJwtBearer("Bearer", ...)` onto the builder
  it returns, throws `InvalidOperationException: Scheme already exists: Bearer` at startup. To change a
  JWT setting, call `services.Configure<JwtBearerOptions>("Bearer", ...)` after the SDK call.
- **There is no configuration binder and no section name.** Options are set in code through the
  callback. If you keep them in `appsettings.json`, the section name is yours to choose and yours to
  read.
- **A `WebhookKey` caller carries no permission claims**, so `[RequirePermission]` always denies on
  that scheme. Use a plain `[Authorize(AuthenticationSchemes = "WebhookKey")]` instead.
- **Webhook keys travel in the URL query string**, where they end up in server access logs and
  referrer headers. A request that is not HTTPS is logged but still accepted.
- **The token store is one process-wide slot, not one per user.** A `LoginAsync` call replaces the
  token for the whole process. This is a service-identity design, not a user-session one.
- **Failures are silent.** A validation call that fails for any reason — server unreachable, refused,
  malformed response — returns the same "not valid" answer as a genuinely bad key.
~~~

**Before you use this readme, update it for whatever you changed in section 2.** If you fixed the
gateway-token defect and added an `Authorization` header to the validation calls, the "Known limits"
list should say so.

**Links inside a package readme must be absolute.** A relative link like `../DEVELOPER_GUIDE.md` works on
a repository page and breaks on a package page, because the readme is rendered on the feed's own website.
Use a full `https://` address, which means you first need an answer to section 2.1 item 3.

### 4.3 Settle the license

**This step is blocked until the owner decides.** There is no `LICENSE` file in this repository, and a
package published without a license is a package nobody's legal review can approve. Do not paste a
license identifier into the project file to make an error go away — publishing with
`PackageLicenseExpression` set is a public, irrevocable claim about the terms of use.

When the license is decided:

1. Add the license file at the repository root, named `LICENSE`.
2. Set `PackageLicenseExpression` in the project file to the matching SPDX identifier. SPDX (Software
   Package Data Exchange) is the standard short-code list for licenses; `MIT` and `Apache-2.0` are two
   examples of such codes.

If the answer is "this package will never be public and goes to an internal feed only", say that
explicitly in the readme rather than inventing a license.

### 4.4 Add the publishing properties to the project file

These are the only changes the project file needs. Add them to the existing `<PropertyGroup>` in
`Auth/Auth.Sdk/Auth.Sdk.csproj` — do not replace the file, and do not touch the `<ItemGroup>` sections.

```xml
<!-- Add inside the existing <PropertyGroup>, after <RepositoryType>git</RepositoryType> -->

<!-- The page a developer lands on when they find the package. Requires step 4.2. -->
<PackageReadmeFile>README.md</PackageReadmeFile>

<!-- Where the source lives, and where the project's home page is.
     Replace both with the real addresses once the owner has decided (section 2.1). -->
<RepositoryUrl>https://github.com/{owner}/{repository}</RepositoryUrl>
<PackageProjectUrl>https://github.com/{owner}/{repository}</PackageProjectUrl>

<!-- Add ONLY after the license is settled (section 4.3). Leave it out until then. -->
<!-- <PackageLicenseExpression>{SPDX identifier}</PackageLicenseExpression> -->
```

```xml
<!-- Add as a new <ItemGroup>. This is what actually puts the readme file into the package;
     the property above only names it. -->
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

**Three things to be careful about.**

**Naming a file you have not created makes `dotnet pack` fail, not warn.** Setting
`PackageReadmeFile` before creating `Auth/Auth.Sdk/README.md` stops the pack with
`error NU5039: The readme file 'README.md' does not exist in the package.` That is why step 4.2 comes
first. (Measured on this project with .NET SDK 10.0.400, by packing with the property set and the file
absent.)

**Do not add a `PackageIcon`.** There is no icon file in the project. Setting `PackageIcon` without
shipping the image fails the pack with `error NU5046: The icon file 'icon.png' does not exist in the
package.`, and it fails that way even if the item that includes the file is written conditionally —
because the property is unconditional. If the owner supplies an icon later, add the property and the
`None` item together, in one change.

**Do not add package references for `Microsoft.Extensions.Http` or
`Microsoft.Extensions.Caching.Memory`.** Both already ship inside the `Microsoft.AspNetCore.App` shared
framework that the project references, so adding them produces warning `NU1510` twice and gains nothing.
The comment in the project file at `Auth/Auth.Sdk/Auth.Sdk.csproj:29-32` says this explicitly, and
`NU1510` is *not* one of the warnings this repository escalates to an error — so the mistake would pass
the build quietly.

**Leave the JwtBearer version alone.** The project pins
`Microsoft.AspNetCore.Authentication.JwtBearer` at `10.0.10`
(`Auth/Auth.Sdk/Auth.Sdk.csproj:33`). If you need the number, read it from the file rather than copying
it from documentation.

### 4.5 Properties you may see recommended, and what they actually do

| Property | Verdict for this project | Why |
|---|---|---|
| `PackageId` | Already set | The name on the feed. Required |
| `Version` | Already set | See section 5 |
| `Authors` | Already set | Shown on the package page. Required |
| `Description` | Already set | Shown on the package page. Required |
| `PackageTags` | Already set | Search keywords, separated by semicolons |
| `GenerateDocumentationFile` | Already set | Produces `Auth.Sdk.xml`, which gives consumers IntelliSense text |
| `PackageReadmeFile` | **Add** (step 4.4) | The package page's content |
| `RepositoryUrl` | **Add** (step 4.4) | Shows a link to the source repository on the package page. It does **not** enable step-into debugging — that needs a symbol package and a Source Link package, and this project has neither |
| `PackageProjectUrl` | **Add** (step 4.4) | The project's home page link |
| `PackageLicenseExpression` | **Blocked** (step 4.3) | Owner decision. Never a default |
| `PackageIcon` | **Leave out** | No icon file exists; setting it fails the pack |
| `GeneratePackageOnBuild` | **Leave out** | It would make every ordinary build produce a package. Packing on demand is correct |
| `IncludeSymbols` | Optional, not covered here | Produces a separate debug-symbol package. Only useful alongside Source Link, which this project does not have |

---

## 5. Choosing the version number

### 5.1 Where the version lives and what derives from it

The version is one line in `Auth/Auth.Sdk/Auth.Sdk.csproj`:

```xml
<Version>1.0.0</Version>
```

Changing that single value changes the package version, the assembly version, and the file version
together — you do not set those separately.

**The pre-release label is dropped from the assembly identities.** This surprises people, so it is worth
seeing measured. Building with `<Version>2.5.0-beta.1</Version>` produces package version
`2.5.0-beta.1`, but assembly version `2.5.0.0` and file version `2.5.0.0`. The `-beta.1` part reaches the
feed; it never reaches the compiled assembly's identity.

*Measured:* `dotnet msbuild Auth.Sdk.csproj -p:Version=2.5.0-beta.1 -t:GetAssemblyVersion
-getProperty:PackageVersion -getProperty:AssemblyVersion -getProperty:FileVersion`.

### 5.2 The versioning rule, going forward

**Nothing has been released yet.** The version has read `1.0.0` since the project was created, and
`git tag --list` returns zero tags. So this is not a description of an established practice — it is the
rule to follow once the first package goes out.

**Once published, the SDK follows Semantic Versioning 2.0.0 — "SemVer" for short.** The scheme is three
numbers and an optional label:

```text
MAJOR.MINOR.PATCH[-prerelease]
```

| Change type | Version bump | Examples |
|---|---|---|
| **Patch** (`1.0.x`) | Bug fixes, no API changes | Fix cache key collision, fix claim mapping bug |
| **Minor** (`1.x.0`) | New APIs added, old APIs deprecated (not removed) | Add an `AddAuthSystemAuthentication` overload, add a new authentication scheme |
| **Major** (`x.0.0`) | Breaking changes — remove deprecated APIs, rename public types, change method signatures | Remove `[Obsolete]` members, change the shape of `AuthSystemOptions` |

**Pre-release labels mark a version as not-yet-final.** A feed treats them as lower than the plain
version and hides them from consumers who have not opted in:

```text
1.0.0-alpha.1    early development, unstable
1.0.0-beta.1     feature-complete, testing
1.0.0-rc.1       release candidate, final testing
1.0.0            stable release
```

### 5.3 Removing a public method is the change that hurts

**Deleting or renaming a public method breaks every application that calls it, and they find out when
their build fails.** That is the whole reason for a deprecation cycle.

**The safe sequence is: mark it obsolete in a minor release, remove it in the next major release.**
Consumers get a compiler warning (`CS0618`) pointing at the replacement, and at least one full version in
which their code still compiles.

```text
v1.0.0  →  v1.1.0 (mark the old method obsolete)  →  v2.0.0 (remove it)
```

**Step 1 — mark it obsolete in a minor release.** The example below is an *illustration only*: there is
no `AddAuthSystem` method in this SDK. Its entire public registration surface is the single method
`AddAuthSystemAuthentication(IServiceCollection, Action<AuthSystemOptions>)`, at
`Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:23-25`.

```csharp
[Obsolete("Use AddAuthSystemAuthentication(Action<AuthSystemOptions>) instead. Will be removed in v2.0.0.")]
public static void AddAuthSystem(this IServiceCollection services, string baseUrl)
{
    // Keep it working: forward to the new method.
    AddAuthSystemAuthentication(services, opts => opts.BaseUrl = baseUrl);
}
```

**Step 2 — remove it in the next major release**, once consumers have had a full version to move.

**How much this matters right now: not at all.** There are no known consumers, in this repository or
outside it. Whether an out-of-repository application uses the SDK is an open question for the owner. Until
that is answered yes, a breaking change costs nothing — and once it is answered yes, the cycle above is
mandatory.

### 5.4 Version decision tree

```text
Is the change backward-compatible?
├── Yes → Does it add new public APIs?
│   ├── Yes → Bump MINOR (1.0.0 → 1.1.0)
│   └── No  → Bump PATCH (1.0.0 → 1.0.1)
└── No  → Have you deprecated the old API in a prior minor release?
    ├── Yes → Bump MAJOR (1.x.x → 2.0.0)
    └── No  → First deprecate in a MINOR release, then bump MAJOR in the next release
```

---

## 6. Build, inspect, and check the package before you publish

This is one ordered procedure. Do not skip ahead to section 7 until every step here has passed, because
a push to a public feed cannot be taken back.

**Step 1 — confirm the blocking work from section 2 is done.** The three defects are fixed, and at least
one test runs the SDK against a live API and asserts a successful validation. If that test does not
exist, stop here: nothing further in this guide can tell you whether the package works.

**Step 2 — confirm the preparation from section 4 is done.** `Auth/Auth.Sdk/README.md` exists, the
license question has an answer, and the properties in step 4.4 are in the project file.

**Step 3 — set the version.** Edit `<Version>` in `Auth/Auth.Sdk/Auth.Sdk.csproj` to the number section 5
says this release should carry.

**Step 4 — build the SDK in Release.** From the repository root, change into the solution folder:

```bash
cd "Auth"
```

```bash
dotnet build Auth.Sdk/Auth.Sdk.csproj -c Release
```

Success is a final line reading `Build succeeded.` with `0 Error(s)`. An error whose code starts with
`CS86` is this repository's nullable-warnings-as-errors rule (section 3, item 4).

**Build the project file, not the whole solution.** Running a bare `dotnet build -c Release` from `Auth/`
builds `Auth.sln`, which contains the SSDT database project `Auth_DB/Auth_DB.sqlproj`. The dotnet CLI
cannot build that project type, so the run ends in `Build FAILED.` with:

```text
Auth_DB.sqlproj(56,3): error MSB4278: The imported file "$(MSBuildExtensionsPath)\Microsoft\VisualStudio\v$(VisualStudioVersion)\SSDT\Microsoft.Data.Tools.Schema.SqlTasks.targets" does not exist and appears to be part of a Visual Studio component.
```

Every C# project in the solution compiles first and only the database project fails, so this is not your
change breaking — but the command still returns a non-zero exit code, which is why every build and pack
instruction in this guide names a project file. Building the database project needs `MSBuild.exe` from a
Visual Studio install with SSDT, and packing the SDK never needs it.

**Step 5 — run the backend test suite, and know what it does not prove.** From `Auth/`:

```bash
dotnet test Auth_API.Tests/Auth_API.Tests.csproj -c Release
```

Success is a final line of the shape
`Passed!  - Failed: 0, Passed: 1724, Skipped: 0, Total: 1724` — the count will move as tests are added.

There is exactly one backend test project, `Auth/Auth_API.Tests/`, and **none of its tests touch an SDK
type**. A green run tells you the API is intact; it says nothing about the package you are about to
publish. That is what step 1 is for. Note also that this suite has not been verified to pass on a machine
without a database available — if it fails at a data-access test, check that before assuming your change
broke it.

**Step 6 — pack.** From the repository root:

```bash
cd "Auth/Auth.Sdk"
```

```bash
dotnet pack -c Release
```

Success is `Successfully created package '...\bin\Release\AuthSystem.Sdk.<version>.nupkg'.` Once step 4.2
is complete, the "missing a readme" warning from section 1.3 should be gone; if it is still there, the
readme file is not being packed and you should recheck the `<None Include="README.md" ... />` item.

To build a specific version without editing the project file — useful for a pre-release build — pass the
version on the command line instead:

```bash
dotnet pack -c Release -p:Version=1.1.0-beta.1
```

**Step 7 — open the package and look inside it.** A `.nupkg` is a zip archive, so list its contents
rather than trusting that the pack did what you meant.

On Windows PowerShell, copy it to a `.zip` name first because `Expand-Archive` insists on the extension:

```powershell
Copy-Item bin\Release\AuthSystem.Sdk.1.0.0.nupkg "$env:TEMP\sdk-package.zip"
```

```powershell
Expand-Archive "$env:TEMP\sdk-package.zip" -DestinationPath "$env:TEMP\sdk-package" -Force
```

```powershell
Get-ChildItem -Recurse "$env:TEMP\sdk-package" | Select-Object -ExpandProperty FullName
```

With a Unix-style shell, one command does it:

```bash
unzip -l bin/Release/AuthSystem.Sdk.1.0.0.nupkg
```

**What must be inside.** `lib/net10.0/Auth.Sdk.dll` (the library), `lib/net10.0/Auth.Sdk.xml` (the
IntelliSense documentation), `AuthSystem.Sdk.nuspec` (the generated metadata — open it and check the
version, description, and license are what you expect), and `README.md` once step 4.2 is done. If the
readme is absent, do not push: the package page will be blank.

**Step 8 — write down what changed.** Breaking changes and deprecations need release notes somewhere a
consumer can find them. This repository has no changelog file, so decide where those notes live before
the first release rather than after it.

**Step 9 — only now, publish.** Continue to section 7.

---

## 7. Choose a feed and publish

**A feed is a server that stores packages so other projects can install them.** You need exactly one to
start. **No feed is configured in this repository** — there is no `nuget.config` anywhere — so whichever
you choose, you are setting it up from nothing.

**These are vendor procedures, and this repository cannot verify them.** The commands below follow each
vendor's published process, but nothing here exercises them and this environment cannot reach those
services. Check each vendor's current documentation before you run their commands.

**A note on how tokens are written in this section.** Commands that carry a token are shown in PowerShell,
because that is the shell on the machines this project is developed on, and PowerShell reads an
environment variable as `$env:NAME`. In a Unix-style shell the same variable is `$NAME`. The `%NAME%` form
appears only inside `nuget.config` files, where NuGet itself performs the substitution — it does **not**
work on a command line in either shell.

**Never commit a credential.** Two of the options below need an access token. Before you create any
`nuget.config` file, add it to `.gitignore` — this repository's `.gitignore` files do not currently
mention `nuget.config`, so there is no safety net in place. A token committed to source control is a
leaked credential from the moment it is pushed, and rotating it is the only fix.

### 7.1 Option A — Azure DevOps Artifacts

Choose this if the organization already runs Azure DevOps and wants the package private.

**Step 1 — create the feed.** In Azure DevOps, open Artifacts and create a feed. Give it a name (for
example `authsystem-packages`) and set its visibility to the organization or to a single project.

**Step 2 — create a Personal Access Token.** A Personal Access Token (PAT) is a long random string that
stands in for your password when a command-line tool signs in. In Azure DevOps, open your user settings,
choose Personal Access Tokens, create a new one, and give it the **Packaging → Read & Write** scope. Copy
the value immediately; it is shown only once.

**Step 3 — store the token in an environment variable**, so it never appears in a command or a file. In
PowerShell, for the current session:

```powershell
$env:AZURE_DEVOPS_PAT = "<paste the token here>"
```

**Step 4 — register the feed as a package source.** Run this as one line, substituting your organization,
project, and feed name:

```powershell
dotnet nuget add source "https://pkgs.dev.azure.com/{org}/{project}/_packaging/{feed}/nuget/v3/index.json" --name "AuthSystemFeed" --username "az" --password $env:AZURE_DEVOPS_PAT
```

Do not add `--store-password-in-clear-text`. It writes the token, unencrypted, into a configuration file
on disk.

**Step 5 — push the package.** From `Auth/Auth.Sdk`:

```powershell
dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg --source "AuthSystemFeed" --api-key az --skip-duplicate
```

The `--api-key az` value is a placeholder — Azure DevOps authenticates with the credentials attached to
the source in step 4, not with this flag, but the command still expects the flag to be present.
`--skip-duplicate` makes the command succeed instead of failing if that version is already on the feed.

**Step 6 — tell consumers how to reach the feed.** Each consuming solution needs a `nuget.config` at its
root. **Add `nuget.config` to that solution's `.gitignore` first.**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="AuthSystemFeed" value="https://pkgs.dev.azure.com/{org}/{project}/_packaging/{feed}/nuget/v3/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <AuthSystemFeed>
      <add key="Username" value="az" />
      <add key="ClearTextPassword" value="%AZURE_DEVOPS_PAT%" />
    </AuthSystemFeed>
  </packageSourceCredentials>
</configuration>
```

The `%AZURE_DEVOPS_PAT%` form reads the token from an environment variable at restore time, so the file
itself holds no secret. Every developer and build machine must set that variable.

### 7.2 Option B — GitHub Packages

Choose this if the source already lives on GitHub and the package should stay private to the
organization. This repository's remote is `https://github.com/SEBAKHI/Auth`.

**Step 1 — create a Personal Access Token** in your GitHub account settings, with the `write:packages`
and `read:packages` scopes. Copy it immediately.

**Step 2 — store it in an environment variable.** In PowerShell, for the current session:

```powershell
$env:GITHUB_TOKEN = "<paste the token here>"
```

**Step 3 — register the feed as a package source**, as one line, substituting the account or organization
that owns the repository:

```powershell
dotnet nuget add source "https://nuget.pkg.github.com/{owner}/index.json" --name "GitHubPackages" --username "{github-username}" --password $env:GITHUB_TOKEN
```

**Step 4 — make the package's repository URL match the account you are pushing to.** GitHub Packages
decides which account owns a package from the `RepositoryUrl` inside it. If step 4.4 still has the
placeholder `https://github.com/{owner}/{repository}`, the push is rejected. This is why section 2.1
item 3 has to be answered first.

**Step 5 — push.** From `Auth/Auth.Sdk`:

```powershell
dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg --source "GitHubPackages" --api-key $env:GITHUB_TOKEN --skip-duplicate
```

**Step 6 — tell consumers how to reach the feed.** Same shape as Option A. **Add `nuget.config` to that
solution's `.gitignore` first.**

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="GitHubPackages" value="https://nuget.pkg.github.com/{owner}/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <GitHubPackages>
      <add key="Username" value="{github-username}" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </GitHubPackages>
  </packageSourceCredentials>
</configuration>
```

### 7.3 Option C — a self-hosted feed server (not used by this project, extra tooling required)

**You run the feed server yourself instead of paying a vendor to run it.** That is the whole difference,
and it is not a small one: you take on the machine, the storage, the backups, the upgrades, and the
access control for a service other teams' builds now depend on.

**Nothing in this system prepares you for that.** This project deploys to Internet Information Services
(IIS) under Plesk and uses no container tooling of any kind, so a self-hosted feed would be the first
piece of infrastructure here that nobody has operating experience with. It is also the only option of the
three that this repository cannot help you evaluate — which open-source NuGet server software is worth
running today is a question no file in this repository can answer.

**Do not pick this unless Options A and B are both unavailable.** If you do, the consumer side is the same
shape as the other two options: a `nuget.config` listing the server's `index.json` address as a package
source.

### 7.4 Publishing to the public nuget.org

**Only do this if the owner has decided the package should be public and has settled the license
(section 4.3).** A public release is not reversible.

**Step 1 — create a nuget.org account** at `https://www.nuget.org`.

**Step 2 — create an API key.** In your account settings, open API Keys and create one. Set the package
owner to your account, set the glob pattern to `AuthSystem.*` so the key cannot push anything else, and
give it the Push scope. The key is displayed once — copy it now.

**Step 3 — store it in an environment variable** rather than typing it into a command that lands in your
shell history. In PowerShell, for the current session:

```powershell
$env:NUGET_API_KEY = "<paste the key here>"
```

**Step 4 — push.** From `Auth/Auth.Sdk`, as one line:

```powershell
dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg --source "https://api.nuget.org/v3/index.json" --api-key $env:NUGET_API_KEY --skip-duplicate
```

**Step 5 — what to do when you publish something wrong.** Read this before step 4, not after.

**A published version is permanent.** You cannot overwrite it and you cannot delete it. Those are the
only two facts that matter, and everything below follows from them.

- **You can unlist a version.** Unlisting hides it from search and from the package page's version list.
  It does **not** delete it: anyone who asks for that exact version still receives it, and any project
  that already has it pinned keeps building.
- **The version number is burned forever.** Once `1.0.0` is taken, it is taken. You cannot re-publish a
  corrected `1.0.0`.
- **The only way forward is a new version.** Fix the defect, bump to `1.0.1`, publish that, and unlist the
  broken one.

This is exactly why section 2 exists. The three defects listed there are the kind that only show up when a
consumer tries to use the package — by which point the broken version is public and permanent.

---

## 8. How a consumer references the SDK

**No application in this repository uses the SDK, and no package has been published yet**, so both forms
below describe what a *future* consumer would write. Neither is in use today.

**The consuming project must be an ASP.NET Core application targeting .NET 10.** The package carries a
framework reference on `Microsoft.AspNetCore.App`, so a plain console or class-library project cannot
install it without carrying that reference itself.
*In code:* `Auth/Auth.Sdk/Auth.Sdk.csproj:4,25`.

**Form 1 — a project reference**, for a consumer whose source sits alongside this repository. It always
compiles against the current source, so there is no version to manage and no feed to configure — and no
stability either, because every change to the SDK reaches the consumer immediately.

The path must be written relative to the consuming project's own `.csproj` file. If that project lives in
a solution folder that sits beside the `AuthSystem` repository folder, the path is:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\AuthSystem\Auth\Auth.Sdk\Auth.Sdk.csproj" />
</ItemGroup>
```

Count the `..\` segments against your own layout; the SDK project's location inside this repository is
`Auth/Auth.Sdk/Auth.Sdk.csproj` from the repository root.

**Form 2 — a package reference**, once a package exists on a feed the consumer can reach. The consumer
picks when to move to a new version.

```xml
<ItemGroup>
  <PackageReference Include="AuthSystem.Sdk" Version="1.0.0" />
</ItemGroup>
```

### 8.1 What the version syntax means

| Syntax | Meaning |
|---|---|
| `Version="1.0.0"` | A lower bound — this version or a later one may be used. This is the plain form and the most common |
| `Version="[1.0.0]"` | Exactly this version, and nothing else |
| `Version="[1.0.0, 2.0.0)"` | A range with an upper bound — a `1.x` version may be used, `2.0.0` and above may not |

**Do not assume a range keeps you on the newest package inside it.** Which version inside a range gets
installed is decided by NuGet's own resolution rules, not by the range itself, and nothing in this
repository exercises that behaviour — there is no consumer to test it against. If you intend to rely on a
range rather than a fixed number, confirm the current resolution rule in NuGet's own documentation first.

**The predictable option is a fixed version.** Write the exact version you tested against, and change it
deliberately when you want to move.

---

## 9. Automating the publish — templates you must create

**This project has no automation today.** The `.github/workflows/` directory exists and is completely
empty. Nothing builds, tests, packs, or publishes automatically, and no tag has ever been created
(`git tag --list` returns zero lines). Everything in this section is a file **you** create; none of it is
running now, and committing the workflow below would make it the first automated process this project has
ever had.

**Do the first publish by hand, following sections 6 and 7.** Automate only once you have seen the manual
path work end to end. A pipeline that pushes an unbuilt, untested package to a public feed burns a
version number permanently (section 7.4).

### 9.1 A GitHub Actions workflow template

This repository's remote is on GitHub, so this template targets GitHub Actions. If the organization uses a
different automation service, the equivalent pipeline has to be written from that service's own
documentation — this repository has no example to copy from.

**Two things to check before you commit this file.** First, confirm the current major version of each
action (`actions/checkout` and `actions/setup-dotnet`) against its own documentation — nothing in this
repository pins them, and the numbers below may be out of date. Second, add a repository secret named
`NUGET_API_KEY` containing the key from section 7.4, or the push step has nothing to authenticate with.

Create the file at `.github/workflows/publish-sdk.yml`:

```yaml
name: Publish AuthSystem.Sdk

on:
  push:
    tags:
      - 'sdk-v*'   # e.g. sdk-v1.0.0, sdk-v1.1.0-beta.1

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Read the version out of the tag
        id: version
        run: echo "VERSION=${GITHUB_REF_NAME#sdk-v}" >> $GITHUB_OUTPUT

      - name: Fail if the tag and the project file disagree
        run: |
          CSPROJ_VERSION=$(grep -oPm1 '(?<=<Version>)[^<]+' Auth/Auth.Sdk/Auth.Sdk.csproj)
          if [ "$CSPROJ_VERSION" != "${{ steps.version.outputs.VERSION }}" ]; then
            echo "Tag says ${{ steps.version.outputs.VERSION }} but Auth.Sdk.csproj says $CSPROJ_VERSION."
            exit 1
          fi

      # Project files, never Auth.sln: the solution contains the SSDT database
      # project, which the dotnet CLI cannot build (section 6, step 4).
      - name: Build the SDK
        run: dotnet build Auth/Auth.Sdk/Auth.Sdk.csproj -c Release

      # This suite does not cover the SDK (see section 6, step 5). It is here to catch a
      # broken backend, not to prove the package works.
      - name: Test
        run: dotnet test Auth/Auth_API.Tests/Auth_API.Tests.csproj -c Release

      - name: Pack
        run: dotnet pack Auth/Auth.Sdk/Auth.Sdk.csproj -c Release --no-build -p:Version=${{ steps.version.outputs.VERSION }}

      - name: Push to nuget.org
        run: dotnet nuget push "Auth/Auth.Sdk/bin/Release/*.nupkg" --source "https://api.nuget.org/v3/index.json" --api-key ${{ secrets.NUGET_API_KEY }} --skip-duplicate
```

**If the test step blocks you**, find out why before deleting it. This suite has not been verified to run
on a bare build machine with no database available; if that is the cause, the honest fix is to scope the
step to the tests that do not need one, not to remove the gate.

### 9.2 The release sequence, once the workflow exists

**Step 0 — the workflow file must already be committed on the default branch, and the `NUGET_API_KEY`
secret must already be set.** Until both are true, creating a tag does nothing at all.

**Step 1 — set the version** in `Auth/Auth.Sdk/Auth.Sdk.csproj`, for example `<Version>1.1.0</Version>`.

**Step 2 — commit just that file:**

```bash
git add "Auth/Auth.Sdk/Auth.Sdk.csproj"
```

```bash
git commit -m "Bump AuthSystem.Sdk to 1.1.0"
```

**Step 3 — push the commit** so the tag points at a commit that exists on the remote:

```bash
git push origin main
```

**Step 4 — create the tag.** The name must be `sdk-v` followed by exactly the version in the project file,
or the workflow's consistency check fails the job:

```bash
git tag sdk-v1.1.0
```

**Step 5 — push that one tag.** Push it by name. `--tags` would push every tag you happen to have locally,
which is not what you want:

```bash
git push origin sdk-v1.1.0
```

**What success looks like:** the Actions tab shows a run named "Publish AuthSystem.Sdk" triggered by the
tag, and the package appears on the feed a few minutes after the run goes green.

---

## 10. Quick reference

### 10.1 Commands

Every command assumes you are in `Auth/Auth.Sdk` unless stated otherwise.

| Task | Command |
|---|---|
| Pack | `dotnet pack -c Release` |
| Pack a specific version | `dotnet pack -c Release -p:Version=1.1.0` |
| Pack a pre-release | `dotnet pack -c Release -p:Version=1.1.0-beta.1` |
| List the package's contents | `unzip -l bin/Release/AuthSystem.Sdk.1.0.0.nupkg` |
| Push to nuget.org (PowerShell) | `dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg --source "https://api.nuget.org/v3/index.json" --api-key $env:NUGET_API_KEY --skip-duplicate` |
| Push to a named feed (PowerShell) | `dotnet nuget push bin/Release/AuthSystem.Sdk.1.0.0.nupkg --source "FeedName" --api-key $env:TOKEN --skip-duplicate` |
| List configured feeds | `dotnet nuget list source` |
| Add a feed | `dotnet nuget add source {url} --name {name}` |
| Remove a feed | `dotnet nuget remove source {name}` |
| Build the SDK in Release (run from `Auth/`) | `dotnet build Auth.Sdk/Auth.Sdk.csproj -c Release` |
| Run the backend tests (run from `Auth/`) | `dotnet test Auth_API.Tests/Auth_API.Tests.csproj -c Release` |

### 10.2 Related documents

- [APPLICATION_INTEGRATION_GUIDE.md](APPLICATION_INTEGRATION_GUIDE.md) — how an application integrates
  with this Auth system, including how a human being actually signs in.
- [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) — how to get the API and both web applications running
  locally.
- [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md) — how the API, the gateway, and both
  web applications are deployed.

Remember that these are relative links inside this repository. The readme *inside the package* is rendered
on a feed's website and needs full `https://` addresses instead (section 4.2).
