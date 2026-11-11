/**
 * Names the platform's tab icon in the HTML of both apps.
 *
 * The page is a static file that cannot know which icon an administrator uploaded, so it
 * names the address the API keeps that icon at (`platform-favicon.ts`). The browser asks for
 * it while it parses the page, before any script: no template icon first, nothing for a
 * script to swap in afterwards. None in the design frame's page, which is never a tab, and
 * none when the API's address is not absolute.
 */
import type { HtmlTagDescriptor, Plugin } from "vite"

import { GOOGLE_CALLBACK_HTML } from "./google-callback"
import { LOGIN_DESIGN_FRAME_HTML } from "./login-design-frame"
import { platformFaviconUrl } from "./packages/api/src/platform-favicon"

export function faviconLink(): Plugin {
  let href: string | null = null

  return {
    name: "authsystem-favicon-link",
    configResolved(config) {
      href = platformFaviconUrl(config.env.VITE_API_BASE_URL)
    },
    transformIndexHtml: {
      order: "post",
      handler: (_html, context) =>
        // The Google callback page closes at once and loads only its module (google-callback.ts).
        context.filename.endsWith(LOGIN_DESIGN_FRAME_HTML) || context.filename.endsWith(GOOGLE_CALLBACK_HTML)
          ? []
          : faviconTags(href),
    },
  }
}

/** The icon's link; none without an address. */
export function faviconTags(href: string | null): HtmlTagDescriptor[] {
  if (!href) return []
  return [{ tag: "link", attrs: { rel: "icon", type: "image/png", href }, injectTo: "head" }]
}
