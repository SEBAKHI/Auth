/**
 * Puts the colour mode on `<html>` before the first paint, in both apps.
 *
 * Serves `bootTheme` (`packages/ui/src/theme/boot-theme.ts`) as a classic,
 * blocking `<script src>` at the top of `<head>`: a module script runs only
 * after the browser has painted the empty page, so a dark user saw it white
 * until React ran. A file on the app's own origin, never an inline script, so
 * the CSP's `script-src 'self'` admits it unchanged. Its name carries a hash
 * of its content, like the bundle's own assets, so a changed script is never
 * served from a stale cache; dev and build serve it at the same path.
 *
 * The file served is `bootTheme` as THIS config's loader transpiled it, which
 * no unit test sees and CI does not run. After any change to `bootTheme`, run
 * the isolated `first-paint` specs locally (both apps, rebuilt):
 * `npx playwright test first-paint --config playwright.isolated.config.ts`.
 */
import { createHash } from "node:crypto"
import type { Plugin } from "vite"

import { LOGIN_DESIGN_FRAME_HTML } from "./login-design-frame"
import { bootThemeScript, type BootOptions } from "./packages/ui/src/theme/boot-theme"

/**
 * `hints`: whether the app captures an application's colour mode from the
 * address and keeps it for the flow (the accounts app); the console does not.
 */
export function themeBoot(options: BootOptions): Plugin {
  const source = bootThemeScript(options)
  const hash = createHash("sha256").update(source).digest("hex").slice(0, 8)
  const fileName = `assets/theme-boot-${hash}.js`

  return {
    name: "authsystem-theme-boot",
    configureServer(server) {
      server.middlewares.use(`/${fileName}`, (_request, response) => {
        response.setHeader("Content-Type", "text/javascript; charset=utf-8")
        response.end(source)
      })
    },
    generateBundle() {
      this.emitFile({ type: "asset", fileName, source })
    },
    transformIndexHtml: {
      // After Vite's own processing, so the tag stays a classic script: Vite
      // would otherwise warn about, or rewrite, a script without type="module".
      order: "post",
      // Not into the design frame's page: it has no colour mode of its own (the page
      // that frames it sends one), and its policy runs no script but its receiver.
      handler: (_html, context) =>
        context.filename.endsWith(LOGIN_DESIGN_FRAME_HTML)
          ? []
          : [{ tag: "script", attrs: { src: `/${fileName}` }, injectTo: "head-prepend" }],
    },
  }
}
