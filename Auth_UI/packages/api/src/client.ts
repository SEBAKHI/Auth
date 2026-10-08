import createClient, { type Middleware } from "openapi-fetch"

import { API_BASE_URL } from "@authsystem/api/env"
import { DEVICE_ID_HEADER, getDeviceId } from "@authsystem/api/device-id"
import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"
import { readProblem } from "@authsystem/api/errors"
import i18n from "@authsystem/i18n"
import {
  emitSessionExpired,
  hasFreshAccessToken,
  publishAccessToken,
  startTabSync,
  waitForBroadcastAccessToken,
  withRefreshLock,
} from "@authsystem/api/tab-sync"
import {
  REFRESH_SENTINEL,
  clearLogoutPending,
  clearRefreshPending,
  clearRefreshSpending,
  clearTokens,
  confirmCookie,
  currentGeneration,
  getAccessToken,
  getLogoutPendingSession,
  getRefreshToken,
  hasSession,
  isLogoutPending,
  legacyRefreshToken,
  markRefreshPending,
  markRefreshSpending,
  noteCookieMissing,
  setAccessToken,
  setTokens,
} from "@authsystem/api/token-store"
import type { paths, Schemas } from "./types"

const REFRESH_PATH = "/api/v1/Auth/refresh"
const COOKIE_LOGOUT_PATH = "/api/v1/Auth/logout/cookie"
const LOGIN_PATH = "/api/v1/Auth/login"
const TWO_FACTOR_VERIFY_PATH = "/api/v1/auth/2fa/verify"
// Verify-first sign-up: the three steps of creating an account, the last of
// which mints the session. All three are made by someone who has none.
const REGISTRATION_START_PATH = "/api/v1/Auth/registration/start"
const REGISTRATION_VERIFY_PATH = "/api/v1/Auth/registration/verify"
const REGISTRATION_COMPLETE_PATH = "/api/v1/Auth/registration/complete"
// An application's organization-creation step. Authenticated by the single
// sign-on cookie ALONE, so it acts for the user the authorize endpoint sees: the
// SPA's bearer would be ignored, and a 401 there means "no SSO session", not
// "this tab's token expired" - refreshing on it would end a session that is fine.
const ORGANIZATION_SETUP_PATH = "/api/v1/Auth/organization-setup"

export { SESSION_EXPIRED_EVENT } from "@authsystem/api/tab-sync"

/**
 * Error codes that mean the refresh token itself is finished, so keeping it can
 * only lead to replaying it. Keyed on the problem's `code` and never on the
 * status class: `Auth.ApplicationInactive` is also a 403 but leaves the
 * token perfectly valid, and a 429 from a CDN or WAF is indistinguishable from
 * an application 4xx by status alone — treating those as final would sign the
 * whole fleet out during a traffic spike. Anything not listed here (unparseable
 * body, 429, 5xx, transport failure) is "unknown": keep the token, do not
 * replay it.
 */
const FINAL_REFRESH_REJECTIONS = new Set<PublishedErrorCode>([
  "Auth.TokenRevoked",
  "Auth.RefreshTokenRevoked",
  "Auth.RefreshTokenNotFound",
  "Auth.RefreshTokenExpired",
  "User.NotFound",
  "User.AccountLocked",
  "User.AccountLockedUntil",
])

/** De-duplicates concurrent refreshes within this tab into one lock acquisition. */
let refreshPromise: Promise<boolean> | null = null

async function finalRejectionCode(response: Response): Promise<PublishedErrorCode | null> {
  const { code } = await readProblem(response)
  return FINAL_REFRESH_REJECTIONS.has(code as PublishedErrorCode)
    ? (code as PublishedErrorCode)
    : null
}

/**
 * The raw refresh request. `legacyToken` goes in the body when the session still
 * holds a real token (legacy mode, or a session about to migrate); otherwise the
 * body is `{}` and the browser attaches the app's HttpOnly refresh cookie, which
 * `credentials: "include"` makes it send and store across origins.
 */
