import { createHash } from "node:crypto"

import type { Route } from "@playwright/test"

import { ORIGINS } from "./topology"

/**
 * The harness's stand-in for what S01 makes the API do with a first-party
 * app's refresh token. The harness never runs the real API (H8), so this is a
 * MODEL of its answers: the server's own decisions are pinned by the xUnit
 * suites (FirstPartySessionResultFilterTests, RefreshCredentialSourceTests,
 * RequireFirstPartyOriginFilterTests). What the browser checks prove on top is
 * what only a browser decides — what it stores, what it attaches, what it
 * withholds — under the real CSP, on real same-site HTTPS origins.
 */

export const SENTINEL = "__cookie__"

/** The first-party apps of the harness topology, as their Origin header reads. */
export const FIRST_PARTY_ORIGINS = [ORIGINS.console, ORIGINS.accounts] as const

/**
 * The cookie name the API derives for an app: `__Host-auth_rt_` and the first 16
 * hex digits of the SHA-256 of the normalized origin — the same rule as
 * FirstPartyOriginResolver.CookieNameFor on the server.
 */
export function refreshCookieName(origin: string): string {
  const digest = createHash("sha256").update(origin.toLowerCase().replace(/\/$/, "")).digest("hex")
  return `__Host-auth_rt_${digest.slice(0, 16)}`
}

/** What the model answers a refresh or a sign-in with. */
export type DeliveryMode =
  /** SpaRefreshCookieEnabled on: the token goes into the cookie, the body gets the sentinel. */
  | "cookie"
  /** The switch turned off: the real token in the body, and the cookie expired (B10). */
  | "rollback"
  /** Negative control for B10: the real token in the body, the cookie left in place. */
  | "rollback-keeping-cookie"
  /** Negative control for B1/B7: the old server, a real token in the body and no cookie. */
  | "legacy-body"

/** One refresh as the API received it. */
export interface SeenRefresh {
  origin: string | null
  /** The raw request body. */
  body: string
  /** The value of the requesting app's refresh cookie, when the browser sent one. */
  cookie: string | null
  /** Every cookie name the browser attached. */
  cookieNames: string[]
}

function cookiesOf(header: string | undefined): Map<string, string> {
  const cookies = new Map<string, string>()
  for (const part of (header ?? "").split(";")) {
    const at = part.indexOf("=")
    if (at > 0) cookies.set(part.slice(0, at).trim(), part.slice(at + 1).trim())
  }
  return cookies
}

function accessToken(sequence: number): string {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url")
  return `${encode({ alg: "none", typ: "JWT" })}.${encode({
    jti: `harness-${sequence}`,
    exp: Math.floor(Date.now() / 1000) + 3600,
  })}.signature`
}

function cookieHeader(name: string, value: string, maxAge: number): string {
  return `${name}=${value}; Max-Age=${maxAge}; Path=/; Secure; HttpOnly; SameSite=Strict`
}

/**
 * The model's state for one test: the delivery mode, every refresh it saw, and
 * how long it holds a refresh before answering (B3 needs two in flight).
 */
export class FirstPartyServer {
  mode: DeliveryMode = "cookie"
  refreshDelayMs = 0
  /** When true, sign-out requests get a dropped connection (B11). */
  dropLogout = false
  readonly refreshes: SeenRefresh[] = []
  readonly logouts: { origin: string | null; authorization: string | null }[] = []
  /** Refreshes and sign-outs in arrival order, as "refresh <origin>" / "logout <origin>". */
  readonly sequence: string[] = []
  #issued = 0

  clear() {
    this.mode = "cookie"
    this.refreshDelayMs = 0
    this.dropLogout = false
    this.refreshes.length = 0
    this.logouts.length = 0
    this.sequence.length = 0
    this.#issued = 0
  }

  /** Refreshes sent from one app's origin. */
  from(origin: string): SeenRefresh[] {
    return this.refreshes.filter((refresh) => refresh.origin === origin)
  }

  #deliver(origin: string | null, realToken: string) {
    const name = origin ? refreshCookieName(origin) : null
    const firstParty = origin !== null && (FIRST_PARTY_ORIGINS as readonly string[]).includes(origin)
    switch (this.mode) {
      case "cookie":
        return firstParty && name
          ? { refreshToken: SENTINEL, setCookie: cookieHeader(name, realToken, 604800) }
          : { refreshToken: realToken, setCookie: null }
      case "rollback":
        return { refreshToken: realToken, setCookie: name && firstParty ? cookieHeader(name, "", 0) : null }
      case "rollback-keeping-cookie":
      case "legacy-body":
        return { refreshToken: realToken, setCookie: null }
    }
  }

  #fulfill(route: Route, body: object, setCookie: string | null, status = 200) {
    return route.fulfill({
      status,
      contentType: "application/json",
      headers: setCookie ? { "set-cookie": setCookie } : {},
      body: JSON.stringify(body),
    })
  }

  /** Answers /api/v1/auth/refresh and /api/v1/auth/logout; false for anything else. */
  async answer(route: Route, url: URL): Promise<boolean> {
    const path = url.pathname.toLowerCase()
    const request = route.request()
    const headers = await request.allHeaders()
    const origin = headers["origin"] ?? null

    if (path === "/api/v1/auth/logout") {
      this.logouts.push({ origin, authorization: headers["authorization"] ?? null })
      this.sequence.push(`logout ${origin}`)
      if (this.dropLogout) {
        ;(route as unknown as { dropConnection(): void }).dropConnection()
        return true
      }
      const name = origin ? refreshCookieName(origin) : null
      await route.fulfill({ status: 204, headers: name ? { "set-cookie": cookieHeader(name, "", 0) } : {} })
      return true
    }

    if (path !== "/api/v1/auth/refresh") return false

    const raw = request.postData() ?? ""
    const cookies = cookiesOf(headers["cookie"])
    const name = origin ? refreshCookieName(origin) : null
    const cookie = name ? (cookies.get(name) ?? null) : null
    this.refreshes.push({ origin, body: raw, cookie, cookieNames: [...cookies.keys()] })
    this.sequence.push(`refresh ${origin}`)
    if (this.refreshDelayMs) await new Promise((resolve) => setTimeout(resolve, this.refreshDelayMs))

    const parsed = raw ? (JSON.parse(raw) as { refreshToken?: string }) : {}
    const bodyToken = parsed.refreshToken && parsed.refreshToken !== SENTINEL ? parsed.refreshToken : null
    // A real body token wins (migration); otherwise the cookie of the app the
    // Origin names — never another app's.
    if (!bodyToken && !cookie) {
      await this.#fulfill(route, { code: "Auth.RefreshTokenNotFound", status: 404 }, null, 404)
      return true
    }

    this.#issued += 1
    const { refreshToken, setCookie } = this.#deliver(origin, `rotated-${this.#issued}`)
    await this.#fulfill(
      route,
      { accessToken: accessToken(this.#issued), refreshToken, expiresIn: 3600, refreshExpiresIn: 604800 },
      setCookie
    )
    return true
  }

  /** A successful sign-in response for `origin`, delivered per the mode. */
  async answerSignIn(route: Route, user: object, secret: string): Promise<void> {
    const origin = (await route.request().allHeaders())["origin"] ?? null
    const { refreshToken, setCookie } = this.#deliver(origin, secret)
    this.#issued += 1
    await this.#fulfill(
      route,
      {
        token: { accessToken: accessToken(this.#issued), refreshToken, expiresIn: 3600, refreshExpiresIn: 604800 },
        user,
        requiresPasswordChange: false,
        requiresTwoFactor: false,
      },
      setCookie
    )
  }
}
