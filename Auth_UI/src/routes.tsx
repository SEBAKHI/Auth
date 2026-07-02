import { createBrowserRouter } from "react-router-dom"

import { AppShell } from "@/components/layout/app-shell"
import { RequireAnonymous, RequireAuth } from "@/lib/auth/require-auth"
import { PermissionRoute } from "@/lib/auth/require-permission"
import { PERMISSIONS } from "@/lib/constants"
import { ForcePasswordChangePage } from "@/pages/auth/force-password-change"
import { ForgotPasswordPage } from "@/pages/auth/forgot-password"
import { LoginPage } from "@/pages/auth/login"
import { ResetPasswordPage } from "@/pages/auth/reset-password"
import { TwoFactorNoticePage } from "@/pages/auth/two-factor-notice"
import { ApiKeysPage } from "@/pages/api-keys/api-keys-page"
import { ApplicationsPage } from "@/pages/applications/applications-page"
import { AuditLogsPage } from "@/pages/audit-logs/audit-logs-page"
import { DashboardPage } from "@/pages/dashboard/dashboard-page"
import { ForbiddenPage } from "@/pages/error/forbidden"
import { NotFoundPage } from "@/pages/error/not-found"
import { OrganizationDetailPage } from "@/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@/pages/organizations/organizations-page"
import { PermissionsPage } from "@/pages/permissions/permissions-page"
import { ProfilePage } from "@/pages/profile/profile-page"
import { RolesPage } from "@/pages/roles/roles-page"
import { SecretsPage } from "@/pages/secrets/secrets-page"
import { UsersPage } from "@/pages/users/users-page"
import { WebhookKeysPage } from "@/pages/webhook-keys/webhook-keys-page"

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
          { index: true, element: <DashboardPage /> },
          { path: "profile", element: <ProfilePage /> },
          { path: "organizations", element: <OrganizationsPage /> },
          { path: "organizations/:id", element: <OrganizationDetailPage /> },
          {
            element: <PermissionRoute permission={PERMISSIONS.users.read} />,
            children: [{ path: "users", element: <UsersPage /> }],
          },
          {
            element: <PermissionRoute permission={PERMISSIONS.roles.read} />,
            children: [{ path: "roles", element: <RolesPage /> }],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.permissions.read} />
            ),
            children: [{ path: "permissions", element: <PermissionsPage /> }],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.applications.read} />
            ),
            children: [{ path: "applications", element: <ApplicationsPage /> }],
          },
          {
            element: <PermissionRoute permission={PERMISSIONS.apiKeys.read} />,
            children: [{ path: "api-keys", element: <ApiKeysPage /> }],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.webhookKeys.read} />
            ),
            children: [{ path: "webhook-keys", element: <WebhookKeysPage /> }],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.auditLogs.read} />
            ),
            children: [{ path: "audit-logs", element: <AuditLogsPage /> }],
          },
          {
            element: (
              <PermissionRoute permission={PERMISSIONS.secrets.manage} />
            ),
            children: [{ path: "admin/secrets", element: <SecretsPage /> }],
          },
        ],
      },
    ],
  },
  { path: "/403", element: <ForbiddenPage /> },
  { path: "*", element: <NotFoundPage /> },
])
