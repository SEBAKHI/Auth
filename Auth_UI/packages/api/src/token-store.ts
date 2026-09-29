/**
 * Token storage.
 *
 * Security posture:
 *   - Access token  -> kept in memory only, never written to disk. It is also
 *     published to the other same-origin tabs over a BroadcastChannel (see
 *     tab-sync.ts) so they can adopt a rotation instead of racing their own.
 *     That widens its reach from one tab to every tab of the origin; it does NOT
 *     widen it to disk, and nothing outside the origin can open that channel.
 *   - Refresh token -> NOT readable by this code once the API delivers it as a
 *     cookie. For the platform's own apps (IdentityProvider:FirstPartySpaOrigins
 *     with SpaRefreshCookieEnabled) the API sets it in a per-app HttpOnly,
 *     Secure, SameSite=Strict `__Host-` cookie on its own host, and the response
 *     body carries REFRESH_SENTINEL in its place. The browser attaches the cookie
 *     to the credentialed refresh request; no script — ours, an XSS, a hostile
 *     dependency — can read it or carry the session to another machine.
 *
 * `auth.refreshToken` stays the one session key, whichever mode the server
 * chose (the server decides; this code follows what it finds in storage):
 *   - REFRESH_SENTINEL: cookie mode. The key is only the session hint that says
 *     "there is a session to resume on reload".
 *   - any other value: a real token, still delivered in the body. That is the
 *     legacy mode, used while the switch is off, and the state a session stored
 *     before the switch is in until its first refresh migrates it to the cookie.
 * The key is never removed on migration — only on sign-out or a final refusal —
 * so an older bundle open in another tab, whose `storage` listener reads a
 * removal as a sign-out, never sees one (tab-sync.ts).
 *
 * The refresh token is SINGLE USE: the server rotates it and treats a second
 * presentation of the same value as theft, revoking every token the account
 * has. Whether it sits in localStorage or in the cookie jar, every tab of the
 * origin shares one copy, so every mutation here has a cross-tab consequence.
 * Read tab-sync.ts before changing anything below.
 */

const REFRESH_TOKEN_KEY = "auth.refreshToken"

/**
 * What the API puts in `refreshToken` when the real token went into the cookie.
 * Also what this app stores. Never a credential: sending it back is harmless,
 * and the server reads it as "no token in the body".
 */
export const REFRESH_SENTINEL = "__cookie__"

/**
 * LEGACY mode only: records the refresh token a context is about to spend, so
 * that a context which dies mid-flight can tell on the next load that it
 * consumed a token without ever learning the outcome. Replaying such a token is
 * what the server reports as reuse. See reconcilePendingRefresh().
 */
const PENDING_REFRESH_KEY = "auth.refreshPending"

/**
 * COOKIE mode: the same record without a secret — only WHEN a context started
 * spending the cookie. The server answers a just-rotated cookie once more within
 * its replay grace window, so a young marker is harmless and an old one ends the
 * session locally. See reconcilePendingRefresh().
 */
const SPENDING_REFRESH_KEY = "auth.refreshSpending"

/**
 * A sign-out whose request never reached the server. The cookie it should have
 * revoked is still valid, so the next load of the app retries the sign-out before
 * it shows anything signed in (auth-context.tsx). Holds a time, no secret.
 */
const LOGOUT_PENDING_KEY = "auth.logoutPending"

/**
 * Whether the cookie of a fresh cookie-mode sign-in has been proven by a refresh
 * yet: "unconfirmed" until the first refresh succeeds, "blocked" when that first
 * refresh found no cookie — the browser refused to store or send it. The login
 * page reads "blocked" to explain why the session ended.
 */
const COOKIE_CHECK_KEY = "auth.cookieCheck"

let accessToken: string | null = null

/**
 * Where localStorage cannot be read at all (a locked-down private mode), the
 * session state lives here instead. It starts TRUE so a load makes exactly one
 * attempt to resume a session from the cookie, and the first failed refresh
 * turns it off.
 */
let memorySession = true

/**
 * Bumped whenever the session is torn down. A refresh that started before the
 * teardown must not write its result afterwards, or it would resurrect a
 * session the user just ended — a real window now that refreshes queue behind
 * a cross-tab lock and can wait seconds before storing anything.
 */
let generation = 0

function read(key: string): string | null | undefined {
  try {
    return localStorage.getItem(key)
  } catch {
    return undefined
  }
}

function write(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    /* storage unavailable (private mode) */
  }
}

function remove(key: string): void {
  try {
    localStorage.removeItem(key)
  } catch {
    /* ignore */
  }
}

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
}

