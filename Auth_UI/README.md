# Auth UI workspace (Auth_UI)

A pnpm workspace hosting the frontend apps of the Auth system, built with
**React + Vite + TypeScript** and **shadcn/ui**:

- `apps/console` — the admin console: users, roles, permissions, applications,
  organizations, API/webhook keys, audit logs, signing secrets, system settings,
  notification templates, dashboard. Signs in with email + Google.
- `apps/accounts` — the end-user self-service app: sign in/up (email + Google +
  Apple), password flows, invitations, profile, organization self-service.

> **The console owns everything.** Features that also exist in `accounts`
> (profile, organization self-service) live in `packages/account` and are
> mounted by **both** apps — never copy-pasted, and never reached by sending an
> administrator to the accounts origin.

> **External sign-in providers** live in `packages/auth/src/external/`; each app
> fills the shared login page's `providers` slot with `<ExternalProviders>`.
> Which providers are enabled comes from `GET /api/v1/Auth/external-providers`
> at runtime — `VITE_GOOGLE_CLIENT_ID` / `VITE_APPLE_SERVICES_ID` are only
> build-time fallbacks for an API older than the build.
>
> **Google Identity Services runs on both apps.** Every origin that renders the
> button needs `https://accounts.google.com` in its `script-src`, `connect-src`
> **and** `frame-src` (the last one for the personalized-button iframe), and
> must be an Authorized JavaScript origin on the Google OAuth client — GSI
> renders *nothing at all*, silently, when it is not.
>
> **Apple Sign-In runs on `accounts` only**, and needs
> `https://appleid.cdn-apple.com` in `script-src` plus
> `https://appleid.apple.com` in `connect-src`. Adding it to the console means
> adding those origins to the console's CSP too — a whole-app decision, since a
> SPA has one policy for every route.
- `packages/api` — typed API client (openapi-fetch + generated schema), token
  store/JWT, cross-tab session coordination, upload helpers, error
  normalization, query client.
- `packages/auth` — AuthProvider, route/permission guards, the shared auth
  pages, the post-login destination rule (`login-completion.ts`), and the
  external-provider buttons (`external/`).
- `packages/account` — self-service pages (profile, organizations) shared by
  **both** apps. Console passes href-builder props for admin drill-down;
  accounts does not, so the same page renders names as plain text there.
- `packages/i18n` — i18next setup, the 7 locales, RTL DirectionProvider,
  timezone display helpers.
- `packages/ui` — shadcn primitives, shared widgets (`common/`), the DataTable
  system, hooks, formatting utils, theme + branding providers.

> The UIs are static SPAs that talk to the .NET Auth API (JWT bearer). This is
> a Node/React workspace — **not** a Visual Studio C# project — and is best
> worked on in VS Code.

## Stack

- **Vite + React 19 + TypeScript** (SPA)
- **shadcn/ui** via the customized preset `b1tel7QNE` (style: `radix-luma`,
  Tailwind v4, lucide icons, IBM Plex Sans, RTL enabled; supersedes `b1VlIzU8`
  by switching the `--chart-*` tokens to a cyan ramp). **All styling comes
  from the preset** — no custom colors/themes are introduced.
- **React Router** (routing + guards), **TanStack Query** (server state),
  **TanStack Table** (data tables)
- **openapi-fetch + openapi-typescript** — a fully typed API client generated
  from the API's OpenAPI document
- **react-hook-form + zod** (forms), **react-i18next** — **7 display languages**
  (en, ar, tr, fr, zh, ur, fa; ar/ur/fa are RTL)
