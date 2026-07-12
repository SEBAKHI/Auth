import { createBrowserRouter, Navigate } from "react-router-dom"

import { RequireAnonymous, RequireAuth } from "@astoom/auth/require-auth"
import { AcceptInvitationPage } from "@astoom/auth/pages/accept-invitation"
import { ForcePasswordChangePage } from "@astoom/auth/pages/force-password-change"
import { ForgotPasswordPage } from "@astoom/auth/pages/forgot-password"
import { LoginPage } from "@astoom/auth/pages/login"
import { ResetPasswordPage } from "@astoom/auth/pages/reset-password"
import { TwoFactorNoticePage } from "@astoom/auth/pages/two-factor-notice"
import { NotFoundPage } from "@astoom/ui/error-pages/not-found"

import { AccountShell } from "@/components/account-shell"
import { LoginFooter } from "@/pages/auth/login-footer"
import { GoogleSignIn } from "@/components/google-sign-in"
import { RegisterPage } from "@/pages/auth/register"
import { OrganizationDetailPage } from "@/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@/pages/organizations/organizations-page"
import { ProfilePage } from "@/pages/profile/profile-page"

export const router = createBrowserRouter([
  {
    element: <RequireAnonymous />,
    children: [
      {
        path: "/login",
        element: (
          <LoginPage providers={<GoogleSignIn />} footer={<LoginFooter />} />
        ),
      },
      { path: "/register", element: <RegisterPage /> },
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
        element: <AccountShell />,
        children: [
          { index: true, element: <Navigate to="/profile" replace /> },
          { path: "profile", element: <ProfilePage /> },
          { path: "organizations", element: <OrganizationsPage /> },
          { path: "organizations/:id", element: <OrganizationDetailPage /> },
        ],
      },
    ],
  },
  // Top-level on purpose: the page serves both anonymous invitees (register /
  // sign-in-to-accept) and already-authenticated users (one-click accept), so
  // it must live under neither RequireAnonymous nor RequireAuth.
  { path: "/accept-invitation", element: <AcceptInvitationPage /> },
  { path: "*", element: <NotFoundPage /> },
])
