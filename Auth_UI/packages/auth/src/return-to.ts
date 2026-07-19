import { API_BASE_URL } from "@astoom/api/env"

/**
 * OAuth authorize path on the auth origin — the ONLY destination a returnTo
 * parameter may point to. Everything else is treated as an open-redirect
 * attempt and ignored.
 */
const AUTHORIZE_PATH_RE = /^\/api\/v\d+\/auth\/authorize$/i

/**
 * Validates the `returnTo` query parameter of the hosted login page.
 * Accepts only an absolute URL on the API origin whose path is the OAuth
 * authorize endpoint; anything else returns null and the login behaves as a
 * plain first-party sign-in.
 */
export function getValidReturnTo(search: string): string | null {
  const raw = new URLSearchParams(search).get("returnTo")
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
