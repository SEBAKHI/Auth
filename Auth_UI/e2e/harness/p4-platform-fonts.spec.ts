import { mkdirSync, readFileSync } from "node:fs"
import { join } from "node:path"

import type { Page, Route } from "@playwright/test"

import { fulfillJson } from "../isolated/mock-authenticated-api"
import { ORIGINS, expect, test } from "./fixtures"

/**
 * The platform's own fonts (UI batch P4, item 7) as they ship: both apps' pages and the
 * sign-in page design frame (an opaque origin) load the WOFF2 files from the API origin under
 * the CSP their web.config sends, and render in them. The csp fixture fails any test here on a
 * violation not registered in expected-csp-violations.ts.
 *
 * The files are IBM Plex's own, chosen to be told apart: the heading font is Plex Sans
 * italic, the body font the THIN Latin-only subset of Plex Sans Arabic, so the Arabic text of
 * a page has no glyph in it and must come from the shipped IBM Plex Sans Arabic.
 *
 * Deliberate breaks, each turning a test here red: drop the API origin from font-src in either
 * app's web.config (a font-src violation, and the face never loads); drop the Access-Control-
 * Allow-Origin of fonts in api-host.ts (the face errors); drop DEFAULT_FONT_STACK from
 * platformFontsCss (the Arabic text falls to a system font); make CACHED_FONTS_BUDGET_MS 0
 * (content is drawn before the font).
 */

const FILES = join(process.cwd(), "apps", "accounts", "node_modules")
const HEADING_FILE = readFileSync(
  join(FILES, "@fontsource-variable", "ibm-plex-sans", "files", "ibm-plex-sans-latin-wght-italic.woff2")
)
const BODY_FILE = readFileSync(
  join(FILES, "@fontsource", "ibm-plex-sans-arabic", "files", "ibm-plex-sans-arabic-latin-100-normal.woff2")
)

const HEADING_URL = `${ORIGINS.api}/uploads/images/fonts/${"a".repeat(64)}.woff2`
const BODY_URL = `${ORIGINS.api}/uploads/images/fonts/${"b".repeat(64)}.woff2`
const FONTS = {
  heading: { url: HEADING_URL, familyName: "IBM Plex Sans" },
  body: { url: BODY_URL, familyName: "IBM Plex Sans Arabic Thin" },
}
const HEADING_FAMILY = "AuthSystem Platform Heading"
const BODY_FAMILY = "AuthSystem Platform Body"

const SCREENS = "D:/tmp/ui-batch-14/impl-P4/screens"

/** Answers the two font files as the API's uploads middleware does, after `delayMs`. */
function fonts(delayMs = 0) {
  return async (route: Route, url: URL) => {
    const file = url.href === HEADING_URL ? HEADING_FILE : url.href === BODY_URL ? BODY_FILE : null
    if (!file) return false
    if (delayMs) await new Promise((resolve) => setTimeout(resolve, delayMs))
    await route.fulfill({
      status: 200,
      contentType: "font/woff2",
      headers: { "x-content-type-options": "nosniff", "cache-control": "public, max-age=31536000, immutable" },
      body: file,
    })
    return true
  }
}

