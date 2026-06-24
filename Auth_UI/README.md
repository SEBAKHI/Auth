# Auth Console (Auth_UI)

A production admin console for the Auth system, built with **React + Vite +
TypeScript** and **shadcn/ui**. It lets an operator manage users, roles,
permissions, applications, organizations, API/webhook keys, audit logs, and
signing secrets, with a dashboard and data tables.

> The UI is a static SPA that talks to the .NET Auth API (JWT bearer). It is a
> Node/React project — **not** a Visual Studio C# project — and is best worked on
> in VS Code.

## Stack

- **Vite + React 19 + TypeScript** (SPA)
- **shadcn/ui** via the customized preset `b1VlIzU8` (style: `radix-luma`,
  Tailwind v4, lucide icons, IBM Plex Sans, RTL enabled). **All styling comes
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
pnpm install
pnpm gen:api      # regenerate the typed client (requires the API running)
pnpm dev          # http://localhost:5173
```

Configure the API origin via Vite env files:

- `.env.development` → `VITE_API_BASE_URL=http://localhost:5100`
- `.env.production` → the deployed API origin (keep in sync with the CSP in
  `public/web.config`)

## Scripts

| Script | Purpose |
|--------|---------|
| `pnpm dev` | Start the dev server |
| `pnpm build` | Type-check (`tsc -b`) and build to `dist/` |
| `pnpm typecheck` | Type-check only |
| `pnpm gen:api` | Regenerate `src/lib/api/schema.d.ts` from `/openapi/v1.json` |
| `pnpm test` / `pnpm test:coverage` | Unit tests |
| `pnpm e2e` | Playwright e2e (run `pnpm exec playwright install` first) |
| `pnpm lint` / `pnpm format` | Lint / format |

## Architecture

```
src/
  lib/
    api/        typed client (openapi-fetch) + generated schema + auth middleware
    auth/       token store, JWT decode, AuthProvider, route + permission guards
    i18n/       i18next setup, en/ar resources, RTL DirectionProvider
    constants   permission codes + sidebar nav
  components/
    ui/         shadcn components (preset-styled)
    layout/     AppShell (sidebar + header), menus
    common/     PageHeader, DataTable, ConfirmDialog, SecretRevealDialog, …
  pages/        one folder per feature area
  routes.tsx    route tree (public / authenticated / permission-gated)
```

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
- A strict CSP ships in `public/web.config`.

### Internationalization & RTL

English and Arabic are bundled; switching language updates the document `dir`
(`rtl`/`ltr`) and `lang`. Layout uses logical CSS only.

## Deployment (IIS / Plesk)

```bash
pnpm build           # outputs static files to dist/
```

Deploy `dist/` (including `web.config`) as a static site — e.g.
`app.astoom.com`, which is already in the API's production CORS allow-list.
`web.config` provides SPA fallback routing and security headers. Update the CSP
`connect-src` and `VITE_API_BASE_URL` to match your API origin.

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
