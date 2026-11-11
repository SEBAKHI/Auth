/**
 * The page Google sends the person back to after the Google sign-in (S02 PR D), served by both
 * apps beside their index.html. Its one module (packages/auth/src/external/google-callback-page.ts)
 * hands the one-time code to the sign-in page and closes the window.
 *
 * It is a Vite entry so its module is a hashed file on the app's own origin, which the site-wide
 * `script-src 'self'` admits with no inline script and no hash. The html plugins
 * (theme-boot.ts, api-preconnect.ts, favicon-link.ts) leave it alone: it shows nothing, and it
 * makes no request but its module's.
 */
export const GOOGLE_CALLBACK_HTML = "google-callback.html"

/** The build input of the callback page. */
export function googleCallbackInputs(appDir: string): Record<string, string> {
  return { "google-callback": `${appDir}/${GOOGLE_CALLBACK_HTML}` }
}
