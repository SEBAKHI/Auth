import { GOOGLE_CHANNEL, readCallback } from "./google-code-flow"

/**
 * The whole job of /google-callback.html (both apps), in this order:
 * 1. read `code`, `state` and `error` from the address, then at once take them out of it
 *    (history.replaceState), so the code does not stay in this window's history or address bar;
 * 2. hand them to the sign-in page: on the same-origin BroadcastChannel, and to the opener by
 *    postMessage when there still is one. Google's page may send a Cross-Origin-Opener-Policy
 *    that cuts the link to the opener, and the channel does not depend on that link;
 * 3. close this window.
 *
 * It loads nothing else: no app bundle, no translations, no API call, nothing from another
 * origin. The page is a separate Vite entry with an external module script, so the site-wide
 * `script-src 'self'` covers it with no inline script and no hash.
 */
export function runGoogleCallback(win: Window = window): void {
  const message = readCallback(win.location.search)
  win.history.replaceState(null, "", win.location.pathname)

  if (message) {
    try {
      const channel = new BroadcastChannel(GOOGLE_CHANNEL)
      channel.postMessage(message)
      channel.close()
    } catch {
      // No BroadcastChannel (a very old browser): the opener below is the only way.
    }
    try {
      win.opener?.postMessage(message, win.location.origin)
    } catch {
      // The opener is gone or another origin: the channel above carried the message.
    }
  }

  win.close()
}
