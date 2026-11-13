/**
 * Cloudflare Turnstile's script, loaded once per page (S13, BD9), and only
 * when the server named a sitekey: a deployment left on Off loads nothing from
 * Cloudflare. Its host is in both apps' script-src and frame-src.
 */
export const TURNSTILE_SCRIPT_URL =
  "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit"

/** Longer than a slow network needs; an extension that blocks the script never answers at all. */
export const TURNSTILE_LOAD_TIMEOUT_MS = 15_000

/** The parts of `window.turnstile` this app uses. */
export interface TurnstileApi {
  render: (container: HTMLElement, options: TurnstileRenderOptions) => string | undefined
  reset: (widgetId: string) => void
  remove: (widgetId: string) => void
}

export interface TurnstileRenderOptions {
  sitekey: string
  action: string
  language: string
  theme: "light" | "dark"
  appearance: "interaction-only"
  size: "flexible"
  retry: "auto"
  "refresh-expired": "auto"
  callback: (token: string) => void
  "error-callback": (code: string) => boolean
  "expired-callback": () => void
  "timeout-callback": () => void
  "before-interactive-callback": () => void
  "after-interactive-callback": () => void
  "unsupported-callback": () => void
}

declare global {
  interface Window {
    turnstile?: TurnstileApi
  }
}

let turnstilePromise: Promise<TurnstileApi> | null = null

/**
 * Loads the script once and resolves with `window.turnstile`. A failure (the
 * script refused, blocked or too slow) drops the promise, so the next attempt
 * loads it again: the page's "Try again" does.
 */
export function loadTurnstile(): Promise<TurnstileApi> {
  turnstilePromise ??= new Promise<TurnstileApi>((resolve, reject) => {
    if (window.turnstile) {
      resolve(window.turnstile)
      return
    }
    const script = document.createElement("script")
    script.src = TURNSTILE_SCRIPT_URL
    script.async = true
    const fail = (message: string) => {
      window.clearTimeout(timer)
      turnstilePromise = null
      script.remove()
      reject(new Error(message))
    }
    const timer = window.setTimeout(() => fail("Turnstile did not load in time"), TURNSTILE_LOAD_TIMEOUT_MS)
    script.onload = () => {
      if (!window.turnstile) {
        fail("Turnstile loaded without its API")
        return
      }
      window.clearTimeout(timer)
      resolve(window.turnstile)
    }
    script.onerror = () => fail("Failed to load Turnstile")
    document.head.appendChild(script)
  })
  return turnstilePromise
}

/**
 * Whether Cloudflare's script is in this document: loaded, or on its way. It
 * is never removed once added, so it stays for the life of the page (OD3).
 */
export function turnstileLoaded(): boolean {
  return (
    window.turnstile !== undefined ||
    document.querySelector(`script[src="${TURNSTILE_SCRIPT_URL}"]`) !== null
  )
}

/** Tests only: forget the loaded script. */
export function forgetTurnstileForTests(): void {
  turnstilePromise = null
}
