import { API_BASE_URL } from "@authsystem/api/env"

/**
 * The pending authorize request, and the one module that knows its shapes.
 *
 * The authorize endpoint sends a browser without a session to an accounts page
 * (sign-in, registration, organization creation), and the page sends it back
 * when it is done. Since OI-145 the page URL carries the request as
 * `?client_id=<application code>&auth_request=<64 hex>`: the server stores the
 * request, and this app resumes it at the API's resume endpoint. The page used
 * to carry the whole authorize URL in `?returnTo=`, and a web application
 * firewall reads a parameter holding an off-site `https://` address as a remote
 * file inclusion and refuses the page. The old shape is still READ (links in
 * flight during a deploy, and the server's switch turned off), never written
 * from here by anything but `toAccountsSearch`.
 *
 * Inside the app the pending request is one absolute URL on the API origin:
 * either the legacy authorize URL or the resume URL. Both are validated by the
 * same rule wherever they come from, and both are resumed with one top-level
 * navigation.
 */

/**
 * OAuth authorize path on the auth origin — a legacy `returnTo` may point
 * nowhere else. Everything else is treated as an open-redirect attempt and
 * ignored.
 */
const AUTHORIZE_PATH_RE = /^\/api\/v\d+\/auth\/authorize$/i

/** The resume endpoint on the auth origin, where a stored request is picked up again. */
const RESUME_PATH_RE = /^\/api\/v\d+\/auth\/authorize\/resume$/i

/** The resume URL this app builds. */
const RESUME_PATH = "/api/v1/auth/authorize/resume"

/** A handle is 32 random bytes as 64 lowercase hex characters, nothing else. */
const HANDLE_RE = /^[0-9a-f]{64}$/

const CLIENT_ID_PARAM = "client_id"
const HANDLE_PARAM = "auth_request"
const SKIP_ORGANIZATION_PARAM = "skip_organization"
const LEGACY_PARAM = "returnTo"

/** The authorize parameter that asks for the organization-creation page. */
const CREATE_ORGANIZATION_PARAM = "create_organization"

/**
 * Whether a resume URL's query is exactly what this app builds: one
 * `client_id`, one well-formed `auth_request`, and at most one
 * `skip_organization=true`. Nothing else may ride to the API.
 */
function isResumeQuery(params: URLSearchParams): boolean {
  const keys = [...params.keys()]
  const allowed = new Set([
    CLIENT_ID_PARAM,
    HANDLE_PARAM,
    SKIP_ORGANIZATION_PARAM,
  ])
  if (keys.some((key) => !allowed.has(key))) return false
  if (new Set(keys).size !== keys.length) return false

  const skip = params.get(SKIP_ORGANIZATION_PARAM)
  return (
    Boolean(params.get(CLIENT_ID_PARAM)) &&
    HANDLE_RE.test(params.get(HANDLE_PARAM) ?? "") &&
    (skip === null || skip === "true")
  )
}

/**
 * Validates a pending request, wherever it came from: the query string, router
 * state across the interstitial screens (2FA, email verification, forced
 * password change, recovery), or the 2FA memory slot. Those hops are
 * re-validated at the point of use rather than trusted, so that exactly one rule
 * decides what is a legal destination.
 *
 * Accepts only an absolute URL on the API origin that is either the OAuth
 * authorize endpoint (the legacy shape) or the resume endpoint with exactly the
 * parameters this app puts there. Anything else returns null and the sign-in
 * behaves as a plain first-party sign-in.
 *
 * A legacy request is passed back to the authorize endpoint EXACTLY as it
 * arrived. This used to delete `prompt` and `max_age` before resuming, to stop
 * the browser looping between authorize and login — the server demanded step-up
 * on the literal presence of `prompt=login`, so the only way out was to remove
 * the demand. That made re-authentication satisfiable by deleting the request
 * for it: anyone holding a live session cookie could strip the parameter by
 * hand and be issued a code against the stale session, which is exactly the
 * person step-up exists to stop. The server now records its own demand in a
 * signed HttpOnly cookie and checks that the session was minted after it, so
 * the loop ends on evidence and this client has nothing left to remove.
 *
 * REQUIRES the API to be deployed first. Against an older API — one that still
 * returns step-up on the bare parameter — carrying `prompt=login` through would
 * loop the browser.
 */