/** What the page's fonts are: each platform face's status, and the families in use. */
function readFonts(page: Page) {
  return page.evaluate(async () => {
    await document.fonts.ready
    const status = (family: string) => [...document.fonts].find((face) => face.family.replace(/"/g, "") === family)?.status ?? "absent"
    const arabic = document.querySelector('[lang="ar"], html[lang="ar"] body') as HTMLElement | null
    return {
      heading: status("AuthSystem Platform Heading"),
      body: status("AuthSystem Platform Body"),
      plexArabic: [...document.fonts].some((face) => face.family.includes("IBM Plex Sans Arabic") && face.status === "loaded"),
      bodyFamily: getComputedStyle(document.body).fontFamily,
      titleFamily: getComputedStyle(document.querySelector("h1")!).fontFamily,
      arabicFamily: arabic ? getComputedStyle(arabic).fontFamily : null,
    }
  })
}

test("the accounts sign-in page is set in the platform's fonts, and its Arabic stays in IBM Plex Sans Arabic", async ({ page, api }) => {
  await page.addInitScript(() => window.localStorage.setItem("auth.language", "ar"))
  await api.useAnonymous(fonts(), { branding: { fonts: FONTS } })
  await page.goto(`${ORIGINS.accounts}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible()

  const seen = await readFonts(page)

  expect(seen.body).toBe("loaded")
  expect(seen.heading).toBe("loaded")
  expect(seen.bodyFamily).toMatch(new RegExp(`^"${BODY_FAMILY}", "IBM Plex Sans Variable", "IBM Plex Sans Arabic"`))
  expect(seen.titleFamily).toMatch(new RegExp(`^"${HEADING_FAMILY}", "${BODY_FAMILY}"`))
  // The body font has no Arabic glyph: the page's Arabic needs the shipped Arabic face.
  expect(seen.plexArabic).toBe(true)
  expect(
    await page.evaluate(() => document.fonts.check('16px "AuthSystem Platform Body"', "ب"))
  ).toBe(true)

  mkdirSync(SCREENS, { recursive: true })
  for (const width of [375, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    await page.screenshot({ path: `${SCREENS}/accounts-login-ar-${width}.png` })
  }
})

test("a returning visitor's first drawn frame is already in the platform's font", async ({ page, api }) => {
  // A returning visitor: the branding of the last visit is in storage, so the entry applies its
  // fonts before React mounts. The file is not in the HTTP cache (a new font, or a cleared
  // cache) and answers slowly, within the entry's budget: nothing may be drawn before it has
  // loaded. Every frame's state is sampled before it is painted.
  await page.addInitScript(
    ([branding]) => {
      window.localStorage.setItem("auth.ui.branding", branding)
      const frames: { content: boolean; body: string }[] = []
      ;(window as unknown as { __frames: typeof frames }).__frames = frames
      const sample = () => {
        const face = [...document.fonts].find(
          (candidate) => candidate.family.replace(/"/g, "") === "AuthSystem Platform Body"
        )
        frames.push({
          content: (document.getElementById("root")?.childElementCount ?? 0) > 0,
          body: face?.status ?? "absent",
        })
        if (frames.length < 600) requestAnimationFrame(sample)
      }
      requestAnimationFrame(sample)
    },
    [JSON.stringify({ platformName: "AuthSystem", fonts: FONTS })] as const
  )
  await api.useAnonymous(fonts(300), { branding: { fonts: FONTS } })
  await page.goto(`${ORIGINS.accounts}/login`)
  await expect(page.locator('input[type="email"]')).toBeVisible()

  const frames = await page.evaluate(() => (window as unknown as { __frames: { content: boolean; body: string }[] }).__frames)
  const drawn = frames.filter((frame) => frame.content)
  expect(drawn.length).toBeGreaterThan(0)
  // The floor: the font was still on its way during some frames, so the wait was real.
  expect(frames.filter((frame) => !frame.content && frame.body === "loading").length).toBeGreaterThan(0)
  expect(drawn.filter((frame) => frame.body !== "loaded")).toEqual([])
})

test("a sign-in page design uses the platform's fonts inside its opaque frame", async ({ page, api }) => {
  const design = { id: "d1", contentHash: "c".repeat(64), showPlatformLogo: true }
  const answerFonts = fonts()
  await api.useAnonymous(
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/platform/login-design") {
        await fulfillJson(route, {
          ...design,
          language: "en",
          dir: "ltr",
          html: `<main id="design"><h2>Welcome</h2><p>Designed text</p><p lang="ar">مرحبًا بك</p></main>`,
        })
        return true
      }
      return answerFonts(route, url)
    },
    { branding: { fonts: FONTS, loginDesign: design } }
  )
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${ORIGINS.accounts}/login`)
  await expect(page.locator('[data-slot="auth-panel"]')).toHaveAttribute("data-design-state", "rendered")
  const frame = page.frameLocator('[data-slot="auth-panel"] iframe')

  const inside = await frame.locator("#design").evaluate(async () => {
    await document.fonts.ready
    const status = (family: string) => [...document.fonts].find((face) => face.family.replace(/"/g, "") === family)?.status ?? "absent"
    return {
      origin: self.origin,
      heading: status("AuthSystem Platform Heading"),
      body: status("AuthSystem Platform Body"),
      textFamily: getComputedStyle(document.querySelector("main p")!).fontFamily,
      titleFamily: getComputedStyle(document.querySelector("h2")!).fontFamily,
      plexArabic: [...document.fonts].some((face) => face.family.includes("IBM Plex Sans Arabic") && face.status === "loaded"),
    }
  })

  expect(inside).toMatchObject({ origin: "null", heading: "loaded", body: "loaded", plexArabic: true })
  expect(inside.textFamily).toMatch(new RegExp(`^"?${BODY_FAMILY}"?, "IBM Plex Sans Variable", "IBM Plex Sans Arabic"`))
  expect(inside.titleFamily).toMatch(new RegExp(`^"?${HEADING_FAMILY}"?`))
  await page.screenshot({ path: `${SCREENS}/accounts-login-design-en-1440.png` })
})

test("the console is set in the platform's fonts, and its Fonts tab names them", async ({ page, api }) => {
  for (const language of ["en", "ar"] as const) {
    // The signed-in profile's language is the one the console takes.
    await api.useAuthenticated(["platform-settings:manage"], async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, { platformName: "AuthSystem", fonts: FONTS, theme: null })
        return true
      }
      return fonts()(route, url)
    }, { branding: { fonts: FONTS }, preferredLanguage: language })
    for (const scheme of ["light", "dark"] as const) {
      await page.emulateMedia({ colorScheme: scheme })
      await page.addInitScript(
        ([lang, mode]) => {
          window.localStorage.setItem("auth.language", lang)
          window.localStorage.setItem("theme", mode)
        },
        [language, scheme] as const
      )
      await page.goto(`${ORIGINS.console}/admin/platform-settings?tab=fonts`)
      await expect(page.locator('[data-slot="font-preview"]').first()).toBeVisible()
      const seen = await readFonts(page)
      expect(seen).toMatchObject({ heading: "loaded", body: "loaded" })
      expect(seen.titleFamily).toMatch(new RegExp(`^"${HEADING_FAMILY}"`))
      await expect(page.locator("html")).toHaveAttribute("lang", language)
      await expect(page.getByText("IBM Plex Sans Arabic Thin")).toBeVisible()
      for (const width of [375, 1440]) {
        await page.setViewportSize({ width, height: 1000 })
        await page.screenshot({ path: `${SCREENS}/console-fonts-tab-${language}-${scheme}-${width}.png`, fullPage: true })
      }
    }
  }
})

test("a font picked in the console is previewed from its bytes, then sent as multipart", async ({ page, api }) => {
  const sent: { path: string; type: string; body: Buffer }[] = []
  await api.useAuthenticated(["platform-settings:manage"], async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/admin/platform-settings") {
      await fulfillJson(route, { platformName: "AuthSystem", fonts: {}, theme: null })
      return true
    }
    if (path === "/api/v1/admin/platform-settings/fonts/heading" && route.request().method() === "PUT") {
      sent.push({
        path,
        type: (await route.request().headerValue("content-type")) ?? "",
        body: route.request().postDataBuffer() ?? Buffer.alloc(0),
      })
      await fulfillJson(route, { platformName: "AuthSystem", fonts: { heading: FONTS.heading }, theme: null })
      return true
    }
    return fonts()(route, url)
  })
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.goto(`${ORIGINS.console}/admin/platform-settings?tab=fonts`)
  const heading = page.locator('[data-slot="card"]').filter({ hasText: "Heading font" })

  await page.getByTestId("font-file-heading").setInputFiles({
    name: "Plex-Italic.woff2",
    mimeType: "font/woff2",
    buffer: HEADING_FILE,
  })

  await expect(heading.getByText("Not saved yet")).toBeVisible()
  expect(sent).toEqual([])
  // The preview is set in the picked file, registered from its bytes (no fetch, so no
  // font-src question): its face has loaded.
  const preview = await heading.locator('[data-slot="font-preview"] [data-slot="item-content"]').evaluate((element) => {
    const family = getComputedStyle(element).fontFamily.split(",")[0].replace(/"/g, "").trim()
    const face = [...document.fonts].find((candidate) => candidate.family.replace(/"/g, "") === family)
    return { family, status: face?.status ?? "absent" }
  })
  expect(preview.family).toMatch(/^AuthSystem Font Preview heading/)
  expect(preview.status).toBe("loaded")
  await page.screenshot({ path: `${SCREENS}/console-fonts-tab-staged-en-1440.png`, fullPage: true })

  await heading.getByRole("button", { name: "Save" }).click()
  await expect(heading.getByText("Not saved yet")).toBeHidden()

  expect(sent).toHaveLength(1)
  expect(sent[0].type).toMatch(/^multipart\/form-data; boundary=/)
  expect(sent[0].body.includes(HEADING_FILE.subarray(0, 64))).toBe(true)
  expect(sent[0].body.toString("latin1")).toContain('name="file"; filename="Plex-Italic.woff2"')
})
