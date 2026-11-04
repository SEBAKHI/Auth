import * as React from "react"
import { useLocation, useNavigate } from "react-router-dom"

import { currentMfaRequirement } from "@authsystem/api/mfa-requirement"

import { launchApplication } from "./launch-application"
import {
  getPendingReturnTo,
  getPendingSignInReason,
  setPendingReturnTo,
  type TwoFactorMethod,
} from "./pending-challenge"
import { getValidReturnTo, validateReturnToUrl } from "./return-to"
import {
  readSignInReason,
  validateSignInReason,
  type SignInReason,
} from "./sign-in-reason"

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
  /** Why the pending request's sign-in was asked for (S23); validated on read. */
  reason?: unknown
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
  /**
   * Why an application's two-step requirement sent the person back to sign in
   * (S23), read from the same place as `returnTo`; null without a request.
   */
  reason: SignInReason | null
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
  // a legal destination, and it is the same rule at every hop. The sign-in
  // reason (S23) comes from the same place as the request it belongs to, never
  // from another: a reason left behind by an earlier request is not this one's.
  const { returnTo, reason } = React.useMemo(() => {
    const fromQuery = getValidReturnTo(location.search)
    if (fromQuery) {
      return { returnTo: fromQuery, reason: readSignInReason(location.search) }
    }
    const fromState = validateReturnToUrl(state?.returnTo)
    if (fromState) {
      return { returnTo: fromState, reason: validateSignInReason(state?.reason) }
    }
    const fromStore = options.resumePending
      ? validateReturnToUrl(getPendingReturnTo())
      : null
    if (fromStore) {
      return { returnTo: fromStore, reason: getPendingSignInReason() }
    }
    return { returnTo: null, reason: null }
  }, [location.search, state?.returnTo, state?.reason, options.resumePending])

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
          state: { from: destination, returnTo, reason },
        })
        return
      }

      if (returnTo) {
        // An application that requires two-step verification found no
        // authenticator on this account (S23): it is set up here first, on the
        // page that then resumes the request. The reason is spent here: not
        // carried on, so that page's own completion goes to the application,
        // and dropped from the in-memory store, so a later sign-in in this tab
        // does not open the page again. The server decides again on the
        // resume; the word only chose the page.
        if (reason?.reason === "mfa_enroll") {
          setPendingReturnTo(null)
          navigate("/two-factor/required", {
            replace: true,
            state: { mode: "application", returnTo, from: destination },
          })
          return
        }
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
    [navigate, from, returnTo, reason]
  )

  const challenge = React.useCallback(
    (challengeToken: string, methods?: readonly TwoFactorMethod[] | null) => {
      setPendingReturnTo(returnTo, reason)
      navigate("/two-factor", {
        replace: true,
        state: {
          challengeToken,
          methods: methods ?? null,
          from,
          returnTo,
          reason,
        },
      })
    },
    [navigate, from, returnTo, reason]
  )

  const interstitial = React.useCallback(
    (
      path: string,
      extraState?: Record<string, unknown>,
      options?: { replace?: boolean }
    ) => {
      navigate(path, {
        replace: options?.replace ?? false,
        state: { ...extraState, from, returnTo, reason },
      })
    },
    [navigate, from, returnTo, reason]
  )

  return { returnTo, reason, from, complete, challenge, interstitial }
}