- **Vitest + Testing Library** (unit) and **Playwright** in three tiers, with
  **axe-core** for automated WCAG — see [Testing](#testing)

> **Start here before changing anything structural:** the **`frontend-playbook`
> skill** — a project-agnostic engineering playbook for building a SPA that
> reflects a backend (Arabic). Invoke it with `/frontend-playbook`, then read the
> `rules/*.md` file for the area you are touching; the entry point alone is a
> routing table, not the detail.
>
> It is a **user-level skill**, deliberately not vendored into this repo: it
> carries no project-specific content and is shared across every frontend project
> on the machine. It lives in its own git repository, cloned to
> `~/.claude/skills/frontend-playbook`. If it is missing on your machine, clone it
> there — nothing in this repo will pull it in for you.
>
> Its "mistakes that look correct" table is the fastest read: eleven traps that
> each pass code review and work on your machine.

## Prerequisites

- Node.js 22+ and **pnpm** (`corepack enable` or `npm i -g pnpm`). The workspace
  types target Node 24.
- The Auth API running locally on its **`https` launch profile**
  (`https://localhost:5101`)
- A dev TLS certificate for the Vite servers — see below

### Dev TLS (one-time, per machine)

Both dev servers serve **https**, because the API does. Chrome's *schemeful
same-site* rule counts `http://localhost` and `https://localhost` as different
sites, so from an http SPA the browser refuses to store the `SameSite=Lax` IdP
session cookie the API sets at login: sign-in appears to succeed, then
`/auth/authorize` bounces back to `/login` forever. Serving https makes dev
match production, where both origins sit on one domain over https.

Export the ASP.NET dev certificate — already trusted by your machine, so Chrome
shows no warning and no extra tool is needed:

```bash
dotnet dev-certs https --export-path "$env:USERPROFILE\.aspnet\https\localhost.pem" --format PEM --no-password
```

Then point each app at it in `apps/<app>/.env.development.local` (gitignored):

```
DEV_HTTPS_CERT=C:\Users\<you>\.aspnet\https\localhost.pem
DEV_HTTPS_KEY=C:\Users\<you>\.aspnet\https\localhost.key
```

Without these the servers still start on http and print a warning saying so —
everything works except OAuth sign-in. The logic lives in `dev-https.ts`.

## Getting started

```bash
pnpm install      # once, at the workspace root
pnpm gen:api      # regenerate the typed client (requires the API running)
pnpm dev          # console app on https://localhost:5173
```

Configure the API origin via each app's Vite env files:

- `apps/console/.env.development` → `VITE_API_BASE_URL=https://localhost:5101`
- `apps/console/.env.production` → the deployed API origin (keep in sync with
  the CSP in `apps/console/public/web.config`)

`VITE_API_BASE_URL` must match the API's `IdentityProvider:PublicBaseUrl`
exactly. The authorize endpoint builds its `returnTo` from that setting, and
`getValidReturnTo` rejects any other origin as an open-redirect attempt — a
mismatch silently drops the post-login resume.

## Scripts (run at the workspace root)

| Script | Purpose |
|--------|---------|
| `pnpm dev` | Start the console dev server (https://localhost:5173) |
| `pnpm dev:accounts` | Start the accounts dev server (https://localhost:5174) |
| `pnpm build` | Type-check (`tsc -b`) and build every app to its `dist/` |
| `pnpm typecheck` | Type-check only |
| `pnpm gen:api` | Regenerate `packages/api/src/schema.d.ts` from `/openapi/v1.json` |
| `pnpm test` / `pnpm test:watch` | Unit tests (all apps + packages) |
| `pnpm test:coverage` | Unit tests with Istanbul coverage |
| `pnpm test:coverage:changed` | Coverage gated on **changed lines** (threshold 90) |
| `pnpm e2e:isolated` | **Primary browser tier** — builds both apps, serves them, mocks the API |
| `pnpm e2e` | Credentialed e2e against real dev servers + a real API |
| `pnpm e2e:production` | Both apps built, against the production-shaped config |
| `pnpm e2e:harness` | Both apps built to `dist-harness`, served with their `web.config` headers (CSP included) on HTTPS same-site origins with a real API host — see [Browser harness](#browser-harness-e2eharness) |
| `pnpm lint` / `pnpm format` | Lint / format |

Run `pnpm exec playwright install` once before any of the e2e scripts.

> `pnpm format` rewrites whole files, and several committed files predate the
> formatter — running it over one of them reformats hundreds of unrelated lines
> and buries your change. Check `git diff --numstat` before staging: insertions
> far above the size of your edit means the formatter, not the edit.

## Architecture

```
apps/
  console/                  admin console  → https://localhost:5173
    src/
      lib/        console-only constants (permissions + nav), breadcrumbs,
                  record hrefs, backend catalogue mirrors (audit, notifications)
      components/ layout (AppShell, sidebar), global search
      pages/      one folder per feature area
      routes.tsx  route tree (public / authenticated / permission-gated),
                  every screen behind `lazyRoute`
  accounts/                 end-user self-service → https://localhost:5174
packages/
  api/      typed client (openapi-fetch) + generated schema + auth middleware,
            token store, JWT decode, tab-sync, uploads, error helpers,
            query client (incl. `resetUserScopedCache`)
  auth/     AuthProvider, RequireAuth/RequireAnonymous, PermissionRoute,
            login-completion, return-to validation, external providers
  account/  profile + organization pages mounted by BOTH apps
  i18n/     i18next setup, locales (en/ar/tr/fr/zh/ur/fa), RTL DirectionProvider,
            timezone helpers
  ui/       shadcn components (preset-styled), shared widgets (common/),
            data-table system, hooks, format utils, ThemeProvider,
            BrandingProvider, chunk recovery
e2e/
  isolated/   API-mocked suite against the built apps (primary tier);
              accounts/ holds the signed-out journeys of the accounts app
  console/    credentialed console specs
  accounts/   credentialed accounts specs
  production/ production-shaped build checks
```

Workspace packages are consumed as `@authsystem/api`, `@authsystem/auth`,
`@authsystem/account`, `@authsystem/i18n`, and `@authsystem/ui` — resolved from
source via tsconfig paths + Vite aliases (no per-package build step).

> Adding a package is **four config touchpoints per app**: the Vite alias, the
> tsconfig `paths` + `include`, the `@source` line in `index.css`, and the
> `package.json` dependency. The `@source` line is the one that fails silently —
> each package's `src` must be registered **explicitly**; a parent directory or a
> wildcard segment scans nothing, and the symptom is a deployed site with design
> tokens but no component styles.

### Auth & security

- The **access token is kept in memory**. Where the API runs with
  `IdentityProvider:SpaRefreshCookieEnabled` and lists the app's origin in
  `IdentityProvider:FirstPartySpaOrigins`, the **refresh token never reaches
  JavaScript**: the API sets it in a per-app `__Host-` HttpOnly, Secure,
  SameSite=Strict cookie on its own host, and the body carries the sentinel
  `"__cookie__"`, which the app stores under `auth.refreshToken` as its session
  hint. The refresh request sends `{}` with `credentials: "include"`. With the
  switch off (the default, and the rollback), the token comes in the body and is
  kept in `localStorage`, as before; a stored token is migrated to the cookie by
  its first refresh once the switch is on. The server decides the mode per
  request; `token-store.ts` explains the key's two states. The API client
  middleware attaches the bearer token, proactively refreshes an expired token,
  and ends the session if refresh fails.
- **Sign-out survives a network failure.** In cookie mode clearing local state
  does not end the session, so a sign-out that never reached the API leaves
  `auth.logoutPending`, and the next load finishes it (`completePendingLogout` in
  `client.ts`) before anything signed in is shown.
- **The refresh token is single-use and shared by every tab of the origin.** The
  server rotates it and treats a second presentation as theft, revoking the whole
  account — so refreshing is serialised across tabs with a `navigator.locks` lock
  and the result broadcast on a `BroadcastChannel`. **Read
  `packages/api/src/tab-sync.ts` before touching anything in `token-store.ts`.**
  The refresh call inside the lock uses raw `fetch` on purpose: Web Locks is not
  reentrant, and routing it through the instrumented client deadlocks.
- **The query cache outlives the session.** `resetUserScopedCache` (in
  `packages/api/src/query.ts`) *removes* — not invalidates — every user-scoped key
  at all four session boundaries: sign-out, expiry, a failed `/me`, and before
  adopting new tokens. Three anonymous keys are spared deliberately.
- **RBAC**: navigation, routes, and actions are gated by the `permissions` claim,
  mirroring the API's `[RequirePermission]`. The API remains the source of truth
  (403s are handled gracefully). The `PERMISSIONS` map in
  `apps/console/src/lib/permissions.ts` is written by hand and **held to the
  backend by `permissions.test.ts`**, which reads
  `Auth/Auth.Domain/Constants/PermissionCodes.cs` and compares
  `[key path, code]` pairs — so a rename, and a swap of two codes between keys,
  both fail the suite. The same test forbids gating on a literal, because a gate
  that skips the map is a gate no comparison can see. The API side is symmetric:
  its 140 `[RequirePermission]` attributes reference `PermissionCodes` rather
  than repeating strings, and a backend test forbids the literal there too.
  Organization-scoped (`org:`) codes are deliberately out of the map; see
  *Known constraints*.
- Generated secret material (API/webhook keys, generated PEM/token, 2FA recovery
  codes) is shown **once** in a copy dialog and never persisted in app state.
  Destructive key operations additionally require an emailed code.
- A strict CSP ships in **each app's** `public/web.config`, and is pinned by a
  unit test that parses the deployed file directive by directive
  (`apps/console/src/console-login-surface.test.ts`). No runtime check, e2e run,
  or `curl` can catch a CSP break — only that test can.

### Internationalization & RTL

All **7 languages** ship in the bundle (en, ar, tr, fr, zh, ur, fa). Switching
language updates the document `dir` (`rtl`/`ltr`) and `lang`, sends
`Accept-Language` on every request so the API localizes its own errors, and PUTs
the choice to the profile. After sign-in the profile's `preferredLanguage` wins.

Every non-`en` locale file is typed against `en`, so a missing or extra key is a
**typecheck error**, and a parity test additionally checks `{{placeholder}}`
agreement. Layout uses logical CSS only (`ms-*`/`me-*`/`start`/`end`).

> Do **not** force `dir="ltr"` on identifiers, permission codes or URLs. An
> RTL page rendering `apikeys:*` as `*:apikeys` is correct — the reader meets the
> directional islands in order. Alignment comes from `text-start` on a container
> that runs in the page direction, never from a `dir` attribute on the value.

## Testing

Four browser tiers, split by what they depend on:

| Tier | Depends on | Use it for |
|------|-----------|------------|
| `e2e:isolated` | nothing — built console + built accounts + in-process API mocks | **default**: layout, a11y, permissions, i18n, bundle weight, sign-up |
| `e2e` | dev servers + a running API + a real database | server behaviour and real sign-in ceremonies |
| `e2e:production` | production-shaped build of both apps | deploy-shaped checks |
| `e2e:harness` | nothing but `openssl` (Git for Windows ships it) — built apps + IIS headers + HTTPS same-site origins + a real API host | browser controls: CSP, cookies (`Secure`, `SameSite`), CORS/CSRF, several tabs of both apps |

The isolated tier runs the **real production build** behind `vite preview`, so
bundling, code-splitting, routing and CSS stay real while credentials, shared
databases and rate limits disappear. `installAuthenticatedApi(page, permissions,
handler, { preferredLanguage })` gives any permission set and any of the 7
languages in one call. The accounts project starts signed out instead:
`installAnonymousApi(page, handler, { seen })` answers only what a stranger's
screens ask for and records every non-GET request with its parsed body, so a
spec asserts what was **sent**, not only what was drawn.

It carries the invariants that cheaper checks cannot see:

- `layout-overflow.ts` — whole-DOM sweeps for **silent** loss: content crushed by
  a flex parent, and ring/shadow clipped by a scroll pane. Measured at the
  element that actually clips, never at `documentElement`.
- `accessibility.spec.ts` — axe over a theme × direction matrix. It reaches about
  a third of WCAG AA; what it cannot see is recorded as open, not implied passing.
- `login-payload.spec.ts` — page weight measured from response bodies, asserting
  named heavy chunks are **absent** on the signed-out entries.
- `accounts/registration.spec.ts` — verify-first sign-up end to end: a reload
  of the code screen keeps the pending identity, a reload of the password screen
  forgets the code, the completion body carries no address, and the session it
  answers with lands on the profile. What no browser automation covers is how
  each password manager treats the new credential; that matrix is manual and
  lives in `ReadMe/DEVELOPER_GUIDE.md` under the two web applications.

### Browser harness (`e2e:harness`)

The isolated tier runs on `http://localhost` behind `vite preview`, which never
reads `web.config` and puts the two apps on cross-site origins. So it cannot see
a broken CSP, and a `Secure; SameSite` cookie is never sent there. The harness
closes that gap, without admin rights, a hosts-file edit, a certificate install
or any new npm dependency:

```bash
pnpm e2e:harness
```

It builds both apps exactly as `build:test` does (`tsc -b`, `vite build`,
`seal-web-config.mjs --allow-placeholder`), into **`dist-harness`**. Every
`VITE_` key is pinned to its value in the tracked `.env.production`; a key that
only an untracked file or the shell sets is refused by name. `dist` is
fingerprinted before and after the build and must not change, so the harness can
never leave a placeholder build where `pnpm build` output belongs. Then Playwright
runs `e2e/harness/*.spec.ts` (`playwright.harness.config.ts`, Chromium only).

**Topology.** One `node:https` server per worker serves five hosts, with a fresh
self-signed certificate made by `openssl` on each run (the fallback is an
*existing* `dotnet dev-certs` certificate; none is ever created or installed):

| Host | Serves |
|------|--------|
| `https://console.example.com` | `apps/console/dist-harness` |
| `https://accounts.example.com` | `apps/accounts/dist-harness`, plus `/privacy` from a folder the test mounts |
| `https://auth.example.com` | the API host: the origin both builds bake in |
| `https://example.com` | a same-site attacker page the test writes |
| `https://attacker.example.net` | a cross-site attacker page the test writes |

The first four share the registrable domain `example.com`, so they are
**same-site**. Chromium reaches the server only through a local HTTP CONNECT
proxy (`e2e/harness/proxy.ts`). The proxy refuses every other host, and the test
then fails naming that host, so nothing reaches the real `example.com`. This
covers every context the worker's browser makes, including one a test builds
with `browser.newContext()`, and the `request` fixture. The SPA
hosts send what IIS would send from `dist-harness/web.config`, read at run time:
every `customHeaders` entry, `Cache-Control` from `clientCache` and the `assets`
`<location>`, and the rewrite rules. Any element the model
(`e2e/harness/web-config-model.ts`) does not understand fails start-up with its
path. `web-config-model.test.ts` pins the model of each tracked `web.config`, so
editing one means reviewing the harness in the same commit.

**The API host.** Specs never call `page.route`: harness pages refuse route
interception by name. With interception on, Playwright answers every CORS
preflight itself and adds permissive CORS headers. The `api` fixture answers
instead, on the real server, with the same defaults as the isolated helpers:

- `api.useAuthenticated(permissions, handle?, { preferredLanguage, session })`
  seeds a cookie session by default — the sentinel in `auth.refreshToken` and a
  `__Host-` cookie per app on the API host (`session: "legacy"` seeds a real
  token, `"none"` nothing) — answers refresh and sign-out through the model in
  `e2e/harness/first-party-session.ts` (`api.firstParty`), then like
  `installAuthenticatedApi`;
- `api.useAnonymous(handle?, { seen })` answers like `installAnonymousApi`.

`handle(route, url)` has the isolated signature; `route.fulfill` may set
`Set-Cookie`. Anything unclaimed gets 404 with `x-harness-unmatched: 1`. CORS
comes from the host alone. The allow-list holds console, accounts and the apex,
with credentials. The apex is **deliberately** kept (production no longer lists
it), so a test can prove a request that passes the preflight is still refused by
the server's Origin checks. The `requests` fixture lists every request as the
server received it, with `Cookie`, `Origin` and `Sec-Fetch-Site`. A route
handler never sees those reliably.

**CSP.** The `csp` fixture records violations through three channels:

- the `securitypolicyviolation` event;
- Chromium's console, which also covers `sandbox=""` srcdoc frames;
- CDP `Audits`.

It fails a test on a violation nobody declared, and on a declared one that
never happened. Declare a real one with its cause:

```ts
csp.expect({ directive: "img-src", blocked: "https://x.test/a.png", reason: "why" })
```

Violations the apps raise **today** live in one register,
`e2e/harness/expected-csp-violations.ts`. Each entry names the script that
causes it, so the same directive from any other source is still a failure.
Every test tolerates them, and `harness-smoke.spec.ts` requires them, so a fixed
cause cannot leave a stale entry. Today it holds one: zod's `new Function("")` eval probe on the sign-in
pages. `harness-csp-selftest.spec.ts` deliberately breaks `script-src`,
`img-src` (top document and a sandboxed frame) and `connect-src`. An inline
`<style>` is its negative control while `style-src` keeps `'unsafe-inline'`.

**Spike (2026-09-29; Chromium 149.0.7827.55, Playwright 1.61.0, Windows 11,
OpenSSL 3.5.7).** Each design premise was first proved with a throwaway probe:

1. Chromium sends a CONNECT per https host through the context proxy: **pass**.
2. With `ignoreHTTPSErrors`, `isSecureContext` is true, and a
   `Secure; HttpOnly; SameSite=Lax` cookie is stored and sent with `Origin` and
   `Sec-Fetch-Site`: **pass**. Without it, with
   `--ignore-certificate-errors-spki-list`: also **pass**.
3. Without routes, the OPTIONS preflight reaches the server: **pass**.
4. A violation inside a `sandbox=""` srcdoc frame reaches the console and
   Audits: **pass**.
5. `openssl` makes the certificate: **pass** locally. Not yet run on a
   `windows-latest` runner; that comes with S30b.
6. CSP and `X-Frame-Options` are applied under the certificate override:
   **pass**.
7. WebAuthn with a CDP virtual authenticator, RP ID `example.com`, under both
   (2): **pass**.

The harness runs on Playwright's bundled headless Chromium. The full `chromium`
channel makes background requests (`www.google.com`, autofill) that the proxy
refuses.

**Fidelity limits: what the harness does not reproduce.**

- IIS module order, compression, ETag and the full MIME map. The harness adds
  `customHeaders` to every response (404 and redirects included) and serves only
  a small pinned MIME table.
- Implicit IIS behaviour, beyond `index.html` as the default document and
  refusing `*.config` paths. The courtesy trailing-slash redirect and the
  403.14 response for a directory without `index.html` are not modelled.
- Whether rewrite **conditions** ignore case. The reference states it only for
  `<match>`, so the model assumes it.
- A `web.config` inside the production `/privacy` virtual directory, if one
  exists. It is outside git; the harness assumes the directory inherits the site.
- The YARP gateway: header rewriting, its real CORS list, rate limits. The
  harness list mirrors the production facts it was given and is not read from
  gateway config.
- The real API, its own headers (including the API's CSP), and the database.
- TLS: the certificate chain is not validated (errors are ignored), and the
  server speaks HTTP/1.1 where production may use HTTP/2.
- Firefox and WebKit, and service workers (blocked in the harness).
- CSP violations inside a dedicated or shared **Worker**, and violations raised
  while a page unloads (after the end-of-test verdict), are not observed.
- File-name case: IIS on NTFS ignores it. The harness follows the file system it
  runs on, so it ignores case on Windows only.
- Code in a spec that opens its own network path is outside the proxy: a new
  `chromium.launch()`, `playwright.request.newContext()`, or Node's `fetch`. Use
  the fixtures (`page`, `context`, `browser`, `request`) instead.
- Run it only through `pnpm e2e:harness`. Running `playwright test` with the
  harness config on its own serves whatever `dist-harness` already holds.

Unit tests run on Vitest + jsdom. Two environment facts that cost real time:
jsdom has no `ResizeObserver` and Node ships a `localStorage` global that stays
`undefined` and **shadows** the one jsdom would provide — `test/setup.ts` handles
both. Any module that reads a global at import time must be `await import()`ed
*after* the stub, never statically imported in its own test.

## Deployment (IIS / Plesk)

```bash
pnpm build           # outputs static files to apps/<app>/dist/
```

Deploy each app's `dist/` (including its `web.config`) as its own static site
(e.g. `apps/console/dist/` → `console.example.com` and `apps/accounts/dist/` →
`accounts.example.com`); both origins must be in the API's production CORS
allow-list. `web.config` provides SPA fallback routing and security headers.
Update the CSP `connect-src` and `VITE_API_BASE_URL` to match your API origin.
Invitation/reset emails link to the accounts origin (`Email:FrontendBaseUrl`).

### Do not "simplify" these three rules in `web.config`

They exist together, and removing any one of them turns an ordinary deploy into a
permanently blank tab for anyone who had the app open.

1. **The SPA fallback rewrite excludes `^/assets/`.** A deploy deletes the previous
   build's fingerprinted chunks. Without the exclusion the catch-all answers a
   request for a deleted `/assets/x-HASH.js` with `200 text/html`; the module
   loader rejects it and the dynamic `import()` fails.
2. **`index.html` is `DisableCache`, `/assets` is immutable for a year.** Reverse
   these and the browser caches an HTML body under a `.js` URL — at which point
   reloading cannot rescue the tab.
3. **The app mounts a root error boundary and a `vite:preloadError` listener**
   (`packages/ui/src/common/chunk-recovery.ts`). A rejected dynamic import with no
   boundary renders nothing at all, and preload failures never reach a route error
   boundary. Recovery reloads exactly once, keyed on the running entry-script URL.

### Privacy notice on the accounts origin

`https://accounts.yourdomain.com/privacy` is the canonical public address. The
accounts site serves it as static HTML from persistent storage, without the SPA,
JavaScript, the API Gateway, or ARR. Publishing a revision renders all seven
languages and writes them to the persistent directory before the database marks
the revision as published. A storage failure stops the publish command and leaves
the previous public files and database state intact.

One-time Plesk setup for production:

- physical directory:
  `C:\...\privacy-policy`;
- virtual directory on `accounts.yourdomain.com`: `/privacy`;
- Read permission on; Write permission and directory browsing off;
- **Create application** off; execute permissions: **None**.

Set the Auth API production configuration to the same physical directory:

```json
"PrivacyPolicyPublication": {
  "PhysicalPath": "C:\\.....\\privacy-policy"
}
```

The Windows identity running the Auth API application pool needs Modify access
to that physical directory. The virtual directory itself remains read-only to
HTTP visitors. Accounts builds and deployments do not touch this directory,
because it is outside the Accounts document root.

First rollout order:

1. create the physical and virtual directories as above;
2. deploy the Auth API with `PrivacyPolicyPublication:PhysicalPath` configured;
3. publish the current revision once from the console and verify that the seven
   `.html` files and the version directory were created;
4. deploy the Accounts build containing this `web.config`;
5. verify `accounts.yourdomain.com/privacy/ar` stays on the Accounts origin, returns
   `200`, is styled, and remains readable while the API application is stopped.

If step 3 fails, fix the Auth API application's filesystem permission and try
the user-initiated publish again; there is no background retry. Do not deploy
step 4 until step 3 succeeds. For rollback, restore the previous Accounts
`web.config`; the persistent policy files are not removed.

## Known constraints

*The three constraints previously listed here — no login-time 2FA verification,
role permissions read-only, and a single unsplit bundle — were all lifted during
August 2026. What remains:*

- **Nothing proves a control is gated on its own endpoint's permission.**
  `permissions.test.ts` now holds the map and
  `Auth/Auth.Domain/Constants/PermissionCodes.cs` to the same codes, and forbids
  gating on a literal — so the *set* can no longer drift and no gate can hide
  from the comparison. But the relation that actually decides whether a screen is
  right runs button → endpoint → code, and no test follows it. A delete button
  gated on `users:update` passes every check here. Treat a green suite as
  "the two halves know the same words", not "the gates are correct".
- **The eight `org:` codes are enforced by the API and absent from the map, on
  purpose.** They are satisfied from the `org_perm` claim, which this client does
  not read, so a gate keyed on one would evaluate false for the organization
  owner who holds it. `permissions.test.ts` names all eight and fails on a ninth,
  which forces a decision rather than a silent omission. Closing this means
  teaching the client to read that claim *and* answering what it does when the
  claim is absent because the token predates the membership — the server answers
  that with a live membership lookup the client has no equivalent of.
  Consequently the organization screen's **Invite member**, **Change role** and
  **Remove member** controls are shown to every viewer. No security consequence
  (the API refuses), but the UI promises what it cannot deliver.
- **The wildcard matching rule lives in five hand-kept copies.** Four in C#
  (`PermissionRequirementHandler`, `PermissionChecker`, `PermissionCode.Matches`,
  and `Auth.Sdk/Authorization`) and the port in
  `packages/auth/src/permission-matching.ts`. Each is unit-tested against its own
  expectations; nothing tests them against each other, and the SDK copy differs
  from the other three. Codes are now guarded; the rule that interprets them is
  not.
- **A permission still lives in three places.** `PermissionCodes.cs`, the SQL
  seed, and this map. Each *pair* is bound by a test, so none can drift from
  another silently — but adding a permission is still three edits, and the suite
  tells you which one you forgot rather than doing it for you.
- **Signed-out arrival at `/` still costs more than `/login`.** The router
  resolves a matched route's module before rendering, and `RequireAuth` is a
  render-time element, so typing the bare origin loads the shell and dashboard
  before redirecting. Lazy dashboard tabs cut this by ~412 kB; closing the rest
  means mounting an anonymous router until the session is known, which touches
  the `returnTo` contract and is a decision, not a tidy-up. Budgets are recorded
  and ratcheted in `e2e/isolated/login-payload.spec.ts`.
- **One palette pair misses WCAG AA contrast by 0.16.** `--muted-foreground` on
  `--muted` measures 4.34:1 in the light theme against the required 4.5:1. Both
  values come from the preset and the pair is used by a dozen shipped components,
  so correcting it is the design system owner's call. It is excluded in the axe
  run by the exact colour pair, not by selector, so it stops matching by itself
  once the token is darkened.
- **About two-thirds of WCAG AA is outside automation** — tab order, focus
  destination after a dialog closes, whether an error reads sensibly aloud, and
  meaning carried by colour alone. These are recorded as open, deliberately, not
  implied as passing.

## Working in this repo

- **The shadcn CLI does not work here.** `shadcn add` fails at the workspace root
  with `Could not resolve the following aliases: components, ui, lib`, because
  `components.json` maps aliases to `@authsystem/ui`, which the CLI cannot map to
  a filesystem path. Add a component by writing the canonical upstream source into
  `packages/ui/src/<name>.tsx` by hand, adapted to house conventions (`cn` from
  `@authsystem/ui/utils`, `data-slot` attributes, `cva` variants) — mirror an
  existing sibling such as `badge.tsx`. No `package.json` exports entry is needed.
- **All styling comes from the preset.** No custom colours, themes, or CSS
  overrides. Spacing is owned by the component (`FieldGroup` owns the gap between
  fields, `Field` owns label↔control) — never `space-y-*` or per-usage `gap-*` on
  a form. The only visual decision left to you is picking the right control.
- **Any new user-visible string needs all 7 locales**, or `pnpm typecheck` fails.
