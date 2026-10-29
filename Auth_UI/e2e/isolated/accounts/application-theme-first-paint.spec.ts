import { expect, test, type Page } from "@playwright/test"

import { fulfillJson } from "../mock-authenticated-api"
import { holdAppScripts, sampleFrames, type PaintedFrame } from "../first-paint"
import { installAnonymousApi } from "./mock-anonymous-api"

/**
 * An application's own appearance on the accounts pages, from the very first
 * frame. The browser keeps the platform's computed stylesheet and, beside it,
 * one per application seen switched on; the first frame of a page reads only
 * the scope of the application the page names (its validated request, or on
 * the sign-out pages and the expired request, its client id), or the platform's.
 *
 * Each case first visits pages normally, so the cache is the one a real
 * visitor builds; then holds the app's scripts back and samples every
 * animation frame (each precedes a paint) of the page under test. The body's
 * background tells the scopes apart: each appearance has a custom base colour,
 * which IS the page background. The colours are read from the page itself
 * once it settles, never computed here.
 *
 * Every case runs in English and in Arabic. In English React mounts within the
 * entry script's own frame and corrects a first-frame mistake before the
 * browser paints it; an Arabic visitor waits for the Arabic chunk (held back
 * too), so the frames between the entry script and the app are painted by the
 * first-frame cache read alone. A wrong read can only be seen there.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const pageOf = (clientId: string) => `/login?client_id=${clientId}&auth_request=${HANDLE}`
/** The organization step of a flow: its own page, with its own header. */
const setupOf = (clientId: string) => `/create-organization?client_id=${clientId}&auth_request=${HANDLE}`
/**
 * A page that names an application by the client id of its own address, with no
 * request: where a relying party's sign-out ends (owner, 2026-10-29: it takes the
 * application's appearance too). The other two such pages settle the same way.
 */
const signedOutOf = (clientId: string) => `/signed-out?client_id=${clientId}`
const NAMED_PAGES = ["/logout", "/signed-out", "/sign-in-expired"] as const
const PLATFORM_PAGE = "/login"
const HOLD_MS = 800
/** A floor, so a run that sampled nothing cannot pass. */
const MIN_FRAMES = 10
/** preset.css's own light background: what every frame before the app's module runs shows. */
const PRESET_LIGHT = "oklch(1 0 0)"

type Appearance = ReturnType<typeof appearance>

const appearance = (light: string, dark: string) => ({
  base: { preset: "custom", light, dark },
  theme: { preset: "blue" },
  chart: { preset: "cyan" },
  radius: "default",
  menuAccent: "subtle",
  controlSize: { preset: "default" },
  tableRowHeight: { preset: "default" },
})

/**
 * The API of one test, with three appearances no two scopes share (test data,
 * not styling). Returns the applications' appearances, which a test may switch
 * off mid-way as an administrator would.
 */
async function installApi(page: Page) {
  const appearances: Record<string, Appearance | null> = {
    crm: appearance("#fef3c7", "#78350f"),
    desk: appearance("#dcfce7", "#14532d"),
  }
  // A test may hold the applications' answers back, and count them; and answer an
  // application 404, as the API does once it is unknown or switched off.
  const answers = { heldMs: 0, answered: 0, missing: new Set<string>() }
  await installAnonymousApi(page, async (route, url) => {
    const match = /^\/api\/v1\/applications\/([^/]+)\/public-branding$/i.exec(url.pathname)
    if (match) {
      const clientId = decodeURIComponent(match[1])
      if (answers.heldMs) await new Promise((resolve) => setTimeout(resolve, answers.heldMs))
      if (answers.missing.has(clientId)) {
        await fulfillJson(route, { title: "Not Found", code: "Application.NotFound" }, 404)
        answers.answered++
        return true
      }
      await fulfillJson(route, { name: clientId.toUpperCase(), theme: appearances[clientId] ?? null })
      answers.answered++
      return true
    }
    // The organization step, open: a signed-in person who may create one.
    if (url.pathname.toLowerCase() === "/api/v1/auth/organization-setup") {
      await fulfillJson(route, {
        canCreate: true,
        limit: 1,
        ownedOrganizations: [],
        email: "customer@example.test",
        emailConfirmed: true,
      })
      return true
    }
    return false
  })
  // Registered after the defaults, so it answers first: the platform's branding with its appearance.
  await page.route(
    (url) => url.pathname.toLowerCase() === "/api/v1/platform/branding",
    (route) =>
      fulfillJson(route, {
        platformName: "AuthSystem",
        staySignedInOffered: true,
        theme: appearance("#dbeafe", "#1e3a8a"),
      })
  )
  return { appearances, answers }
}

