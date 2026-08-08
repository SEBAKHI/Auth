import createClient, { type Middleware } from "openapi-fetch"

import { API_BASE_URL } from "@authsystem/api/env"
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
  clearRefreshPending,
  clearTokens,
  currentGeneration,
  getAccessToken,
  getRefreshToken,
  markRefreshPending,
  setTokens,
} from "@authsystem/api/token-store"
import type { paths, Schemas } from "./types"

const REFRESH_PATH = "/api/v1/Auth/refresh"
const LOGIN_PATH = "/api/v1/Auth/login"
const TWO_FACTOR_VERIFY_PATH = "/api/v1/auth/2fa/verify"

export { SESSION_EXPIRED_EVENT } from "@authsystem/api/tab-sync"

/**
 * Error codes that mean the refresh token itself is finished, so keeping it can
 * only lead to replaying it. Keyed on the code (ProblemDetails.title) and never
 * on the status class: `Auth.ApplicationInactive` is also a 403 but leaves the
 * token perfectly valid, and a 429 from a CDN or WAF is indistinguishable from
 * an application 4xx by status alone — treating those as final would sign the
 * whole fleet out during a traffic spike. Anything not listed here (unparseable
 * body, 429, 5xx, transport failure) is "unknown": keep the token, do not
 * replay it.
 */
const FINAL_REFRESH_REJECTIONS = new Set([
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

async function isFinalRejection(response: Response): Promise<boolean> {
  try {
    const problem = (await response.json()) as { title?: string } | null
    return (
      typeof problem?.title === "string" &&
      FINAL_REFRESH_REJECTIONS.has(problem.title)
    )
  } catch {
    return false
  }
}

/**
 * Spends `refreshToken` for a new pair. Only ever called while holding the
 * cross-tab refresh lock, with a token read from storage inside that lock.
 */
async function performRefresh(
  refreshToken: string,
  generation: number
): Promise<boolean> {
  // Recorded before the request so that a context which dies mid-flight can
  // tell, on its next load, that it spent this token without learning the
  // outcome — see reconcilePendingRefresh().
  markRefreshPending(refreshToken)

  let res: Response
  try {
    res = await fetch(`${API_BASE_URL}${REFRESH_PATH}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Accept-Language": i18n.language,
      },
      credentials: "include",
      body: JSON.stringify({ refreshToken }),
    })
  } catch {
    // Transport failure: the network failed, not the credential. Keep the token.
    return false
  } finally {
    // Any settled fetch means this context is alive and has handled the
    // outcome. The marker exists only for the case where no handler ever ran.
    clearRefreshPending()
  }

  if (!res.ok) {
    if (await isFinalRejection(res)) clearTokens()
    return false
  }

  let data: Schemas["TokenResponse"] | null = null
  try {
    data = (await res.json()) as Schemas["TokenResponse"]
  } catch {
    return false
  }
  if (!data?.accessToken || !data?.refreshToken) return false

  // Drops the result if the session was torn down while we held the lock,
  // rather than resurrecting a session the user just ended.
  if (!setTokens(data.accessToken, data.refreshToken, generation)) return false

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

    let current = getRefreshToken()
    if (!current) return false

    if (current !== observed) {
      // Someone rotated under us, so their access token is already in flight.
      // Missing it is not a failure — we simply spend the CURRENT token below,
      // which is a legitimate rotation rather than a reuse.
      await waitForBroadcastAccessToken()
      if (hasFreshAccessToken()) return true

      current = getRefreshToken()
      if (!current) return false
    }

    return performRefresh(current, generation)
  }).finally(() => {
    refreshPromise = null
  })

  return refreshPromise
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
  if (!getRefreshToken()) return null

  if (!(await sharedRefresh())) {
    emitSessionExpired()
    return null
  }

  return getAccessToken()
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
  [REFRESH_PATH, LOGIN_PATH, TWO_FACTOR_VERIFY_PATH].map((path) =>
    path.toLowerCase()
  )
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

    if (isAuthFlow(request.url)) return request

    // Proactively refresh an expired/missing access token so requests rarely 401.
    const token = await ensureFreshAccessToken()

    if (token) {
      request.headers.set("Authorization", `Bearer ${token}`)
    }
    return request
  },

  async onResponse({ request, response }) {
    if (response.status !== 401 || isAuthFlow(request.url)) return response

    // We presented no token, so this 401 was a foregone conclusion and there is
    // nothing to retry. Refreshing here would spend the same dead token a
    // second time — the exact pattern the server reports as reuse, and the
    // reason a single failed refresh used to produce two "reuse" warnings.
    if (!request.headers.has("Authorization")) {
      emitSessionExpired()
      return response
    }

    // The token was rejected (e.g. revoked). Try one refresh so a query retry
    // succeeds; if refresh is impossible, end the session.
    if (getRefreshToken()) {
      const ok = await sharedRefresh()
      if (!ok) emitSessionExpired()
    } else {
      emitSessionExpired()
    }
    return response
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
