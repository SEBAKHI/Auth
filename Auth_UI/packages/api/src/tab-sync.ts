/**
 * Cross-tab session coordination.
 *
 * The refresh token lives in localStorage, so every tab of the origin shares
 * one copy — but the server rotates it on use and treats a second presentation
 * of the same value as theft, revoking every token the account holds. Two tabs
 * refreshing at the same moment therefore log the user out of everything, and
 * because the mass revocation kills the other tabs' tokens too, each of them
 * reports "reuse" on its next refresh: a self-sustaining cascade.
 *
 * Three primitives fix that, all origin-scoped exactly like localStorage:
 *
 *   1. A Web Lock, so only one context spends the token at a time. Chosen over
 *      a localStorage lock because the browser releases it automatically when
 *      the holder's document dies — a localStorage lock needs a TTL, and a TTL
 *      is a guess: too short and two tabs proceed together (the original bug),
 *      too long and the second tab freezes. It also queues, so the second tab
 *      WAITS and then succeeds rather than failing.
 *   2. A BroadcastChannel carrying the new access token, so a tab that queued
 *      behind the lock can adopt the winner's result instead of spending the
 *      token again. Only the access token travels here; the refresh token is
 *      already in localStorage and putting it on the channel would widen its
 *      exposure for nothing.
 *   3. A `storage` listener, so a rotation or a logout in one tab is seen by
 *      the others instead of leaving them on a dead session.
 */

import { decodeJwt, isTokenExpired } from "@authsystem/api/jwt"
import {
  clearRefreshPending,
  clearTokens,
  getAccessToken,
  getPendingRefresh,
  getRefreshToken,
  hasRefreshToken,
  setAccessToken,
} from "@authsystem/api/token-store"

const CHANNEL_NAME = "auth.tokens"
const REFRESH_LOCK_NAME = "auth.refresh"
const REFRESH_TOKEN_KEY = "auth.refreshToken"

/**
 * How long a queued context waits for the winner's broadcast before giving up
 * and refreshing itself. Missing the broadcast is not a failure: the waiter
 * re-reads the CURRENT refresh token inside the lock, so it performs a
 * legitimate rotation, never a reuse.
 */
const BROADCAST_WAIT_MS = 150

/**
 * Ceiling on waiting for the lock. Web Locks has no timeout of its own and
 * releases only when the holder's document dies — not when its fetch hangs. A
 * single tab on a captive portal would otherwise freeze every tab of the origin
 * forever, which is a strictly wider blast radius than the bug being fixed.
 */
const LOCK_WAIT_TIMEOUT_MS = 10_000

/** Event dispatched when the session can no longer be refreshed. */
export const SESSION_EXPIRED_EVENT = "auth:session-expired"

type TabMessage =
  /** A context rotated the token pair and is publishing the new access token. */
  | { kind: "access"; token: string }
  /** A freshly opened context asking whether anyone holds a live access token. */
  | { kind: "hello" }
  /** A context ended the session. */
  | { kind: "cleared" }

let channel: BroadcastChannel | null = null
let started = false
let accessTokenWaiters: ((token: string | null) => void)[] = []

/** True when this context holds an access token that is not known to be expired. */
export function hasFreshAccessToken(): boolean {
  const token = getAccessToken()
  if (!token) return false
  const claims = decodeJwt(token)
  return claims === null || !isTokenExpired(claims)
}

function publish(message: TabMessage): void {
  try {
    channel?.postMessage(message)
  } catch {
    /* channel closed or structured-clone failure — sync is best-effort */
  }
}

/** Publishes a freshly rotated access token to the other tabs of this origin. */
export function publishAccessToken(token: string): void {
  publish({ kind: "access", token })
}

function resolveAccessTokenWaiters(token: string | null): void {
  const waiters = accessTokenWaiters
  accessTokenWaiters = []
  for (const resolve of waiters) resolve(token)
}

/**
 * Waits briefly for another tab to publish an access token. Resolves with the
 * token, or with null once the window closes.
 */
export function waitForBroadcastAccessToken(
  timeoutMs = BROADCAST_WAIT_MS
): Promise<string | null> {
  if (!channel) return Promise.resolve(null)

  return new Promise((resolve) => {
    let settled = false
    const finish = (token: string | null) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      resolve(token)
    }
    const timer = setTimeout(() => {
      accessTokenWaiters = accessTokenWaiters.filter((w) => w !== finish)
      finish(null)
    }, timeoutMs)
    accessTokenWaiters.push(finish)
  })
}

/**
 * Ends the session locally and tells the other tabs. Pass broadcast:false when
 * reacting to another tab's teardown, so the two do not bounce the message back
 * and forth.
 */
export function emitSessionExpired(options?: { broadcast?: boolean }): void {
  const hadSession = getAccessToken() !== null || hasRefreshToken()

  clearTokens()
  resolveAccessTokenWaiters(null)

  if (hadSession && options?.broadcast !== false) {
    publish({ kind: "cleared" })
  }

  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent(SESSION_EXPIRED_EVENT))
  }
}

