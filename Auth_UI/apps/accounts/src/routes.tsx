import { createBrowserRouter, Navigate } from "react-router-dom"

import { RequireAnonymous, RequireAuth } from "@astoom/auth/require-auth"
import { crumb } from "@astoom/ui/crumbs"
import { NotFoundPage } from "@astoom/ui/error-pages/not-found"
import { lazyRoute, RouteFallback } from "@astoom/ui/lazy-route"

import { AccountShell } from "@/components/account-shell"
import { AccountsLoginPage } from "@/pages/auth/login"

/**
 * Every page except the login screen and the shell is loaded on demand.
 *
 * The app previously imported all of them statically, so signing in downloaded the
 * organization pages, the deletion wizard and the seven-language privacy policy
 * before rendering a single field. Login stays eager because it is the first thing
 * most visitors see.
 */
export const router = createBrowserRouter([
  {
    // Pathless root purely to own the hydrate fallback, so a cold load of any lazy
    // route shows a spinner instead of nothing. Matching is unaffected.
    HydrateFallback: RouteFallback,
    children: [
  {
    element: <RequireAnonymous />,
    children: [
      { path: "/login", element: <AccountsLoginPage /> },
      {
        path: "/register",
        lazy: lazyRoute(() => import("@/pages/auth/register"), (m) => m.RegisterPage),
      },
      {
        path: "/forgot-password",
        lazy: lazyRoute(
          () => import("@astoom/auth/pages/forgot-password"),
          (m) => m.ForgotPasswordPage
        ),
      },
      {
        path: "/reset-password",
        lazy: lazyRoute(
          () => import("@astoom/auth/pages/reset-password"),
          (m) => m.ResetPasswordPage
        ),
      },
      // Public no-login deletion wizard (compliance surface).
      {
        path: "/delete-account",
        lazy: lazyRoute(
          () => import("@/pages/delete-account"),
          (m) => m.DeleteAccountPage
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
          () => import("@astoom/auth/pages/force-password-change"),
          (m) => m.ForcePasswordChangePage
        ),
      },
      {
        element: <AccountShell />,
        children: [
          { index: true, element: <Navigate to="/profile" replace /> },
          {
            path: "profile",
            lazy: lazyRoute(
              () => import("@astoom/account/pages/profile/profile-page"),
              (m) => () => <m.ProfilePage showDangerZone />
            ),
            handle: crumb("profile", "/profile"),
          },
          {
            path: "organizations",
            lazy: lazyRoute(
              () => import("@astoom/account/pages/organizations/organizations-page"),
              (m) => m.OrganizationsPage
            ),
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            lazy: lazyRoute(
              () =>
                import(
                  "@astoom/account/pages/organizations/organization-detail-page"
                ),
              (m) => m.OrganizationDetailPage
            ),
            handle: crumb("organizations", "/organizations", true),
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
      () => import("@/pages/auth/two-factor"),
      (m) => m.AccountsTwoFactorPage
    ),
  },
  // Top-level on purpose: reached right after registration (no session yet) and
  // from the login page for an unconfirmed email; verifying signs the user in,
  // so it belongs under neither RequireAnonymous nor RequireAuth.
  {
    path: "/verify-email",
    lazy: lazyRoute(
      () => import("@astoom/auth/pages/verify-email-page"),
      (m) => m.VerifyEmailPage
    ),
  },
  // Top-level on purpose: the page serves both anonymous invitees (register /
  // sign-in-to-accept) and already-authenticated users (one-click accept), so
  // it must live under neither RequireAnonymous nor RequireAuth.
  {
    path: "/accept-invitation",
    lazy: lazyRoute(
      () => import("@astoom/auth/pages/accept-invitation"),
      (m) => m.AcceptInvitationPage
    ),
  },
  // Top-level on purpose: the user arrives unauthenticated but recovering
  // signs them in, so it belongs under neither RequireAnonymous nor RequireAuth.
  {
    path: "/account-recovery",
    lazy: lazyRoute(
      () => import("@/pages/account-recovery"),
      (m) => m.AccountRecoveryPage
    ),
  },
  // Top-level on purpose: shown right after the session is revoked by a
  // deletion request; it must render while fully signed out.
  {
    path: "/deletion-scheduled",
    lazy: lazyRoute(
      () => import("@/pages/deletion-scheduled"),
      (m) => m.DeletionScheduledPage
    ),
  },
  // Top-level on purpose: the public compliance surface (KVKK Art. 10
  // disclosure + store-listing data-deletion entry point) must be readable
  // both signed out and signed in.
  {
    path: "/privacy",
    lazy: lazyRoute(
      () => import("@/pages/privacy/privacy-policy"),
      (m) => m.PrivacyPolicyPage
    ),
  },
  { path: "*", element: <NotFoundPage /> },
    ],
  },
])
