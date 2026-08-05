/**
 * Token storage.
 *
 * Security posture (the Auth API returns tokens in the response body, not in an
 * HttpOnly cookie, so the SPA must hold them):
 *   - Access token  -> kept in memory only, never written to disk. It is also
 *     published to the other same-origin tabs over a BroadcastChannel (see
 *     tab-sync.ts) so they can adopt a rotation instead of racing their own.
 *     That widens its reach from one tab to every tab of the origin, which is
 *     the same blast radius the refresh token below already has; it does NOT
 *     widen it to disk, and nothing outside the origin can open that channel.
 *   - Refresh token -> persisted in localStorage so a page reload can silently
 *     re-establish a session. This is the standard trade-off for a token-based
 *     SPA; it is mitigated by a strict CSP and never rendering untrusted HTML.
 *
 * The refresh token is SINGLE USE: the server rotates it and treats a second
 * presentation of the same value as theft, revoking every token the account
 * has. Because localStorage is shared by every tab of the origin, that makes it
 * a shared single-use resource, and every mutation here has a cross-tab
 * consequence. Read tab-sync.ts before changing anything below.
 */

const REFRESH_TOKEN_KEY = "auth.refreshToken"

/**
 * Records the refresh token a context is about to spend, so that a context
 * which dies mid-flight (reload, crash, navigation) can tell on the next load
 * that it consumed a token without ever learning the outcome. Replaying such a
 * token is what the server reports as reuse. See reconcilePendingRefresh().
 */
const PENDING_REFRESH_KEY = "auth.refreshPending"

let accessToken: string | null = null

/**
 * Bumped whenever the session is torn down. A refresh that started before the
 * teardown must not write its result afterwards, or it would resurrect a
 * session the user just ended — a real window now that refreshes queue behind
 * a cross-tab lock and can wait seconds before storing anything.
 */
let generation = 0

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

export function getRefreshToken(): string | null {
  try {
    return localStorage.getItem(REFRESH_TOKEN_KEY)
  } catch {
    return null
  }
}

/**
 * Stores a token pair. When `expectedGeneration` is supplied and no longer
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
  try {
    localStorage.setItem(REFRESH_TOKEN_KEY, refresh)
  } catch {
    /* storage unavailable (private mode) — session stays in-memory only */
  }
  return true
}

export function clearTokens(): void {
  generation += 1
  accessToken = null
  try {
    localStorage.removeItem(REFRESH_TOKEN_KEY)
    localStorage.removeItem(PENDING_REFRESH_KEY)
  } catch {
    /* ignore */
  }
}

export function hasRefreshToken(): boolean {
  return getRefreshToken() !== null
}

/** Records that `token` is being spent right now. */
export function markRefreshPending(token: string): void {
  try {
    localStorage.setItem(PENDING_REFRESH_KEY, token)
  } catch {
    /* ignore */
  }
}

export function clearRefreshPending(): void {
  try {
    localStorage.removeItem(PENDING_REFRESH_KEY)
  } catch {
    /* ignore */
  }
}

export function getPendingRefresh(): string | null {
  try {
    return localStorage.getItem(PENDING_REFRESH_KEY)
  } catch {
    return null
  }
}
