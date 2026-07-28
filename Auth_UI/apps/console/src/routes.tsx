import * as React from "react"
import { createBrowserRouter, Navigate, useParams } from "react-router-dom"

import { ProfilePage } from "@astoom/account/pages/profile/profile-page"
import { ACCOUNTS_URL } from "@astoom/api/env"
import { RequireAnonymous, RequireAuth } from "@astoom/auth/require-auth"
import { PermissionRoute } from "@astoom/auth/require-permission"
import { ForcePasswordChangePage } from "@astoom/auth/pages/force-password-change"
import { ForgotPasswordPage } from "@astoom/auth/pages/forgot-password"
import { LoginPage } from "@astoom/auth/pages/login"
import { TwoFactorVerifyPage } from "@astoom/auth/pages/two-factor-verify"
import { VerifyEmailPage } from "@astoom/auth/pages/verify-email-page"
import { crumb } from "@astoom/ui/crumbs"
import { ForbiddenPage } from "@astoom/ui/error-pages/forbidden"
import { NotFoundPage } from "@astoom/ui/error-pages/not-found"
import { AppShell } from "@/components/layout/app-shell"
import { PERMISSIONS } from "@/lib/constants"
import { ApiKeysPage } from "@/pages/api-keys/api-keys-page"
import { ApplicationDetailPage } from "@/pages/applications/application-detail-page"
import { ApplicationsPage } from "@/pages/applications/applications-page"
import { AuditLogsPage } from "@/pages/audit-logs/audit-logs-page"
import { DashboardPage } from "@/pages/dashboard/dashboard-page"
import {
  ConsoleOrganizationDetailPage,
  ConsoleOrganizationsPage,
} from "@/pages/organizations/organizations-page"
import { NotificationLayoutDetailPage } from "@/pages/notifications/notification-layout-detail-page"
import { NotificationLayoutsPage } from "@/pages/notifications/notification-layouts-page"
import { NotificationOutboxPage } from "@/pages/notifications/notification-outbox-page"
import { NotificationPolicyDetailPage } from "@/pages/notifications/notification-policy-detail-page"
import { NotificationPolicyPage } from "@/pages/notifications/notification-policy-page"
import { NotificationsOverviewPage } from "@/pages/notifications/notifications-overview-page"
import { NotificationTemplateDetailPage } from "@/pages/notifications/notification-template-detail-page"
import { NotificationTemplatesPage } from "@/pages/notifications/notification-templates-page"
import { PermissionDetailPage } from "@/pages/permissions/permission-detail-page"
import { PermissionsPage } from "@/pages/permissions/permissions-page"
import { PlatformSettingsPage } from "@/pages/platform-settings/platform-settings-page"
import { RoleDetailPage } from "@/pages/roles/role-detail-page"
import { RolesPage } from "@/pages/roles/roles-page"
import { SecretsPage } from "@/pages/secrets/secrets-page"
import { UserDetailPage } from "@/pages/users/user-detail-page"
import { UsersPage } from "@/pages/users/users-page"
import { WebhookKeysPage } from "@/pages/webhook-keys/webhook-keys-page"

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
    element: <RequireAnonymous />,
    children: [
      { path: "/login", element: <LoginPage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/reset-password", element: <ResetPasswordRedirect /> },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      { path: "/force-password-change", element: <ForcePasswordChangePage /> },
      {
        element: <AppShell />,
        children: [
          {
            index: true,
            element: <DashboardPage />,
            handle: crumb("dashboard", "/"),
          },
          {
            element: <PermissionRoute permission={PERMISSIONS.users.read} />,
            children: [
              {
                path: "users",
                element: <UsersPage />,
                handle: crumb("users", "/users"),
              },
              {
                path: "users/:id",
                element: <UserDetailPage />,
                handle: crumb("users", "/users", true),
              },
            ],
          },
          {
            element: <PermissionRoute permission={PERMISSIONS.roles.read} />,
            children: [
              {
                path: "roles",
                element: <RolesPage />,
                handle: crumb("roles", "/roles"),
              },
              {
                path: "roles/:id",
                element: <RoleDetailPage />,
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
                element: <PermissionsPage />,
                handle: crumb("permissions", "/permissions"),
              },
              {
                path: "permissions/:id",
                element: <PermissionDetailPage />,
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
                element: <ApplicationsPage />,
                handle: crumb("applications", "/applications"),
              },
              {
                path: "applications/:id",
                element: <ApplicationDetailPage />,
                handle: crumb("applications", "/applications", true),
              },
            ],
          },
          // Platform admins (organizations:read) manage ALL organizations;
          // everyone else gets the membership-scoped self-service list.
          {
            path: "organizations",
            element: <ConsoleOrganizationsPage />,
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            element: <ConsoleOrganizationDetailPage />,
            handle: crumb("organizations", "/organizations", true),
          },
          {
            path: "profile",
            element: <ProfilePage />,
            handle: crumb("profile", "/profile"),
          },
          {
            element: <PermissionRoute permission={PERMISSIONS.apiKeys.read} />,
            children: [
              {
                path: "api-keys",
                element: <ApiKeysPage />,
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
                element: <WebhookKeysPage />,
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
                element: <AuditLogsPage />,
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
                  { index: true, element: <NotificationsOverviewPage /> },
                  {
                    path: "templates",
                    element: <NotificationTemplatesPage />,
                    handle: crumb(
                      "notificationTemplates",
                      "/notifications/templates"
                    ),
                  },
                  {
                    path: "templates/:id",
                    element: <NotificationTemplateDetailPage />,
                    handle: crumb(
                      "notificationTemplates",
                      "/notifications/templates",
                      true
                    ),
                  },
                  {
                    path: "layouts",
                    element: <NotificationLayoutsPage />,
                    handle: crumb(
                      "notificationLayouts",
                      "/notifications/layouts"
                    ),
                  },
                  {
                    path: "layouts/:id",
                    element: <NotificationLayoutDetailPage />,
                    handle: crumb(
                      "notificationLayouts",
                      "/notifications/layouts",
                      true
                    ),
                  },
                  {
                    path: "outbox",
                    element: <NotificationOutboxPage />,
                    handle: crumb(
                      "notificationOutbox",
                      "/notifications/outbox"
                    ),
                  },
                  {
                    path: "policy",
                    element: <NotificationPolicyPage />,
                    handle: crumb(
                      "notificationPolicy",
                      "/notifications/policy"
                    ),
                  },
                  {
                    path: "policy/:version",
                    element: <NotificationPolicyDetailPage />,
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
                element: <SecretsPage />,
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
                element: <PlatformSettingsPage />,
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
  { path: "/two-factor", element: <TwoFactorVerifyPage /> },
  // Top-level on purpose: shared with accounts; an unconfirmed-email sign-in
  // lands here, and verifying completes the session.
  { path: "/verify-email", element: <VerifyEmailPage /> },
  { path: "/accept-invitation", element: <AcceptInvitationRedirect /> },
  { path: "/403", element: <ForbiddenPage /> },
  { path: "*", element: <NotFoundPage /> },
])
