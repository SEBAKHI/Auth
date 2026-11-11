import { SUPPORTED_LANGUAGES, type LanguageCode } from "@authsystem/i18n/languages"

import { GOOGLE_CHANNEL, readCallback } from "./google-code-flow"

/**
 * The one line this page may show: when a browser refuses to close a window that Google's
 * page moved to another window group (a Cross-Origin-Opener-Policy), the window stays open,
 * and an empty page there would look broken. In every language the apps ship, chosen from the
 * browser's language: this page loads no translations, only the list of languages.
 */
export const CLOSE_THIS_WINDOW: Record<LanguageCode, string> = {
  en: "You can close this window.",
  ar: "يمكنك إغلاق هذه النافذة.",
  tr: "Bu pencereyi kapatabilirsiniz.",
  fr: "Vous pouvez fermer cette fenêtre.",
  zh: "您可以关闭此窗口。",
  ur: "آپ یہ ونڈو بند کر سکتے ہیں۔",
  fa: "می‌توانید این پنجره را ببندید.",
}

/** How long the page waits for close() to take effect before it says it can be closed. */
export const STILL_OPEN_AFTER_MS = 600

function closeLine(win: Window): void {
  const tag = (win.navigator.language || "en").split("-")[0].toLowerCase()
  const language = SUPPORTED_LANGUAGES.find((candidate) => candidate.code === tag) ?? SUPPORTED_LANGUAGES[0]
  const root = win.document.documentElement
  root.lang = language.code
  root.dir = language.dir
  win.document.body.textContent = CLOSE_THIS_WINDOW[language.code]
}

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
  // Still here a moment later: the browser kept the window open. Say it can be closed.
  win.setTimeout(() => {
    if (!win.closed) closeLine(win)
  }, STILL_OPEN_AFTER_MS)
}
