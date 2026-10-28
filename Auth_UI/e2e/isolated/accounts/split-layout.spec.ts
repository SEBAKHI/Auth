import { expect, type Page, test } from "@playwright/test"

import { expectNoCrushedContent } from "../layout-overflow"
import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * The sign-in pages in two halves (owner, 2026-10-28): the form half on the
 * inline-start side, the panel on the other from the `lg` breakpoint up, and
 * one large logo per page.
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

async function open(page: Page, path: string, language: "en" | "ar", width: number) {
  await page.addInitScript((lang) => localStorage.setItem("auth.language", lang), language)
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() !== "/api/v1/applications/app/public-branding") return false
    await fulfillJson(route, { name: "Partner App", logoUrl: APP_LOGO, logoUrlDark: null, homeUrl: null })
    return true
  })
  // Registered after the defaults, so it answers first: a platform with a logo.
  await page.route("**/api/v1/Platform/branding", (route) =>
    fulfillJson(route, { platformName: "AuthSystem", logoUrl: PLATFORM_LOGO, staySignedInOffered: true })
  )
  await page.route("https://auth.example.com/uploads/images/*.svg", (route) =>
    route.fulfill({ contentType: "image/svg+xml", body: wordmark(280) })
  )
  await page.setViewportSize({ width, height: 900 })
  await page.goto(path)
  await expect(page.getByRole("img", { name: "AuthSystem" })).toBeVisible()
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
  test(`at ${width}px the application's logo is the large one, the platform's stays small in the corner`, async ({
    page,
  }) => {
    await open(page, FLOW, "ar", width)

    const app = page.getByRole("main").getByRole("img", { name: "Partner App" })
    await expect(app).toBeVisible()
    await expect(page.getByRole("main").getByText("للمتابعة إلى Partner App.")).toBeVisible()
    const appHeight = (await app.boundingBox())!.height
    const cornerHeight = (await box(page, "header img")).height
    // A floor, so a logo drawn at zero height cannot pass.
    expect(cornerHeight).toBeGreaterThan(16)
    expect(appHeight).toBeGreaterThan(cornerHeight * 1.5)
  })
}

test("without an application, the platform's mark takes its full corner size and is the only logo", async ({
  page,
}) => {
  await open(page, "/login", "en", 1440)

  const flowCorner = 28
  expect((await box(page, "header img")).height).toBeGreaterThan(flowCorner)
  await expect(page.getByRole("main").getByRole("img")).toHaveCount(0)
  await expect(page.getByText(/Secured by/)).toHaveCount(0)
})
