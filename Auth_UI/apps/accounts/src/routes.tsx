import { createBrowserRouter, Navigate } from "react-router-dom"

import { RequireAnonymous, RequireAuth } from "@astoom/auth/require-auth"
import { AcceptInvitationPage } from "@astoom/auth/pages/accept-invitation"
import { ForcePasswordChangePage } from "@astoom/auth/pages/force-password-change"
import { ForgotPasswordPage } from "@astoom/auth/pages/forgot-password"
import { ResetPasswordPage } from "@astoom/auth/pages/reset-password"
import { VerifyEmailPage } from "@astoom/auth/pages/verify-email-page"
import { crumb } from "@astoom/ui/crumbs"
import { NotFoundPage } from "@astoom/ui/error-pages/not-found"

import { OrganizationDetailPage } from "@astoom/account/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@astoom/account/pages/organizations/organizations-page"
import { ProfilePage } from "@astoom/account/pages/profile/profile-page"
import { AccountShell } from "@/components/account-shell"
import { AccountRecoveryPage } from "@/pages/account-recovery"
import { AccountsLoginPage } from "@/pages/auth/login"
import { DeleteAccountPage } from "@/pages/delete-account"
import { DeletionScheduledPage } from "@/pages/deletion-scheduled"
import { RegisterPage } from "@/pages/auth/register"
import { AccountsTwoFactorPage } from "@/pages/auth/two-factor"

export const router = createBrowserRouter([
  {
    element: <RequireAnonymous />,
    children: [
      { path: "/login", element: <AccountsLoginPage /> },
      { path: "/register", element: <RegisterPage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/reset-password", element: <ResetPasswordPage /> },
      // Public no-login deletion wizard (compliance surface).
      { path: "/delete-account", element: <DeleteAccountPage /> },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      { path: "/force-password-change", element: <ForcePasswordChangePage /> },
      {
        element: <AccountShell />,
        children: [
          { index: true, element: <Navigate to="/profile" replace /> },
          {
            path: "profile",
            element: <ProfilePage showDangerZone />,
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
        ],
      },
    ],
  },
  // Top-level on purpose: the user holds a 2FA challenge but no tokens yet,
  // so the page belongs under neither RequireAnonymous nor RequireAuth.
  { path: "/two-factor", element: <AccountsTwoFactorPage /> },
  // Top-level on purpose: reached right after registration (no session yet) and
  // from the login page for an unconfirmed email; verifying signs the user in,
  // so it belongs under neither RequireAnonymous nor RequireAuth.
  { path: "/verify-email", element: <VerifyEmailPage /> },
  // Top-level on purpose: the page serves both anonymous invitees (register /
  // sign-in-to-accept) and already-authenticated users (one-click accept), so
  // it must live under neither RequireAnonymous nor RequireAuth.
  { path: "/accept-invitation", element: <AcceptInvitationPage /> },
  // Top-level on purpose: the user arrives unauthenticated but recovering
  // signs them in, so it belongs under neither RequireAnonymous nor RequireAuth.
  { path: "/account-recovery", element: <AccountRecoveryPage /> },
  // Top-level on purpose: shown right after the session is revoked by a
  // deletion request; it must render while fully signed out.
  { path: "/deletion-scheduled", element: <DeletionScheduledPage /> },
  { path: "*", element: <NotFoundPage /> },
])
