/**
 * The action each surface asks the widget for; the server expects the same word
 * back from Cloudflare (S13, BD5), so a token made for one request is refused on
 * another.
 */
export type BotAction = "login" | "register" | "forgot-password" | "email-code"

/** What the check's field shows: nothing, the widget's own box, or a sentence. */
export type CheckState =
  | { kind: "quiet" }
  | { kind: "interactive" }
  | { kind: "blocked"; code: string }
  | { kind: "load-failed" }
  | { kind: "incomplete" }
  | { kind: "stalled" }

/**
 * The widget's token, which action it was made for, and who waits for one. Plain
 * state outside React: a submit handler reads it between renders.
 */
export class TokenSlot {
  action: BotAction
  /** No token can come from this widget (its script refused, a lasting error, an unsupported browser). */
  failed = false
  /** Cloudflare's box is on screen, waiting for the person's click. */
  interactive = false
  private token: { value: string; action: BotAction } | null = null
  private waiters: Array<{ action: BotAction; resolve: (token: string | null) => void }> = []
  private resetWidget: (() => void) | null = null

  constructor(action: BotAction) {
    this.action = action
  }

  /** A widget was rendered for `action`: an earlier token goes with the widget that made it. */
  begin(action: BotAction, reset: () => void) {
    this.action = action
    this.token = null
    this.failed = false
    this.interactive = false
    this.resetWidget = reset
  }

  end() {
    this.token = null
    this.resetWidget = null
  }

  /** The widget made a token, for the action it was rendered with. */
  put(value: string, madeFor: BotAction) {
    if (madeFor !== this.action) return
    const waiting = this.waiters.filter((waiter) => waiter.action === madeFor)
    if (waiting.length > 0) {
      // Handed to the submit that waited: it is spent with that request.
      this.waiters = this.waiters.filter((waiter) => waiter.action !== madeFor)
      waiting[0].resolve(value)
      for (const other of waiting.slice(1)) other.resolve(null)
      return
    }
    this.token = { value, action: madeFor }
  }

  clear() {
    this.token = null
  }

  /** No token is coming (a lasting error, an unsupported browser, the page left): the waiting submits stop. */
  fail() {
    this.failed = true
    const waiting = this.waiters
    this.waiters = []
    for (const waiter of waiting) waiter.resolve(null)
  }

  /** The ready token for `action`, handed out once. */
  take(action: BotAction): string | null {
    if (!this.token || this.token.action !== action) return null
    const value = this.token.value
    this.token = null
    return value
  }

  wait(action: BotAction, ms: number): Promise<string | null> {
    return new Promise((resolve) => {
      const waiter = {
        action,
        resolve: (token: string | null) => {
          window.clearTimeout(timer)
          resolve(token)
        },
      }
      const timer = window.setTimeout(() => {
        this.waiters = this.waiters.filter((other) => other !== waiter)
        resolve(null)
      }, ms)
      this.waiters.push(waiter)
    })
  }

  /** The token went out: the widget makes a fresh one. */
  renew() {
    this.token = null
    this.resetWidget?.()
  }
}