function sendRefresh(legacyToken: string | null): Promise<Response> {
  return fetch(`${API_BASE_URL}${REFRESH_PATH}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "Accept-Language": i18n.language,
    },
    credentials: "include",
    body: JSON.stringify(legacyToken ? { refreshToken: legacyToken } : {}),
  })
}

/**
 * A failed response rewritten as its problem with the transport status in it
 * ({@link readProblem}), so every error a call returns - including an empty 401
 * from the authentication layer or a proxy's HTML 502 - is an object that
 * classifies by what actually happened. Headers such as Retry-After survive.
 */
async function withTransportStatus(response: Response): Promise<Response> {
  const headers = new Headers(response.headers)
  headers.set("Content-Type", "application/problem+json")
  headers.delete("Content-Length")
  headers.delete("Content-Encoding")

  return new Response(JSON.stringify(await readProblem(response)), {
    status: response.status,
    statusText: response.statusText,
    headers,
  })
}

/**
 * Refreshes the session for a new pair. Only ever called while holding the
 * cross-tab refresh lock. The mode is read from storage HERE, inside the lock,
 * on every refresh — never cached — so a tab whose session another tab just
 * migrated sends `{}` rather than a token that is already spent:
 *   - a real token stored: send it in the body (legacy mode, or the one-time
 *     migration of a session stored before the cookie);
 *   - the sentinel, or storage unreadable: send `{}` and let the cookie ride.
 */
async function performRefresh(generation: number): Promise<boolean> {
  const legacyToken = legacyRefreshToken()
  const viaCookie = legacyToken === null

  // Recorded before the request so that a context which dies mid-flight can
  // tell, on its next load, that it spent the session without learning the
  // outcome — see reconcilePendingRefresh(). The cookie marker holds no secret.
  if (legacyToken) markRefreshPending(legacyToken)
  else markRefreshSpending()

  let res: Response | null = null
  try {
    res = await sendRefresh(legacyToken)
  } catch {
    // Transport failure: the network failed, not the credential. In cookie mode
    // try once more, straight away and still inside the lock: either the first
    // attempt never reached the server, or it did and this one arrives within
    // the server's replay grace window (or loses the race to it and is given a
    // sibling token). A body token is never re-sent: if the first request
    // rotated it, the replay would be reported as theft.
    if (viaCookie) {
      try {
        res = await sendRefresh(null)
      } catch {
        res = null
      }
    }
  } finally {
    // Any settled fetch means this context is alive and has handled the
    // outcome. The markers exist only for the case where no handler ever ran.
    clearRefreshPending()
    clearRefreshSpending()
  }

  // Still no answer: keep the session, fail this refresh.
  if (!res) return false

  if (!res.ok) {
    const code = await finalRejectionCode(res)
    if (code) {
      // The first refresh of a cookie sign-in found no cookie: the browser
      // refused to store or send it. Remembered so the login page can say so.
      if (viaCookie && code === "Auth.RefreshTokenNotFound") noteCookieMissing()
      clearTokens()
    }
    return false
  }

  let data: Schemas["TokenResponse"]
  try {
    data = (await res.json()) as Schemas["TokenResponse"]
  } catch {
    return false
  }
  if (!data?.accessToken || !data?.refreshToken) return false

  // Drops the result if the session was torn down while we held the lock,
  // rather than resurrecting a session the user just ended. The refresh value
  // is stored as delivered: the sentinel (cookie mode, including a migration
  // that just moved a stored token into the cookie) or a real token (legacy).
  if (!setTokens(data.accessToken, data.refreshToken, generation)) return false
  if (viaCookie && data.refreshToken === REFRESH_SENTINEL) confirmCookie()

  publishAccessToken(data.accessToken)
  return true
}

/**
 * Refreshes the token pair, serialised across every tab of this origin.
 *
 * The refresh token ROTATES on use and the server treats a second presentation
 * as theft, so callers must never race their own refresh — always go through
 * this. The in-tab promise below collapses a burst of 401s; the lock inside
 * withRefreshLock() collapses the tabs.
 */
export function sharedRefresh(): Promise<boolean> {
  if (refreshPromise) return refreshPromise

  // Captured before queueing so that, once we hold the lock, we can tell
  // whether another context rotated while we waited.
  const observed = getRefreshToken()
  const generation = currentGeneration()

  refreshPromise = withRefreshLock(async () => {
    // A tab that rotated while we queued broadcasts its access token; adopting
    // it is what makes concurrent tabs cost one network refresh, not N.
    if (hasFreshAccessToken()) return true
    if (!hasSession()) return false

    if (getRefreshToken() !== observed) {
      // Someone rotated under us, so their access token is already in flight.
      // Missing it is not a failure — we simply spend the CURRENT session below,
      // which is a legitimate rotation rather than a reuse. (In cookie mode the
      // stored value never changes and the browser always sends the newest
      // cookie in its jar, so this branch is legacy mode's alone.)
      await waitForBroadcastAccessToken()
      if (hasFreshAccessToken()) return true
      if (!hasSession()) return false
    }

    return performRefresh(generation)
  }).finally(() => {
    refreshPromise = null
  })

  return refreshPromise
}

/**
 * Refreshes now, even though the access token this tab holds is still valid, and
 * resolves true once a new one is stored.
 *
 * For the moment the session changed on the server while its token did not: a
 * second factor proved inside the session (step-up, or switching two-factor on)
 * — the platform authority a withheld token lacks comes back only with a newly
 * minted one. Rides the same serialised refresh as everything else
 * ({@link sharedRefresh}): it waits for a refresh already under way, then drops
 * this tab's token so the lock holder does not answer "still fresh". A token
 * another tab rotated meanwhile is adopted instead, which is right too: it was
 * minted after the change.
 *
 * A refusal that ends the session ends it here, as a request's would. An
 * unknown outcome (network, 5xx) keeps the session and the token it had, and
 * resolves false: the caller may try again.
 */
export async function refreshSessionNow(): Promise<boolean> {
  if (refreshPromise) {
    await refreshPromise.catch(() => false)
  }

  const previous = getAccessToken()
  setAccessToken(null)

  if (await sharedRefresh()) return true

  if (!hasSession()) {
    emitSessionExpired()
    return false
  }

  // Nothing settled: put back the token this tab had, unless another tab's
  // newer one arrived while we waited.
  if (getAccessToken() === null && previous) setAccessToken(previous)
  return false
}

/**
 * Returns an access token that is not known to be expired, refreshing first
 * when needed. Non-client callers (raw fetch, e.g. multipart uploads) must use
 * this instead of reading the token store directly, or they will send stale
 * tokens after the access-token lifetime.
 *
 * Ends the session when the refresh fails, instead of returning null and
 * leaving the caller to fire an unauthenticated request whose 401 would trigger
 * a second refresh with the same dead token.
 */
export async function ensureFreshAccessToken(): Promise<string | null> {
  if (hasFreshAccessToken()) return getAccessToken()
  if (!hasSession()) return null

  if (!(await sharedRefresh())) {
    emitSessionExpired()
    return null
  }

  return getAccessToken()
}

/** What a cookie sign-out did (see endCookieSession). */
export type CookieSignOutOutcome =
  /** The cookie's session is over: ended now, or already dead. */
  | "ended"
  /** The cookie belongs to another session (a newer sign-in); it was left alone. */
  | "kept"
  /** No answer that settles it (network, rate limit, 5xx): try again later. */
  | "unknown"

/**
 * Signs this browser out with its HttpOnly refresh cookie alone, no bearer.
 *
 * The bearer sign-out answers 401 before anything runs once the access token
 * has expired or was refused (an idle tab whose last refresh failed), and the
 * cookie would then outlive a sign-out the screen reports as done. `sessionId`
 * binds it to the session the user meant to end: when the cookie now belongs to
 * another session, the server keeps it.
 *
 * A raw fetch: it may run under the refresh lock (completePendingLogout), where
 * the typed client's middleware would re-enter it.
 */
export async function endCookieSession(sessionId: string | null): Promise<CookieSignOutOutcome> {
  let res: Response
  try {
    res = await fetch(`${API_BASE_URL}${COOKIE_LOGOUT_PATH}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Accept-Language": i18n.language,
      },
      credentials: "include",
      body: JSON.stringify(sessionId ? { sessionId } : {}),
    })
  } catch {
    return "unknown"
  }

  if (res.ok) {
    try {
      const body = (await res.json()) as { ended?: boolean }
      return body?.ended === false ? "kept" : "ended"
    } catch {
      return "ended"
    }
  }

  // A refusal that retrying cannot change (no first-party list: legacy mode,
  // where the sign-out already took the token out of storage) settles it too.
  return res.status === 429 || res.status >= 500 ? "unknown" : "ended"
}

