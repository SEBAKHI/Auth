import { currentModuleEntry, reloadWithCacheBust } from "./app-version"

const RECOVERED_ENTRY_STORAGE_KEY = "auth.ui.chunk-recovery"

/**
 * How a missing lazy chunk surfaces. The first three are the browsers' wording
 * for a failed dynamic import; the last three are what a *deploy* looks like
 * when the SPA-fallback rewrite answers a deleted `/assets/*.js` with the
 * index document, so the loader receives HTML where a module was expected.
 */
const CHUNK_ERROR_SIGNATURES = [
  "failed to fetch dynamically imported module",
  "error loading dynamically imported module",
  "importing a module script failed",
  "failed to load module script",
  "is not a valid javascript mime type",
  "unexpected token '<'",
]

function messageOf(error: unknown): string {
  if (error instanceof Error) return error.message
  if (typeof error === "string") return error
  if (error && typeof error === "object" && "message" in error) {
    return String((error as { message: unknown }).message)
  }
  return ""
}

/** True when the failure is a chunk the running build can no longer fetch. */
export function isChunkLoadError(error: unknown): boolean {
  const message = messageOf(error).toLowerCase()
  if (!message) return false
  return CHUNK_ERROR_SIGNATURES.some((signature) => message.includes(signature))
}

function readRecoveredEntry(): string | null {
  try {
    return window.sessionStorage.getItem(RECOVERED_ENTRY_STORAGE_KEY)
  } catch {
    // Storage may be unavailable in privacy-restricted contexts.
    return null
  }
}

function writeRecoveredEntry(entry: string): void {
  try {
    window.sessionStorage.setItem(RECOVERED_ENTRY_STORAGE_KEY, entry)
  } catch {
    // Without storage the guard degrades to "never retry", which is the safe
    // direction: a reload loop is worse than an honest error screen.
  }
}

/**
 * The running module entry. A document with no module script cannot be
 * identified across reloads, so it gets one attempt under a fixed key rather
 * than an unbounded loop.
 */
function recoveryIdentity(): string {
  return currentModuleEntry(window.location.origin) ?? "unknown-entry"
}

/**
 * Whether a reload is still worth attempting — a pure read, safe to call during
 * render so a component can decide what to show without causing what it shows.
 *
 * The guard is the running module entry, not a counter: it changes on every
 * build, so "I already reloaded and I am *still* on this entry" is exactly the
 * case where reloading again would loop.
 */
export function canRecoverFromChunkLoadError(): boolean {
  return readRecoveredEntry() !== recoveryIdentity()
}

/**
 * Reloads once so the browser fetches the current index document and learns the
 * new chunk names. Returns false when the guard above has already been spent,
 * and the caller must surface an error instead.
 *
 * Idempotent by construction, so double invocation under StrictMode — or from
 * both the preload listener and the route error boundary — reloads once.
 */
export function recoverFromChunkLoadError(
  reload: () => void = reloadWithCacheBust
): boolean {
  if (!canRecoverFromChunkLoadError()) return false

  writeRecoveredEntry(recoveryIdentity())
  reload()
  return true
}

/**
 * Listens for Vite's preload failures, which fire for chunks that are fetched
 * ahead of a navigation and therefore never reach a route error boundary.
 *
 * The event is not cancelled: the import still rejects, the router still routes
 * it to `RouteErrorBoundary`, and the guard above keeps the two paths from
 * reloading twice.
 */
export function installChunkLoadRecovery(): () => void {
  const onPreloadError = () => {
    recoverFromChunkLoadError()
  }

  window.addEventListener("vite:preloadError", onPreloadError)
  return () => window.removeEventListener("vite:preloadError", onPreloadError)
}
