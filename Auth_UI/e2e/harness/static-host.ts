import { readFileSync, readdirSync, statSync } from "node:fs"
import type { IncomingMessage, ServerResponse } from "node:http"
import { extname, isAbsolute, join, relative, resolve, sep } from "node:path"

import type { PrivacyMount } from "./privacy-mount"
import {
  cacheControlValue,
  clientCacheFor,
  resolveRequest,
  type SiteFiles,
  type WebConfigModel,
} from "./web-config-model"

/**
 * A small, pinned MIME table. IIS serves only extensions its mimeMap knows; the
 * harness does the same with this list, and refuses to start when a build or a
 * mounted folder holds anything else, instead of guessing a type IIS may not send.
 */
const MIME: Record<string, string> = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".webp": "image/webp",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".txt": "text/plain; charset=utf-8",
}

/** web.config itself is never served (requestFiltering), so it needs no type. */
const NEVER_SERVED = new Set([".config"])

/** Throws on the first file whose extension is outside the MIME table. */
export function assertServableTree(root: string) {
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = join(dir, entry.name)
      if (entry.isDirectory()) {
        walk(full)
        continue
      }
      const extension = extname(entry.name).toLowerCase()
      if (!MIME[extension] && !NEVER_SERVED.has(extension)) {
        throw new Error(
          `harness: ${full} has extension "${extension || "(none)"}", which is not in the ` +
            `harness MIME table (e2e/harness/static-host.ts). Add it only if IIS serves it too.`
        )
      }
    }
  }
  walk(root)
}

function statOf(path: string) {
  try {
    return statSync(path)
  } catch {
    return undefined
  }
}

export interface StaticHostOptions {
  /** Host name, for messages. */
  name: string
  /** The built application: apps/<app>/dist-harness. */
  root: string
  model: WebConfigModel
  privacy?: PrivacyMount
}

/**
 * Serves one SPA host the way IIS serves it from `root` with `model`: the rewrite
 * rules in order, the default document, requestFiltering of ".config", every
 * customHeaders entry, and Cache-Control from clientCache and <location>.
 * Everything comes from the model, which is read from dist-harness/web.config.
 */
export function createStaticHost({ name, root, model, privacy }: StaticHostOptions) {
  const base = resolve(root)

  /** URL path → physical path, honouring the /privacy mount; null when outside. */
  function physical(urlPath: string): string | null {
    const mounted = privacy?.map(urlPath)
    const [folder, inner] = mounted ? [mounted.base, mounted.relative] : [base, urlPath]
    if (!folder) return null
    const target = resolve(folder, `.${inner.startsWith("/") ? inner : `/${inner}`}`)
    const rel = relative(folder, target)
    if (isAbsolute(rel) || rel.startsWith("..") || rel.includes(`..${sep}`)) return null
    return target
  }

  const files: SiteFiles = {
    isFile: (urlPath) => {
      const path = physical(urlPath)
      return !!path && !!statOf(path)?.isFile()
    },
    isDirectory: (urlPath) => {
      const path = physical(urlPath)
      return !!path && !!statOf(path)?.isDirectory()
    },
  }

  return function serve(request: IncomingMessage, response: ServerResponse, url: URL) {
    for (const [header, value] of model.headers) response.setHeader(header, value)

    if (request.method !== "GET" && request.method !== "HEAD") {
      response.writeHead(405, { "content-type": "text/plain; charset=utf-8", allow: "GET, HEAD" })
      response.end(`${name}: static files answer GET and HEAD only`)
      return
    }

    let path: string
    try {
      path = decodeURIComponent(url.pathname)
    } catch {
      response.writeHead(400, { "content-type": "text/plain; charset=utf-8" }).end("bad path")
      return
    }
    const outcome = resolveRequest(model, path, url.search.slice(1), files)

    if (outcome.kind === "redirect") {
      response.writeHead(outcome.status, { location: outcome.location }).end()
      return
    }
    if (outcome.kind === "status") {
      response
        .writeHead(outcome.status, outcome.reason, { "content-type": "text/plain; charset=utf-8" })
        .end(outcome.description)
      return
    }

    const file = physical(outcome.urlPath)!
    const type = MIME[extname(file).toLowerCase()]
    if (!type) {
      response.writeHead(404, { "content-type": "text/plain; charset=utf-8" }).end("no MIME type")
      return
    }
    const body = readFileSync(file)
    const cacheControl = cacheControlValue(clientCacheFor(model, outcome.urlPath))
    if (cacheControl) response.setHeader("cache-control", cacheControl)
    response.writeHead(200, { "content-type": type, "content-length": body.length })
    response.end(request.method === "HEAD" ? undefined : body)
  }
}
