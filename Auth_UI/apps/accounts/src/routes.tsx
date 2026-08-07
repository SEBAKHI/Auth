import { createBrowserRouter, Navigate } from "react-router-dom"

import { RequireAnonymous, RequireAuth } from "@authsystem/auth/require-auth"
import { crumb } from "@authsystem/ui/crumbs"
import { NotFoundPage } from "@authsystem/ui/error-pages/not-found"
import { RouteErrorBoundary } from "@authsystem/ui/error-pages/route-error"
import { lazyRoute, RouteFallback } from "@authsystem/ui/lazy-route"

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
    // Every descendant bubbles its errors here. Without it a chunk that a deploy
    // removed took the whole tab blank, silently.
    errorElement: <RouteErrorBoundary />,
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
          () => import("@authsystem/auth/pages/forgot-password"),
          (m) => m.ForgotPasswordPage
        ),
      },
      {
        path: "/reset-password",
        lazy: lazyRoute(
          () => import("@authsystem/auth/pages/reset-password"),
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
          () => import("@authsystem/auth/pages/force-password-change"),
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
              () => import("@authsystem/account/pages/profile/profile-page"),
              (m) => () => <m.ProfilePage showDangerZone />
            ),
            handle: crumb("profile", "/profile"),
          },
          {
            path: "organizations",
            lazy: lazyRoute(
              () => import("@authsystem/account/pages/organizations/organizations-page"),
              (m) => m.OrganizationsPage
            ),
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            lazy: lazyRoute(
              () =>
                import(
                  "@authsystem/account/pages/organizations/organization-detail-page"
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
      () => import("@authsystem/auth/pages/verify-email-page"),
      (m) => m.VerifyEmailPage
    ),
  },
  // Top-level on purpose: the page serves both anonymous invitees (register /
  // sign-in-to-accept) and already-authenticated users (one-click accept), so
  // it must live under neither RequireAnonymous nor RequireAuth.
  {
    path: "/accept-invitation",
    lazy: lazyRoute(
      () => import("@authsystem/auth/pages/accept-invitation"),
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
  // No /privacy route. Publishing writes complete HTML to the persistent IIS
  // virtual directory, so the notice survives frontend deploys and API outages
  // and needs no script to be readable. Links to it are plain anchors built
  // with privacyPolicyUrl(); IIS serves the path on the Accounts origin.
  { path: "*", element: <NotFoundPage /> },
    ],
  },
])
