import { expect, type Page, test } from "@playwright/test"

import { expectNoCrushedContent } from "../layout-overflow"
import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * The sign-in pages in two halves (owner, 2026-10-28): the form half on the
 * inline-start side, the panel on the other from the `lg` breakpoint up, and
 * each logo in its own place at its own size: the platform's mark 60px in the
 * corner, never smaller when an application is named, and the application's
 * 48px above the title (owner, 2026-10-31).
 *
 * Measured in the built app, because each of these is a CSS fact a unit test
 * cannot see: which side each half lands on under RTL, where the panel starts
 * to show, and how tall each logo is drawn.
 */

const AUTHORIZE = "https://auth.example.com/api/v1/auth/authorize?client_id=app&state=xyz"
const FLOW = `/login?returnTo=${encodeURIComponent(AUTHORIZE)}`
const APP_LOGO = "https://auth.example.com/uploads/images/partner.svg"
const PLATFORM_LOGO = "https://auth.example.com/uploads/images/platform.svg"

/** A wide wordmark, so the logo sizes are those of a real one, not of a square. */
const wordmark = (width: number) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="64"><rect width="${width}" height="64" fill="#777"/></svg>`

async function open(
  page: Page,
  path: string,
  language: "en" | "ar",
  width: number,
  names: { platform?: string; platformLogo?: boolean; app?: string } = {}
) {
  const platformName = names.platform ?? "AuthSystem"
  const platformLogo = names.platformLogo ?? true
  await page.addInitScript((lang) => localStorage.setItem("auth.language", lang), language)
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() !== "/api/v1/applications/app/public-branding") return false
    await fulfillJson(route, {
      name: names.app ?? "Partner App",
      logoUrl: APP_LOGO,
      logoUrlDark: null,
      homeUrl: null,
    })
    return true
  })
  // Registered after the defaults, so it answers first.
  await page.route("**/api/v1/Platform/branding", (route) =>
    fulfillJson(route, {
      platformName,
      logoUrl: platformLogo ? PLATFORM_LOGO : null,
      staySignedInOffered: true,
    })
  )
  await page.route("https://auth.example.com/uploads/images/*.svg", (route) =>
    route.fulfill({ contentType: "image/svg+xml", body: wordmark(280) })
  )
  await page.setViewportSize({ width, height: 900 })
  await page.goto(path)
  if (platformLogo) await expect(page.getByRole("img", { name: platformName })).toBeVisible()
  else await expect(page.locator("header").getByText(platformName)).toBeVisible()
}

const box = (page: Page, selector: string) =>
  page.locator(selector).first().evaluate((el) => {
    const r = el.getBoundingClientRect()
    return { left: r.left, right: r.right, height: r.height, display: getComputedStyle(el).display }
  })

test("the panel shows from 1024px up, and only from there", async ({ page }) => {
  await open(page, FLOW, "en", 1023)
  expect((await box(page, '[data-slot="auth-panel"]')).display).toBe("none")

  await page.setViewportSize({ width: 1024, height: 900 })
  expect((await box(page, '[data-slot="auth-panel"]')).display).toBe("block")
})

for (const language of ["en", "ar"] as const) {
  test(`${language}: the form half is on the inline-start side and the panel on the other`, async ({
    page,
  }) => {
    await open(page, FLOW, language, 1440)

    const form = await box(page, "main")
    const panel = await box(page, '[data-slot="auth-panel"]')
    if (language === "ar") expect(form.left).toBeGreaterThan(panel.left)
    else expect(form.left).toBeLessThan(panel.left)

    // The corner mark starts the header on the same side as the form half.
    const corner = await box(page, "header img")
    const toggles = await box(page, "header > div:last-child")
    if (language === "ar") expect(corner.left).toBeGreaterThan(toggles.left)
    else expect(corner.left).toBeLessThan(toggles.left)

    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0)
    await expectNoCrushedContent(page, `${language} at 1440px`)
  })
}

for (const width of [375, 1440]) {
  test(`at ${width}px each logo keeps its own size: the application's 48px above the title, the platform's 60px in the corner`, async ({
    page,
  }) => {
    await open(page, FLOW, "ar", width)

    const app = page.getByRole("main").getByRole("img", { name: "Partner App" })
    await expect(app).toBeVisible()
    await expect(page.getByRole("main").getByText("للمتابعة إلى Partner App.")).toBeVisible()
    const appHeight = (await app.boundingBox())!.height
    const cornerHeight = (await box(page, "header img")).height
    expect(appHeight).toBeCloseTo(48, 0)
    // No smaller for the application being named (it was 28px before).
    expect(cornerHeight).toBeCloseTo(60, 0)
  })
}

test("without an application, the platform's mark is 60px and the only logo", async ({
  page,
}) => {
  await open(page, "/login", "en", 1440)

  expect((await box(page, "header img")).height).toBeCloseTo(60, 0)
  await expect(page.getByRole("main").getByRole("img")).toHaveCount(0)
  await expect(page.getByText(/Secured by/)).toHaveCount(0)
})

test("ar at 320px: a long Latin platform name in the corner keeps its beginning and loses its end", async ({
  page,
}) => {
  const platform = "Platform For Everyone Everywhere"
  await open(page, "/login", "ar", 320, { platform, platformLogo: false })

  const name = page.locator("header").getByText(platform)
  const cut = await name.evaluate((el) => el.scrollWidth > el.clientWidth)
  // A floor: without the cut there is nothing for the assertion below to see.
  expect(cut).toBe(true)

  const firstLetterInside = await name.evaluate((el) => {
    const range = document.createRange()
    range.setStart(el.firstChild!, 0)
    range.setEnd(el.firstChild!, 1)
    const letter = range.getBoundingClientRect()
    const shown = el.getBoundingClientRect()
    return letter.width > 0 && letter.left >= shown.left - 1 && letter.right <= shown.right + 1
  })
  expect(firstLetterInside).toBe(true)
})

test("at 375px a long application name with no space stays inside the screen", async ({ page }) => {
  const app = "A".repeat(68)
  await open(page, FLOW, "en", 375, { app })
  await expect(page.getByRole("main").getByText(`Continue to ${app}.`)).toBeVisible()

  // The text, not the boxes: an unbreakable word spills out of its paragraph
  // while the paragraph itself stays on screen.
  const lines = await page.evaluate(() => {
    const walker = document.createTreeWalker(document.querySelector("main")!, NodeFilter.SHOW_TEXT)
    const rects: { left: number; right: number }[] = []
    while (walker.nextNode()) {
      if (!walker.currentNode.textContent?.trim()) continue
      const range = document.createRange()
      range.selectNodeContents(walker.currentNode)
      for (const r of range.getClientRects()) if (r.width > 0) rects.push({ left: r.left, right: r.right })
    }
    return { measured: rects.length, outside: rects.filter((r) => r.left < -1 || r.right > innerWidth + 1).length }
  })
  // A floor, so a walk that finds no text cannot pass.
  expect(lines.measured).toBeGreaterThan(5)
  expect(lines.outside).toBe(0)
})
