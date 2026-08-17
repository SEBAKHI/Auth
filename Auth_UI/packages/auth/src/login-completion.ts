import * as React from "react"
import { useLocation, useNavigate } from "react-router-dom"

import { getPendingReturnTo, setPendingReturnTo } from "./pending-challenge"
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

export interface LoginCompletion {
  /** The validated pending authorize request, or null for a plain sign-in. */
  returnTo: string | null
  /** Where a plain first-party sign-in should land. */
  from: string
  /** Every successful authentication ends here. */
  complete: (result: { requiresPasswordChange?: boolean }) => void
  /** Every 2FA challenge is handed off here. */
  challenge: (challengeToken: string) => void
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
    (result: { requiresPasswordChange?: boolean }) => {
      if (result.requiresPasswordChange) {
        // An interstitial, not a destination: the pending request has to
        // survive it, which is why it carries the state forward.
        navigate("/force-password-change", {
          replace: true,
          state: { from, returnTo },
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

      navigate(from, { replace: true })
    },
    [navigate, from, returnTo]
  )

  const challenge = React.useCallback(
    (challengeToken: string) => {
      setPendingReturnTo(returnTo)
      navigate("/two-factor", {
        replace: true,
        state: { challengeToken, from, returnTo },
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
