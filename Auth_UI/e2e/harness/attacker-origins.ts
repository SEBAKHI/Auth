import type { ServerResponse } from "node:http"

import { HOSTS } from "./topology"

/**
 * The hosts whose pages a test writes: the two attackers, the fake linked application, and
 * the stand-in for Google's authorization page (S02 PR D).
 */
export type AttackerHost = typeof HOSTS.apex | typeof HOSTS.attacker | typeof HOSTS.linkedApp | typeof HOSTS.google

/**
 * The two attacker origins: https://example.com (the apex - SAME-SITE with the
 * applications, the position a subdomain takeover or a page on the marketing
 * site would hold) and https://attacker.example.net (cross-site). Each serves
 * only the pages the test writes; every request they receive, and every request
 * their pages send to the API host, lands in the request log like any other.
 */
export class AttackerPages {
  readonly #pages = new Map<string, { html: string; headers: Record<string, string> }>()
  // Requests to the Google stand-in at a path no test served. Before the stand-in, any request
  // to accounts.google.com failed the egress check; this keeps that true for every request a
  // test did not serve a page for (REVIEW 162 F8.9). Not emptied by clear(): the egress
  // fixture reads it at the test's end, after this fixture's own teardown.
  readonly #unservedGoogle: string[] = []

  /** Serves `html` at https://<host><path> for the rest of the test. */
  serve(host: AttackerHost, path: string, html: string, headers: Record<string, string> = {}) {
    if (!path.startsWith("/")) throw new Error(`attacker page path must start with "/": ${path}`)
    this.#pages.set(`${host}${path}`, { html, headers })
    return `https://${host}${path}`
  }

  clear() {
    this.#pages.clear()
  }

  /** The Google stand-in's requests at a path no test served, oldest first. */
  unservedGoogle(): readonly string[] {
    return [...this.#unservedGoogle]
  }

  clearUnservedGoogle() {
    this.#unservedGoogle.length = 0
  }

  answer(host: string, url: URL, response: ServerResponse) {
    const page = this.#pages.get(`${host}${url.pathname}`)
    if (!page) {
      if (host === HOSTS.google) this.#unservedGoogle.push(`https://${host}${url.pathname}`)
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" })
      response.end(`no attacker page at https://${host}${url.pathname}`)
      return
    }
    response.writeHead(200, { "content-type": "text/html; charset=utf-8", ...page.headers })
    response.end(page.html)
  }
}