function timeoutSignal(ms: number): AbortSignal | undefined {
  try {
    return AbortSignal.timeout(ms)
  } catch {
    return undefined
  }
}

/**
 * Both names matter. A signal cancelled by hand rejects with AbortError, but
 * AbortSignal.timeout() rejects with TimeoutError — verified in Chrome against
 * a lock genuinely held by another tab. Matching only AbortError would rethrow
 * on the timeout path instead of degrading to an unlocked refresh, turning the
 * safety valve into a hard failure of the request that tripped it.
 */
function isAbortError(error: unknown): boolean {
  return (
    error instanceof Error &&
    (error.name === "AbortError" || error.name === "TimeoutError")
  )
}

/**
 * Runs `run` while holding the origin-wide refresh lock.
 *
 * MUST NOT be re-entered. Web Locks is not reentrant, and this lock has no
 * timeout once held, so a nested acquisition self-deadlocks permanently. That
 * is why the refresh call uses a raw fetch instead of the typed `api` client:
 * routing it through the client would send it back through the middleware and
 * straight into a nested acquisition. Do not "tidy" it into api.POST.
 *
 * Degrades to running unlocked where Web Locks is unavailable (or where the
 * wait times out), which is exactly the pre-lock behaviour — never worse.
 */
export function withRefreshLock<T>(run: () => Promise<T>): Promise<T> {
  const locks =
    typeof navigator === "undefined" ? undefined : (navigator.locks as
      | LockManager
      | undefined)
  if (!locks) return run()

  const signal = timeoutSignal(LOCK_WAIT_TIMEOUT_MS)
  const request = signal
    ? locks.request(REFRESH_LOCK_NAME, { signal }, run)
    : locks.request(REFRESH_LOCK_NAME, run)

  return request.catch((error: unknown) => {
    if (isAbortError(error)) return run()
    throw error
  }) as Promise<T>
}

/**
 * Recovers from a refresh whose context died mid-flight.
 *
 * The server rotates and revokes the presented token before it answers, so if
 * the document was destroyed between the request and `setTokens` the stored
 * token is already dead — replaying it is precisely what gets reported as
 * theft, and it takes every other device down with it. When the marker still
 * matches what is stored, end the session locally instead.
 *
 * The trade-off is explicit: if the request never reached the server we sign
 * the user out for nothing. That needs the process to die inside a window of a
 * few hundred milliseconds, and today the very same event signs them out of
 * every device via the mass revocation — so this is strictly the better of the
 * two outcomes.
 */
export function reconcilePendingRefresh(): void {
  const pending = getPendingRefresh()
  if (!pending) return

  clearRefreshPending()
  if (pending === getRefreshToken()) clearTokens()
}

function handleMessage(message: TabMessage): void {
  switch (message.kind) {
    case "access":
      setAccessToken(message.token)
      resolveAccessTokenWaiters(message.token)
      break
    case "hello":
      // Answer only with a token that is actually usable, so a newly opened tab
      // can skip its startup refresh instead of rotating the shared token.
      if (hasFreshAccessToken()) publishAccessToken(getAccessToken() as string)
      break
    case "cleared":
      emitSessionExpired({ broadcast: false })
      break
  }
}

function handleStorage(event: StorageEvent): void {
  if (event.storageArea !== localStorage) return
  if (event.key !== REFRESH_TOKEN_KEY) return

  if (event.newValue === null) {
    // Another tab logged out or lost its session. Without this the tab keeps
    // rendering a signed-in UI and firing requests against a dead session.
    emitSessionExpired({ broadcast: false })
    return
  }

  // A rotation landed. The refresh token is read from storage on every use, so
  // nothing needs adopting here — but a context queued on the lock is waiting
  // for exactly this signal, and this is the only one it gets in a browser
  // without BroadcastChannel.
  resolveAccessTokenWaiters(null)
}

/**
 * Installs the cross-tab listeners. Idempotent, and safe where the browser
 * supports neither primitive — the tab then behaves exactly as it does today.
 */
export function startTabSync(): void {
  if (started || typeof window === "undefined") return
  started = true

  reconcilePendingRefresh()

  if (typeof BroadcastChannel !== "undefined") {
    try {
      channel = new BroadcastChannel(CHANNEL_NAME)
      channel.onmessage = (event: MessageEvent<TabMessage>) =>
        handleMessage(event.data)
    } catch {
      channel = null
    }
  }

  window.addEventListener("storage", handleStorage)
  window.addEventListener("pagehide", stopTabSync, { once: true })

  // Ask the other tabs for a live access token. When one answers, this tab
  // starts already authenticated and never performs the startup refresh that
  // makes every newly opened tab rotate the shared token.
  if (hasRefreshToken()) publish({ kind: "hello" })
}

/** Releases the channel. Exported for tests and for page teardown. */
export function stopTabSync(): void {
  if (!started) return
  started = false

  resolveAccessTokenWaiters(null)
  try {
    channel?.close()
  } catch {
    /* already closed */
  }
  channel = null

  if (typeof window !== "undefined") {
    window.removeEventListener("storage", handleStorage)
  }
}
