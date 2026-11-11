import * as React from "react"

/**
 * The offer to add a passkey after a sign-in (OI-280 PR B, owner decisions D-S20-12 and Q1-Q2):
 * what the completion rule asks before it opens the offer's page, kept small, since that rule
 * travels with every sign-in page.
 */

/** The offer's page, in both apps, beside the two-step set-up page. */
export const PASSKEY_OFFER_PATH = "/passkey/offer"

/**
 * «Don't ask on this device» (D-B7): one flag for the whole browser, holding neither an address
 * nor an account id (D-215-4). On a browser two people share, one person's choice stops the offer
 * for both (the private-device default). «Not now» stores nothing (Q2 (a)).
 */
export const PASSKEY_OFFER_OFF_KEY = "auth.passkeyOffer.off"

/** Whether this browser asked never to be offered a passkey again. Unreadable storage: not asked. */
export function passkeyOfferDeclined(): boolean {
  try {
    return window.localStorage.getItem(PASSKEY_OFFER_OFF_KEY) === "1"
  } catch {
    return false
  }
}

/** «Don't ask on this device». A browser that blocks storage keeps nothing, and asks again. */
export function declinePasskeyOffer(): void {
  try {
    window.localStorage.setItem(PASSKEY_OFFER_OFF_KEY, "1")
  } catch {
    // Not kept: the next sign-in offers it again.
  }
}

// Whether a passkey request on this page answered that passkeys do not run here (the routes' 404,
// the authority over the branding hint), or the browser cannot use one. Kept for the page's life:
// the hint is cached for a minute, and a stale «yes» must not open an offer, or a passkey choice,
// that could only fail. A deployment state, never a person's: nothing to clear at a sign-out.
let answeredOff = false
const listeners = new Set<() => void>()

/** A passkey request here answered 404: no offer, and no passkey choice, for the rest of the page's life. */
export function notePasskeysOff(): void {
  if (answeredOff) return
  answeredOff = true
  for (const listener of listeners) listener()
}

/** Whether passkeys answered off on this page; read when deciding, never while rendering. */
export function passkeysAnsweredOff(): boolean {
  return answeredOff
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** The same, for rendering. */
export function usePasskeysAnsweredOff(): boolean {
  return React.useSyncExternalStore(subscribe, passkeysAnsweredOff, () => false)
}

/**
 * How long the guard follows a completion to the offer: far longer than the page's chunk takes to
 * load, and short enough that a navigation that never arrived cannot steer a later visit.
 */
const OFFER_NAVIGATION_LIFETIME_MS = 10_000

// The offer a completion is on its way to, for the sign-in pages' guard (`RequireAnonymous`). The
// guard sends an authenticated visitor away from the sign-in page as soon as the session flips, so
// it races the page's own completion; the offer's route is lazy, its navigation still loading when
// the guard renders, and the guard's navigation would replace it. The two must agree on where the
// person goes (the guard's own rule), so the guard follows the offer while this is fresh.
let offerNavigation: { state: unknown; at: number } | null = null

/** The completion rule is navigating to the offer, with this router state. */
export function notePasskeyOfferNavigation(state: unknown): void {
  offerNavigation = { state, at: Date.now() }
}

/** The offer a completion is navigating to, while that is fresh. Changes nothing: safe in a render. */
export function pendingPasskeyOfferNavigation(): { state: unknown } | null {
  if (!offerNavigation || Date.now() - offerNavigation.at > OFFER_NAVIGATION_LIFETIME_MS) return null
  return { state: offerNavigation.state }
}

/** The offer's page opened: nothing is left for the guard to follow. */
export function clearPasskeyOfferNavigation(): void {
  offerNavigation = null
}