/** Snapshot of the session generation, to be passed back to setTokens(). */
export function currentGeneration(): number {
  return generation
}

/**
 * The raw value of the session key: REFRESH_SENTINEL, a legacy token, or null.
 * For comparisons only — use legacyRefreshToken() for anything you would SEND.
 */
export function getRefreshToken(): string | null {
  return read(REFRESH_TOKEN_KEY) ?? null
}

/**
 * Whether there is a session to resume: the session key exists, whatever it
 * holds. This is the question the app asks at boot, before an upload retry, on
 * a 401 — never "can I read a refresh token?", which cookie mode answers "no".
 */
export function hasSession(): boolean {
  const value = read(REFRESH_TOKEN_KEY)
  return value === undefined ? memorySession : value !== null
}

/**
 * The refresh token to send in the request body, or null when there is none to
 * send — cookie mode, or no session. REFRESH_SENTINEL is never returned: it is
 * not a token, and must never be treated as one to migrate.
 */
export function legacyRefreshToken(): string | null {
  const value = getRefreshToken()
  return value && value !== REFRESH_SENTINEL ? value : null
}

/** True when the stored session is a cookie session. */
export function isCookieSession(): boolean {
  return getRefreshToken() === REFRESH_SENTINEL
}

/**
 * Stores a token pair — `refresh` as the API delivered it, real or
 * REFRESH_SENTINEL. When `expectedGeneration` is supplied and no longer
 * matches, the session was torn down while this refresh was in flight and the
 * result is dropped; the return value says whether the pair was stored.
 */
export function setTokens(
  access: string,
  refresh: string,
  expectedGeneration?: number
): boolean {
  if (expectedGeneration !== undefined && expectedGeneration !== generation) {
    return false
  }

  accessToken = access
  memorySession = true
  write(REFRESH_TOKEN_KEY, refresh)
  return true
}

export function clearTokens(): void {
  generation += 1
  accessToken = null
  memorySession = false
  remove(REFRESH_TOKEN_KEY)
  remove(PENDING_REFRESH_KEY)
  remove(SPENDING_REFRESH_KEY)
}

/** LEGACY: records that `token` is being spent right now. */
export function markRefreshPending(token: string): void {
  write(PENDING_REFRESH_KEY, token)
}

export function clearRefreshPending(): void {
  remove(PENDING_REFRESH_KEY)
}

export function getPendingRefresh(): string | null {
  return read(PENDING_REFRESH_KEY) ?? null
}

/** COOKIE: records that this context started spending the cookie at `now`. */
export function markRefreshSpending(now = Date.now()): void {
  write(SPENDING_REFRESH_KEY, String(now))
}

export function clearRefreshSpending(): void {
  remove(SPENDING_REFRESH_KEY)
}

/** When the cookie-spend marker was written (epoch ms), or null when none. */
export function getRefreshSpendingSince(): number | null {
  const value = read(SPENDING_REFRESH_KEY)
  if (value === null || value === undefined) return null
  const since = Number(value)
  // An unreadable marker is treated as ancient: the safe side is to end the
  // session locally rather than replay a cookie of unknown age.
  return Number.isFinite(since) ? since : 0
}

/** Records that a sign-out has not reached the server yet. */
export function markLogoutPending(now = Date.now()): void {
  write(LOGOUT_PENDING_KEY, String(now))
}

export function clearLogoutPending(): void {
  remove(LOGOUT_PENDING_KEY)
}

export function isLogoutPending(): boolean {
  const value = read(LOGOUT_PENDING_KEY)
  return value !== null && value !== undefined
}

/** A cookie-mode sign-in just happened; its cookie is not proven yet. */
export function markCookieUnconfirmed(): void {
  write(COOKIE_CHECK_KEY, "unconfirmed")
}

/** The first refresh presented the cookie successfully. */
export function confirmCookie(): void {
  if (read(COOKIE_CHECK_KEY) === "unconfirmed") remove(COOKIE_CHECK_KEY)
}

/**
 * The first refresh after a cookie-mode sign-in found no cookie. Recorded only
 * while unconfirmed, so a cookie that worked and later expired is not blamed on
 * the browser.
 */
export function noteCookieMissing(): void {
  if (read(COOKIE_CHECK_KEY) === "unconfirmed") write(COOKIE_CHECK_KEY, "blocked")
}

/** A sign-in that did not use the cookie: nothing to prove, nothing to blame. */
export function clearCookieCheck(): void {
  remove(COOKIE_CHECK_KEY)
}

/** True when the last session ended because the browser withheld the cookie. */
export function isCookieBlocked(): boolean {
  return read(COOKIE_CHECK_KEY) === "blocked"
}
