import { expect, test, type Locator, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The logo's display size (UI batch P3, owner decisions D5/D6): the platform logo's on the
 * Identity tab, an application logo's on that application's Appearance tab, each outside the
 * upload dialog. Dragging previews the logo at real size in light and dark; Save sends the
 * height alone (no file is chosen, nothing is uploaded); "Use the default" sends null. The
 * console's own sign-in page draws the platform's mark at the height saved.
 *
 * Arabic and English, light and dark, 375px and 1440px; every case leaves a screenshot.
 */

const catalogs = { en, ar } as const
const APP_ID = "5d1b7f2e-8c4a-4e6b-9f3d-2a7c1e9b4d60"
const LOGO = { light: "/isolated-logos/light.svg", dark: "/isolated-logos/dark.svg" }

/** A 4:1 wordmark per mode, each in its own colour, so a tile showing the wrong one is seen. */
const wordmark = (fill: string) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="64" viewBox="0 0 256 64"><rect width="256" height="64" fill="${fill}"/></svg>`

interface Seen {
  puts: { path: string; body: unknown }[]
  uploads: string[]
}

async function install(
  page: Page,
  { mode, language, permissions }: { mode: "light" | "dark"; language: keyof typeof catalogs; permissions: string[] }
): Promise<Seen> {
  const seen: Seen = { puts: [], uploads: [] }
  await page.addInitScript((value) => localStorage.setItem("theme", value), mode)
  await page.route("**/isolated-logos/*", (route) =>
    route.fulfill({
      contentType: "image/svg+xml",
      body: wordmark(route.request().url().endsWith("dark.svg") ? "#f5f5f5" : "#111111"),
    })
  )
  let platformHeight: number | null = null
  let appHeight: number | null = null
  const settings = () => ({
    platformName: "Isolated Platform",
    logoUrl: LOGO.light,
    logoUrlDark: LOGO.dark,
    faviconUrl: null,
    logoHeightPx: platformHeight,
  })
  await installAuthenticatedApi(
    page,
    permissions,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path.startsWith("/api/v1/images")) {
        seen.uploads.push(path)
        await fulfillJson(route, { title: "No upload here" }, 500)
        return true
      }
      if (method === "PUT") {
        const body = route.request().postDataJSON() as { logoHeightPx: number | null }
        seen.puts.push({ path, body })
        if (path === "/api/v1/admin/platform-settings/logo-height") {
          platformHeight = body.logoHeightPx
          await fulfillJson(route, settings())
          return true
        }
        if (path === `/api/v1/applications/${APP_ID}/logo-height`) {
          appHeight = body.logoHeightPx
          await fulfillJson(route, { logoHeightPx: appHeight })
          return true
        }
        return false
      }
      if (path === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, settings())
        return true
      }
      if (path === `/api/v1/applications/${APP_ID}`) {
        await fulfillJson(route, {
          id: APP_ID,
          code: "crm",
          name: "Example CRM",
          isActive: true,
          accessMode: "Restricted",
          logoUrl: LOGO.light,
          logoUrlDark: LOGO.dark,
          logoHeightPx: appHeight,
          themeEnabled: false,
          theme: null,
          redirectUris: [],
          postLogoutRedirectUris: [],
          confidentialRedirectUris: [],
          allowedScopes: [],
          createdAt: "2026-08-01T09:00:00Z",
        })
        return true
      }
      if (path.startsWith(`/api/v1/applications/${APP_ID}/`)) {
        await fulfillJson(route, path.endsWith("/roles") || path.endsWith("/permissions") ? [] : { items: [], totalCount: 0 })
        return true
      }
      return false
    },
    { preferredLanguage: language }
  )
  return seen
}

function sizeCard(page: Page, language: keyof typeof catalogs) {
  return page
    .locator('[data-slot="card"]')
    .filter({ has: page.getByText(catalogs[language].logoDisplaySize.title, { exact: true }) })
}

/** The preview logo in one mode's tile: its source and drawn height. */
async function preview(card: Locator, mode: "light" | "dark") {
  const logo = card.locator(`[data-slot="logo-display-size-preview"][data-mode="${mode}"] img`)
  await expect(logo).toBeVisible()
  await logo.evaluate((img: HTMLImageElement) => img.decode())
  return logo.evaluate((img: HTMLImageElement) => ({
    src: new URL(img.src).pathname,
    height: img.getBoundingClientRect().height,
    width: img.getBoundingClientRect().width,
  }))
}

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    for (const width of [375, 1440]) {
      test(`platform logo size: previews, saves the height alone, resets (${language}, ${mode}, ${width}px)`, async ({
        page,
      }) => {
        const t = catalogs[language]
        await page.setViewportSize({ width, height: 900 })
        const seen = await install(page, { mode, language, permissions: ["platform-settings:manage"] })
        await page.goto("/admin/platform-settings")

        const card = sizeCard(page, language)
        await expect(card).toBeVisible()
        const slider = card.getByRole("slider", { name: t.logoDisplaySize.height })
        await expect(slider).toHaveAttribute("aria-valuenow", "60")
        expect((await preview(card, "light")).height).toBeCloseTo(60, 0)

        await slider.focus()
        await page.keyboard.press("End")
        const light = await preview(card, "light")
        const dark = await preview(card, "dark")
        expect(light.src).toBe(LOGO.light)
        expect(dark.src).toBe(LOGO.dark)
        expect(light.height).toBeCloseTo(120, 0)
        expect(dark.height).toBeCloseTo(120, 0)
        // 4:1 kept, or the tile's width when it is narrower than 480px.
        expect(light.width).toBeLessThanOrEqual(480.5)
        expect(seen.puts).toEqual([])

        await card.getByRole("button", { name: t.common.save, exact: true }).click()
        await expect.poll(() => seen.puts.length).toBe(1)
        expect(seen.puts[0]).toEqual({
          path: "/api/v1/admin/platform-settings/logo-height",
          body: { logoHeightPx: 120 },
        })

        await card.getByRole("button", { name: t.logoDisplaySize.reset.replace("{{px}}", "60") }).click()
        await expect(slider).toHaveAttribute("aria-valuenow", "60")
        await card.getByRole("button", { name: t.common.save, exact: true }).click()
        await expect.poll(() => seen.puts.length).toBe(2)
        expect(seen.puts[1]!.body).toEqual({ logoHeightPx: null })
        await expect(card.getByText(t.logoDisplaySize.isDefault, { exact: true })).toBeVisible()

        // Nothing was uploaded or re-encoded: the control never touches the image.
        expect(seen.uploads).toEqual([])
        await expectNoShellOverflow(page, `platform size card ${language} ${mode} ${width}`)
        await slider.focus()
        await page.keyboard.press("End")
        await card.screenshot({ path: test.info().outputPath(`platform-size-${language}-${mode}-${width}.png`) })
      })
    }
  }
}

for (const language of ["en", "ar"] as const) {
  for (const width of [375, 1440]) {
    test(`application logo size: on the Appearance tab, saved on its own (${language}, ${width}px)`, async ({ page }) => {
      const t = catalogs[language]
      await page.setViewportSize({ width, height: 900 })
      const seen = await install(page, {
        mode: "light",
        language,
        permissions: ["applications:read", "applications:update"],
      })
      await page.goto(`/applications/${APP_ID}?tab=appearance`)

      const card = sizeCard(page, language)
      await expect(card).toBeVisible()
      await expect(card.getByText(t.logoDisplaySize.applicationDescription, { exact: true })).toBeVisible()
      const slider = card.getByRole("slider", { name: t.logoDisplaySize.height })
      await expect(slider).toHaveAttribute("aria-valuenow", "48")
      expect((await preview(card, "dark")).src).toBe(LOGO.dark)

      await slider.focus()
      // Up raises the value in either direction (in Arabic, ArrowRight lowers it, as RTL should).
      await page.keyboard.press("ArrowUp")
      await page.keyboard.press("ArrowUp")
      expect((await preview(card, "light")).height).toBeCloseTo(50, 0)
      await card.getByRole("button", { name: t.common.save, exact: true }).click()
      await expect.poll(() => seen.puts.length).toBe(1)
      expect(seen.puts).toEqual([{ path: `/api/v1/applications/${APP_ID}/logo-height`, body: { logoHeightPx: 50 } }])
      expect(seen.uploads).toEqual([])

      await expectNoShellOverflow(page, `application size card ${language} ${width}`)
      await card.screenshot({ path: test.info().outputPath(`application-size-${language}-${width}.png`) })
    })
  }
}

test("someone who may only read an application sees its logo size and cannot change it", async ({ page }) => {
  await install(page, { mode: "light", language: "en", permissions: ["applications:read"] })
  await page.goto(`/applications/${APP_ID}?tab=appearance`)

  const card = sizeCard(page, "en")
  await expect(card).toBeVisible()
  await expect(card.getByRole("slider", { name: en.logoDisplaySize.height })).toHaveAttribute("data-disabled", "")
  await expect(card.getByRole("button", { name: en.common.save, exact: true })).toHaveCount(0)
})

for (const [stored, expected] of [
  [null, 60],
  [96, 96],
] as const) {
  test(`the console's own sign-in page draws the platform's mark at ${expected}px`, async ({ page }) => {
    await page.route("**/isolated-logos/*", (route) =>
      route.fulfill({ contentType: "image/svg+xml", body: wordmark("#111111") })
    )
    await page.route("**/api/v1/**", async (route) => {
      const path = new URL(route.request().url()).pathname.toLowerCase()
      if (path.endsWith("/platform/branding")) {
        await fulfillJson(route, {
          platformName: "Isolated Platform",
          logoUrl: LOGO.light,
          logoHeightPx: stored,
          staySignedInOffered: true,
        })
        return
      }
      await fulfillJson(route, { title: "Not here" }, 404)
    })
    await page.goto("/login")

    const mark = page.locator("header").getByRole("img", { name: "Isolated Platform" })
    await expect(mark).toBeVisible()
    await mark.evaluate((img: HTMLImageElement) => img.decode())
    const box = await mark.boundingBox()
    expect(box!.height).toBeCloseTo(expected, 0)
    expect(box!.width).toBeCloseTo(expected * 4, 0)
  })
}
