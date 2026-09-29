import type { ServerResponse } from "node:http"

import { HOSTS } from "./topology"

export type AttackerHost = typeof HOSTS.apex | typeof HOSTS.attacker

/**
 * The two attacker origins: https://example.com (the apex - SAME-SITE with the
 * applications, the position a subdomain takeover or a page on the marketing
 * site would hold) and https://attacker.example.net (cross-site). Each serves
 * only the pages the test writes; every request they receive, and every request
 * their pages send to the API host, lands in the request log like any other.
 */
export class AttackerPages {
  readonly #pages = new Map<string, { html: string; headers: Record<string, string> }>()

  /** Serves `html` at https://<host><path> for the rest of the test. */
  serve(host: AttackerHost, path: string, html: string, headers: Record<string, string> = {}) {
    if (!path.startsWith("/")) throw new Error(`attacker page path must start with "/": ${path}`)
    this.#pages.set(`${host}${path}`, { html, headers })
    return `https://${host}${path}`
  }

  clear() {
    this.#pages.clear()
  }

  answer(host: string, url: URL, response: ServerResponse) {
    const page = this.#pages.get(`${host}${url.pathname}`)
    if (!page) {
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" })
      response.end(`no attacker page at https://${host}${url.pathname}`)
      return
    }
    response.writeHead(200, { "content-type": "text/html; charset=utf-8", ...page.headers })
    response.end(page.html)
  }
}
