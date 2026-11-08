import createClient, { type Middleware } from "openapi-fetch"

import { API_BASE_URL } from "@authsystem/api/env"
import { DEVICE_ID_HEADER, getDeviceId } from "@authsystem/api/device-id"
import type { PublishedErrorCode } from "@authsystem/api/error-codes.generated"
import { readProblem } from "@authsystem/api/errors"
import { MFA_REQUIRED_EVENT } from "@authsystem/api/mfa-requirement"
import { retryAfterSeconds } from "@authsystem/api/retry-after"
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
  getRefusedRefresh,
  hasSession,
  isForcedRefresh,
  isLogoutPending,
  legacyRefreshToken,
  markForcedRefresh,
  markRefreshPending,
  markRefreshSpending,
  noteCookieMissing,
  noteTokenAccepted,
  recordRefusedRefresh,
  setTokens,
} from "@authsystem/api/token-store"
import type { paths, Schemas } from "./types"

const REFRESH_PATH = "/api/v1/Auth/refresh"
const COOKIE_LOGOUT_PATH = "/api/v1/Auth/logout/cookie"
const LOGIN_PATH = "/api/v1/Auth/login"
const TWO_FACTOR_VERIFY_PATH = "/api/v1/auth/2fa/verify"
// The emailed second factor's code at sign-in (S24): anonymous like verify, and
// its expired-challenge answer is a 401 that must not read as an expired session.
const TWO_FACTOR_SIGN_IN_EMAIL_CODE_PATH = "/api/v1/auth/2fa/verify/email-code"
// Verify-first sign-up: the three steps of creating an account, the last of
// which mints the session. All three are made by someone who has none.
const REGISTRATION_START_PATH = "/api/v1/Auth/registration/start"
const REGISTRATION_VERIFY_PATH = "/api/v1/Auth/registration/verify"
const REGISTRATION_COMPLETE_PATH = "/api/v1/Auth/registration/complete"
// Sign-in by emailed code (OI-215): the address, the code, then the sign-in or
// the grace-period recovery it leads to. All four are made by someone who has no
// session yet, and the last two mint one.
const EMAIL_CODE_START_PATH = "/api/v1/Auth/email-code/start"
const EMAIL_CODE_VERIFY_PATH = "/api/v1/Auth/email-code/verify"
const EMAIL_CODE_SIGN_IN_PATH = "/api/v1/Auth/email-code/sign-in"
const EMAIL_CODE_RECOVER_PATH = "/api/v1/Auth/email-code/recover"
// An application's organization-creation step. Authenticated by the single
// sign-on cookie ALONE, so it acts for the user the authorize endpoint sees: the
// SPA's bearer would be ignored, and a 401 there means "no SSO session", not
// "this tab's token expired" - refreshing on it would end a session that is fine.
const ORGANIZATION_SETUP_PATH = "/api/v1/Auth/organization-setup"
// The account bar of linked applications: authenticated by the single sign-on cookie, so
// its 401 says nothing about this tab's token. Its frames never load this client
// (apps/accounts/src/embed/embed-api.ts); listed so no page that does can refresh on it.
const ACCOUNT_BAR_PATH = "/api/v1/Auth/account-bar"
// A sign-in with a passkey (S20): its options, the sign-in itself, and the recovery
// of an account waiting for deletion. Made by someone who has no session yet (the
// two-factor page makes them from a session it is about to replace): the passkey
// is the credential, so a bearer here would only spend a refresh for nothing. None
// answers 401 for a dead token, so none may end this tab's session.
const PASSKEY_ASSERTION_OPTIONS_PATH = "/api/v1/auth/passkeys/assertion/options"
const PASSKEY_ASSERTION_VERIFY_PATH = "/api/v1/auth/passkeys/assertion/verify"
const PASSKEY_ASSERTION_RECOVER_PATH = "/api/v1/auth/passkeys/assertion/recover"

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

