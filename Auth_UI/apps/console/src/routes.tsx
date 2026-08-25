import * as React from "react"
import {
  createBrowserRouter,
  Navigate,
  Outlet,
  useParams,
} from "react-router-dom"

import { ACCOUNTS_URL } from "@authsystem/api/env"
import { RequireAnonymous, RequireAuth } from "@authsystem/auth/require-auth"
import { useAuth } from "@authsystem/auth/auth-context"
import { PermissionRoute } from "@authsystem/auth/require-permission"
import { ExternalProviders } from "@authsystem/auth/external/external-providers"
import { LoginPage } from "@authsystem/auth/pages/login"
import { crumb } from "@authsystem/ui/crumbs"
import { ForbiddenPage } from "@authsystem/ui/error-pages/forbidden"
import { NotFoundPage } from "@authsystem/ui/error-pages/not-found"
import { RouteErrorBoundary } from "@authsystem/ui/error-pages/route-error"
import { lazyRoute, RouteFallback } from "@authsystem/ui/lazy-route"
import { PERMISSIONS } from "@/lib/constants"
import {
  notificationDestination,
  notificationLandingPath,
  type NotificationDestinationId,
} from "@/lib/notification-destinations"

/**
 * Every page is loaded on demand.
 *
 * All 33 were statically imported before, so the login screen downloaded recharts
 * (dashboard), CodeMirror (notification editors), react-day-picker (audit filters)
 * and qrcode (2FA) as part of one 2.5 MB chunk. Login and the guards stay eager
 * because they are on the path to every route; the shell no longer is.
 *
 * Note what lazy routes do NOT buy. The router resolves every matched route's
 * `lazy` before it renders anything, and RequireAuth is a render-time element -
 * so a signed-out visitor who types the bare origin still downloads the shell,
 * the dashboard and recharts, and only then gets redirected to /login. Landing
 * on /login directly is the only path this splitting actually keeps light.
 * `login-payload.spec.ts` measures all three so the difference stays visible.
 *
 * See `lazyRoute` for why this uses the router's own `lazy` rather than
 * `React.lazy` + `Suspense`.
 */

/**
 * The accounts app owns account recovery; this app hands off to it.
 *
 * A module constant, never derived from the URL or from router state: an
 * absolute value reaches `window.location.assign`, so anything user-controlled
 * flowing in here would be an open redirect. console-login-surface.test.ts
 * pins that it stays built from ACCOUNTS_URL.
 */
const CONSOLE_RECOVERY_URL = `${ACCOUNTS_URL}/account-recovery`

/**
 * Invitations are an end-user flow owned by the accounts app. Links in old
 * emails may still point here, so forward them (token and all) instead of 404.
 */
function AcceptInvitationRedirect() {
  React.useEffect(() => {
    window.location.replace(
      `${ACCOUNTS_URL}/accept-invitation${window.location.search}`
    )
  }, [])
  return null
}

/**
 * Reset emails are built from a single configured origin (the accounts app), so
 * a link never points here. Forward anything that still does - an old email, a
 * bookmark - instead of 404ing, carrying the token across.
 */
function ResetPasswordRedirect() {
  React.useEffect(() => {
    window.location.replace(
      `${ACCOUNTS_URL}/reset-password${window.location.search}`
    )
  }, [])
  return null
}

/** Carries the record id across the old flat notification paths. */
function LegacyNotificationRedirect({
  section,
}: {
  section: "templates" | "layouts"
}) {
  const { id } = useParams()
  return <Navigate to={`/notifications/${section}/${id}`} replace />
}

function NotificationPermissionRoute({
  destination,
}: {
  destination: NotificationDestinationId
}) {
  return (
    <PermissionRoute
      permission={notificationDestination(destination).permission}
    />
  )
}

/** Exact /notifications entry: overview, policy redirect, or forbidden. */
function NotificationIndexRoute() {
  const { hasPermission } = useAuth()
  const landing = notificationLandingPath(hasPermission)
  if (!landing) return <Navigate to="/403" replace />
  if (landing !== "/notifications") return <Navigate to={landing} replace />
  return <Outlet />
}

