import { fulfillJson } from "../isolated/mock-authenticated-api"
import { ORIGINS, expect, test } from "./fixtures"
import { headersFor } from "./web-config-model"

/**
 * The sign-in page design frame under the headers IIS sends (phase 6): the accounts
 * page's frame-src admits it, the frame page has exactly its own policy, its document has
 * an opaque origin, and the platform's fonts and stylesheet still load from the app's own
 * origin - a cross-origin fetch from an opaque origin, which the /assets CORS header and
 * 'self' must both allow. The csp fixture fails the test on any violation not declared.
 *
 * Deliberate breaks: drop the Access-Control-Allow-Origin of /assets (the fonts stop
 * loading); drop 'self' from the accounts frame-src (a frame-src violation).
 */

const RETURN_TO = `${ORIGINS.api}/api/v1/auth/authorize?client_id=app&state=harness`
const DESIGN = {
  id: "d1",
  contentHash: "a".repeat(64),
  showPlatformLogo: true,
}

async function openWithDesign(
  page: import("@playwright/test").Page,
  api: import("./fixtures").HarnessApi,
  html: string
) {
  await api.useAnonymous(async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/applications/app/public-branding") {
      await fulfillJson(route, { name: "Partner App", logoUrl: null, logoUrlDark: null, homeUrl: null, loginDesign: DESIGN })
      return true
    }
    if (path === "/api/v1/applications/app/login-design") {
      await fulfillJson(route, { ...DESIGN, language: "en", dir: "ltr", html })
      return true
    }
    return false
  })
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${ORIGINS.accounts}/login?returnTo=${encodeURIComponent(RETURN_TO)}`)
  await expect(page.locator('[data-slot="auth-panel"]')).toHaveAttribute("data-design-state", "rendered")
  return page.frameLocator('[data-slot="auth-panel"] iframe')
}

test("is /login-design-frame.html with exactly its own headers", async ({ harness, request }) => {
  const served = await request.get(`${ORIGINS.accounts}/login-design-frame.html`)
  const expected = Object.fromEntries(
    headersFor(harness.models.accounts, "/login-design-frame.html").map(([name, value]) => [name.toLowerCase(), value])
  )
  const headers = served.headers()
  expect(headers["content-security-policy"]).toBe(expected["content-security-policy"])
  expect(headers["content-security-policy"]).toMatch(/^default-src 'none'; script-src 'sha256-[^ ;]+'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src https: data:;/)
  expect(headers["content-security-policy-report-only"]).toBeUndefined()
  expect(headers["x-frame-options"]).toBe("SAMEORIGIN")
})

test("shows a design with the platform's fonts, in an opaque origin, with nothing refused", async ({ page, api }) => {
  const frame = await openWithDesign(
    page,
    api,
    `<main id="design"><h2>Welcome</h2><p lang="ar">مرحبًا بك</p><img id="pixel" alt="" src="data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw=="></main>`
  )

  const inside = await frame.locator("#design").evaluate(async () => {
    await document.fonts.ready
    let storage: string
    try {
      localStorage.getItem("x")
      storage = "readable"
    } catch {
      storage = "refused"
    }
    const loaded = [...document.fonts].filter((face) => face.status === "loaded").map((face) => face.family)
    const image = document.getElementById("pixel") as HTMLImageElement
    return {
      origin: self.origin,
      storage,
      parent: (() => {
        try {
          return parent.document ? "reachable" : "none"
        } catch {
          return "refused"
        }
      })(),
      stylesheets: document.styleSheets.length,
      latin: loaded.some((family) => family.includes("IBM Plex Sans Variable")),
      arabic: loaded.some((family) => family.includes("IBM Plex Sans Arabic")),
      family: getComputedStyle(document.documentElement).fontFamily,
      image: image.complete && image.naturalWidth > 0,
    }
  })

  expect(inside).toMatchObject({ origin: "null", storage: "refused", parent: "refused", latin: true, arabic: true, image: true })
  expect(inside.stylesheets).toBeGreaterThan(0)
  expect(inside.family).toContain("IBM Plex Sans")
})

test("runs none of a design's scripts or handlers", async ({ page, api, csp }) => {
  const frame = await openWithDesign(
    page,
    api,
    `<p id="who">Own</p><script>document.title = "script ran"</script><img src="data:image/png;base64,AAAA" onerror="document.body.dataset.handler = 'ran'">`
  )
  await expect(frame.locator("#who")).toHaveText("Own")
  await csp.settle()
  const state = await frame.locator("#who").evaluate(() => ({
    title: document.title,
    handler: document.body.dataset.handler ?? null,
  }))
  expect(state).toEqual({ title: "Sign-in page design", handler: null })
  csp.expect({
    directive: "script-src-elem",
    blocked: "inline",
    reason: "phase 6 negative check: the design's <script>, refused by the frame's own policy",
  })
  csp.tolerate({
    directive: "script-src-attr",
    blocked: "inline",
    reason: "phase 6 negative check: the design's on* handler, refused by the frame's own policy",
  })
  csp.tolerate({
    directive: "script-src",
    blocked: "inline",
    reason: "phase 6 negative check: the same refusals as reported under script-src",
  })
})
