import * as React from "react"
import { useLocation, useNavigate } from "react-router-dom"

import { currentMfaRequirement } from "@authsystem/api/mfa-requirement"
import { useSignInOptions } from "@authsystem/ui/branding"

import { turnstileLoaded } from "./bot-defense/turnstile-script"
import { launchApplication } from "./launch-application"
import {
  PASSKEY_OFFER_PATH,
  isPasskeyOfferPath,
  notePasskeyOfferNavigation,
  passkeyOfferDeclined,
  passkeysAnsweredOff,
} from "./passkey/passkey-offer"
import { browserHasWebAuthn } from "./passkey/webauthn"
import {
  getPendingReturnTo,
  getPendingSignInReason,
  setPendingReturnTo,
  type TwoFactorMethod,
} from "./pending-challenge"
import { TWO_FACTOR_REQUIRED_PATH } from "./require-mfa-satisfied"
import { getValidReturnTo, validateReturnToUrl } from "./return-to"
import {
  clearSignInMarker,
  writeSignInMarker,
  type SignInProof,
} from "./sign-in-marker"
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
  const path =
    typeof raw === "string" && raw
      ? raw
      : raw && typeof raw === "object" && raw.pathname
        ? raw.pathname + (raw.search ?? "")
        : null
  // The offer to add a passkey stands between a sign-in and where it goes, and is never where
  // one goes (OI-280 PR B): a session that ended on it (signed out from its window, or expired
  // there) records it as the origin, and the next sign-in's way out would lead back to it.
  if (path === null || isPasskeyOfferPath(path)) return fallback
  return path
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
  /**
   * What proved this sign-in's first step, from the screen that ended it
   * (OI-275): kept with the marker a resume leaves, so that the sign-in page
   * the server may send the person back to can skip its form only when the
   * set-up will accept this sign-in. Absent is unknown, and unknown never skips.
   */
  proof?: SignInProof
  /**
   * False for a completion that ends no sign-in: the set-up page resuming the
   * request after its own step (OI-275). It leaves no marker, so the marker
   * always means "a sign-in in this tab just resumed this request".
   */
  endsSignIn?: boolean
  /**
   * This sign-in's first step was a passkey (S20): no offer to add one follows
   * (OI-280 PR B, T-B9 a). Said by the screen that ended it, never read from
   * storage: a passkey sign-in writes no method and leaves no proof (T-C6).
   */
  signedInWithPasskey?: boolean
}

/** What the offer's page carries, to end the sign-in as it would have ended without it. */
export interface PasskeyOfferExit {
  proof: SignInProof | null
  launchUrl: string | null
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
let fullNavigationAfterTurnstile = false

/**
 * The console's choice (S13, OD3): a sign-in on a page that loaded Cloudflare's
 * script ends with a full navigation, so the script does not stay in the
 * session the administrator then works in (the Secrets page included). It costs
 * about a second and one token refresh per sign-in under Monitor and Enforce,
 * and nothing at Off, where no script loads. The accounts app does not ask: the
 * cost would land on every user there.
 */
export function endSignInWithFullNavigationAfterTurnstile(on = true): void {
  fullNavigationAfterTurnstile = on
}

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
  // The deployment's word that passkeys may run here (OI-280), from the branding
  // answer; outside a branding provider, nothing, and so no offer. Read through a
  // ref when deciding, with the page's path, so that `complete` keeps the identity
  // it had before the offer: pages hold it in effects (the Google button starts
  // over with a new nonce when it changes), and the branding answers late.
  const passkeysOffered = useSignInOptions()?.passkeySignInOffered === true
  const passkeysOfferedRef = React.useRef(passkeysOffered)
  const pathnameRef = React.useRef(location.pathname)
  React.useEffect(() => {
    passkeysOfferedRef.current = passkeysOffered
    pathnameRef.current = location.pathname
  }, [passkeysOffered, location.pathname])

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

      // The offer to add a passkey (OI-280 PR B: D-S20-12, Q1 (a)), after any
      // sign-in, whatever its destination, once nothing below claims it first
      // (the forced password change, an application's set-up, the platform's):
      // the set-up page has precedence, and no offer follows it or itself.
      // Asked here only what this page knows: a sign-in that ended here and not
      // with a passkey; the deployment's hint, with no passkey request on this
      // page since that answered they do not run; a browser with WebAuthn; and
      // no «don't ask on this device». The offer's page asks the rest before it
      // shows anything, and every way out of it comes back here.
      const offerPasskey = (): boolean => {
        const pathname = pathnameRef.current
        const due =
          options?.endsSignIn !== false &&
          options?.signedInWithPasskey !== true &&
          pathname !== TWO_FACTOR_REQUIRED_PATH &&
          pathname !== PASSKEY_OFFER_PATH &&
          passkeysOfferedRef.current &&
          !passkeysAnsweredOff() &&
          browserHasWebAuthn() &&
          !passkeyOfferDeclined()
        if (!due) return false
        const exit: PasskeyOfferExit = {
          proof: options?.proof ?? null,
          launchUrl: options?.launchUrl ?? null,
        }
        const offerState = { from: destination, returnTo, reason, passkeyOffer: exit }
        // For the sign-in pages' guard, which the session's flip wakes while this lazy route is
        // still loading: it follows here rather than sending the person on (require-auth.tsx).
        notePasskeyOfferNavigation(offerState)
        navigate(PASSKEY_OFFER_PATH, { replace: true, state: offerState })
        return true
      }

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
        // resume; the word only chose the page. The sign-in marker (OI-275) is
        // spent here too, whether this sign-in ended on the form or skipped it:
        // a set-up the server refuses comes back to the form, never here again.
        if (reason?.reason === "mfa_enroll") {
          setPendingReturnTo(null)
          clearSignInMarker()
          navigate(TWO_FACTOR_REQUIRED_PATH, {
            replace: true,
            state: { mode: "application", returnTo, from: destination },
          })
          return
        }
        // Before the marker and the resume (D-B8): the offer leaves through this
        // same rule, with this sign-in's proof, so the marker is written as before.
        if (offerPasskey()) return
        // A top-level navigation, deliberately, not a router transition: the
        // IdP session cookie is SameSite=Lax and rides along with nothing else,
        // so the authorize endpoint would not recognize the browser.
        //
        // Before it, the marker (OI-275): if the server sends the person back
        // to set up an authenticator for this request, the sign-in page skips
        // the sign-in it saw here. Read from the token just stored. A resume
        // that ends no sign-in leaves none.
        if (options?.endsSignIn === false) clearSignInMarker()
        else writeSignInMarker(returnTo, options?.proof)
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
        navigate(TWO_FACTOR_REQUIRED_PATH, {
          replace: true,
          state: { from: destination },
        })
        return
      }

      // Before the launch and the landing (D-B8).
      if (offerPasskey()) return

      // An accepted invitation's application (OI-192), after everything that
      // must happen inside IAM first, and only through the launch check: a
      // value that fails it is ignored, never followed.
      if (launchApplication(options?.launchUrl)) return

      // OD3: the document that ran Cloudflare's script is left, not kept. A
      // replace, as the router's: the sign-in page is not one step back. Only
      // within this origin, as the router never leaves it (REVIEW-FIXES 167
      // F12): a destination that names another origin takes the router's way.
      if (fullNavigationAfterTurnstile && turnstileLoaded()) {
        const target = new URL(destination, window.location.origin)
        if (target.origin === window.location.origin) {
          window.location.replace(target.pathname + target.search + target.hash)
          return
        }
      }

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