export const router = createBrowserRouter([
  {
    // Pathless root purely to own the hydrate fallback, so a cold load of any lazy
    // route shows a spinner instead of nothing. Matching is unaffected.
    HydrateFallback: RouteFallback,
    // Every descendant bubbles its errors here. Without it a chunk that a deploy
    // removed took the whole tab blank, silently.
    errorElement: <RouteErrorBoundary />,
    children: [
  {
    element: <RequireAnonymous />,
    children: [
      {
        // An administrator whose account was created by signing in with Google has
        // no password to type here, so without this slot the console was closed to
        // them entirely.
        //
        // `recoveryPath` is the accounts origin, not a route here, and that is the
        // decision rather than an accident. This app has no /account-recovery and
        // should not grow one: account lifecycle is the accounts app's job, and the
        // recovery screen needs a provider button, which means yet another surface
        // holding a sign-in. Until this line existed both doors were dead — the
        // Google branch is guarded on `recoveryPath &&`, so it short-circuited into
        // a toast that named the deletion and offered nothing, while the password
        // branch navigated to a route this router does not have and rendered the
        // catch-all 404.
        //
        // Crossing origins drops the captured credential, which used to be the
        // argument against doing this. It no longer is: the recovery screen now
        // obtains its own credential, so arriving empty-handed costs a click.
        path: "/login",
        element: (
          <LoginPage
            recoveryPath={CONSOLE_RECOVERY_URL}
                providers={
                  <ExternalProviders recoveryPath={CONSOLE_RECOVERY_URL} />
                }
          />
        ),
      },
      {
        path: "/forgot-password",
        lazy: lazyRoute(
          () => import("@authsystem/auth/pages/forgot-password"),
          (m) => m.ForgotPasswordPage
        ),
      },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      {
        path: "/force-password-change",
        lazy: lazyRoute(
          () => import("@authsystem/auth/pages/force-password-change"),
          (m) => m.ForcePasswordChangePage
        ),
      },
      {
        // Lazy, like every page under it. The shell carries the sidebar, the
        // command palette and the menus that go with them, and none of that is
        // reachable without a session - so loading it eagerly billed the
        // sign-in screen for an interface its reader has not got to yet.
        lazy: lazyRoute(
          () => import("@/components/layout/app-shell"),
          (m) => m.AppShell
        ),
        children: [
          {
            index: true,
            lazy: lazyRoute(
              () => import("@/pages/dashboard/dashboard-page"),
              (m) => m.DashboardPage
            ),
            handle: crumb("dashboard", "/"),
          },
          {
                element: (
                  <PermissionRoute permission={PERMISSIONS.users.read} />
                ),
            children: [
              {
                path: "users",
                lazy: lazyRoute(
                  () => import("@/pages/users/users-page"),
                  (m) => m.UsersPage
                ),
                handle: crumb("users", "/users"),
              },
              {
                path: "users/:id",
                lazy: lazyRoute(
                  () => import("@/pages/users/user-detail-page"),
                  (m) => m.UserDetailPage
                ),
                handle: crumb("users", "/users", true),
              },
            ],
          },
          {
                element: (
                  <PermissionRoute permission={PERMISSIONS.roles.read} />
                ),
            children: [
              {
                path: "roles",
                lazy: lazyRoute(
                  () => import("@/pages/roles/roles-page"),
                  (m) => m.RolesPage
                ),
                handle: crumb("roles", "/roles"),
              },
              {
                path: "roles/:id",
                lazy: lazyRoute(
                  () => import("@/pages/roles/role-detail-page"),
                  (m) => m.RoleDetailPage
                ),
                handle: crumb("roles", "/roles", true),
              },
            ],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.permissions.read} />
            ),
            children: [
              {
                path: "permissions",
                lazy: lazyRoute(
                  () => import("@/pages/permissions/permissions-page"),
                  (m) => m.PermissionsPage
                ),
                handle: crumb("permissions", "/permissions"),
              },
              {
                path: "permissions/:id",
                lazy: lazyRoute(
                      () =>
                        import("@/pages/permissions/permission-detail-page"),
                  (m) => m.PermissionDetailPage
                ),
                handle: crumb("permissions", "/permissions", true),
              },
            ],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.applications.read} />
            ),
            children: [
              {
                path: "applications",
                lazy: lazyRoute(
                  () => import("@/pages/applications/applications-page"),
                  (m) => m.ApplicationsPage
                ),
                handle: crumb("applications", "/applications"),
              },
              {
                path: "applications/:id",
                lazy: lazyRoute(
                      () =>
                        import("@/pages/applications/application-detail-page"),
                  (m) => m.ApplicationDetailPage
                ),
                handle: crumb("applications", "/applications", true),
              },
            ],
          },
          // Platform admins (organizations:read) manage ALL organizations;
          // everyone else gets the membership-scoped self-service list.
          {
            path: "organizations",
            lazy: lazyRoute(
              () => import("@/pages/organizations/organizations-page"),
              (m) => m.ConsoleOrganizationsPage
            ),
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            lazy: lazyRoute(
              () => import("@/pages/organizations/organizations-page"),
              (m) => m.ConsoleOrganizationDetailPage
            ),
            handle: crumb("organizations", "/organizations", true),
          },
          {
            path: "profile",
            lazy: lazyRoute(
                  () =>
                    import("@authsystem/account/pages/profile/profile-page"),
              (m) => m.ProfilePage
            ),
            handle: crumb("profile", "/profile"),
          },
          {
                element: (
                  <PermissionRoute permission={PERMISSIONS.apiKeys.read} />
                ),
            children: [
              {
                path: "api-keys",
                lazy: lazyRoute(
                  () => import("@/pages/api-keys/api-keys-page"),
                  (m) => m.ApiKeysPage
                ),
                handle: crumb("apiKeys", "/api-keys"),
              },
            ],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.webhookKeys.read} />
            ),
            children: [
              {
                path: "webhook-keys",
                lazy: lazyRoute(
                  () => import("@/pages/webhook-keys/webhook-keys-page"),
                  (m) => m.WebhookKeysPage
                ),
                handle: crumb("webhookKeys", "/webhook-keys"),
              },
            ],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.auditLogs.read} />
            ),
            children: [
              {
                path: "audit-logs",
                lazy: lazyRoute(
                  () => import("@/pages/audit-logs/audit-logs-page"),
                  (m) => m.AuditLogsPage
                ),
                handle: crumb("auditLogs", "/audit-logs"),
              },
            ],
          },
              // The parent is spatial only: every child owns its own permission.
              // Its crumb remains the stable section entry and resolves role-aware
              // at /notifications through NotificationIndexRoute.
              {
                path: "notifications",
                handle: crumb("notifications", "/notifications"),
                children: [
                  {
                    element: <NotificationIndexRoute />,
                children: [
                  {
                    index: true,
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/notifications/notifications-overview-page"),
                      (m) => m.NotificationsOverviewPage
                    ),
                  },
                    ],
                  },
                  {
                    element: (
                      <NotificationPermissionRoute destination="templates" />
                    ),
                    children: [
                  {
                    path: "templates",
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/notifications/notification-templates-page"),
                      (m) => m.NotificationTemplatesPage
                    ),
                    handle: crumb(
                      "notificationTemplates",
                      "/notifications/templates"
                    ),
                  },
                  {
                    path: "templates/:id",
                    lazy: lazyRoute(
                      () =>
                            import("@/pages/notifications/notification-template-detail-page"),
                      (m) => m.NotificationTemplateDetailPage
                    ),
                    handle: crumb(
                      "notificationTemplates",
                      "/notifications/templates",
                      true
                    ),
                  },
                    ],
                  },
                  {
                    element: (
                      <NotificationPermissionRoute destination="layouts" />
                    ),
                    children: [
                  {
                    path: "layouts",
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/notifications/notification-layouts-page"),
                      (m) => m.NotificationLayoutsPage
                    ),
                    handle: crumb(
                      "notificationLayouts",
                      "/notifications/layouts"
                    ),
                  },
                  {
                    path: "layouts/:id",
                    lazy: lazyRoute(
                      () =>
                            import("@/pages/notifications/notification-layout-detail-page"),
                      (m) => m.NotificationLayoutDetailPage
                    ),
                    handle: crumb(
                      "notificationLayouts",
                      "/notifications/layouts",
                      true
                    ),
                  },
                    ],
                  },
                  {
                    element: (
                      <NotificationPermissionRoute destination="outbox" />
                    ),
                    children: [
                  {
                    path: "outbox",
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/notifications/notification-outbox-page"),
                      (m) => m.NotificationOutboxPage
                    ),
                    handle: crumb(
                      "notificationOutbox",
                      "/notifications/outbox"
                    ),
                  },
                    ],
                  },
                  {
                    element: (
                      <NotificationPermissionRoute destination="policy" />
                    ),
                    children: [
                      {
                        path: "policy",
                        lazy: lazyRoute(
                          () =>
                            import("@/pages/notifications/notification-policy-page"),
                          (m) => m.NotificationPolicyPage
                        ),
                        handle: crumb(
                          "notificationPolicy",
                          "/notifications/policy"
                        ),
                      },
                      {
                        // Keyed by immutable revision id; version labels can change.
                        path: "policy/:id",
                        lazy: lazyRoute(
                          () =>
                            import("@/pages/notifications/notification-policy-detail-page"),
                          (m) => m.NotificationPolicyDetailPage
                        ),
                        handle: crumb(
                          "notificationPolicy",
                          "/notifications/policy",
                          true
                        ),
                      },
                    ],
                  },
                ],
              },
              // Legacy template/layout paths keep their redirect contract and the
              // same template-read authority as their canonical destinations.
              {
                element: (
                  <NotificationPermissionRoute destination="templates" />
                ),
                children: [
              {
                path: "notification-templates",
                element: <Navigate to="/notifications/templates" replace />,
              },
              {
                path: "notification-templates/:id",
                element: <LegacyNotificationRedirect section="templates" />,
              },
              {
                path: "notification-layouts",
                element: <Navigate to="/notifications/layouts" replace />,
              },
              {
                path: "notification-layouts/:id",
                element: <LegacyNotificationRedirect section="layouts" />,
              },
              {
                path: "notification-outbox",
                element: <Navigate to="/notifications/outbox" replace />,
              },
            ],
          },
          // The keys page used to sit at this flat path with its own sidebar
          // entry; keep bookmarks and any link already sent out working.
          {
            path: "admin/secrets",
            element: (
              <Navigate
                to="/admin/system-settings/SecretManagement/keys"
                replace
              />
            ),
          },
          {
            element: (
              <PermissionRoute
                permission={PERMISSIONS.platformSettings.manage}
              />
            ),
            children: [
              {
                path: "admin/platform-settings",
                lazy: lazyRoute(
                  () =>
                    import("@/pages/platform-settings/platform-settings-page"),
                  (m) => m.PlatformSettingsPage
                ),
                    handle: crumb(
                      "platformSettings",
                      "/admin/platform-settings"
                    ),
              },
            ],
          },
          {
            // The parent owns the crumb, so everything below it reads as part
            // of system settings — which is the point of hosting the secret
            // keys page here rather than off the sidebar.
            path: "admin/system-settings",
            handle: crumb("systemSettings", "/admin/system-settings"),
            children: [
              {
                element: (
                  <PermissionRoute
                    permission={PERMISSIONS.systemSettings.manage}
                  />
                ),
                children: [
                  {
                    index: true,
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/system-settings/system-settings-page"),
                      (m) => m.SystemSettingsPage
                    ),
                  },
                  {
                    path: ":sectionKey",
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/system-settings/system-settings-page"),
                      (m) => m.SystemSettingsPage
                    ),
                  },
                ],
              },
              {
                // Gated on `secrets.manage` ALONE. The section card above it
                // describes where secrets live; this page is where their values
                // are set, and the two permissions are independent — requiring
                // both here would lock out a holder of either one.
                element: (
                      <PermissionRoute
                        permission={PERMISSIONS.secrets.manage}
                      />
                ),
                children: [
                  {
                    path: "SecretManagement/keys",
                    lazy: lazyRoute(
                      () => import("@/pages/secrets/secrets-page"),
                      (m) => m.SecretsPage
                    ),
                    handle: crumb(
                      "secretKeys",
                      "/admin/system-settings/SecretManagement/keys"
                    ),
                  },
                ],
              },
              {
                // Gated on `auditlogs:read`, not on system-settings:manage: this
                // page only names what the audit trail records, and someone who
                // may read the trail may read its index. A single static segment
                // beside `:sectionKey`, which react-router ranks below it.
                element: (
                  <PermissionRoute permission={PERMISSIONS.auditLogs.read} />
                ),
                children: [
                  {
                    path: "audit-catalog",
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/system-settings/audit-catalog-page"),
                      (m) => m.AuditCatalogPage
                    ),
                    handle: crumb(
                      "auditCatalog",
                      "/admin/system-settings/audit-catalog"
                    ),
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  },
  // Top-level on purpose: the user holds a 2FA challenge but no tokens yet,
  // so the page belongs under neither RequireAnonymous nor RequireAuth.
  {
    path: "/two-factor",
    lazy: lazyRoute(
      () => import("@authsystem/auth/pages/two-factor-verify"),
      (m) => m.TwoFactorVerifyPage
    ),
  },
  // Top-level on purpose: shared with accounts; an unconfirmed-email sign-in
  // lands here, and verifying completes the session.
  {
    path: "/verify-email",
    lazy: lazyRoute(
      () => import("@authsystem/auth/pages/verify-email-page"),
      (m) => m.VerifyEmailPage
    ),
  },
  { path: "/accept-invitation", element: <AcceptInvitationRedirect /> },
  // Top-level on purpose, like its accounts twin: under RequireAnonymous a
  // signed-in administrator opening an old reset link was redirected to "/" and
  // the token was swallowed before this redirect could forward it.
  { path: "/reset-password", element: <ResetPasswordRedirect /> },
  { path: "/403", element: <ForbiddenPage /> },
  { path: "*", element: <NotFoundPage /> },
    ],
  },
])