const background = (page: Page) =>
  page.evaluate(() => getComputedStyle(document.body).backgroundColor)

const cached = (page: Page, key: string) => page.evaluate((name) => localStorage.getItem(name), key)

/** Visits a page normally until its appearance is computed and cached; returns its background. */
async function settle(page: Page, path: string, cacheKey: string): Promise<string> {
  await page.goto(path)
  await expect.poll(() => cached(page, cacheKey)).not.toBeNull()
  await expect.poll(() => background(page)).not.toBe(PRESET_LIGHT)
  return background(page)
}

/** Every frame painted so far on this page (`sampleFrames` must have been installed). */
async function paintedFrames(page: Page): Promise<PaintedFrame[]> {
  // A little longer first: a late replacement would show up here.
  await page.waitForTimeout(300)
  const frames = await page.evaluate(() => (window as unknown as { __frames: PaintedFrame[] }).__frames)
  return frames.filter((frame) => frame.background !== null)
}

/** Every frame of a visit with the scripts held back, until the page settles on `settled`. */
async function framesOf(page: Page, path: string, settled: string): Promise<PaintedFrame[]> {
  await page.goto(path)
  await page.waitForFunction(() => (document.getElementById("root")?.childElementCount ?? 0) > 0)
  await expect.poll(() => background(page)).toBe(settled)
  return paintedFrames(page)
}

const colours = (frames: PaintedFrame[]) => [...new Set(frames.map((frame) => frame.background))]

