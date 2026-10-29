/**
 * The sign-in page design frame's receiver page, served by both apps beside their
 * index.html (packages/ui DesignFrame frames it).
 *
 * It is a Vite entry so its stylesheet carries the platform's fonts with hashed names,
 * and it holds one inline classic script, allowed by hash in its own CSP
 * (web.config <location path="login-design-frame.html">). Its frame is sandboxed without
 * allow-same-origin, so its origin is opaque and every subresource it loads from the
 * app is a cross-origin request: the static host answers /assets with
 * Access-Control-Allow-Origin (web.config), and `assetsCors` does the same on the dev
 * and preview servers.
 */
import type { Plugin } from "vite"

export const LOGIN_DESIGN_FRAME_HTML = "login-design-frame.html"

/** The build inputs of an app: its index.html and the frame page. */
export function htmlInputs(appDir: string): Record<string, string> {
  return {
    index: `${appDir}/index.html`,
    "login-design-frame": `${appDir}/${LOGIN_DESIGN_FRAME_HTML}`,
  }
}

/**
 * Access-Control-Allow-Origin on what the frame loads: /assets on the preview server,
 * as web.config's <location path="assets"> does in production, and anything requested
 * from an opaque origin on the dev server, which serves sources outside /assets.
 * The files are public and the header carries no credentials.
 */
export function assetsCors(): Plugin {
  return {
    name: "authsystem-assets-cors",
    configureServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.headers.origin === "null") response.setHeader("Access-Control-Allow-Origin", "*")
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, response, next) => {
        if (request.url?.startsWith("/assets/")) response.setHeader("Access-Control-Allow-Origin", "*")
        next()
      })
    },
  }
}
