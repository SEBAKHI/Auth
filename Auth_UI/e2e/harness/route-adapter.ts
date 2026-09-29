import type { Route } from "@playwright/test"
import type { IncomingHttpHeaders } from "node:http"

/** The parts of `route.fulfill` options the harness API host honours. */
export interface HarnessFulfill {
  status?: number
  headers?: Record<string, string>
  contentType?: string
  body?: string | Buffer
  json?: unknown
}

export interface HarnessReply {
  status: number
  headers: Record<string, string>
  body: Buffer
}

function notInHarness(name: string): never {
  throw new Error(
    `route.${name}() has no meaning on the harness API host: the request already ` +
      `reached a real server. Answer it with route.fulfill(), or return false to let ` +
      `the harness answer 404 with x-harness-unmatched.`
  )
}

/**
 * A server-side stand-in for Playwright's Route, shaped to exactly what the
 * shared answers use: `request()` (url, method, headers, postData*) and
 * `fulfill()`. It lets `handle(route, url)` and `fulfillJson` from
 * e2e/isolated run unchanged on the harness's real API host, where no route
 * interception exists (card S30a, Q7).
 */
export class HarnessRoute {
  readonly #url: string
  readonly #method: string
  readonly #headers: Record<string, string>
  readonly #body: Buffer
  #reply: HarnessReply | null = null

  constructor(url: string, method: string, headers: IncomingHttpHeaders, body: Buffer) {
    this.#url = url
    this.#method = method
    this.#headers = Object.fromEntries(
      Object.entries(headers)
        .filter(([, value]) => value !== undefined)
        .map(([name, value]) => [name, Array.isArray(value) ? value.join(", ") : String(value)])
    )
    this.#body = body
  }

  request() {
    const body = this.#body
    return {
      url: () => this.#url,
      method: () => this.#method,
      headers: () => ({ ...this.#headers }),
      allHeaders: async () => ({ ...this.#headers }),
      headerValue: async (name: string) => this.#headers[name.toLowerCase()] ?? null,
      postData: () => (body.length ? body.toString("utf8") : null),
      postDataBuffer: () => (body.length ? Buffer.from(body) : null),
      postDataJSON: () => (body.length ? (JSON.parse(body.toString("utf8")) as unknown) : null),
    }
  }

  async fulfill(options: HarnessFulfill = {}) {
    if (this.#reply) throw new Error("route.fulfill() called twice for one harness request")
    const headers: Record<string, string> = {}
    for (const [name, value] of Object.entries(options.headers ?? {})) {
      // CORS is the API host's alone (api-host.ts). A handler that could set it
      // would silently open the API to any origin and pass every CSRF check.
      if (name.toLowerCase().startsWith("access-control-")) {
        throw new Error(
          `route.fulfill() set ${name}: CORS headers belong to the harness API host ` +
            "(e2e/harness/api-host.ts, topology.ts CORS_ALLOWED_ORIGINS), never to a handler."
        )
      }
      headers[name.toLowerCase()] = value
    }
    let body: Buffer
    if (options.json !== undefined) {
      body = Buffer.from(JSON.stringify(options.json))
      headers["content-type"] ??= "application/json"
    } else {
      body = Buffer.isBuffer(options.body) ? options.body : Buffer.from(options.body ?? "")
    }
    if (options.contentType) headers["content-type"] = options.contentType
    this.#reply = { status: options.status ?? 200, headers, body }
  }

  async continue() {
    notInHarness("continue")
  }

  async fallback() {
    notInHarness("fallback")
  }

  async abort() {
    notInHarness("abort")
  }

  /** What the handler answered, or null when nothing claimed the request. */
  get reply() {
    return this.#reply
  }

  /**
   * The same object typed as Playwright's Route, for the shared answers whose
   * signatures take one. Everything they call is implemented above; continue,
   * fallback and abort throw by name, and any other Route method is absent, so
   * a handler that needs one fails loudly instead of doing something plausible.
   */
  asRoute(): Route {
    return this as unknown as Route
  }
}
