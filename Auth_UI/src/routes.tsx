import { createBrowserRouter } from "react-router-dom"

import { AppShell } from "@/components/layout/app-shell"
import { RequireAnonymous, RequireAuth } from "@/lib/auth/require-auth"
import { PermissionRoute } from "@/lib/auth/require-permission"
import type { CrumbHandle } from "@/lib/breadcrumb"
import { PERMISSIONS } from "@/lib/constants"
import { AcceptInvitationPage } from "@/pages/auth/accept-invitation"
import { ForcePasswordChangePage } from "@/pages/auth/force-password-change"
import { ForgotPasswordPage } from "@/pages/auth/forgot-password"
import { LoginPage } from "@/pages/auth/login"
import { ResetPasswordPage } from "@/pages/auth/reset-password"
import { TwoFactorNoticePage } from "@/pages/auth/two-factor-notice"
import { ApiKeysPage } from "@/pages/api-keys/api-keys-page"
import { ApplicationDetailPage } from "@/pages/applications/application-detail-page"
import { ApplicationsPage } from "@/pages/applications/applications-page"
import { AuditLogsPage } from "@/pages/audit-logs/audit-logs-page"
import { DashboardPage } from "@/pages/dashboard/dashboard-page"
import { ForbiddenPage } from "@/pages/error/forbidden"
import { NotFoundPage } from "@/pages/error/not-found"
import { OrganizationDetailPage } from "@/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@/pages/organizations/organizations-page"
import { PermissionDetailPage } from "@/pages/permissions/permission-detail-page"
import { PermissionsPage } from "@/pages/permissions/permissions-page"
import { PlatformSettingsPage } from "@/pages/platform-settings/platform-settings-page"
import { ProfilePage } from "@/pages/profile/profile-page"
import { RoleDetailPage } from "@/pages/roles/role-detail-page"
import { RolesPage } from "@/pages/roles/roles-page"
import { SecretsPage } from "@/pages/secrets/secrets-page"
import { UserDetailPage } from "@/pages/users/user-detail-page"
import { UsersPage } from "@/pages/users/users-page"
import { WebhookKeysPage } from "@/pages/webhook-keys/webhook-keys-page"

/** Breadcrumb metadata: list pages label themselves, `:id` pages add a record crumb. */
function crumb(titleKey: string, href: string, detail = false): CrumbHandle {
  return { crumb: { titleKey, href, detail } }
}

export const router = createBrowserRouter([
  {
    element: <RequireAnonymous />,
    children: [
      { path: "/login", element: <LoginPage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/reset-password", element: <ResetPasswordPage /> },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      { path: "/force-password-change", element: <ForcePasswordChangePage /> },
      { path: "/two-factor", element: <TwoFactorNoticePage /> },
      {
        element: <AppShell />,
        children: [
          {
            index: true,
            element: <DashboardPage />,
            handle: crumb("dashboard", "/"),
          },
          {
            path: "profile",
            element: <ProfilePage />,
            handle: crumb("profile", "/profile"),
          },
          {
            path: "organizations",
            element: <OrganizationsPage />,
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            element: <OrganizationDetailPage />,
            handle: crumb("organizations", "/organizations", true),
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
  // Top-level on purpose: the page serves both anonymous invitees (register /
  // sign-in-to-accept) and already-authenticated users (one-click accept), so
  // it must live under neither RequireAnonymous nor RequireAuth.
  { path: "/accept-invitation", element: <AcceptInvitationPage /> },
  { path: "/403", element: <ForbiddenPage /> },
  { path: "*", element: <NotFoundPage /> },
])
