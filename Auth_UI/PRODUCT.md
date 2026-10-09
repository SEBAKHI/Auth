# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- **Platform administrators** use the console (`apps/console`): they manage users, roles, permissions, applications, organizations, API and webhook keys, notification templates, system settings and audit logs for every application the platform serves.
- **The adopter** — the organization or person who takes this open-source project and installs it — uses Platform settings once or twice at installation to make both applications carry their own identity (name, logos, favicon, appearance), and rarely afterwards. What matters to them is seeing the result in both light and dark mode with confidence before saving.
- **End users** of the adopter's applications use the accounts app (`apps/accounts`): sign in and sign up (email, Google, Apple), password flows, invitations, profile and organization self-service. Most of them see only its sign-in screens, on the way into another application.

## Product Purpose

AuthSystem is an open-source, enterprise identity platform for multi-application, multi-tenant organizations: authentication (who users are), authorization (what they may do) and audit logging (what they did) for all of an organization's applications from one place, with single sign-on across them. It succeeds when an adopter can run it as their own identity provider, under their own brand, without forking the code.

## Positioning

One self-hosted identity provider whose two web applications are entirely re-brandable from the console — name, light and dark logos per platform and per application, and an appearance chosen from shadcn/ui's own colour registry — so an adopter ships it as theirs without touching code.

## Operating Context

- Two web applications from one pnpm workspace (React 19, Vite, TypeScript, Tailwind 4, shadcn/ui radix-luma, TanStack Query), over a .NET 10 REST API.
- The console holds everything an administrator does; it never sends an administrator to the accounts app.
- Seven languages — English, Arabic, Turkish, French, Chinese, Urdu, Persian — with right-to-left layouts for Arabic, Urdu and Persian.
- Light and dark mode in both apps, chosen per person.
- Deployed on IIS behind a strict Content-Security-Policy (no inline scripts).

## Capabilities and Constraints

- Every console screen is permission-gated; the API is the authority.
- Appearance (base colour, theme, chart colour, radius, menu accent) is chosen from shadcn/ui's registry, or as one custom colour per mode; the API stores names and `#rrggbb` codes only, never CSS.
- Logos are never cropped by the system; the adopter's image is shown as uploaded.

## Brand Commitments

- **The visual system is shadcn/ui, used as shipped.** All styling comes from the shadcn preset and its tokens; no custom colours, CSS or restyling in code. The only freedom in code is choosing the right shadcn control for each situation. Administrators choose colours at runtime from shadcn's registry; that is data, not code.
- Logos are always shown in circles, fitted inside, never cropped.
- Colour names stay as shadcn writes them (Neutral, Amber, …), in every language.

## Evidence on Hand

- No customers, testimonials or benchmarks are recorded in this repository; none may be invented.

## Product Principles

1. The adopter's identity, not ours: everything visible is theirs to set from the console.
2. See it before it ships: a change that every visitor will see is previewed by the administrator first.
3. Earned familiarity: standard shadcn controls, used the way shadcn intends, so an administrator never pauses at an unfamiliar widget.
4. Every language is first-class, right-to-left included.

## Accessibility & Inclusion

- WCAG 2.2 AA is the target; text contrast of at least 4.5:1 is checked when an administrator picks a custom base colour.
- Right-to-left layouts use logical properties only.
