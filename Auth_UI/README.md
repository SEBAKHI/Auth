# Auth UI workspace (Auth_UI)

A pnpm workspace hosting the frontend apps of the Auth system, built with
**React + Vite + TypeScript** and **shadcn/ui**:

- `apps/console` — the admin console (`console.astoom.com`): users, roles,
  permissions, applications, API/webhook keys, audit logs, signing secrets,
  dashboard.
- `apps/accounts` — the end-user self-service app (`accounts.astoom.com`):
  sign in/up (email + Google), password flows, invitations, profile,
  organization self-service. Google Identity Services needs
  `VITE_GOOGLE_CLIENT_ID` and the GSI origins in its CSP (accounts only).
- `packages/api` — typed API client (openapi-fetch + generated schema), token
  store/JWT, upload helpers, error normalization, query client.
- `packages/auth` — AuthProvider, route/permission guards.
- `packages/i18n` — i18next setup, the 7 locales, RTL DirectionProvider,
  timezone display helpers.
- `packages/ui` — shadcn primitives, shared widgets (`common/`), hooks,
  formatting utils, theme + branding providers.

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
- **react-hook-form + zod** (forms), **react-i18next** (English/Arabic + RTL)
- **Vitest + Testing Library** (unit) and **Playwright** (e2e)

## Prerequisites

- Node.js 20+ and **pnpm** (`corepack enable` or `npm i -g pnpm`)
- The Auth API running locally (default `http://localhost:5100`)

## Getting started

```bash
pnpm install      # once, at the workspace root
pnpm gen:api      # regenerate the typed client (requires the API running)
pnpm dev          # console app on http://localhost:5173
```

Configure the API origin via each app's Vite env files:

- `apps/console/.env.development` → `VITE_API_BASE_URL=http://localhost:5100`
- `apps/console/.env.production` → the deployed API origin (keep in sync with
  the CSP in `apps/console/public/web.config`)

## Scripts (run at the workspace root)

| Script | Purpose |
|--------|---------|
| `pnpm dev` | Start the console dev server (5173) |
| `pnpm dev:accounts` | Start the accounts dev server (5174) |
| `pnpm build` | Type-check (`tsc -b`) and build every app to its `dist/` |
| `pnpm typecheck` | Type-check only |
| `pnpm gen:api` | Regenerate `packages/api/src/schema.d.ts` from `/openapi/v1.json` |
| `pnpm test` / `pnpm test:coverage` | Unit tests (all apps + packages) |
| `pnpm e2e` | Playwright e2e (run `pnpm exec playwright install` first) |
| `pnpm lint` / `pnpm format` | Lint / format |

## Architecture

```
apps/
  console/
    src/
      lib/        console-only constants (permissions + nav) and breadcrumbs
      components/ layout (AppShell, sidebar) + data-table system
      pages/      one folder per feature area
      routes.tsx  route tree (public / authenticated / permission-gated)
packages/
  api/    typed client (openapi-fetch) + generated schema + auth middleware,
          token store, JWT decode, uploads, error helpers, query client
  auth/   AuthProvider, RequireAuth/RequireAnonymous, PermissionRoute
  i18n/   i18next setup, locales (en/ar/tr/fr/zh/ur/fa), RTL DirectionProvider,
          timezone helpers
  ui/     shadcn components (preset-styled), shared widgets (common/), hooks,
          format utils, ThemeProvider, BrandingProvider
```

Workspace packages are consumed as `@astoom/api`, `@astoom/auth`,
`@astoom/i18n`, and `@astoom/ui` — resolved from source via tsconfig paths +
Vite aliases (no per-package build step).

### Auth & security

- Login returns tokens in the response body. The **access token is kept in
  memory**; the **refresh token in `localStorage`**. The API client middleware
  attaches the bearer token, proactively refreshes an expired token, and ends the
  session if refresh fails.
- **RBAC**: navigation, routes, and actions are gated by the `permissions` claim,
  mirroring the API's `[RequirePermission]`. The API remains the source of truth
  (403s are handled gracefully).
- Generated secret material (API/webhook keys, generated PEM/token, 2FA recovery
  codes) is shown **once** in a copy dialog and never persisted in app state.
- A strict CSP ships in `apps/console/public/web.config`.

### Internationalization & RTL

English and Arabic are bundled; switching language updates the document `dir`
(`rtl`/`ltr`) and `lang`. Layout uses logical CSS only.

## Deployment (IIS / Plesk)

```bash
pnpm build           # outputs static files to apps/<app>/dist/
```

Deploy each app's `dist/` (including its `web.config`) as its own static site:
`apps/console/dist/` → `console.astoom.com` and `apps/accounts/dist/` →
`accounts.astoom.com`; both origins are in the API's production CORS
allow-list. `web.config` provides SPA fallback routing and security headers.
Update the CSP `connect-src` and `VITE_API_BASE_URL` to match your API origin.
Invitation/reset emails link to the accounts origin (`Email:FrontendBaseUrl`).

## Known constraints

- **2FA at login**: the API exposes 2FA only for setup/enable/disable; there is
  no separate login-time verification endpoint. When login returns
  `requiresTwoFactor`, the issued session is used and a notice is shown. Manage
  2FA from **Profile → Security**.
- **Role permissions**: the API allows setting a role's permissions only at
  creation; there are no add/remove-permission-on-role endpoints, so the roles
  screen shows permission counts read-only.
- The main bundle is a single chunk; route-level code-splitting is a possible
  future optimization.