/**
 * Finishes a sign-out whose request never reached the server, before the app
 * shows anything signed in (AuthProvider awaits it ahead of its bootstrap).
 *
 * In cookie mode, clearing local state no longer ends a session: the refresh
 * cookie lives on in the browser. So a sign-out that failed on the network
 * leaves `auth.logoutPending` (with the session it was for), and this ends that
 * session with the cookie, under the refresh lock so no refresh races it. If the
 * session key is still here (the tab was closed mid sign-out) and the server
 * ended it, the local session goes too. Anything unknown keeps the marker for
 * the next load.
 */
export async function completePendingLogout(): Promise<void> {
  if (!isLogoutPending()) return

  await withRefreshLock(async () => {
    if (!isLogoutPending()) return

    const sessionId = getLogoutPendingSession()
    // Without a session to bind to, a session in storage may be a newer sign-in.
    if (!sessionId && hasSession()) {
      clearLogoutPending()
      return
    }

    const outcome = await endCookieSession(sessionId)
    if (outcome === "unknown") return

    clearLogoutPending()
    if (outcome === "ended" && hasSession()) clearTokens()
  })
}

/**
 * The endpoints that establish a session and therefore carry no bearer token.
 *
 * Matched on the whole path, case-insensitively — NOT as a substring. A
 * substring test made "/api/v1/Auth/login-history" an auth-flow request because
 * it starts with the login path, so the client sent it with no Authorization
 * header, took the inevitable 401, and skipped the retry as well. Any future
 * route beginning with one of these words would have hit the same trap, and it
 * fails as a bare 401 with nothing pointing at the cause.
 *
 * Case-insensitive because the API's own routes are inconsistent: the login and
 * refresh paths capitalise the controller ("/api/v1/Auth/…") while the
 * two-factor ones do not ("/api/v1/auth/2fa/…"). Under the old exact-case
 * substring test the two-factor constant never matched anything at all.
 */
