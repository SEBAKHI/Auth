import { expect, test, type Page } from "@playwright/test"

import { fulfillJson } from "../mock-authenticated-api"
import { holdAppScripts, sampleFrames, type PaintedFrame } from "../first-paint"
import { installAnonymousApi } from "./mock-anonymous-api"

/**
 * An application's own appearance on the accounts pages, from the very first
 * frame. The browser keeps the platform's computed stylesheet and, beside it,
 * one per application seen switched on; the first frame of a page reads only
 * the scope of the page's validated request, or the platform's.
 *
 * Each case first visits pages normally, so the cache is the one a real
 * visitor builds; then holds the app's scripts back and samples every
 * animation frame (each precedes a paint) of the page under test. The body's
 * background tells the scopes apart: each appearance has a custom base colour,
 * which IS the page background. The colours are read from the page itself
 * once it settles, never computed here.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const pageOf = (clientId: string) => `/login?client_id=${clientId}&auth_request=${HANDLE}`
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
  // A test may hold the applications' answers back, and count them.
  const answers = { heldMs: 0, answered: 0 }
  await installAnonymousApi(page, async (route, url) => {
    const match = /^\/api\/v1\/applications\/([^/]+)\/public-branding$/i.exec(url.pathname)
    if (!match) return false
    const clientId = decodeURIComponent(match[1])
    if (answers.heldMs) await new Promise((resolve) => setTimeout(resolve, answers.heldMs))
    await fulfillJson(route, { name: clientId.toUpperCase(), theme: appearances[clientId] ?? null })
    answers.answered++
    return true
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

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" })
  await page.addInitScript(() => localStorage.setItem("theme", "light"))
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
  // An Arabic visitor: React mounts only once the Arabic chunk (held back too) has loaded, so
  // the frames between the entry script and the app are painted by the first-frame cache read
  // alone. In English the app mounts within the entry script's own frame and corrects it unseen.
  await page.addInitScript(() => localStorage.setItem("auth.language", "ar"))
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
