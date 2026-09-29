import type { IncomingMessage } from "node:http"

/**
 * One request as it ARRIVED at the harness server - written from the server's
 * own IncomingMessage, never from Playwright's Request. Cookie and Sec-Fetch-*
 * are added by the network stack after any route handler would run, so only the
 * server sees them reliably (microsoft/playwright#42646).
 */
export interface LoggedRequest {
  host: string
  method: string
  /** Path and query, as sent. */
  url: string
  path: string
  cookie?: string
  origin?: string
  secFetchSite?: string
  /** Every header, lower-cased, for the rare test that needs another one. */
  headers: Record<string, string | string[] | undefined>
}

export class RequestLog {
  readonly #entries: LoggedRequest[] = []

  record(host: string, request: IncomingMessage, url: URL) {
    const single = (name: string) => {
      const value = request.headers[name]
      return Array.isArray(value) ? value.join(", ") : value
    }
    this.#entries.push({
      host,
      method: request.method ?? "",
      url: `${url.pathname}${url.search}`,
      path: url.pathname,
      cookie: single("cookie"),
      origin: single("origin"),
      secFetchSite: single("sec-fetch-site"),
      headers: { ...request.headers },
    })
  }

  /** Everything the server saw since the test started, in arrival order. */
  all(): readonly LoggedRequest[] {
    return [...this.#entries]
  }

  /** What one host saw, e.g. requests.to("auth.example.com"). */
  to(host: string): readonly LoggedRequest[] {
    return this.#entries.filter((entry) => entry.host === host)
  }

  clear() {
    this.#entries.length = 0
  }
}
