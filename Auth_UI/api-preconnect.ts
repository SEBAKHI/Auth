/**
 * Opens the connection to the API while the page is still downloading its scripts, in
 * both apps.
 *
 * The API is another origin, and the first requests a sign-in page makes to it (the
 * branding, the sign-in providers) start only once the app's JavaScript has run. On a
 * first visit the connection itself (DNS, TCP, TLS) cost 450–700 ms on a deployed instance
 * (measured 2026-10-30), all of it after the scripts. A `preconnect` hint in the HTML
 * pays for it while they download.
 *
 * Two hints, because the browser keeps credentialed and anonymous connections apart: the
 * typed client sends `credentials: "include"` (the hint without `crossorigin`), and the
 * plain fetches of public data, an application's branding or a sign-in design, send none
 * (the hint with it). None in the design frame's page, which calls no API, and none when
 * the address is not absolute.
 */
import type { HtmlTagDescriptor, Plugin } from "vite"

import { LOGIN_DESIGN_FRAME_HTML } from "./login-design-frame"

export function apiPreconnect(): Plugin {
  let origin: string | null = null

  return {
    name: "authsystem-api-preconnect",
    configResolved(config) {
      origin = absoluteOrigin(config.env.VITE_API_BASE_URL)
    },
    transformIndexHtml: {
      order: "post",
      handler: (_html, context) =>
        context.filename.endsWith(LOGIN_DESIGN_FRAME_HTML) ? [] : preconnectTags(origin),
    },
  }
}

/** The two hints for the API's origin; none without one. */
export function preconnectTags(origin: string | null): HtmlTagDescriptor[] {
  if (!origin) return []
  return [
    { tag: "link", attrs: { rel: "preconnect", href: origin }, injectTo: "head-prepend" },
    { tag: "link", attrs: { rel: "preconnect", href: origin, crossorigin: true }, injectTo: "head-prepend" },
  ]
}

/** The origin of an absolute http(s) address, or null. */
export function absoluteOrigin(value: unknown): string | null {
  if (typeof value !== "string" || !value) return null
  try {
    const url = new URL(value)
    return url.protocol === "https:" || url.protocol === "http:" ? url.origin : null
  } catch {
    return null
  }
}
