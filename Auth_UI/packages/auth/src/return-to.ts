import { API_BASE_URL } from "@authsystem/api/env"

/**
 * OAuth authorize path on the auth origin — the ONLY destination a returnTo
 * parameter may point to. Everything else is treated as an open-redirect
 * attempt and ignored.
 */
const AUTHORIZE_PATH_RE = /^\/api\/v\d+\/auth\/authorize$/i

/**
 * Validates a raw pending-authorize URL, wherever it came from.
 * Accepts only an absolute URL on the API origin whose path is the OAuth
 * authorize endpoint; anything else returns null and the login behaves as a
 * plain first-party sign-in.
 *
 * The request is passed back to the authorize endpoint EXACTLY as it arrived.
 * This used to delete `prompt` and `max_age` before resuming, to stop the
 * browser looping between authorize and login — the server demanded step-up on
 * the literal presence of `prompt=login`, so the only way out was to remove the
 * demand. That made re-authentication satisfiable by deleting the request for
 * it: anyone holding a live session cookie could strip the parameter by hand and
 * be issued a code against the stale session, which is exactly the person
 * step-up exists to stop. The server now records its own demand in a signed
 * HttpOnly cookie and checks that the session was minted after it, so the loop
 * ends on evidence and this client has nothing left to remove.
 *
 * REQUIRES the API to be deployed first. Against an older API — one that still
 * returns step-up on the bare parameter — carrying `prompt=login` through would
 * loop the browser.
 *
 * Exported separately from `getValidReturnTo` because the value does not only
 * arrive in the query string: it is threaded through router state across the
 * interstitial screens (2FA, email verification, forced password change,
 * recovery). Those hops must be re-validated at the point of use rather than
 * trusted, so that exactly one rule decides what is a legal destination.
 */
export function validateReturnToUrl(
  raw: string | null | undefined
): string | null {
  if (!raw) return null

  try {
    const url = new URL(raw)
    const apiOrigin = new URL(API_BASE_URL).origin

    if (url.origin !== apiOrigin) return null
    if (!AUTHORIZE_PATH_RE.test(url.pathname)) return null

    return url.toString()
  } catch {
    return null
  }
}

/**
 * Validates the `returnTo` query parameter of the hosted login page — the
 * entry point of the flow, where the authorize endpoint's redirect lands.
 */
export function getValidReturnTo(search: string): string | null {
  const raw = new URLSearchParams(search).get("returnTo")

  if (import.meta.env.DEV && raw && !validateReturnToUrl(raw)) {
    // Silent rejection is indistinguishable from "no pending request at all":
    // the user simply lands on the default page, which is exactly what the
    // resume bug looked like. The usual cause is a PublicBaseUrl on the API
    // that does not match VITE_API_BASE_URL here, so say so out loud in dev.
    console.warn(
      `[auth] returnTo rejected: ${raw} — expected an authorize URL on ${API_BASE_URL}`
    )
  }

  return validateReturnToUrl(raw)
}

/**
 * Extracts the client_id from a validated returnTo authorize URL, used solely
 * to fetch the app's public branding. Branding data itself is NEVER read from
 * the URL — only this identifier, resolved against the database server-side.
 */
export function getReturnToClientId(returnTo: string | null): string | null {
  if (!returnTo) return null

  try {
    return new URL(returnTo).searchParams.get("client_id")
  } catch {
    return null
  }
}
