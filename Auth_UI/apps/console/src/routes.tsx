import * as React from "react"
import { createBrowserRouter } from "react-router-dom"

import { OrganizationDetailPage } from "@astoom/account/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@astoom/account/pages/organizations/organizations-page"
import { ProfilePage } from "@astoom/account/pages/profile/profile-page"
import { ACCOUNTS_URL } from "@astoom/api/env"
import { RequireAnonymous, RequireAuth } from "@astoom/auth/require-auth"
import { PermissionRoute } from "@astoom/auth/require-permission"
import { ForcePasswordChangePage } from "@astoom/auth/pages/force-password-change"
import { ForgotPasswordPage } from "@astoom/auth/pages/forgot-password"
import { LoginPage } from "@astoom/auth/pages/login"
import { ResetPasswordPage } from "@astoom/auth/pages/reset-password"
import { TwoFactorVerifyPage } from "@astoom/auth/pages/two-factor-verify"
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
          // Self-service (membership-scoped), like the pre-split console:
          // any authenticated admin manages the organizations they belong to.
          {
            path: "organizations",
            element: <OrganizationsPage />,
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            element: (
              <OrganizationDetailPage
                userHref={(userId) => `/users/${userId}`}
                applicationHref={(applicationId) =>
                  `/applications/${applicationId}`
                }
              />
            ),
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
  { path: "/accept-invitation", element: <AcceptInvitationRedirect /> },
  { path: "/403", element: <ForbiddenPage /> },
  { path: "*", element: <NotFoundPage /> },
])
