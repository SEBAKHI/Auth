import { readFileSync } from "node:fs"
import { join } from "node:path"

import type { Page } from "@playwright/test"

/**
 * What a page paints before its app runs. Each animation frame precedes a
 * paint, so a frame sampled here is what the browser drew next: `<html>`'s
 * class, `<body>`'s computed background, and whether `#root` has content yet.
 * The app's module scripts are held back so the empty page stays up long
 * enough to sample; the boot script in `<head>` is not, since it is what is
 * under test.
 */

export interface PaintedFrame {
  dark: boolean
  light: boolean
  background: string | null
  content: number
}

/** Holds back every bundled script except the head's boot script by `delayMs`. */
export async function holdAppScripts(page: Page, delayMs: number) {
  await page.route(
    (url) => /\/assets\/.+\.js$/.test(url.pathname) && !url.pathname.includes("theme-boot"),
    async (route) => {
      await new Promise((resolve) => setTimeout(resolve, delayMs))
      await route.continue()
    }
  )
}

/** Records every frame from the first one the document gets. Call before navigating. */
export async function sampleFrames(page: Page) {
  await page.addInitScript(() => {
    const frames: unknown[] = []
    ;(window as unknown as { __frames: unknown[] }).__frames = frames
    const tick = () => {
      const html = document.documentElement
      frames.push({
        dark: html.classList.contains("dark"),
        light: html.classList.contains("light"),
        background: document.body ? getComputedStyle(document.body).backgroundColor : null,
        content: document.getElementById("root")?.childElementCount ?? 0,
      })
      if (frames.length < 5000) requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
  })
}

/** The frames painted with a body but before the app put any content in. */
export async function framesBeforeContent(page: Page): Promise<PaintedFrame[]> {
  await page.waitForFunction(() => (document.getElementById("root")?.childElementCount ?? 0) > 0)
  const frames = await page.evaluate(
    () => (window as unknown as { __frames: PaintedFrame[] }).__frames
  )
  return frames.filter((frame) => frame.background !== null && frame.content === 0)
}

function decodeXml(value: string) {
  return value
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&amp;/g, "&")
}

/**
 * Serves every page with the Content-Security-Policy the app's sealed
 * `dist/web.config` sends in production (the preview server sends none), and
 * records every violation the browser reports.
 */
export async function enforceSealedCsp(page: Page, app: "accounts" | "console") {
  const config = readFileSync(join(process.cwd(), "apps", app, "dist", "web.config"), "utf8")
  const match = /name="Content-Security-Policy" value="([^"]+)"/.exec(config)
  if (!match) throw new Error(`no Content-Security-Policy in apps/${app}/dist/web.config`)
  const policy = decodeXml(match[1])

  // The app's own page addresses: no file extension, not the API.
  await page.route(
    (url) => !url.pathname.startsWith("/api/") && !/\.[a-z0-9]+$/i.test(url.pathname),
    async (route) => {
      if (route.request().resourceType() !== "document") return route.fallback()
      const response = await route.fetch()
      await route.fulfill({
        response,
        headers: { ...response.headers(), "content-security-policy": policy },
      })
    }
  )
  await page.addInitScript(() => {
    const violations: string[] = []
    ;(window as unknown as { __violations: string[] }).__violations = violations
    document.addEventListener("securitypolicyviolation", (event) => {
      violations.push(`${event.violatedDirective} ${event.blockedURI} ${event.sourceFile}:${event.lineNumber}`)
    })
  })
  return policy
}

export async function cspViolations(page: Page): Promise<string[]> {
  return page.evaluate(() => (window as unknown as { __violations: string[] }).__violations)
}