export function validateReturnToUrl(
  raw: string | null | undefined
): string | null {
  if (!raw) return null

  try {
    const url = new URL(raw)
    const apiOrigin = new URL(API_BASE_URL).origin

    if (url.origin !== apiOrigin) return null
    if (AUTHORIZE_PATH_RE.test(url.pathname)) return url.toString()
    if (RESUME_PATH_RE.test(url.pathname) && isResumeQuery(url.searchParams)) {
      return url.toString()
    }
    return null
  } catch {
    return null
  }
}

/** The handle query: `client_id` and `auth_request`, nothing else. */
function handleQuery(clientId: string, handle: string): string {
  return `?${CLIENT_ID_PARAM}=${encodeURIComponent(clientId)}&${HANDLE_PARAM}=${handle}`
}

/** The resume URL for a stored request, as the API's resume endpoint reads it. */
function resumeUrl(clientId: string, handle: string): string {
  return `${API_BASE_URL}${RESUME_PATH}${handleQuery(clientId, handle)}`
}

/**
 * Reads the pending request from an accounts page's query string — the entry
 * point of the flow, where the authorize endpoint's redirect lands — and returns
 * it as the internal shape, validated.
 *
 * `?client_id=X&auth_request=H` (H exactly 64 lowercase hex, X not empty)
 * becomes the resume URL. Otherwise the legacy `?returnTo=` is read and
 * validated as before.
 */
export function getValidReturnTo(search: string): string | null {
  const params = new URLSearchParams(search)

  const clientId = params.get(CLIENT_ID_PARAM)
  const handle = params.get(HANDLE_PARAM)
  if (clientId && handle !== null && HANDLE_RE.test(handle)) {
    return validateReturnToUrl(resumeUrl(clientId, handle))
  }

  const raw = params.get(LEGACY_PARAM)

  if (import.meta.env.DEV) {
    // Silent rejection is indistinguishable from "no pending request at all":
    // the user simply lands on the default page, which is exactly what the
    // resume bug looked like. The usual cause of a refused returnTo is a
    // PublicBaseUrl on the API that does not match VITE_API_BASE_URL here, so
    // say so out loud in dev.
    if (handle !== null) {
      console.warn(
        `[auth] auth_request rejected: expected client_id and 64 lowercase hex characters`
      )
    }
    if (raw && !validateReturnToUrl(raw)) {
      console.warn(
        `[auth] returnTo rejected: ${raw} — expected an authorize URL on ${API_BASE_URL}`
      )
    }
  }

  return validateReturnToUrl(raw)
}

/**
 * The query string an accounts link carries for a pending request: the handle
 * shape for a resume URL, the legacy `returnTo` for an authorize URL, and
 * nothing for none. Every accounts link that keeps the request writes it with
 * this, so the shape the server chose is the shape that travels on.
 *
 * `skip_organization` is never written: the resume that receives it persists the
 * choice in the stored request before any page could need it again.
 */
export function toAccountsSearch(returnTo: string | null | undefined): string {
  const valid = validateReturnToUrl(returnTo)
  if (!valid) return ""

  const url = new URL(valid)
  if (RESUME_PATH_RE.test(url.pathname)) {
    // Validated above: both are present, and the handle is 64 hex.
    return handleQuery(
      url.searchParams.get(CLIENT_ID_PARAM)!,
      url.searchParams.get(HANDLE_PARAM)!
    )
  }

  return `?${LEGACY_PARAM}=${encodeURIComponent(valid)}`
}

/**
 * The same pending request without the organization step ("continue without an
 * organization"): a resume URL gains `skip_organization=true`, which the server
 * persists in the stored request; a legacy authorize URL loses
 * `create_organization`, and the authorize endpoint then issues the code without
 * asking. Everything else the relying party sent is kept.
 */
export function withoutOrganizationRequest(returnTo: string): string {
  const url = new URL(returnTo)
  if (RESUME_PATH_RE.test(url.pathname)) {
    url.searchParams.set(SKIP_ORGANIZATION_PARAM, "true")
  } else {
    url.searchParams.delete(CREATE_ORGANIZATION_PARAM)
  }
  return url.toString()
}

/**
 * Extracts the client_id from a validated pending request, used solely to fetch
 * the app's public branding. Both shapes carry it as `client_id`. Branding data
 * itself is NEVER read from the URL — only this identifier, resolved against the
 * database server-side; and a resume refuses a client id that is not the stored
 * request's.
 */
export function getReturnToClientId(returnTo: string | null): string | null {
  if (!returnTo) return null

  try {
    return new URL(returnTo).searchParams.get("client_id")
  } catch {
    return null
  }
}
