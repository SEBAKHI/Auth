import { decodeJwt } from "@authsystem/api/jwt"
import { getAccessToken } from "@authsystem/api/token-store"

import type { SignInReason } from "./sign-in-reason"

/**
 * One sign-in, not two, before the set-up page of an application's two-step
 * requirement (OI-275).
 *
 * A person without an authenticator app signs in for an application that
 * requires one. The resume finds no qualifying factor and the server sends the
 * browser back to the sign-in page with `reason=mfa_enroll`, for the same
 * request. Without this module the page asks for the sign-in it saw complete
 * seconds before. With it, the sign-in that resumed the request leaves a marker
 * in this tab, and the page that opens for the same request, account and
 * session moments later goes straight to the set-up page.
 *
 * THE MARKER. One `sessionStorage` key, in this tab only: the pending request
 * (the validated internal URL the completion resumed), the account (`sub`) and
 * the accounts session (`sid`) of the token stored by that sign-in, the time,
 * and what proved the sign-in's first step. No address, no name, no token.
 *
 * WHO WRITES AND WHO SPENDS IT. Only the completion rule writes it, right before
 * it resumes a request. The completion rule also removes it where it spends the
 * `mfa_enroll` reason, which is where every skip goes, so a marker opens the
 * set-up page once: a set-up the server refuses (the sign-in is too old, or it
 * did not prove a first factor) sends the person to the sign-in page with the
 * same request and reason, and that page then shows the form. Signing out in
 * this tab removes it too.
 *
 * WHAT IT CANNOT DO. The skip reaches a page this tab's session can already
 * reach. The set-up still asks for a recent sign-in with a first factor and
 * for the emailed code before the first factor (X02), and the server decides
 * again when the request resumes. A forged marker needs script on this origin,
 * which already holds the session.
 *
 * Every read and write is wrapped: a browser that blocks storage keeps no
 * marker, and the page shows the form as before.
 */

export const SIGN_IN_MARKER_KEY = "auth.signIn.marker"

/**
 * How long after the sign-in the bounce may skip the form. The bounce is one
 * redirect and one page load; two minutes leave at least three of the shortest
 * re-authentication window the set-up accepts (five minutes).
 */
export const SIGN_IN_MARKER_MAX_AGE_MS = 120_000

/**
 * What proved the first step of the sign-in that resumed the request, as the
 * screen that ended it knows: the password, the emailed code, or an external
 * identity (Google, Apple). Absent when that screen does not say.
 */
export type SignInProof = "password" | "code" | "external"

interface SignInMarker {
  request: string
  sub: string
  sid: string
  at: number
  proof: SignInProof | null
}

function isProof(value: unknown): value is SignInProof {
  return value === "password" || value === "code" || value === "external"
}

/** The account and accounts session of the token this tab holds right now. */
function currentSession(): { sub: string; sid: string } | null {
  const token = getAccessToken()
  const claims = token ? decodeJwt(token) : null
  const sub = claims?.sub
  const sid = claims?.sid
  return typeof sub === "string" && sub && typeof sid === "string" && sid
    ? { sub, sid }
    : null
}

export function clearSignInMarker(): void {
  try {
    window.sessionStorage.removeItem(SIGN_IN_MARKER_KEY)
  } catch {
    // Nothing to remove where nothing could be written.
  }
}

/**
 * A sign-in in this tab is about to resume `request`. Read from the token just
 * stored; a token without `sub` or `sid` leaves no marker.
 */
export function writeSignInMarker(
  request: string,
  proof: SignInProof | null | undefined,
  now = Date.now()
): void {
  const session = currentSession()
  if (!session) {
    clearSignInMarker()
    return
  }
  const marker: SignInMarker = {
    request,
    sub: session.sub,
    sid: session.sid,
    at: now,
    proof: proof ?? null,
  }
  try {
    window.sessionStorage.setItem(SIGN_IN_MARKER_KEY, JSON.stringify(marker))
  } catch {
    // Not kept: the page shows the form, as before.
  }
}

function readSignInMarker(): SignInMarker | null {
  try {
    const raw = window.sessionStorage.getItem(SIGN_IN_MARKER_KEY)
    if (!raw) return null
    const value = JSON.parse(raw) as Record<string, unknown> | null
    if (!value || typeof value !== "object") return null
    const { request, sub, sid, at, proof } = value
    if (
      typeof request !== "string" ||
      typeof sub !== "string" ||
      typeof sid !== "string" ||
      typeof at !== "number" ||
      !Number.isFinite(at)
    ) {
      return null
    }
    return { request, sub, sid, at, proof: isProof(proof) ? proof : null }
  } catch {
    return null
  }
}

/**
 * Whether the sign-in page may skip its form and go to the set-up page: the
 * server sent the person back with `mfa_enroll` for `returnTo`, and this tab
 * holds a marker for the same request, written by a sign-in of the same account
 * in the same accounts session at most two minutes ago, whose first step the
 * set-up will accept.
 *
 * That last condition: the method word `code` names an account without a
 * password, whose every sign-in proves a first factor; otherwise only a sign-in
 * that typed the password or used an external identity does. An emailed code
 * alone, for an account with a password, does not (the set-up would refuse it),
 * and neither does a sign-in whose screen did not say: unknown is no skip.
 *
 * Reads only: the marker is spent by the completion the skip goes through.
 */
export function canSkipSignIn(
  returnTo: string | null,
  reason: SignInReason | null,
  now = Date.now()
): boolean {
  if (!returnTo || reason?.reason !== "mfa_enroll") return false

  const marker = readSignInMarker()
  if (!marker || marker.request !== returnTo) return false

  const session = currentSession()
  if (!session || marker.sub !== session.sub || marker.sid !== session.sid) {
    return false
  }

  const age = now - marker.at
  if (!(age >= 0 && age <= SIGN_IN_MARKER_MAX_AGE_MS)) return false

  return (
    reason.method === "code" ||
    marker.proof === "password" ||
    marker.proof === "external"
  )
}
