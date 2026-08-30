# Application Integration Guide

## What this guide covers

You have your own application, and you want AuthSystem to be the thing that decides who its users are and
what they are allowed to do. This guide shows you how to wire the two together.

A few words are used constantly below, so here is what each one means the first time you meet it.
**API** is an application programming interface — the set of HTTP addresses your code calls.
**SDK** is a software development kit — in this repository, a .NET library your application references so
it does not have to hand-write the HTTP calls.
**JWT** is a JSON Web Token — a signed string a caller presents to prove who they are.
**JWKS** is a JSON Web Key Set — the public keys AuthSystem publishes so anyone can check a JWT's
signature without asking AuthSystem about every single request.
**OAuth 2.0** and **OpenID Connect (OIDC)** are the two standards AuthSystem follows when a human signs in
through a browser.
**PKCE** is Proof Key for Code Exchange — an OAuth extension that makes the browser sign-in safe for
applications that cannot keep a secret.
**IdP** is identity provider, which is what AuthSystem is playing the role of here.
**SPA** is a single-page application — a website that runs as JavaScript in the browser.

There are two audiences for this document, and both are served. If your application is written in .NET,
you can use the shipped SDK. If it is written in anything else, skip to
[Integrating without .NET](#integrating-without-net) — everything AuthSystem needs from you is plain HTTP
and standard OAuth, and the SDK is a convenience, not a requirement.

**Scope of the code samples.** Every SDK sample here is written against version `1.0.0` of the SDK,
targeting .NET 10. Your consuming project must also target `net10.0` and must be an ASP.NET Core web
application, because the SDK depends on the ASP.NET Core shared framework.
*In code:* `Auth/Auth.Sdk/Auth.Sdk.csproj:4,9,10,25`

**Honesty note about this SDK.** No project in this repository references the SDK, nothing packages it,
and there is no continuous-integration pipeline that builds or tests it. It is unexercised code. Two of
its three authentication schemes do not work against a default-configured server. Read
[Known limitations](#known-limitations--read-this-before-you-write-code) before you plan an architecture
around it.
*In code:* `Auth/Auth.sln:40` is the only reference to it anywhere in the repository.

## The four ways a caller can reach your application

| Way in | Scheme name | Where the credential travels | Who uses it | Status today |
|---|---|---|---|---|
| A person signing in through a browser | — (produces a JWT) | the browser, via OAuth authorization-code + PKCE | your human users | **Works** |
| JWT Bearer | `Bearer` | `Authorization: Bearer <token>` header | any caller holding a token AuthSystem issued | **Works** |
| API key | `ApiKey` | `X-Api-Key: <key>` header | another server calling yours | **Broken as shipped** — see limitations 1 and 2 |
| Webhook key | `WebhookKey` | `?whk=<key>` in the URL query string | a system posting webhook callbacks to you | **Broken as shipped** — see limitations 1, 2 and 4 |

The first two rows are the same scheme seen from two ends. The browser flow is how a token comes into
existence; the `Bearer` scheme is how your application checks one.
*In code:* `Auth/Auth.Sdk/AuthSystemConstants.cs:11,16,21`

---

## Known limitations — read this before you write code

These are shipped defects, not warnings about misuse. Each one is stated plainly, with the file and line
that proves it, and with what you should do about it today.

**1. The SDK sends its gateway token twice, and every call it makes is therefore rejected with HTTP 403.**
AuthSystem's API can be configured to accept requests only from its own gateway, by requiring a shared
secret in an `X-Gateway-Token` header. The SDK adds that header twice: once when it registers its named
HTTP client, and again every time it hands that client to a caller. HTTP joins repeated header values with
a comma, so the API receives `secret, secret` where it expects `secret`. The API compares the header's
whole string value against the expected token, first by byte length and then in constant time, so a
two-value header can never match, and the request comes back as HTTP 403 with a
`application/problem+json` body.
*In code:* the first addition is in the named-client registration at
`Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:50-52`; the second is inside
`AuthSystemClient.CreateClient()` at `Auth/Auth.Sdk/AuthSystemClient.cs:221`; the comparison is
`Auth/Auth_API/Common/Middleware/GatewayTokenValidationMiddleware.cs:62,66-70` and the 403 is written at
`:132-133`.
**Which calls this breaks:** all four network methods on `AuthSystemClient` — `ValidateApiKeyAsync`,
`ValidateWebhookKeyAsync`, `IntrospectTokenAsync` and `LoginAsync`. Automatic token refresh is *not*
affected, because it builds its own HTTP client and adds the header once
(`Auth/Auth.Sdk/TokenManagement/TokenRefreshHandler.cs:111-112`). Validating a JWT is also not affected,
because that never goes through this client and the `/.well-known/` paths are exempt from gateway-token
checking anyway (`Auth/Auth_API/appsettings.json:96-102`).
**What to do today:** treat the JWT Bearer scheme as the only working scheme. Gateway validation is on by
default (`Auth/Auth_API/appsettings.json:95`) and off in the Development environment
(`Auth/Auth_API/appsettings.Development.json:28`), so these calls will appear to work against a local dev
server and fail against a real one. If you need them for real, take the SDK as source, delete one of the
two header additions, and build it yourself.

**2. The API-key and webhook-key validation endpoints need permissions that no database seed creates.**
Both `POST /api/v1/apikeys/validate` and `POST /api/v1/webhookkeys/validate` require a signed-in caller
holding a specific permission code — `apikeys:validate` and `webhookkeys:validate` respectively. Those
permission codes do not exist. Searching every `.sql` file in the database project finds no row creating
`apikeys:validate`, and no row creating any of the five `webhookkeys:` codes — `webhookkeys:create`,
`webhookkeys:read`, `webhookkeys:revoke`, `webhookkeys:rotate`, `webhookkeys:validate`. They cannot be
granted to a role, because they are not there to grant. On a freshly published database the only grant
that reaches them is the single global `*` permission, which is held by the seeded `super-admin` role.
*In code:* the gates are `Auth/Auth_API/Modules/ApiKeyManagement/Controllers/ApiKeysController.cs:24,123`
and `Auth/Auth_API/Modules/WebhookKeyManagement/Controllers/WebhookKeysController.cs:24,95`. The global
`*` permission is created at `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql:87-90`. The four
remaining API-key codes (`apikeys:read`, `apikeys:create`, `apikeys:revoke`, `apikeys:rotate`) exist only
in `Auth/Auth_DB/dbo/Scripts/SeedData/08_AdditionalPermissions.sql`, and that file is never included by
the post-deployment script, so publishing the database does not run it.
**What to do today:** do your key management signed in as a member of the `super-admin` role, through the
admin console. On a stock install, do not design a narrowly-scoped service account around `apikeys:*` or
`webhookkeys:*` — neither code has a row there to grant — and do not build a feature on the `X-Api-Key` or
`?whk=` schemes until the seed gap is closed.

**If an operator runs `08_AdditionalPermissions.sql` by hand, the two key types end up in different
places.** That file still creates no `apikeys:validate` row and no `webhookkeys:` row of any kind. For API
keys that is survivable, because the same file also creates the wildcard code `apikeys:*`, and the API
grants a wildcard to every code under its prefix — so a role holding `apikeys:*` does satisfy
`apikeys:validate`. For webhook keys nothing helps: there is no `webhookkeys:` row to grant, wildcard or
otherwise, so the global `*` stays the only permission that reaches those five endpoints.
*In code:* the wildcard rule is `Auth/Auth_API/Authorization/PermissionRequirementHandler.cs:138-163`; the
`apikeys:*` row is `Auth/Auth_DB/dbo/Scripts/SeedData/08_AdditionalPermissions.sql:249-254`.

**3. The SDK sends no `Authorization` header on the validate calls unless its token store was filled
first.**
Even with the header problem in limitation 1 fixed and the permissions in limitation 2 created, the SDK
would still fail out of the box: the client attaches only the gateway token. A bearer token is added only
when the SDK's token store already holds one, and the store starts empty — it is filled solely by a
successful `LoginAsync` or by `SetTokensAsync`. With an empty store the API answers 401. The
SDK turns any non-success response into `null`, and the authentication handler turns `null` into the
message "Invalid API key." — so an unauthenticated SDK is indistinguishable from a genuinely bad key.
*In code:* `Auth/Auth.Sdk/AuthSystemClient.cs:60-64,217-223`;
`Auth/Auth.Sdk/Handlers/ApiKeyAuthenticationHandler.cs:41-44`.
**What to do today:** if you must make these calls, call `AuthSystemClient.LoginAsync` or
`SetTokensAsync` first so the SDK's token store is populated — the refresh handler will then attach an
`Authorization` header to outgoing calls on the named client. Read limitation 5 before you do.

**4. A webhook-key caller can never satisfy `[RequirePermission]`.**
The webhook-key handler creates a caller identity carrying an identifier, an application id, a name, a
target URL and an environment — and no permission claim of any kind. The SDK's permission check looks only
at the `permissions`, `permission` and `scope` claims, so it always denies.
*In code:* `Auth/Auth.Sdk/Handlers/WebhookKeyAuthenticationHandler.cs:51-59`;
`Auth/Auth.Sdk/Authorization/PermissionRequirementHandler.cs:16`.
**What to do today:** protect webhook endpoints with a bare
`[Authorize(AuthenticationSchemes = AuthSystemConstants.WebhookKeyScheme)]` and enforce anything finer in
your own code.

**5. The SDK's token store is shared by the whole process, not per user.**
`ITokenStore` is registered as a singleton holding exactly one set of tokens, and the lock that guards
refreshing is `static`. In a multi-user web application, one user calling `LoginAsync` overwrites the
tokens for everybody, and every outbound call the SDK makes then carries that one user's token. This is a
design for a service identity, not for end users.
*In code:* `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:43`;
`Auth/Auth.Sdk/TokenManagement/InMemoryTokenStore.cs`; `Auth/Auth.Sdk/TokenManagement/TokenRefreshHandler.cs:16`.
**What to do today:** never sign your end users in with `LoginAsync`. Send them through the browser flow
in [Step 2](#step-2-how-a-person-actually-signs-in). Use `LoginAsync` only for a single machine identity,
if at all.

**6. Two-factor sign-in cannot be completed through the SDK.**
When a user has two-factor authentication switched on, the server answers the login call with HTTP 200, no
token, `requiresTwoFactor: true`, and a challenge token to present next. The SDK sees a missing token and
returns `false` with no explanation, and its internal model does not even carry the challenge field, so
there is nothing to continue with.
*In code:* `Auth/Auth.Sdk/AuthSystemClient.cs:179-182`; `Auth/Auth.Sdk/Models/LoginResult.cs`;
the real shape is `Auth/Auth.Application/DTOs/LoginResponse.cs:15,30,36`.
**What to do today:** same answer as limitation 5 — send people through the browser flow, which handles
two-factor properly in the accounts application.

**7. When a token refresh fails, the SDK sends the request with no `Authorization` header at all.**
The refresh code comments that it will "send with current (possibly expired) token", but it returns before
the line that attaches the header. The request goes out completely unauthenticated, and you get a 401 that
looks like an expired session rather than a failed refresh.
*In code:* `Auth/Auth.Sdk/TokenManagement/TokenRefreshHandler.cs:52-56` returns before the header is
attached at `:60`.
**What to do today:** watch your logs for the SDK's "Token refresh failed with status" warning; that
warning, not the 401, is the real cause.

---

## Step 1: Register your application in AuthSystem

Nothing else in this guide works until AuthSystem knows your application exists. Registering it creates a
row that carries the identity your application will be known by, the list of addresses AuthSystem is
allowed to send users back to, and the list of people allowed in.

**Where you do this.** AuthSystem ships two web applications, and this one happens in the admin console.
The console is a React single-page application for administrators; the other application, called accounts,
is the self-service site your end users see. In local development the console runs at
`https://localhost:5173` and accounts runs at `https://localhost:5174`; both are HTTPS-only on purpose, so
the browser keeps the identity-provider session cookie.
*In code:* `Auth_UI/apps/console/vite.config.ts:15`, `Auth_UI/apps/accounts/vite.config.ts:14`.

**The steps, in order.**

1. Open the admin console in a browser and sign in as a user who holds the `applications:create`
   permission, or who is a member of the `super-admin` role. Go to the **Applications** page at
   `/applications`. If you cannot see that page in the sidebar, your account does not hold
   `applications:read` and you must ask an administrator.
   *In code:* `Auth_UI/apps/console/src/routes.tsx:175-186`.
2. Create the application. Give it a **Code**, a short machine-readable identifier such as `crm-web`. This
   Code is the single most important value in this whole guide: it is your OAuth `client_id`, and it is
   also the audience stamped into every access token issued for your application.
3. **Note that the Code is stored upper-cased.** If you type `crm-web`, the stored Code is `CRM-WEB`, and
   that upper-case form is what appears in tokens. Use the upper-case form when you configure the SDK's
   `Audience` setting.
   *In code:* `Auth/Auth.Domain/Entities/Application.cs:188`.
4. Register every address AuthSystem may send a signed-in user back to — your redirect URIs. The match is
   exact and case-sensitive over the whole string, so `https://app.example.com/callback` and
   `https://app.example.com/callback/` are two different values, and a URI you did not register is
   rejected outright.
   *In code:* `Auth/Auth.Domain/Entities/Application.cs:276-279`.
5. Grant your users access. A new application's access mode is **Restricted**, which means only users with
   an explicit access row may sign in. Until you grant access, every sign-in attempt ends with
   `error=access_denied` — including yours.
   *In code:* the database default is `Auth/Auth_DB/dbo/Tables/Core/Applications.sql:24` (`DEFAULT 2`), the
   entity default is `Auth/Auth.Domain/Entities/Application.cs:193`, and the check that refuses entry is
   `Auth/Auth.Application/Features/Authentication/Authorize/AuthorizeCommandHandler.cs:165`.

**What success looks like.** The application appears in the console's Applications list, its detail page
shows your redirect URIs, and the users you granted appear under its access list. If you prefer HTTP to
the console, the same thing is `POST /api/v1/applications` with a body carrying `code`, `name` and
`redirectUris`, and it requires the `applications:create` permission.
*In code:* `Auth/Auth_API/Modules/ApplicationManagement/Controllers/ApplicationsController.cs:207-208`;
request shape at `Auth/Auth_API/Modules/ApplicationManagement/Contracts/CreateApplicationRequest.cs`.

**Creating roles and permissions** — the things that end up inside a token — is covered in the sibling
document [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md).

---

## Step 2: How a person actually signs in

**Your application never sees a user's password.** It does not show a login form, it does not collect
credentials, and it does not call the login endpoint on a user's behalf. Sign-in happens on AuthSystem's
own accounts application, and your application receives a short-lived code that it trades for tokens. This
is the standard OAuth 2.0 authorization-code flow with PKCE, and it is the flow AuthSystem advertises in
its discovery document.

**The whole journey, step by step.**

1. Your application generates a random string called the **code verifier**, and a **code challenge** that
   is the URL-safe base64 encoding of the SHA-256 hash of that verifier. It keeps the verifier and sends
   only the challenge. The challenge must be 43 to 128 characters from the set `A-Z a-z 0-9 - . _ ~`.
   *In code:* the pattern is
   `Auth/Auth.Application/Features/Authentication/Authorize/AuthorizeCommandHandler.cs:34-35`.
2. Your application sends the user's browser to AuthSystem's authorize address:

   ```text
   GET https://auth.example.com/api/v1/auth/authorize
       ?response_type=code
       &client_id=CRM-WEB
       &redirect_uri=https://app.example.com/callback
       &code_challenge=<the challenge from step 1>
       &code_challenge_method=S256
       &state=<a random value you will check on the way back>
   ```

   `response_type` must be exactly `code` and `code_challenge_method` must be exactly `S256`; nothing else
   is accepted. `state` is optional but you should send one — it is echoed back untouched and is how you
   detect a forged callback. It is capped at 512 characters.
   *In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:219-231`;
   validation at `AuthorizeCommandHandler.cs:97-116`.
3. **If the user has no valid AuthSystem session,** the browser is redirected to the accounts
   application's login page, carrying your original authorize URL as a `returnTo` parameter. The accounts
   application shows the sign-in form, and it will only ever honour a `returnTo` that points back at the
   authorize endpoint on the auth origin — anything else is treated as an open-redirect attempt and
   ignored.
   *In code:* the redirect is built at `AuthorizeCommandHandler.cs:262-266` from the configured
   `IdentityProvider:AccountsBaseUrl`; the validation is `Auth_UI/packages/auth/src/return-to.ts:26-43`.
4. **The user signs in on the accounts application** — password, two-factor if enabled, external provider
   if configured. None of this touches your application. When it succeeds, the accounts application sends
   the browser back to the authorize URL it was holding.
   *In code:* `Auth_UI/packages/auth/src/pages/login.tsx:99-102`.
5. **AuthSystem checks that this user is allowed into your application.** If they are not, the browser is
   redirected to your registered `redirect_uri` with `error=access_denied` and your `state`. No reason is
   given to the client; the detail is in the server log.
   *In code:* `AuthorizeCommandHandler.cs:165-173`.
6. **If they are allowed,** the browser arrives at your `redirect_uri` with a one-time code and your
   `state`:

   ```text
   https://app.example.com/callback?code=<one-time code>&state=<your state>
   ```

   That code is valid for 60 seconds by default and can be used exactly once. A second attempt to use it is
   logged as a replay.
   *In code:* the lifetime default is
   `Auth/Auth.Application/Configuration/IdentityProviderSettings.cs:43`; replay detection is
   `Auth/Auth.Application/Features/Authentication/TokenExchange/ExchangeAuthorizationCodeCommandHandler.cs:73-85`.
7. **Your application exchanges the code for tokens** with a form-encoded POST from your server. There is
   no client secret — AuthSystem treats these as public clients and relies on PKCE instead.

   ```http
   POST /api/v1/auth/token HTTP/1.1
   Host: auth.example.com
   Content-Type: application/x-www-form-urlencoded

   grant_type=authorization_code&code=<the code>&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcallback&client_id=CRM-WEB&code_verifier=<the verifier from step 1>
   ```

   The `redirect_uri` must be byte-for-byte the one the code was issued for, and the `client_id` must be
   the application the code was issued to.
   *In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:267-274`; field names at
   `Auth/Auth_API/Modules/Authentication/Contracts/OAuthTokenRequest.cs:11-27`; the checks are
   `ExchangeAuthorizationCodeCommandHandler.cs:101-112`.
8. **What comes back** is a standard OAuth token response with snake_case field names:

   ```json
   {
     "access_token": "eyJhbGciOiJSUzI1NiIs...",
     "token_type": "Bearer",
     "expires_in": 900,
     "refresh_token": "base64-encoded-random-value",
     "refresh_expires_in": 604800
   }
   ```

   `expires_in` and `refresh_expires_in` are seconds. With the shipped defaults that is 15 minutes for the
   access token and 7 days for the refresh token.
   *In code:* the response shape is
   `Auth/Auth.Application/Features/Authentication/TokenExchange/ExchangeAuthorizationCodeCommand.cs:36-52`;
   the lifetimes are `Auth/Auth.Application/Configuration/JwtSettings.cs:23,28`.
9. **When the access token nears expiry**, POST to the same address with
   `grant_type=refresh_token&refresh_token=<the refresh token>`. Refresh tokens rotate by default, so the
   response carries a new refresh token and the old one stops working — always store the newest one.
   *In code:* `AuthController.cs:296-314`; rotation default at `JwtSettings.cs:56`.

**The one thing to remember about the token you get back.** Its audience (`aud`) is your application's
Code, not AuthSystem's platform audience. That matters when you configure the SDK — see
[the audience rule](#the-audience-rule-read-this-twice).
*In code:* `ExchangeAuthorizationCodeCommandHandler.cs:144`;
`Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs:130`.

---

## Step 3: Add the SDK to your project

Before the XML below will work, two things must be true of your project, and neither is optional. Your
project must target `net10.0`, and it must be an ASP.NET Core web application — created from the
`Microsoft.NET.Sdk.Web` project SDK, or carrying an explicit
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` of its own. The AuthSystem SDK carries that
framework reference, and a plain console or class-library project cannot consume it.
*In code:* `Auth/Auth.Sdk/Auth.Sdk.csproj:4,25`.

The SDK ships as source only. Nothing in this repository packages it, publishes it, or references it, so
the verified way to consume it is a project reference. Open your application's `.csproj` in a text editor
and add the item group below, adjusting the relative path so it points at
`Auth/Auth.Sdk/Auth.Sdk.csproj` from wherever your project file lives.

```xml
<ItemGroup>
  <!-- Path shown from a project at <repo-root>/MyApp/MyApp.csproj, with the
       AuthSystem repository checked out beside it as ../AuthSystem/ -->
  <ProjectReference Include="..\AuthSystem\Auth\Auth.Sdk\Auth.Sdk.csproj" />
</ItemGroup>
```

If you would rather have a package, you must build one yourself. From the `Auth/Auth.Sdk/` directory, run:

```bash
dotnet pack -c Release
```

**What success looks like:** the command prints a line ending in
`AuthSystem.Sdk.1.0.0.nupkg`. Note the package id is `AuthSystem.Sdk`, not `Auth.Sdk`. The produced
package carries no licence, no README and no repository URL, because the project file sets none of them.
*In code:* `Auth/Auth.Sdk/Auth.Sdk.csproj:9-10`; the absent properties are absent from all 36 lines of
that file.

Then reference it the usual way:

```xml
<ItemGroup>
  <PackageReference Include="AuthSystem.Sdk" Version="1.0.0" />
</ItemGroup>
```

---

## Step 4: Configure the SDK

The SDK has one options class with eight properties. It has **no** configuration binder and **no** section
name of its own — unlike the server's own settings classes, which each declare one. That means the
`"AuthSystem"` wrapper in the JSON below is a name you are choosing, and you must read the values out of
it yourself in Step 5.
*In code:* `Auth/Auth.Sdk/AuthSystemOptions.cs`; contrast `JwtSettings.SectionName = "Jwt"` at
`Auth/Auth.Application/Configuration/JwtSettings.cs:8`.

```json
{
  "AuthSystem": {
    "BaseUrl": "https://auth.example.com",
    "Issuer": "https://auth.example.com",
    "Audience": "CRM-WEB",
    "GatewayToken": "REPLACE-ME-ask-your-AuthSystem-operator",
    "ApiKeyCacheDuration": "00:01:00",
    "WebhookKeyCacheDuration": "00:05:00",
    "EnableAutoRefresh": true,
    "RefreshBufferSeconds": 120
  }
}
```

Every value above marked `REPLACE-ME` or shown as `example.com` is a placeholder. Against a local
development server the first three become `https://localhost:5101` for `BaseUrl` and `Issuer`, and your
application's upper-case Code for `Audience`.
*In code:* the development issuer and audience are `Auth/Auth_API/appsettings.Development.json:22-23`.

| Property | Type | Default in code | Required? | What it does |
|---|---|---|---|---|
| `BaseUrl` | string | empty | **Yes** | The origin of the AuthSystem API. Used to build every URL the SDK calls and to fetch the discovery document. Empty throws a `UriFormatException` the first time anything uses it. |
| `Issuer` | string | empty | **Yes** | The exact string the SDK will require in a token's `iss` claim. Must equal the server's `Jwt:Issuer`. |
| `Audience` | string | empty | **Yes** | The exact string the SDK will require in a token's `aud` claim. Read the audience rule below before you set this. |
| `GatewayToken` | string | empty | Only when the server requires it | The shared secret sent in the `X-Gateway-Token` header. See the gateway-token paragraph below. |
| `ApiKeyCacheDuration` | TimeSpan | 60 seconds | No | How long a **valid** API key's metadata is kept in memory. |
| `WebhookKeyCacheDuration` | TimeSpan | 5 minutes | No | How long a **valid** webhook key's metadata is kept in memory. |
| `EnableAutoRefresh` | bool | `true` | No | Whether outgoing calls on the SDK's named HTTP client refresh a stored access token automatically. |
| `RefreshBufferSeconds` | int | `120` | No | How many seconds before expiry to refresh proactively. `0` switches the early refresh off: the SDK then refreshes only once the token has actually expired, or after a 401 response. |

**Neither cache duration caches a failure.** Only a result whose `active` field is `true` is stored. A
stream of requests carrying an invalid key therefore hits the auth server once per request, with no
throttle in front of it.
*In code:* `Auth/Auth.Sdk/AuthSystemClient.cs:68-71,111-114`.

### The audience rule, read this twice

**A token's audience depends on how the user signed in, and the SDK can only accept one audience.**

- A token from the browser flow in [Step 2](#step-2-how-a-person-actually-signs-in) carries your
  application's Code as its audience — `CRM-WEB` in the running example.
- A token from a direct `POST /api/v1/auth/login` carries the server's own configured `Jwt:Audience`.

Set `Audience` to whichever one your users will actually arrive with. If they can arrive both ways, the
SDK on its own cannot validate both, because it sets exactly one `ValidAudience`. You would have to add
the second one yourself, with the `Configure<JwtBearerOptions>` call shown in
[Step 5](#step-5-register-authentication-in-your-applications-startup), setting
`jwt.TokenValidationParameters.ValidAudiences` to the full list. Do not try to do it by chaining another
`AddJwtBearer("Bearer", …)` onto the builder the SDK returns — that throws at startup, for the reason
Step 5 explains.
*In code:* the per-application audience is set at
`Auth/Auth.Application/Features/Authentication/TokenExchange/ExchangeAuthorizationCodeCommandHandler.cs:144`
and applied at `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs:130`; the SDK's single
`ValidAudience` is `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:74-75`.

### Where the gateway token comes from

**Ask the person who operates AuthSystem for the current value, and paste it in.** The token is a shared
secret held in AuthSystem's encrypted secrets file. There is no endpoint that reads it back to you — the
secrets status endpoint deliberately returns no secret values.
*In code:* `Auth/Auth_API/Modules/Administration/Controllers/SecretsController.cs:49-52`.

**Do not call `POST /api/v1/admin/secrets/generate/gateway-token` to "get" it.** That endpoint *rotates*
the secret. Its own documentation says the gateway must then be reconfigured with the new value, and until
it is, every proxied request is rejected. It also requires the `secrets.manage` permission, an explicitly
enabled admin API, and a two-call challenge-and-verify handshake before it will run at all.
*In code:* `SecretsController.cs:216-218,226-227`.

**Against a development server you can leave `GatewayToken` empty.** The Development configuration turns
gateway-token validation off, with a comment saying the API is meant to be reachable without running the
gateway.
*In code:* `Auth/Auth_API/appsettings.Development.json:27-28`. Where the token lives on a real server is
covered in [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md).

### Should `BaseUrl` point at the API or at the gateway?

**This repository does not answer that, and this guide will not guess.** Both hosts are reachable in
principle: the gateway's route list forwards `/api/v{version}/auth/`, `/api/v{version}/apikeys/`,
`/api/v{version}/webhookkeys/` and `/.well-known/`. But the gateway adds its own `X-Gateway-Token` to
every request it proxies without removing an inbound one, and every URL committed to this repository is a
placeholder, so there is no verified answer here. Ask your operator which origin external applications are
expected to use.
*In code:* the gateway routes are `Auth/API_Gateway/appsettings.json:31-36,58-64,107-113,177-182`; the
header is added at `Auth/API_Gateway/Program.cs:114-121`.

---

## Step 5: Register authentication in your application's startup

The SDK gives you exactly one registration method, `AddAuthSystemAuthentication`, and it takes a
configuration action — there is no overload that takes an `IConfiguration`. Everything below goes in your
application's `Program.cs`, which sits at the root of your project.

1. Add the `using` line for the SDK's extensions namespace.
2. Call `AddAuthSystemAuthentication` and copy your settings into the options object by hand.
3. Call `AddAuthorization()` yourself. **The SDK does not call it.** Its single registration file contains
   no `AddAuthorization` call, and without it your application throws at `UseAuthorization()`.
   *In code:* `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs` — the whole file, 98 lines.
4. Call `AddControllers()` yourself if you use controllers. The SDK does not register the MVC services
   either, and `MapControllers()` throws without them.
5. Add `UseAuthentication()` before `UseAuthorization()` in the middleware order.

```csharp
using Auth.Sdk.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Step 2 — register the three schemes: Bearer, ApiKey, WebhookKey.
builder.Services.AddAuthSystemAuthentication(options =>
{
    var section = builder.Configuration.GetSection("AuthSystem");
    options.BaseUrl      = section["BaseUrl"]!;
    options.Issuer       = section["Issuer"]!;
    options.Audience     = section["Audience"]!;
    options.GatewayToken = section["GatewayToken"] ?? string.Empty;

    // Optional — these are already the defaults.
    options.ApiKeyCacheDuration     = TimeSpan.FromSeconds(60);
    options.WebhookKeyCacheDuration = TimeSpan.FromMinutes(5);
});

// Step 3 — the SDK never calls this. Without it, UseAuthorization() throws.
builder.Services.AddAuthorization();

// Step 4 — the SDK never calls this either. Without it, MapControllers() throws.
builder.Services.AddControllers();

var app = builder.Build();

// Step 5 — authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
```

**What success looks like.** Run your application from its project directory with `dotnet run`. It starts
and prints its listening address without throwing. Then confirm your application's host can actually reach
AuthSystem, because the SDK fetches AuthSystem's metadata lazily on the first token it validates and a
network problem there looks like a bad token. From any terminal on the same machine, run:

```bash
curl -s https://auth.example.com/.well-known/openid-configuration
```

You should get a JSON document whose `issuer` field is exactly the value you put in `Issuer`. If it is
not, token validation will fail with an issuer mismatch and no amount of correct tokens will help.

### What `AddAuthSystemAuthentication` actually registers

| What it registers | Lifetime | Why you care |
|---|---|---|
| `AuthSystemClient` | Singleton | Inject it to call AuthSystem from your own code. |
| `IAuthorizationPolicyProvider` → `PermissionPolicyProvider` | Singleton | Turns `[RequirePermission("x")]` into a policy named `Permission:x`. |
| `IAuthorizationHandler` → `PermissionRequirementHandler` | Scoped | Decides whether the caller holds the permission. |
| `AddMemoryCache()` | — | Backs the key-validation cache. |
| `ITokenStore` → `InMemoryTokenStore` | Singleton | Process-wide token storage — see limitation 5. |
| `TokenRefreshHandler` | Transient | The automatic-refresh message handler. |
| Named `HttpClient` `"AuthSystem"` with the refresh handler attached | — | Every SDK call to the auth server goes through it. |
| `AddAuthentication` with `Bearer` as the default authenticate and challenge scheme | — | A bare `[Authorize]` means JWT Bearer. |
| `AddJwtBearer("Bearer", …)` | — | The JWT scheme, configured as in the next table. |
| `AddScheme<…, ApiKeyAuthenticationHandler>("ApiKey", …)` | — | The `X-Api-Key` scheme. |
| `AddScheme<…, WebhookKeyAuthenticationHandler>("WebhookKey", …)` | — | The `?whk=` scheme. |

*In code:* `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:33-94`.

**These are unconditional registrations, not "add if missing".** The SDK uses `AddSingleton` and
`AddScoped`, never `TryAdd`. If you want your own `ITokenStore`, register it **after** this call or it
will be overwritten.
*In code:* `ServiceCollectionExtensions.cs:33,36,37,43,44`.

**Call `AddAuthSystemAuthentication` exactly once.** A second call does not quietly duplicate the
registrations — it registers the `Bearer` scheme again, and the application throws
`System.InvalidOperationException: Scheme already exists: Bearer` while it starts.

**The scheme names are available as constants**, so you never have to type the strings:
`AuthSystemConstants.BearerScheme` (`"Bearer"`), `ApiKeyScheme` (`"ApiKey"`), `WebhookKeyScheme`
(`"WebhookKey"`), `ApiKeyHeaderName` (`"X-Api-Key"`), `WebhookKeyQueryParam` (`"whk"`),
`GatewayTokenHeaderName` (`"X-Gateway-Token"`) and `HttpClientName` (`"AuthSystem"`). They live in the
`Auth.Sdk` namespace.
*In code:* `Auth/Auth.Sdk/AuthSystemConstants.cs:11-41`.

### How the SDK checks a JWT

The SDK does not ask AuthSystem about every request. It fetches AuthSystem's OpenID Connect discovery
document once, follows the `jwks_uri` in that document to the public signing keys, and then verifies
signatures in your own process. After the first fetch there is no call to the auth server per request.

**One thing to watch behind a reverse proxy.** The `jwks_uri` is built from the server's configured
`IdentityProvider:PublicBaseUrl`, so it can point at a different origin than your `BaseUrl`. Your
application host must be able to reach whatever that document advertises.
*In code:* `Auth/Auth.Sdk/Extensions/ServiceCollectionExtensions.cs:66-67`;
`Auth/Auth.Application/Features/Discovery/GetDiscoveryDocument/GetDiscoveryDocumentQueryHandler.cs:34`;
`Auth/Auth.Application/Configuration/IdentityProviderSettings.cs:36-37`.

| Validation setting | Value the SDK uses |
|---|---|
| Metadata address | `{BaseUrl}/.well-known/openid-configuration` |
| Require HTTPS metadata | `true`, unless `BaseUrl` contains the text `localhost` |
| Validate issuer / audience / lifetime / signing key | all `true` |
| Permitted signing algorithms | `RS256` only |
| Clock skew allowance | 30 seconds |
| Role claim type | `roles` |
| Name claim type | `sub` |

*In code:* `ServiceCollectionExtensions.cs:64-86`. The server signs with RS256 and publishes a key whose
identifier defaults to `auth-key-1`.
*In code:* `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs:47`;
`Auth/Auth.Application/Configuration/JwtSettings.cs:51`.

**The HTTPS check is a substring test on the whole `BaseUrl`, not a hostname comparison.** A production
host named `https://auth.localhost.example.com` silently disables HTTPS metadata enforcement. Do not name
a real host anything containing `localhost`.
*In code:* `ServiceCollectionExtensions.cs:68`.

**The SDK does not turn off inbound claim-type mapping, and the AuthSystem API does.** The API sets
`MapInboundClaims = false` with the comment "Disable claim type mapping to preserve original JWT claim
names"; the SDK never sets it at all. If mapping is on in your host, the standard claims `sub`, `email`,
`name`, `given_name` and `family_name` are rewritten to long WS-\* URIs before your code sees them, and
`User.FindFirst("sub")` returns `null`. The custom claims — `permissions`, `roles`, `org_perm`, `sid`,
`locale`, `timezone`, `theme` — are not in the standard map and survive either way.
*In code:* `Auth/Auth_API/Program.cs:724-725`; the SDK's options block is `ServiceCollectionExtensions.cs:70-85`.

**What to do:** set the option yourself, on the same named options the SDK already configured, in a
separate call placed **after** `AddAuthSystemAuthentication`. Configuration callbacks for one named
options object run in registration order, so yours runs last and wins.

**Do not chain a second `AddJwtBearer("Bearer", …)` onto the builder the SDK returns.** That registers the
`Bearer` scheme a second time, and the application dies while it starts with
`System.InvalidOperationException: Scheme already exists: Bearer` — before it serves a single request.
The form below was run against this SDK and works; the chained form was run and throws.

```csharp
using Auth.Sdk;
using Auth.Sdk.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;

builder.Services.AddAuthSystemAuthentication(options => { /* as above */ });

// Same scheme name, configured again after the SDK — this one wins.
builder.Services.Configure<JwtBearerOptions>(
    AuthSystemConstants.BearerScheme,
    jwt => jwt.MapInboundClaims = false);
```

### Claims reference

These are the claims your application can read off an authenticated caller. They are listed here, next to
the registration that produces them, because every controller sample below consumes them.

**From a JWT (the `Bearer` scheme).** Built by the token service, one claim per value.
*In code:* `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs:60-131`;
names defined in `Auth/Auth.Domain/Constants/JwtClaimNames.cs`.

| Claim | Present | Value |
|---|---|---|
| `sub` | always | The user's id, a GUID |
| `email` | always | The user's email address |
| `jti` | always | Unique id of this token |
| `iat` | always | Issued-at time, Unix seconds |
| `name` | always | The user's full name |
| `given_name` | always | First name |
| `family_name` | always | Last name |
| `sid` | when the sign-in created a session | Session id, stable across access-token refreshes |
| `locale` | when the user set a preferred language | Language code, e.g. `ar` |
| `timezone` | when the user set one | IANA timezone name |
| `theme` | when the user set one | `light`, `dark` or `system` |
| `roles` | one claim per role | The role's **Code**, e.g. `admin` — not a display name |
| `permissions` | one claim per permission | A permission code, e.g. `content:read` |
| `org_perm` | one claim per organization-scoped permission | `{organizationId}:{permissionCode}` |
| `iss`, `aud`, `exp`, `nbf` | always | Issuer, audience, expiry and not-before |

**`permissions` is application-wide authority. `org_perm` is authority inside one organization.**
The two are separate claims because they answer separate questions, and the SDK now has an attribute
for each:

| Your endpoint acts on… | Use | Reads |
|---|---|---|
| the application as a whole | `[RequirePermission("code")]` | `permissions`, `permission`, `scope` |
| one organization's data | `[RequireOrganizationPermission("code")]` | `org_perm`, narrowed to the organization in the route |

`[RequireOrganizationPermission]` takes the target organization from the route — name the parameter
`orgId` or `organizationId`, or pass your own name as the second argument. If the route names no
organization, **authorization fails**: an unresolvable scope is not an absent one, and failing closed
surfaces a mis-annotated endpoint on its first call instead of after an incident.

```csharp
[HttpDelete("organizations/{orgId:guid}/invoices/{id:guid}")]
[RequireOrganizationPermission("invoices:delete")]
public Task<IActionResult> Delete(Guid orgId, Guid id) { ... }
```

**Why this matters, concretely.** A user who belongs to two organizations that both enable your
application signs in once and gets one token. Application tokens used to flatten every organization's
delegated permissions into the unscoped `permissions` claim, so that user arrived carrying a bare
`invoices:delete` with nothing recording which organization granted it — and `[RequirePermission]`,
reading exactly that claim, would grant it against either organization's data. Delegated permissions
now ride only in `org_perm`, tagged with the organization that granted them.

**If you are upgrading:** any endpoint that acts on one organization's records and is currently
annotated `[RequirePermission]` must move to `[RequireOrganizationPermission]`. Left alone it will
start denying rather than over-granting — a visible failure, not a silent one, which is the correct
direction for this class of change.

*In code:* `Auth/Auth.Sdk/Authorization/PermissionRequirementHandler.cs` (application-wide) and
`Auth/Auth.Sdk/Authorization/OrganizationPermissionRequirementHandler.cs` (organization-scoped).

**From the `ApiKey` scheme.** Minted locally by the SDK from the validation response.
*In code:* `Auth/Auth.Sdk/Handlers/ApiKeyAuthenticationHandler.cs:46-59`.

| Claim | Value |
|---|---|
| `ClaimTypes.NameIdentifier` | The API key's id |
| `apikey_id` | The API key's id |
| `application_id` | The owning application's id |
| `apikey_name` | The key's name |
| `environment` | `production`, `staging` or `development` |
| `scope` **and** `permission` | One of each, per scope the key holds |

**From the `WebhookKey` scheme.**
*In code:* `Auth/Auth.Sdk/Handlers/WebhookKeyAuthenticationHandler.cs:51-59`.

| Claim | Value |
|---|---|
| `ClaimTypes.NameIdentifier` | The webhook key's id |
| `webhookkey_id` | The webhook key's id |
| `application_id` | The owning application's id |
| `webhookkey_name` | The key's name |
| `target_url` | The registered target URL |
| `environment` | `production`, `staging` or `development` |

Note that `sub` appears only on a JWT, and `ClaimTypes.NameIdentifier` — the long WS-\* URI — appears only
on the two key schemes. They are not the same claim.

---

## Step 6: Protect your endpoints

Everything below is ordinary ASP.NET Core authorization. The only AuthSystem-specific pieces are the
scheme names and the `[RequirePermission]` attribute.

### 6A. Endpoints your users reach with a JWT

A bare `[Authorize]` means the `Bearer` scheme, because the SDK sets it as the default.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/articles")]
[Authorize]
public class ArticlesController : ControllerBase
{
    [HttpGet]
    public IActionResult GetArticles()
    {
        var userId      = User.FindFirst("sub")?.Value;
        var email       = User.FindFirst("email")?.Value;
        var roles       = User.FindAll("roles").Select(c => c.Value).ToList();
        var permissions = User.FindAll("permissions").Select(c => c.Value).ToList();

        return Ok(new { userId, email, roles });
    }
}
```

**If `userId` or `email` come back `null` while `roles` and `permissions` are populated,** inbound claim
mapping is on. Either set `MapInboundClaims = false` as shown in Step 5, or read those two through
`User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)` and `ClaimTypes.Email`.

### 6B. Restricting by role

The `roles` claim carries the role's **Code** from AuthSystem's role catalogue, not a name you invent.
The eight codes a clean database publish creates are `super-admin`, `admin`, `user-manager`, `auditor`,
`user`, `org-owner`, `org-admin` and `org-member`.
*In code:* `Auth/Auth.Application/Features/Authentication/Common/TokenClaimsResolver.cs:41,60`; the seeded
codes are `Auth/Auth_DB/dbo/PostDeployment/Script.PostDeployment.sql:38-76` and
`Auth/Auth_DB/dbo/Scripts/SeedData/07_OrganizationRolesPermissions.sql:16-35`.

```csharp
// Only members of the "admin" role
[Authorize(Roles = "admin")]
[HttpDelete("{id}")]
public IActionResult DeleteArticle(Guid id) { /* ... */ }

// Members of either "admin" or "user-manager"
[Authorize(Roles = "admin,user-manager")]
[HttpPut("{id}")]
public IActionResult UpdateArticle(Guid id, [FromBody] UpdateRequest request) { /* ... */ }

// Two attributes means AND — the caller must hold both roles
[Authorize(Roles = "admin")]
[Authorize(Roles = "super-admin")]
[HttpPost("dangerous-action")]
public IActionResult DangerousAction() { /* ... */ }

// Checking in code
[Authorize]
[HttpGet("dashboard")]
public IActionResult Dashboard()
{
    if (User.IsInRole("admin"))
    {
        // show the admin dashboard
    }
    return Ok();
}
```

### 6C. Restricting by permission

`[RequirePermission("...")]` is the SDK's own attribute. It builds a policy named `Permission:<the
string>` and checks the caller's `permissions`, `permission` and `scope` claims.

```csharp
using Auth.Sdk.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/articles")]
[Authorize]
public class ArticlesController : ControllerBase
{
    [RequirePermission("content:read")]
    [HttpGet]
    public IActionResult GetArticles() { /* ... */ }

    [RequirePermission("content:publish")]
    [HttpPost("{id}/publish")]
    public IActionResult PublishArticle(Guid id) { /* ... */ }

    // Two attributes means AND — the caller must hold both
    [RequirePermission("content:write")]
    [RequirePermission("content:approve")]
    [HttpPost("{id}/approve-and-publish")]
    public IActionResult ApproveAndPublish(Guid id) { /* ... */ }
}
```

**A permission string is not something you invent in your own code.** `content:read` must exist as a
permission row inside AuthSystem, and must be granted to the user — or to the API key as a scope — before
it can ever appear in a token. Your attribute checks a claim; it does not create one. Creating permission
rows requires the `permissions:create` permission, which is itself one of the codes a clean database
publish does not seed, so on a fresh install only a `super-admin` can do it.
*In code:* permissions are resolved into the token at
`Auth/Auth.Application/Features/Authentication/Common/TokenClaimsResolver.cs:47-58`.

**Wildcard matching.** A held permission ending in `:*` grants everything under that prefix.

| Caller holds | Endpoint requires | Result |
|---|---|---|
| `*` | anything | Granted |
| `content:*` | `content:read` | Granted |
| `content:*` | `content:publish` | Granted |
| `content:*` | `content` | Granted |
| `content:read` | `content:publish` | Denied |
| `app:content:*` | `app:content:read` | Granted |

Exact matches ignore letter case. A `:*` grant matches only when the required permission carries on with a
colon after the prefix, or is exactly the prefix on its own — so `content:*` does not grant
`contented:read`.
*In code:* `Auth/Auth.Sdk/Authorization/PermissionRequirementHandler.cs:54-76`.

**Which schemes this works with.** `[RequirePermission]` works for the `Bearer` scheme, through the
`permissions` claim, and for the `ApiKey` scheme, through the `scope` and `permission` claims. It **never**
works for the `WebhookKey` scheme, because that identity carries no permission claims at all — see
limitation 4.

**When a check denies, the SDK logs a warning listing every permission the caller actually held.** That
log line is the fastest way to find a typo in a permission code.
*In code:* `PermissionRequirementHandler.cs:46-48`.

### 6D. Endpoints another system reaches with an API key

Remember limitations 1, 2 and 3 before you build on this: as shipped, these calls fail against a
default-configured server.

```csharp
using Auth.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/content")]
[Authorize(AuthenticationSchemes = AuthSystemConstants.ApiKeyScheme)]
public class ContentApiController : ControllerBase
{
    [HttpGet("pages")]
    public IActionResult GetPages()
    {
        var apiKeyId      = User.FindFirst("apikey_id")?.Value;
        var applicationId = User.FindFirst("application_id")?.Value;
        var scopes        = User.FindAll("scope").Select(c => c.Value).ToList();
        var environment   = User.FindFirst("environment")?.Value;

        return Ok(new { apiKeyId, applicationId, scopes, environment });
    }

    [HttpPost("publish")]
    public IActionResult PublishContent([FromBody] PublishRequest request)
    {
        if (!User.HasClaim("scope", "content:publish"))
            return Forbid();

        return Ok();
    }
}
```

**What happens on each request:** the caller sends `X-Api-Key: ak_prod_…`; the SDK looks in its memory
cache; on a miss it POSTs the raw key to `/api/v1/apikeys/validate` on the auth server; a successful
response becomes the claims above and is cached for `ApiKeyCacheDuration`.
*In code:* `Auth/Auth.Sdk/AuthSystemClient.cs:43-80`.

**If the auth server is unreachable, your application reports the key as invalid.** Network failure,
timeout, 401, 403 and a genuinely revoked key all collapse into the same
`AuthenticateResult.Fail("Invalid API key.")`. You cannot tell them apart from the HTTP response — check
your logs, where `AuthSystemClient` writes a warning carrying the actual status code.
*In code:* `Auth/Auth.Sdk/Handlers/ApiKeyAuthenticationHandler.cs:41-44`;
`Auth/Auth.Sdk/AuthSystemClient.cs:60-64,75-79`.

### 6E. Endpoints that receive webhook callbacks

Remember limitation 4: do not put `[RequirePermission]` on these. A bare `[Authorize]` naming the scheme
is the only thing that can succeed.

```csharp
using Auth.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/webhooks")]
[Authorize(AuthenticationSchemes = AuthSystemConstants.WebhookKeyScheme)]
public class WebhooksController : ControllerBase
{
    // Called as: POST /api/webhooks/content-updated?whk=wk_prod_xyz789...
    [HttpPost("content-updated")]
    public IActionResult OnContentUpdated([FromBody] WebhookPayload payload)
    {
        var webhookKeyId  = User.FindFirst("webhookkey_id")?.Value;
        var applicationId = User.FindFirst("application_id")?.Value;
        var targetUrl     = User.FindFirst("target_url")?.Value;

        return Ok();
    }
}
```

**A webhook key sent over plain HTTP is logged as a warning and then accepted anyway.** The SDK does not
refuse it. Because the key travels in the query string it also lands in web-server access logs, proxy logs
and any `Referer` header the receiving page emits. Terminate webhook endpoints on HTTPS only, at your
reverse proxy, and do not rely on the SDK to enforce that.
*In code:* `Auth/Auth.Sdk/Handlers/WebhookKeyAuthenticationHandler.cs:40-43`;
`Auth/Auth.Sdk/AuthSystemConstants.cs:31`.

### 6F. Endpoints that accept more than one scheme

Name both schemes, then decide which one authenticated the request by looking for a claim only that scheme
mints. **Do not switch on `User.Identity.AuthenticationType` expecting the string `"Bearer"`** — the SDK
never sets that value for the JWT scheme, so it is whatever the framework defaults to, and a literal
`"Bearer"` comparison silently falls through to your rejection branch. The two key handlers *do* set it
explicitly, but the JWT one does not, which is enough to make the whole comparison unsafe.
*In code:* the SDK sets no `AuthenticationType` for JWT anywhere in `Auth/Auth.Sdk/`; the key handlers set
theirs at `ApiKeyAuthenticationHandler.cs:61-63` and `WebhookKeyAuthenticationHandler.cs:61-63`.

```csharp
using Auth.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/data")]
[Authorize(AuthenticationSchemes = AuthSystemConstants.BearerScheme + "," + AuthSystemConstants.ApiKeyScheme)]
public class DataController : ControllerBase
{
    [HttpGet]
    public IActionResult GetData()
    {
        // Discriminate on a claim only one scheme produces.
        var apiKeyId = User.FindFirst("apikey_id")?.Value;
        if (apiKeyId is not null)
            return Ok(new { source = "apikey", keyId = apiKeyId });

        var userId = User.FindFirst("sub")?.Value;
        if (userId is not null)
            return Ok(new { source = "user", userId });

        return Unauthorized();
    }
}
```

---

## Calling the auth server from your own code

Inject `AuthSystemClient` wherever you need it. It has six public methods.

| Method | What it does | What it returns on failure |
|---|---|---|
| `ValidateApiKeyAsync(string rawApiKey, CancellationToken)` | POSTs to `/api/v1/apikeys/validate` | `null` |
| `ValidateWebhookKeyAsync(string rawWebhookKey, CancellationToken)` | POSTs to `/api/v1/webhookkeys/validate` | `null` |
| `IntrospectTokenAsync(string token, CancellationToken)` | POSTs to `/api/v1/auth/introspect` (RFC 7662 token introspection) | `null` |
| `LoginAsync(string email, string password, string applicationId, CancellationToken)` | POSTs to `/api/v1/auth/login` and stores the tokens | `false` |
| `SetTokensAsync(string accessToken, string refreshToken, int expiresInSeconds, CancellationToken)` | Puts tokens you obtained elsewhere into the SDK's store | — |
| `LogoutAsync(CancellationToken)` | Clears the store | — |

*In code:* `Auth/Auth.Sdk/AuthSystemClient.cs:43,86,128,162,204,212`.

**Every one of these swallows every exception.** A network failure, a timeout, malformed JSON and a
rejected credential all produce the same `null` or `false`, after a log entry. If you need to distinguish
them, your only source is the log.
*In code:* `AuthSystemClient.cs:75-79,118-122,146-150,193-197`.

**`IntrospectTokenAsync` is opt-in.** No SDK handler calls it — validating a JWT locally is the normal
path, and introspection exists for the case where you want the server's live opinion about a token.

**`LoginAsync`'s `applicationId` argument goes nowhere.** The SDK sends it, but the server's login request
contract declares only `Email`, `Password` and `DeviceId`, so the value is discarded. Conversely the SDK
never sends `deviceId`.
*In code:* `AuthSystemClient.cs:169`;
`Auth/Auth_API/Modules/Authentication/Contracts/LoginRequest.cs`.

### Automatic token refresh

When the SDK's token store holds tokens, outgoing calls on its named `"AuthSystem"` HTTP client carry them
and refresh them without you asking. Four facts describe the whole behaviour.

- It refreshes **proactively** when the access token is within `RefreshBufferSeconds` of expiry, before
  sending your request.
- It refreshes **reactively**, once, when a response comes back 401, and then replays the request.
- It calls `POST /api/v1/auth/refresh`, which is an anonymous endpoint — no bearer token needed to refresh.
- A 401 from that refresh call clears the store, so the next request goes out unauthenticated.

*In code:* `Auth/Auth.Sdk/TokenManagement/TokenRefreshHandler.cs:36-125`;
`Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:194-195`.

Two implementation details are worth knowing because they will show up in production diagnostics. A failed
refresh sends your request with **no** `Authorization` header, not with the stale one (limitation 7). And
each refresh creates a brand-new `HttpClient` rather than using the injected factory, which is the classic
socket-exhaustion and stale-DNS pattern.
*In code:* `TokenRefreshHandler.cs:52-56,111`.

---

## Step 7: Create API keys and webhook keys

Read limitations 1, 2 and 3 first. On a freshly published database, only a member of the `super-admin`
role can complete any of this.

**The easy path is the admin console.** Sign in at the console, open **API Keys** at `/api-keys` or
**Webhook Keys** at `/webhook-keys`, and use the create button. The console shows the new key exactly
once.
*In code:* `Auth_UI/apps/console/src/routes.tsx:224-250`.

**The HTTP path, step by step.** Every command below is written for a terminal; replace anything in angle
brackets.

1. Sign in and capture an access token. Run this from any terminal that can reach the auth server:

   ```bash
   curl -s -X POST https://auth.example.com/api/v1/auth/login \
     -H "Content-Type: application/json" \
     -d '{"email":"<super-admin email>","password":"<password>"}'
   ```

   **What success looks like:** a JSON body containing a `token` object. Your access token is
   `token.accessToken` — note the nesting; the token is not at the top level. If `requiresTwoFactor` is
   `true` and `token` is `null`, this account has two-factor enabled and you must complete the challenge
   before you get a token.
   *In code:* `Auth/Auth.Application/DTOs/LoginResponse.cs:10-36`;
   `Auth/Auth.Application/DTOs/TokenResponse.cs:8-31`.
2. Find the id of the application the key belongs to. `GET /api/v1/applications` with that token, and read
   the `id` of the row whose `code` matches yours.
3. Create the key.

### Create an API key

```http
POST /api/v1/apikeys HTTP/1.1
Host: auth.example.com
Authorization: Bearer <the accessToken from step 1>
Content-Type: application/json

{
  "applicationId": "<the application id from step 2>",
  "name": "Content Sync",
  "description": "Key for syncing content to your application",
  "environment": "production",
  "rateLimitPerMinute": 60,
  "rateLimitPerDay": 10000,
  "permissionIds": ["<guid of content:read>", "<guid of content:publish>"]
}
```

`environment` defaults to `production`, `rateLimitPerMinute` to `60` and `rateLimitPerDay` to `10000` when
you omit them.
*In code:* `Auth/Auth_API/Modules/ApiKeyManagement/Controllers/ApiKeysController.cs:78-80`.

**Those two rate-limit numbers are stored and returned, and nothing in AuthSystem enforces them.** No
limiter reads them. If you need throttling on an API key, implement it in your own application.

**An unknown `permissionIds` entry is silently skipped.** The handler looks each id up and quietly ignores
any that does not resolve — no error comes back. A key can therefore be created with fewer scopes than you
asked for. Check the created key's scopes afterwards.
*In code:* `Auth/Auth.Application/Features/ApiKeys/CreateApiKey/CreateApiKeyCommandHandler.cs:70-82`.

**Response — HTTP 201, and the key is shown exactly once:**

```json
{
  "id": "generated-guid",
  "apiKey": "ak_prod_aBcDeFgHiJkLmNoPqRsTuVwXyZ012345",
  "keyPrefix": "ak_prod_",
  "createdAt": "2026-03-22T10:15:00Z",
  "expiresAt": null
}
```

*In code:* `Auth/Auth.Application/DTOs/ApiKeyDto.cs:36-43`.

### Create a webhook key

```http
POST /api/v1/webhookkeys HTTP/1.1
Host: auth.example.com
Authorization: Bearer <the accessToken from step 1>
Content-Type: application/json

{
  "applicationId": "<the application id from step 2>",
  "name": "Content Update Webhook",
  "targetUrl": "https://app.example.com/api/webhooks/content-updated",
  "environment": "production"
}
```

**Response — HTTP 201, key shown exactly once:**

```json
{
  "id": "generated-guid",
  "webhookKey": "wk_prod_aBcDeFgHiJkLmNoPqRsTuVwXyZ012345",
  "keyPrefix": "wk_prod_",
  "createdAt": "2026-03-22T10:15:00Z",
  "expiresAt": null
}
```

*In code:* `Auth/Auth.Application/DTOs/WebhookKeyDto.cs:34-41`.

### How keys are protected

**A key exists in readable form for exactly one HTTP response.** After that AuthSystem holds only a prefix
and a hash, and nobody — not an administrator, not the database — can recover the original. If you lose
it, rotate it.

**The prefix tells you the environment**, and it is what AuthSystem uses to narrow the search when
validating.

| Environment you asked for | API key prefix | Webhook key prefix |
|---|---|---|
| `production` | `ak_prod_` | `wk_prod_` |
| `staging` | `ak_stag_` | `wk_stag_` |
| `development` | `ak_dev_` | `wk_dev_` |
| anything else | `ak_` | `wk_` |

*In code:* `Auth/Auth.Infrastructure/Security/ApiKeyGenerator.cs:27-33`;
`Auth/Auth.Infrastructure/Security/WebhookKeyGenerator.cs:27-33`.

**The two key types are hashed differently, and the difference matters.** An API key is hashed with
Argon2id, a deliberately slow password hash — expensive to attack offline even if the database leaks, but
also expensive to check, so AuthSystem fetches every active key sharing the presented prefix and verifies
them one at a time. A webhook key is hashed with HMAC-SHA256, which is fast and deterministic, so it can
be looked up directly — but an HMAC hash offers no offline-cracking resistance if the HMAC key leaks
alongside the database. That is the trade-off, stated plainly: API keys are slower and safer at rest;
webhook keys are faster and depend entirely on the secrecy of the HMAC key.
*In code:* `ApiKeyGenerator.cs:7,36`; `WebhookKeyGenerator.cs:7,36`;
`Auth/Auth.Application/Features/ApiKeys/ValidateApiKey/ValidateApiKeyQueryHandler.cs:38-47,74-88`.

**The SDK never caches a raw key.** Its cache key is the base64 of the SHA-256 hash of the raw key, so the
key itself is not sitting in your process's memory cache.
*In code:* `Auth/Auth.Sdk/AuthSystemClient.cs:229-233`.

### Key management endpoints

Every one of these requires a signed-in caller holding the listed permission. See limitation 2 — on a
clean database publish, none of these permission codes can be granted, and only the `super-admin` role's
global `*` reaches them.

| Method and path | Required permission | Success | Notes |
|---|---|---|---|
| `GET /api/v1/apikeys` | `apikeys:read` | 200 | `applicationId` is an **optional** query filter. Omit it and you get every application's keys. |
| `POST /api/v1/apikeys` | `apikeys:create` | 201 | Returns the raw key once. |
| `POST /api/v1/apikeys/{id}/revoke` | `apikeys:revoke` | 204 | Optional `reason` in the body. |
| `POST /api/v1/apikeys/{id}/rotate` | `apikeys:rotate` | 200 | Optional `gracePeriodMinutes`, default 60. |
| `POST /api/v1/apikeys/validate` | `apikeys:validate` | 200 | The endpoint the SDK's `ApiKey` scheme calls. |
| `GET /api/v1/webhookkeys` | `webhookkeys:read` | 200 | `applicationId` optional, same as above. |
| `POST /api/v1/webhookkeys` | `webhookkeys:create` | 201 | Returns the raw key once. |
| `POST /api/v1/webhookkeys/validate` | `webhookkeys:validate` | 200 | The endpoint the SDK's `WebhookKey` scheme calls. |
| `POST /api/v1/webhookkeys/{id}/revoke` | `webhookkeys:revoke` | 204 | Optional `reason` in the body. |
| `POST /api/v1/webhookkeys/{id}/rotate` | `webhookkeys:rotate` | 200 | Optional `gracePeriodMinutes`, default 60. |

*In code:* `Auth/Auth_API/Modules/ApiKeyManagement/Controllers/ApiKeysController.cs:42-43,65-66,97-98,122-123,141-142`;
`Auth/Auth_API/Modules/WebhookKeyManagement/Controllers/WebhookKeysController.cs:41-42,64-65,94-95,113-114,138-139`.

A successful `POST /api/v1/apikeys/validate` returns:

```json
{
  "active": true,
  "apiKeyId": "guid",
  "applicationId": "guid",
  "name": "Content Sync",
  "environment": "production",
  "scopes": ["content:read", "content:publish"],
  "rateLimitPerMinute": 60,
  "rateLimitPerDay": 10000
}
```

*In code:* `Auth/Auth.Application/Features/ApiKeys/ValidateApiKey/ValidateApiKeyResponse.cs:6-16`.

---

## What you can discover at runtime

Three addresses on the auth server are anonymous, unversioned, and exempt from gateway-token checking.
They are how any client, in any language, learns what it needs.
*In code:* `Auth/Auth_API/Controllers/DiscoveryController.cs:18-20,35,54,69`;
the exemption is `Auth/Auth_API/appsettings.json:96-102`.

**`GET /.well-known/openid-configuration`** returns the OpenID Connect discovery document. Its fields, as
this server builds them:

| Field | Value |
|---|---|
| `issuer` | The server's configured `Jwt:Issuer` |
| `jwks_uri` | `{public base URL}/.well-known/jwks.json` |
| `authorization_endpoint` | `{public base URL}/api/v1/auth/authorize` |
| `token_endpoint` | `{public base URL}/api/v1/auth/token` |
| `userinfo_endpoint` | `{public base URL}/api/v1/auth/me` |
| `end_session_endpoint` | `{public base URL}/api/v1/auth/logout` |
| `revocation_endpoint` | `{public base URL}/api/v1/auth/revoke` |
| `introspection_endpoint` | `{public base URL}/api/v1/auth/introspect` |
| `response_types_supported` | `["code"]` |
| `subject_types_supported` | `["public"]` |
| `token_endpoint_auth_methods_supported` | `["none"]` — public clients, no client secret |
| `grant_types_supported` | `["authorization_code", "refresh_token"]` |
| `code_challenge_methods_supported` | `["S256"]` |
| `claims_supported` | `["sub","email","name","roles","permissions","iat","exp","aud","iss"]` |

*In code:* `Auth/Auth.Application/Features/Discovery/GetDiscoveryDocument/GetDiscoveryDocumentQueryHandler.cs:31-50`.
The `v1` in those paths is a hard-coded literal, not derived from your request. There is no
`id_token_signing_alg_values_supported` and no `scopes_supported`: this server does not issue OIDC
id_tokens, and the document deliberately omits what it does not implement.

**`GET /.well-known/jwks.json`** returns the public signing keys, one entry, shaped
`{"kty":"RSA","use":"sig","alg":"RS256","kid":"<key id>","n":"…","e":"…"}`.
*In code:* `Auth/Auth.Infrastructure/Authentication/JwtTokenService.cs:229-242`.

**`GET /.well-known/public-key.pem`** returns the same public key as `text/plain`, in a
`-----BEGIN RSA PUBLIC KEY-----` block, for tooling that wants PEM rather than JWKS.
*In code:* `JwtTokenService.cs:251-264`.

---

## Error responses you will see

Three different shapes come back from AuthSystem, and telling them apart saves an afternoon.

**A gateway-token rejection is HTTP 403 with `Content-Type: application/problem+json`:**

```json
{
  "type": "https://httpstatuses.com/403",
  "title": "Forbidden",
  "status": 403,
  "detail": "Invalid gateway token.",
  "instance": "/api/v1/apikeys/validate"
}
```

The `detail` is `"Direct API access is not allowed. Please use the API Gateway."` when the header is
missing entirely, and `"Invalid gateway token."` when it is present but does not match — which is exactly
what limitation 1 produces. Both are localized, so the wording follows the request's language.
*In code:* `Auth/Auth_API/Common/Middleware/GatewayTokenValidationMiddleware.cs:57-58,79-80,132-142`.

**A business-rule failure is a standard ASP.NET Core `ProblemDetails` document** — every controller
converts handler errors through `Problem(errors)`.

**A rate-limit rejection from the API is HTTP 429 with a different shape entirely:**

```json
{ "error": "Too many requests. Please try again later.", "retryAfter": 60 }
```

`retryAfter` is seconds expressed as a **decimal number**, not an integer, and the API sets **no**
`Retry-After` header. The gateway, if you are going through it, answers 429 differently again: a
`type`/`title`/`status`/`detail`/`retryAfter` body with an integer `retryAfter`, plus a real `Retry-After`
header. If you write retry logic, handle both.
*In code:* `Auth/Auth_API/Program.cs:822-838`; `Auth/API_Gateway/Program.cs:263-272`.

---

## Rate limits on the endpoints you will call

**Sign-in endpoints are throttled at 20 requests per 60 seconds, per client IP address.** That applies to
`POST /api/v1/auth/login` and to `POST /api/v1/auth/token` — the token-exchange endpoint of the browser
flow. The window is fixed and the queue length is zero, so request 21 inside the window is rejected
immediately rather than waiting.
*In code:* the policy is `Auth/Auth_API/Program.cs:799-807`; the numbers are
`Auth/Auth_API/appsettings.json:243-244`; the endpoints opt in at
`Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:72,269`.

**There is deliberately no general rate limit on the API.** Only two named policies exist — the sign-in
one above and a stricter one for password reset — and no global limiter is configured. The key-validation
endpoints are therefore not throttled by the API at all. If you go through the gateway, its own `api`
policy applies instead.
*In code:* `Auth/Auth_API/Program.cs:784-791`.

---

## Asking for a language

AuthSystem returns error text, validation messages and success messages in seven languages: English
(`en`), Arabic (`ar`), Turkish (`tr`), French (`fr`), Chinese (`zh`), Urdu (`ur`) and Persian (`fa`).
*In code:* `Auth/Auth_Localization/Extensions/LocalizationServiceExtensions.cs:19`.

**Send a standard `Accept-Language` header.** That is the normal way and it works:

```http
GET /api/v1/apikeys HTTP/1.1
Host: auth.example.com
Authorization: Bearer <token>
Accept-Language: ar
```

**Four sources are checked, in this fixed order, and the first one that yields a supported language
wins:**

1. A `culture` query-string parameter, for example `?culture=ar`.
2. A culture cookie.
3. The `Accept-Language` header.
4. A custom `X-Language` header.

*In code:* `LocalizationServiceExtensions.cs:56-69`.

**The trap in that ordering:** `Accept-Language` is checked *before* `X-Language`. A client that sends
`X-Language: ar` and `Accept-Language: en` gets **English**, because the third provider already answered.
If you use `X-Language`, do not also send a conflicting `Accept-Language`.

**An unsupported or malformed value never produces an error.** The `X-Language` provider checks the value
against the supported list and returns nothing when it does not match, so the next provider — or the
default — takes over. An `Accept-Language` naming a language that is not supported falls through the same
way. Either path ends at English.
*In code:* `LocalizationServiceExtensions.cs:48,90-95`.

**Your own application's language is your own business.** The SDK sends no language header on the calls it
makes, so anything AuthSystem logs or returns to the SDK comes back in English.

---

## Integrating without .NET

There is no SDK for any other language in this repository, and you do not need one. Everything the SDK
does over the wire is standard.

1. **Sign users in** with the authorization-code + PKCE flow in
   [Step 2](#step-2-how-a-person-actually-signs-in). Any OAuth 2.0 or OpenID Connect client library
   supports it. Point the library at `https://<your auth host>/.well-known/openid-configuration` and it
   will discover the rest.
2. **Validate the access tokens** yourself. They are RS256-signed JWTs. Fetch
   `/.well-known/jwks.json`, cache the keys, and verify the signature, the `iss` claim, the `aud` claim
   and the expiry. Read authorization out of the `permissions` and `roles` claims as described in the
   [claims reference](#claims-reference).
3. **If you want the server's live opinion on a token**, `POST /api/v1/auth/introspect` with
   `{"token": "<the token>"}`. This endpoint requires a bearer token of its own — it is not anonymous.
   *In code:* `Auth/Auth_API/Modules/Authentication/Controllers/AuthController.cs:670-671`.
4. **API-key and webhook-key validation are effectively unavailable to you**, for the same reason they are
   unavailable to the SDK: the permission codes those endpoints demand do not exist in any database seed.
   See limitation 2.

---

## Check it works

Do these three in order. Each one isolates a different failure.

1. **Confirm your application starts.** From your project's directory, run `dotnet run`. It should print
   its listening addresses and stay running. A crash at `UseAuthorization()` means you skipped
   `AddAuthorization()`; a crash at `MapControllers()` means you skipped `AddControllers()`.
2. **Confirm your host can reach AuthSystem's metadata.** From a terminal on the same machine, run
   `curl -s https://auth.example.com/.well-known/openid-configuration`. You should get JSON whose
   `issuer` exactly equals your `Issuer` setting. A connection error here will later look like a bad
   token, not a network problem.
3. **Call one protected endpoint of your own with a real token.** Get a token by completing the browser
   flow in Step 2, then call your endpoint with `Authorization: Bearer <token>`. A 200 means the whole
   chain works.

**The three failures you will actually hit, and how to tell them apart.**

- **401 from your own application** — the token was missing, expired, signed by a different key, or its
  `iss`/`aud` did not match your settings. Check `Issuer` and `Audience` first; the audience rule catches
  most people.
- **403 with a `application/problem+json` body naming a gateway token** — this is limitation 1. Your call
  never reached the endpoint.
- **403 from your own application after a successful authentication** — a permission check denied it. Look
  in your application's log for the SDK's warning line, which prints every permission the caller actually
  held next to the one you required.
  *In code:* `Auth/Auth.Sdk/Authorization/PermissionRequirementHandler.cs:46-48`.

---

## Where to read next

- [DEVELOPER_GUIDE.md](DEVELOPER_GUIDE.md) — running AuthSystem locally, and creating the applications,
  roles and permissions this guide assumes exist.
- [PRODUCTION_DEPLOYMENT_GUIDE.md](PRODUCTION_DEPLOYMENT_GUIDE.md) — where the gateway token lives, and
  how the API, the gateway and the two web applications are deployed.
- [02_AUTH_SYSTEM_DOCUMENTATION_EN.md](02_AUTH_SYSTEM_DOCUMENTATION_EN.md) — what the platform does, for a
  reader deciding whether to adopt it.
- [SDK_PUBLISHING_GUIDE.md](SDK_PUBLISHING_GUIDE.md) — packaging the SDK, if you decide to.