/** What a refresh came to. */
export type RefreshResult =
  /** A usable access token is stored: refreshed now, or adopted from another tab or request. */
  | { status: "fresh" }
  /**
   * No new token. The session is over only when the server said so with a code
   * from FINAL_REFRESH_REJECTIONS, which already cleared it (hasSession() is
   * false then); after anything else it is kept as it was.
   */
  | { status: "failed" }
  /**
   * The server is rate-limiting refreshes (429). Nothing was spent, ended or
   * cleared, and the access token this tab holds stays until it expires; the
   * caller may try again after `retryAfterSeconds`, when the server said.
   */
  | { status: "deferred"; retryAfterSeconds: number | null }

export interface RefreshOptions {
  /**
   * The access token the server just refused (S12): refresh even though it has
   * not expired. A token can be refused while unexpired — its signing key was
   * withdrawn, its session ended, its authority changed — and waiting for its
   * expiry would leave every request failing until then. `null` when this tab
   * holds none. The refused value itself is never adopted back, and a value
   * that a forced refresh produced is not forced again until a request with it
   * succeeds.
   */
  rejected?: string | null
}

const FRESH: RefreshResult = { status: "fresh" }
const FAILED: RefreshResult = { status: "failed" }

/** How long a 429 on a forced refresh holds the next attempt off when it names no wait. */
const DEFAULT_RETRY_AFTER_SECONDS = 30

/**
 * The outcome recorded for a refused token whose forced refresh already failed
 * or was deferred, or null when a forced refresh may go ahead (S12, one forced
 * refresh per refused value). After a failure, none: the ordinary refresh at
 * the token's expiry takes over. After a 429, exactly one more, no earlier than
 * the wait the server asked for.
 */
function recordedRefusal(rejected: string, now = Date.now()): RefreshResult | null {
  const record = getRefusedRefresh(rejected)
  if (!record) return null
  if (record.outcome === "failed") return FAILED
  if (now < record.notBefore) {
    return { status: "deferred", retryAfterSeconds: Math.ceil((record.notBefore - now) / 1000) }
  }
  return record.retried ? { status: "deferred", retryAfterSeconds: null } : null
}

/** Records what a forced refresh of `rejected` came to, when it produced no new token. */
function recordForcedOutcome(rejected: string, result: RefreshResult, now = Date.now()): void {
  // A final refusal cleared the session, and the record with it.
  if (!hasSession()) return
  if (result.status === "failed") {
    recordRefusedRefresh(rejected, { outcome: "failed" })
  } else if (result.status === "deferred") {
    const previous = getRefusedRefresh(rejected)
    recordRefusedRefresh(rejected, {
      outcome: "deferred",
      notBefore: now + (result.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS) * 1000,
      retried: previous?.outcome === "deferred",
    })
  }
}

/** De-duplicates concurrent refreshes within this tab into one lock acquisition. */
let refreshPromise: Promise<RefreshResult> | null = null

/** The bearer token a request carried, or null when it carried none. */
function bearerOf(request: Request): string | null {
  const header = request.headers.get("Authorization")
  return header?.startsWith("Bearer ") ? header.slice("Bearer ".length) : null
}

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
 * A 403 for the platform authority the server withholds until this session
 * proves a second factor (S08) is not an ordinary refusal: the page asked for
 * something the account may do, once the session completes the step. Says so,
 * and leaves the failure to its caller as it is.
 */
