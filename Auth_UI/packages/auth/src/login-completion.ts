import * as React from "react"
import { useLocation, useNavigate } from "react-router-dom"

import { currentMfaRequirement } from "@authsystem/api/mfa-requirement"

import { launchApplication } from "./launch-application"
import {
  getPendingReturnTo,
  setPendingReturnTo,
  type TwoFactorMethod,
} from "./pending-challenge"
import { getValidReturnTo, validateReturnToUrl } from "./return-to"

/**
 * Router state understood by every screen that can end an authentication.
 *
 * `from` arrives in two shapes and both are legal: RequireAuth stores the whole
 * location object, while the screens that forward it store the pre-joined
 * string. Normalizing here is what lets one rule serve both.
 */
interface CompletionState {
  from?: { pathname?: string; search?: string } | string | null
  returnTo?: string | null
}

function resolveFrom(
  raw: CompletionState["from"],
  fallback: string
): string {
  if (typeof raw === "string" && raw) return raw
  if (raw && typeof raw === "object" && raw.pathname) {
    return raw.pathname + (raw.search ?? "")
  }
  return fallback
}

/** What one caller of `complete` may add to the rule, for that sign-in only. */
export interface CompletionOptions {
  /**
   * An application to enter once nothing else claims the sign-in (OI-192): an
   * accepted invitation's launch address. Checked with `launchTarget`; one
   * that fails is ignored and the sign-in lands on `from`.
   */
  launchUrl?: string | null
  /**
   * Where this sign-in lands instead of the recorded origin, for a screen that
   * learns its destination only once the sign-in starts (the organization a
   * registration just joined). Carried through the interstitials like `from`.
   */
  from?: string
}

export interface LoginCompletion {
  /** The validated pending authorize request, or null for a plain sign-in. */
  returnTo: string | null
  /** Where a plain first-party sign-in should land. */
  from: string
  /** Every successful authentication ends here. */
  complete: (
    result: { requiresPasswordChange?: boolean },
    options?: CompletionOptions
  ) => void
  /**
   * Every 2FA challenge is handed off here, with the methods the server offered
   * for it (S24); null when it named none.
   */
  challenge: (
    challengeToken: string,
    methods?: readonly TwoFactorMethod[] | null
  ) => void
  /** Every screen that stands between credentials and a session. */
  interstitial: (
    path: string,
    extraState?: Record<string, unknown>,
    options?: { replace?: boolean }
  ) => void
}

/**
 * The single rule deciding where a user goes once they are authenticated.
 *
 * This exists because that decision used to live in each screen that could end
 * a sign-in, and there are nine of them: password, Google, Apple, 2FA, email
 * verification, forced password change, account recovery, registration, and the
 * anonymous-route guard. Two honored a pending OAuth authorize request and
 * seven silently dropped it, so a relying party that sent the user here got its
 * authorization code only if they happened to take one of two paths.
 *
 * A tenth screen added later inherits the correct behavior instead of
 * re-deriving it — that, not the seven fixes, is the point.
 */
export function useLoginCompletion(
  options: {
    /**
     * Consult the in-memory pending request when neither the query string nor
     * router state carries one. Only the 2FA screen may ask for this: it is the
     * one screen reachable after the state that held the destination was lost,
     * and restricting it keeps a settled request from resuming a later sign-in.
     */
    resumePending?: boolean
    /** Where a plain sign-in lands when nothing recorded an origin. */
    defaultFrom?: string
  } = {}
): LoginCompletion {
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as CompletionState | null

  const from = resolveFrom(state?.from, options.defaultFrom ?? "/")

  // Router state is re-validated rather than trusted: one rule decides what is
  // a legal destination, and it is the same rule at every hop.
  const returnTo = React.useMemo(
    () =>
      getValidReturnTo(location.search) ??
      validateReturnToUrl(state?.returnTo) ??
      (options.resumePending ? validateReturnToUrl(getPendingReturnTo()) : null),
    [location.search, state?.returnTo, options.resumePending]
  )

  const complete = React.useCallback(
    (
      result: { requiresPasswordChange?: boolean },
      options?: CompletionOptions
    ) => {
      const destination = options?.from ?? from

      if (result.requiresPasswordChange) {
        // An interstitial, not a destination: the pending request has to
        // survive it, which is why it carries the state forward. A launch does
        // not: the person lands on `destination` once the password is changed.
        navigate("/force-password-change", {
          replace: true,
          state: { from: destination, returnTo },
        })
        return
      }

      if (returnTo) {
        // A top-level navigation, deliberately, not a router transition: the
        // IdP session cookie is SameSite=Lax and rides along with nothing else,
        // so the authorize endpoint would not recognize the browser.
        window.location.assign(returnTo)
        return
      }

      // A platform administrator whose platform authority the server withheld
      // until this session proves a second factor (S08). After the pending
      // authorize request, which the withholding never touches (an application's
      // token is not platform authority), and before the console itself. Read
      // from the token just stored: the auth context has not re-rendered yet.
      // The server enforces it; RequireMfaSatisfied catches every other way in.
      if (currentMfaRequirement() !== "none") {
        navigate("/two-factor/required", {
          replace: true,
          state: { from: destination },
        })
        return
      }

      // An accepted invitation's application (OI-192), after everything that
      // must happen inside IAM first, and only through the launch check: a
      // value that fails it is ignored, never followed.
      if (launchApplication(options?.launchUrl)) return

      navigate(destination, { replace: true })
    },
    [navigate, from, returnTo]
  )

  const challenge = React.useCallback(
    (challengeToken: string, methods?: readonly TwoFactorMethod[] | null) => {
      setPendingReturnTo(returnTo)
      navigate("/two-factor", {
        replace: true,
        state: { challengeToken, methods: methods ?? null, from, returnTo },
      })
    },
    [navigate, from, returnTo]
  )

  const interstitial = React.useCallback(
    (
      path: string,
      extraState?: Record<string, unknown>,
      options?: { replace?: boolean }
    ) => {
      navigate(path, {
        replace: options?.replace ?? false,
        state: { ...extraState, from, returnTo },
      })
    },
    [navigate, from, returnTo]
  )

  return { returnTo, from, complete, challenge, interstitial }
}