const ANONYMOUS_PATHS = new Set(
  [
    REFRESH_PATH,
    LOGIN_PATH,
    TWO_FACTOR_VERIFY_PATH,
    REGISTRATION_START_PATH,
    REGISTRATION_VERIFY_PATH,
    REGISTRATION_COMPLETE_PATH,
    ORGANIZATION_SETUP_PATH,
  ].map((path) => path.toLowerCase())
)

function isAuthFlow(url: string): boolean {
  let pathname: string
  try {
    pathname = new URL(url, API_BASE_URL).pathname
  } catch {
    return false
  }

  return ANONYMOUS_PATHS.has(pathname.toLowerCase())
}

const authMiddleware: Middleware = {
  async onRequest({ request }) {
    // Culture signal for backend localization (errors, validation, emails) —
    // sent on every request, including the anonymous login flow.
    request.headers.set("Accept-Language", i18n.language)

    // Which browser this is. A transport header rather than a body field,
    // because it describes the client and not the command: it used to ride in
    // the body of each request that mints a session, which made every such
    // endpoint responsible for remembering it. verify-email did not, so the
    // sign-in that completes registration was recorded under a signature
    // derived from an EMPTY device id, and the next real login — which did send
    // one — hashed to something else and was filed as a second browser, with a
    // "new device" email to match. Sent here, beside Accept-Language and above
    // the auth-flow early return, no endpoint can omit it and none has to know
    // about it. The IP and user agent already worked this way; this was the one
    // client fact that did not.
    const deviceId = getDeviceId()
    if (deviceId) {
      request.headers.set(DEVICE_ID_HEADER, deviceId)
    }

    if (isAuthFlow(request.url)) return request

    // Proactively refresh an expired/missing access token so requests rarely 401.
    const token = await ensureFreshAccessToken()

    if (token) {
      request.headers.set("Authorization", `Bearer ${token}`)
    }
    return request
  },

  async onResponse({ request, response }) {
    if (response.ok || request.method === "HEAD") return response
    const failure = await withTransportStatus(response)
    if (response.status !== 401 || isAuthFlow(request.url)) return failure

    // We presented no token, so this 401 was a foregone conclusion and there is
    // nothing to retry. Refreshing here would spend the same dead token a
    // second time — the exact pattern the server reports as reuse, and the
    // reason a single failed refresh used to produce two "reuse" warnings.
    if (!request.headers.has("Authorization")) {
      emitSessionExpired()
      return failure
    }

    // The token was rejected (e.g. revoked). Try one refresh so a query retry
    // succeeds; if refresh is impossible, end the session.
    if (hasSession()) {
      const ok = await sharedRefresh()
      if (!ok) emitSessionExpired()
    } else {
      emitSessionExpired()
    }
    return failure
  },
}

/**
 * The single, fully typed API client used across the app. Credentials are
 * included so the HttpOnly IdP session cookie is set at login and cleared at
 * logout (CORS restricts this to the explicitly allowed origins).
 */
export const api = createClient<paths>({
  baseUrl: API_BASE_URL,
  credentials: "include",
})
api.use(authMiddleware)

// Must run before anything reads the token store: it recovers from a refresh
// whose context died mid-flight, and asks the other tabs for a live access
// token so this one can skip its startup refresh entirely.
startTabSync()