async function announceMfaRequirement(failure: Response): Promise<void> {
  const { code } = await readProblem(failure.clone())
  if (code === "TwoFactor.RequiredByPolicy") {
    window.dispatchEvent(new Event(MFA_REQUIRED_EVENT))
  }
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
async function performRefresh(generation: number, forced: boolean): Promise<RefreshResult> {
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
  if (!res) return FAILED

  if (!res.ok) {
    // Rate-limited: the server spent nothing, so neither does this tab. Said as
    // such, so no caller reads it as a reason to end the session.
    if (res.status === 429) {
      return { status: "deferred", retryAfterSeconds: retryAfterSeconds(res) }
    }

    const code = await finalRejectionCode(res)
    if (code) {
      // The first refresh of a cookie sign-in found no cookie: the browser
      // refused to store or send it. Remembered so the login page can say so.
      if (viaCookie && code === "Auth.RefreshTokenNotFound") noteCookieMissing()
      clearTokens()
    }
    return FAILED
  }

  let data: Schemas["TokenResponse"]
  try {
    data = (await res.json()) as Schemas["TokenResponse"]
  } catch {
    return FAILED
  }
  if (!data?.accessToken || !data?.refreshToken) return FAILED

  // Drops the result if the session was torn down while we held the lock,
  // rather than resurrecting a session the user just ended. The refresh value
  // is stored as delivered: the sentinel (cookie mode, including a migration
  // that just moved a stored token into the cookie) or a real token (legacy).
  if (!setTokens(data.accessToken, data.refreshToken, generation)) return FAILED
  if (viaCookie && data.refreshToken === REFRESH_SENTINEL) confirmCookie()

  if (forced) markForcedRefresh(data.accessToken)
  publishAccessToken(data.accessToken, forced)
  return FRESH
}

/**
 * True when this tab holds a usable access token other than `rejected`: one
 * another tab or request obtained while this caller queued. The refused value
 * itself never counts, however fresh its expiry says it is.
 */
function holdsReplacementFor(rejected: string | null): boolean {
  const stored = getAccessToken()
  return stored !== null && stored !== rejected && hasFreshAccessToken()
}

/**
 * Refreshes the token pair, serialised across every tab of this origin. The one
 * refresh primitive: every caller goes through it.
 *
 * The refresh token ROTATES on use and the server treats a second presentation
 * as theft, so callers must never race their own refresh — always go through
 * this. The in-tab promise below collapses a burst of 401s; the lock inside
 * withRefreshLock() collapses the tabs.
 *
 * With `rejected` (S12) it refreshes although the access token has not
 * expired, once per refused value across the origin: whoever holds the lock
 * first refreshes, and every other caller refused on the same token adopts the
 * result instead. See RefreshOptions.
 */
export function sharedRefresh(options: RefreshOptions = {}): Promise<RefreshResult> {
  return refreshFor(options, true)
}

/**
 * The body of {@link sharedRefresh}. `honourRecord` false is step-up's: it
 * neither reads nor writes the record of refused tokens, so a step-up the
 * server could not answer can be tried again at once, as before.
 */
function refreshFor(options: RefreshOptions, honourRecord: boolean): Promise<RefreshResult> {
  const forced = options.rejected !== undefined
  const rejected = options.rejected ?? null

  if (refreshPromise) {
    // An ordinary caller shares the refresh under way. A forced one waits for it
    // and decides again: it may have replaced the refused token (adopted below),
    // or been an ordinary refresh that found that token still "fresh".
    if (!forced) return refreshPromise
    return refreshPromise.catch(() => FAILED).then(() => refreshFor(options, honourRecord))
  }

  // Captured before queueing so that, once we hold the lock, we can tell
  // whether another context rotated while we waited.
  const observed = getRefreshToken()
  const generation = currentGeneration()

  refreshPromise = withRefreshLock(async (): Promise<RefreshResult> => {
    // A tab that rotated while we queued broadcasts its access token; adopting
    // it is what makes concurrent tabs cost one network refresh, not N.
    if (forced) {
      if (holdsReplacementFor(rejected)) return FRESH
      // The refused token is itself the product of a forced refresh that no
      // request has succeeded with: its refusal is not about its age, and
      // forcing again would spend a refresh per request. The session stays; it
      // refreshes as usual once that token expires.
      if (rejected !== null && isForcedRefresh(rejected)) return FAILED
      // One forced refresh per refused value: this one already failed or was
      // deferred, in this tab or another. Answered as it was, with no request.
      if (honourRecord && rejected !== null) {
        const recorded = recordedRefusal(rejected)
        if (recorded) return recorded
      }
    } else if (hasFreshAccessToken()) {
      return FRESH
    }
    if (!hasSession()) return FAILED

    if (getRefreshToken() !== observed) {
      // Someone rotated under us, so their access token is already in flight.
      // Missing it is not a failure — we simply spend the CURRENT session below,
      // which is a legitimate rotation rather than a reuse. (In cookie mode the
      // stored value never changes and the browser always sends the newest
      // cookie in its jar, so this branch is legacy mode's alone.)
      await waitForBroadcastAccessToken()
      if (forced ? holdsReplacementFor(rejected) : hasFreshAccessToken()) return FRESH
      if (!hasSession()) return FAILED
    }

    const result = await performRefresh(generation, forced)
    if (forced && honourRecord && rejected !== null) recordForcedOutcome(rejected, result)
    return result
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
 * minted one. A caller of the one primitive ({@link sharedRefresh}), with the
 * token this tab holds as the refused one: it waits for a refresh already under
 * way (whose request may predate the change), then refuses whatever token this
 * tab holds after it. A token another tab rotated meanwhile is adopted instead,
 * which is right too: it was minted after the change. The token is never
 * dropped meanwhile, so the tab is never left without one.
 *
 * A refusal that ends the session ends it here, as a request's would. An
 * unknown outcome (network, 5xx) or a rate limit keeps the session and the
 * token it had, and resolves false: the caller may try again.
 */
export async function refreshSessionNow(): Promise<boolean> {
  if (refreshPromise) {
    await refreshPromise.catch(() => FAILED)
  }

  const result = await refreshFor({ rejected: getAccessToken() }, false)
  if (result.status === "fresh") return true

  if (result.status === "failed" && !hasSession()) emitSessionExpired()
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

  if ((await sharedRefresh()).status !== "fresh") {
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
    TWO_FACTOR_SIGN_IN_EMAIL_CODE_PATH,
    REGISTRATION_START_PATH,
    REGISTRATION_VERIFY_PATH,
    REGISTRATION_COMPLETE_PATH,
    EMAIL_CODE_START_PATH,
    EMAIL_CODE_VERIFY_PATH,
    EMAIL_CODE_SIGN_IN_PATH,
    EMAIL_CODE_RECOVER_PATH,
    ORGANIZATION_SETUP_PATH,
    ACCOUNT_BAR_PATH,
    PASSKEY_ASSERTION_OPTIONS_PATH,
    PASSKEY_ASSERTION_VERIFY_PATH,
    PASSKEY_ASSERTION_RECOVER_PATH,
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
    //
    // On commands only: every endpoint that reads it is a POST, and a custom
    // header turns a cross-origin request into one the browser must clear with
    // an OPTIONS preflight first. On a GET it bought nothing and cost every
    // anonymous read of a sign-in page (branding, sign-in providers) one more
    // round trip before it could start.
    const deviceId = getDeviceId()
    if (deviceId && request.method !== "GET" && request.method !== "HEAD") {
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
    if (response.ok) {
      // A token that a forced refresh produced has now been accepted (S12).
      const presented = bearerOf(request)
      if (presented) noteTokenAccepted(presented)
      return response
    }
    if (request.method === "HEAD") return response
    const failure = await withTransportStatus(response)
    if (response.status === 403) await announceMfaRequirement(failure)
    if (response.status !== 401 || isAuthFlow(request.url)) return failure

    // We presented no token, so this 401 was a foregone conclusion and there is
    // nothing to retry. Refreshing here would spend the same dead token a
    // second time — the exact pattern the server reports as reuse, and the
    // reason a single failed refresh used to produce two "reuse" warnings.
    if (!request.headers.has("Authorization")) {
      emitSessionExpired()
      return failure
    }

    // The token was refused, perhaps before it expired: its signing key was
    // withdrawn, or its session ended (S12). Force one refresh for that value so
    // a query retry succeeds. Only the refresh's own answer ends the session —
    // a final code there has already cleared it; an unknown outcome, a rate
    // limit, or a refused token this origin already force-refreshed keeps it.
    if (!hasSession()) {
      emitSessionExpired()
      return failure
    }
    const result = await sharedRefresh({ rejected: bearerOf(request) })
    if (result.status === "failed" && !hasSession()) emitSessionExpired()
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
