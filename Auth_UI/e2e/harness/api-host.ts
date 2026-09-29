import type { IncomingMessage, ServerResponse } from "node:http"

import { HarnessRoute } from "./route-adapter"
import { CORS_ALLOWED_ORIGINS, CORS_EXPOSED_HEADERS } from "./topology"

/**
 * One test's answer for a request to the API host. Returns true when it
 * fulfilled the route; false lets the host answer 404 with x-harness-unmatched.
 */
export type ApiAnswer = (route: HarnessRoute, url: URL) => Promise<boolean>

const ALLOWED_METHODS = "GET, POST, PUT, PATCH, DELETE"

/**
 * auth.example.com: a real HTTPS endpoint, so the browser sends what it would
 * send to production - the Cookie it stored, Origin, Sec-Fetch-Site, and a real
 * CORS preflight - and the harness server logs it as it arrives (server.ts).
 *
 * CORS is answered HERE and only here, from CORS_ALLOWED_ORIGINS (topology.ts:
 * console, accounts and the apex, with credentials - deliberately stricter than
 * production, see there). Any other Origin gets no Access-Control-Allow-Origin,
 * so the browser refuses to hand the response to the page. Because harness
 * pages never use route interception, Playwright adds no CORS headers of its own
 * and never answers a preflight itself.
 */
export class ApiHost {
  #answer: ApiAnswer | null = null

  /** Installs the test's answer; the api fixture composes it with the defaults. */
  answerWith(answer: ApiAnswer) {
    this.#answer = answer
  }

  clear() {
    this.#answer = null
  }

  #cors(request: IncomingMessage, response: ServerResponse) {
    const origin = request.headers.origin
    response.setHeader("vary", "Origin")
    if (!origin || !CORS_ALLOWED_ORIGINS.includes(origin)) return false
    response.setHeader("access-control-allow-origin", origin)
    response.setHeader("access-control-allow-credentials", "true")
    response.setHeader("access-control-expose-headers", CORS_EXPOSED_HEADERS.join(", "))
    return true
  }

  async serve(request: IncomingMessage, response: ServerResponse, url: URL, body: Buffer) {
    const allowed = this.#cors(request, response)

    const preflight =
      request.method === "OPTIONS" && request.headers["access-control-request-method"] !== undefined
    if (preflight) {
      if (allowed) {
        response.setHeader("access-control-allow-methods", ALLOWED_METHODS)
        const requested = request.headers["access-control-request-headers"]
        if (requested) response.setHeader("access-control-allow-headers", requested)
        // No preflight cache: every cross-origin write shows its preflight in
        // the request log, which is what a CSRF test needs to see.
        response.setHeader("access-control-max-age", "0")
      }
      response.writeHead(204).end()
      return
    }

    const route = new HarnessRoute(url.href, request.method ?? "GET", request.headers, body)
    const answered = this.#answer ? await this.#answer(route, url) : false
    if (answered && route.dropped) {
      // A network failure as the browser sees one: no status, no body.
      request.socket.destroy()
      return
    }
    const reply = route.reply
    if (answered && reply) {
      response.writeHead(reply.status, reply.headers).end(reply.body)
      return
    }
    if (answered) {
      response.writeHead(500, { "content-type": "text/plain; charset=utf-8" })
      response.end(`harness: the handler claimed ${url.pathname} but never called route.fulfill()`)
      return
    }
    response.writeHead(404, {
      "content-type": "application/json",
      "x-harness-unmatched": "1",
    })
    response.end(JSON.stringify({ title: "Unexpected harness API request", path: url.pathname }))
  }
}