for (const language of ["en", "ar"] as const) {
  test.describe(`${language} visitor`, () => {
    test.beforeEach(async ({ page }) => {
      await page.emulateMedia({ colorScheme: "light" })
      await page.addInitScript((code) => {
        localStorage.setItem("theme", "light")
        localStorage.setItem("auth.language", code)
      }, language)
    })

    test("an application's page paints with its own appearance from the first frame, never another's or the platform's", async ({
      page,
    }) => {
      const { answers } = await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")
      const desk = await settle(page, pageOf("desk"), "auth.ui.theme-css:app:desk")
      expect(new Set([PRESET_LIGHT, platform, crm, desk]).size).toBe(4)

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      // The application's answer is held back: whatever the page shows meanwhile came from the cache.
      answers.heldMs = 2000
      const before = answers.answered
      await page.goto(pageOf("crm"))
      await page.waitForFunction(() => (document.getElementById("root")?.childElementCount ?? 0) > 0)
      expect(await background(page)).toBe(crm)
      expect(answers.answered, "the application's branding is still on its way").toBe(before)
      await expect.poll(() => answers.answered).toBe(before + 1)
      const frames = await paintedFrames(page)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(colours(frames).filter((colour) => colour !== PRESET_LIGHT && colour !== crm)).toEqual([])
      expect(frames.filter((frame) => frame.background === crm).length).toBeGreaterThan(MIN_FRAMES)
    })

    test("an application seen for the first time paints with the platform's, never with another application's", async ({
      page,
    }) => {
      await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      // Desk's appearance is not cached: until its branding answers, the platform's shows.
      await page.goto(pageOf("desk"))
      await expect.poll(() => cached(page, "auth.ui.theme-css:app:desk")).not.toBeNull()
      await expect.poll(() => background(page)).not.toBe(platform)
      const desk = await background(page)
      const frames = await paintedFrames(page)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(desk).not.toBe(crm)
      expect(colours(frames)).not.toContain(crm)
      expect(colours(frames).filter((colour) => ![PRESET_LIGHT, platform, desk].includes(colour!))).toEqual([])
    })

    test("the platform's own pages never show an application's appearance", async ({ page }) => {
      await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")
      const desk = await settle(page, pageOf("desk"), "auth.ui.theme-css:app:desk")

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      const frames = await framesOf(page, PLATFORM_PAGE, platform)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(colours(frames)).not.toContain(crm)
      expect(colours(frames)).not.toContain(desk)
      expect(colours(frames).filter((colour) => colour !== PRESET_LIGHT && colour !== platform)).toEqual([])
    })

    test("an application whose appearance is switched off shows the platform's, and its cached one is forgotten", async ({
      page,
    }) => {
      const { appearances } = await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")

      // The administrator switches it off: the branding now carries no appearance.
      appearances.crm = null
      await page.goto(pageOf("crm"))
      await expect.poll(() => cached(page, "auth.ui.theme-css:app:crm")).toBeNull()
      await expect.poll(() => background(page)).toBe(platform)

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      const frames = await framesOf(page, pageOf("crm"), platform)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(colours(frames)).not.toContain(crm)
      expect(colours(frames).filter((colour) => colour !== PRESET_LIGHT && colour !== platform)).toEqual([])
    })

    // The organization step has its own header rather than the flow's: it must
    // name the application's appearance itself. A person with a single sign-on
    // session is sent straight to it, so it may be the flow's only page.
    test("the organization step takes the application's appearance, though nothing of it is cached", async ({
      page,
    }) => {
      await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")

      await page.goto(setupOf("crm"))
      await expect(page.getByRole("textbox")).toBeVisible()
      await expect.poll(() => cached(page, "auth.ui.theme-css:app:crm")).not.toBeNull()
      await expect.poll(() => background(page)).not.toBe(platform)
      const crm = await background(page)

      // The same colours as on the application's sign-in page.
      expect(await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")).toBe(crm)
    })

    test("a page that names an application by its client id paints with that application's appearance from the first frame", async ({
      page,
    }) => {
      const { answers } = await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")
      await settle(page, pageOf("desk"), "auth.ui.theme-css:app:desk")

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      // The application's answer is held back: whatever the page shows meanwhile came from the cache.
      answers.heldMs = 2000
      const before = answers.answered
      await page.goto(signedOutOf("crm"))
      await page.waitForFunction(() => (document.getElementById("root")?.childElementCount ?? 0) > 0)
      expect(await background(page)).toBe(crm)
      expect(answers.answered, "the application's branding is still on its way").toBe(before)
      await expect.poll(() => answers.answered).toBe(before + 1)
      const frames = await paintedFrames(page)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(platform).not.toBe(crm)
      expect(colours(frames).filter((colour) => colour !== PRESET_LIGHT && colour !== crm)).toEqual([])
      expect(frames.filter((frame) => frame.background === crm).length).toBeGreaterThan(MIN_FRAMES)
    })

    test("every page that names an application by its client id settles on that application's appearance", async ({
      page,
    }) => {
      await installApi(page)
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")

      for (const path of NAMED_PAGES) {
        // Nothing cached for it: only the page itself, through its header, can put the
        // application's appearance on (and cache it again).
        await page.evaluate(() => localStorage.removeItem("auth.ui.theme-css:app:crm"))
        await page.goto(`${path}?client_id=crm`)
        await expect(page.getByRole("heading", { level: 1 })).toBeVisible()
        await expect.poll(() => background(page), path).toBe(crm)
        await expect.poll(() => cached(page, "auth.ui.theme-css:app:crm"), path).not.toBeNull()
      }
    })

    test("a client id the server does not know paints with the platform's, never another application's", async ({
      page,
    }) => {
      const { answers } = await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")
      answers.missing.add("nobody")

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      const frames = await framesOf(page, signedOutOf("nobody"), platform)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(colours(frames)).not.toContain(crm)
      expect(colours(frames).filter((colour) => colour !== PRESET_LIGHT && colour !== platform)).toEqual([])
    })

    test("an application switched off since its appearance was cached shows the platform's there, and its cached one is forgotten", async ({
      page,
    }) => {
      const { answers } = await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")

      // The administrator deactivates it: its branding now answers 404.
      answers.missing.add("crm")
      await page.goto(signedOutOf("crm"))
      await expect.poll(() => cached(page, "auth.ui.theme-css:app:crm")).toBeNull()
      await expect.poll(() => background(page)).toBe(platform)

      await sampleFrames(page)
      await holdAppScripts(page, HOLD_MS)
      const frames = await framesOf(page, signedOutOf("crm"), platform)

      expect(frames.length).toBeGreaterThan(MIN_FRAMES)
      expect(colours(frames)).not.toContain(crm)
      expect(colours(frames).filter((colour) => colour !== PRESET_LIGHT && colour !== platform)).toEqual([])
    })

    test("the organization step of an application switched off shows the platform's, and forgets its cached one", async ({
      page,
    }) => {
      const { appearances } = await installApi(page)
      const platform = await settle(page, PLATFORM_PAGE, "auth.ui.theme-css")
      const crm = await settle(page, pageOf("crm"), "auth.ui.theme-css:app:crm")

      appearances.crm = null
      await page.goto(setupOf("crm"))
      await expect(page.getByRole("textbox")).toBeVisible()
      await expect.poll(() => cached(page, "auth.ui.theme-css:app:crm")).toBeNull()
      await expect.poll(() => background(page)).toBe(platform)
      expect(platform).not.toBe(crm)
    })
  })
}
