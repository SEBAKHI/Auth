import * as React from "react"
import { createBrowserRouter, Navigate, useParams } from "react-router-dom"

import { ACCOUNTS_URL } from "@astoom/api/env"
import { RequireAnonymous, RequireAuth } from "@astoom/auth/require-auth"
import { PermissionRoute } from "@astoom/auth/require-permission"
import { LoginPage } from "@astoom/auth/pages/login"
import { crumb } from "@astoom/ui/crumbs"
import { ForbiddenPage } from "@astoom/ui/error-pages/forbidden"
import { NotFoundPage } from "@astoom/ui/error-pages/not-found"
import { lazyRoute, RouteFallback } from "@astoom/ui/lazy-route"
import { AppShell } from "@/components/layout/app-shell"
import { PERMISSIONS } from "@/lib/constants"

/**
 * Every page is loaded on demand.
 *
 * All 33 were statically imported before, so the login screen downloaded recharts
 * (dashboard), CodeMirror (notification editors), react-day-picker (audit filters)
 * and qrcode (2FA) as part of one 2.5 MB chunk. Login, the shell and the guards stay
 * eager because they are on the path to every route.
 *
 * See `lazyRoute` for why this uses the router's own `lazy` rather than
 * `React.lazy` + `Suspense`.
 */

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

export const router = createBrowserRouter([
  {
    // Pathless root purely to own the hydrate fallback, so a cold load of any lazy
    // route shows a spinner instead of nothing. Matching is unaffected.
    HydrateFallback: RouteFallback,
    children: [
  {
    element: <RequireAnonymous />,
    children: [
      { path: "/login", element: <LoginPage /> },
      {
        path: "/forgot-password",
        lazy: lazyRoute(
          () => import("@astoom/auth/pages/forgot-password"),
          (m) => m.ForgotPasswordPage
        ),
      },
      { path: "/reset-password", element: <ResetPasswordRedirect /> },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      {
        path: "/force-password-change",
        lazy: lazyRoute(
          () => import("@astoom/auth/pages/force-password-change"),
          (m) => m.ForcePasswordChangePage
        ),
      },
      {
        element: <AppShell />,
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
            element: <PermissionRoute permission={PERMISSIONS.users.read} />,
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
            element: <PermissionRoute permission={PERMISSIONS.roles.read} />,
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
                  () => import("@/pages/permissions/permission-detail-page"),
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
                  () => import("@/pages/applications/application-detail-page"),
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
              () => import("@astoom/account/pages/profile/profile-page"),
              (m) => m.ProfilePage
            ),
            handle: crumb("profile", "/profile"),
          },
          {
            element: <PermissionRoute permission={PERMISSIONS.apiKeys.read} />,
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
          {
            element: (
              <PermissionRoute
                permission={PERMISSIONS.notificationTemplates.read}
              />
            ),
            children: [
              // Nested on purpose: the section owns a URL of its own (every
              // other section has one), and the parent `handle` is what puts a
              // clickable "Notifications" crumb ahead of each sub-section.
              {
                path: "notifications",
                handle: crumb("notifications", "/notifications"),
                children: [
                  {
                    index: true,
                    lazy: lazyRoute(
                      () =>
                        import("@/pages/notifications/notifications-overview-page"),
                      (m) => m.NotificationsOverviewPage
                    ),
                  },
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
                        import(
                          "@/pages/notifications/notification-template-detail-page"
                        ),
                      (m) => m.NotificationTemplateDetailPage
                    ),
                    handle: crumb(
                      "notificationTemplates",
                      "/notifications/templates",
                      true
                    ),
                  },
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
                        import(
                          "@/pages/notifications/notification-layout-detail-page"
                        ),
                      (m) => m.NotificationLayoutDetailPage
                    ),
                    handle: crumb(
                      "notificationLayouts",
                      "/notifications/layouts",
                      true
                    ),
                  },
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
                    // Keyed by the revision's id, not its version string: the
                    // string is editable, so a URL built on it dies on rename.
                    path: "policy/:id",
                    lazy: lazyRoute(
                      () =>
                        import(
                          "@/pages/notifications/notification-policy-detail-page"
                        ),
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
              // The section used to live at these flat paths; keep bookmarks and
              // any link already sent out working, ids included.
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
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.secrets.manage} />
            ),
            children: [
              {
                path: "admin/secrets",
                lazy: lazyRoute(
                  () => import("@/pages/secrets/secrets-page"),
                  (m) => m.SecretsPage
                ),
                handle: crumb("secrets", "/admin/secrets"),
              },
            ],
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
                handle: crumb("platformSettings", "/admin/platform-settings"),
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
      () => import("@astoom/auth/pages/two-factor-verify"),
      (m) => m.TwoFactorVerifyPage
    ),
  },
  // Top-level on purpose: shared with accounts; an unconfirmed-email sign-in
  // lands here, and verifying completes the session.
  {
    path: "/verify-email",
    lazy: lazyRoute(
      () => import("@astoom/auth/pages/verify-email-page"),
      (m) => m.VerifyEmailPage
    ),
  },
  { path: "/accept-invitation", element: <AcceptInvitationRedirect /> },
  { path: "/403", element: <ForbiddenPage /> },
  { path: "*", element: <NotFoundPage /> },
    ],
  },
])
