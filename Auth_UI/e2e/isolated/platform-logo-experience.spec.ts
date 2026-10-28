import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The platform's logos get the experience an application's logo has: the
 * branding card shows ONE circle for the light and dark logos, painted with the
 * logo of the mode the page is viewed in, and its Change dialog holds one circle
 * per mode, each painted in its own mode. The favicon stays its own circle
 * beside it (a separate field: a wordmark is illegible at tab-icon size).
 *
 * Run in Arabic and English, light and dark, at a phone and a desktop width;
 * each case leaves a screenshot of the card and of the dialog.
 */

// Every slot has its own file, so a circle wired to the wrong field shows up.
const LOGO = {
  light: "/isolated-logos/light.svg",
  dark: "/isolated-logos/dark.svg",
  favicon: "/isolated-logos/favicon.svg",
}

function wordmark(text: string, fill: string, width = 240) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="80" viewBox="0 0 ${width} 80"><text x="50%" y="55%" dominant-baseline="middle" text-anchor="middle" font-family="sans-serif" font-size="40" font-weight="700" fill="${fill}">${text}</text></svg>`
}

const SVG: Record<string, string> = {
  "light.svg": wordmark("LIGHT", "#111111"),
  "dark.svg": wordmark("DARK", "#f5f5f5"),
  "favicon.svg": wordmark("F", "#2563eb", 80),
}

const settings = {
  platformName: "Isolated Platform",
  logoUrl: LOGO.light,
  logoUrlDark: LOGO.dark,
  faviconUrl: LOGO.favicon,
}

const catalogs = { en, ar } as const

async function installPlatformSettings(
  page: Page,
  { theme, language }: { theme: "light" | "dark"; language: keyof typeof catalogs }
) {
  await page.addInitScript((value) => {
    localStorage.setItem("theme", value)
  }, theme)
  await page.route("**/isolated-logos/*", async (route) => {
    const file = new URL(route.request().url()).pathname.split("/").pop() ?? ""
    await route.fulfill({ contentType: "image/svg+xml", body: SVG[file] ?? "" })
  })
  await installAuthenticatedApi(
    page,
    ["platform-settings:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, settings)
        return true
      }
      return false
    },
    { preferredLanguage: language }
  )
}

for (const language of ["en", "ar"] as const) {
  for (const theme of ["light", "dark"] as const) {
    for (const width of [375, 1440]) {
      test(`platform logo: one themed circle + favicon (${language}, ${theme}, ${width}px)`, async ({
        page,
      }, testInfo) => {
        const t = catalogs[language]
        await page.setViewportSize({ width, height: 900 })
        await installPlatformSettings(page, { theme, language })
        await page.goto("/admin/platform-settings")

        const card = page
          .locator('[data-slot="card"]')
          .filter({ has: page.getByText(t.platformSettings.brandingTitle, { exact: true }) })
        await expect(card).toBeVisible()

        // Two circles: the themed logo and the favicon. Not three.
        const circles = card.getByRole("button", { name: t.common.avatar })
        await expect(circles).toHaveCount(2)
        await expect(card.getByText(t.platformSettings.logo, { exact: true })).toBeVisible()
        await expect(card.getByText(t.platformSettings.favicon, { exact: true })).toBeVisible()
        await expect(card.getByText(t.platformSettings.logoLight, { exact: true })).toHaveCount(0)
        await expect(card.getByText(t.platformSettings.logoDark, { exact: true })).toHaveCount(0)

        // The page circle carries the logo of the mode the page is in; the
        // favicon circle carries the favicon, whatever the mode.
        await expect(circles.nth(0).locator("img")).toHaveAttribute("src", LOGO[theme])
        await expect(circles.nth(1).locator("img")).toHaveAttribute("src", LOGO.favicon)

        await expectNoShellOverflow(page, `platform settings ${language} ${theme} ${width}`)
        await card.screenshot({ path: testInfo.outputPath(`card-${language}-${theme}-${width}.png`) })

        await circles.nth(0).click()
        await page.getByRole("menuitem", { name: t.common.change, exact: true }).click()

        const dialog = page.getByRole("dialog", { name: t.platformSettings.logoDialogTitle })
        await expect(dialog).toBeVisible()
        await expect(dialog.getByText(t.platformSettings.logoDialogDescription)).toBeVisible()

        // One circle per mode, each painted in its own mode with its own logo.
        const lightSlot = dialog.locator('[data-mode="light"]')
        const darkSlot = dialog.locator('[data-mode="dark"]')
        await expect(lightSlot.getByText(t.platformSettings.logoLight, { exact: true })).toBeVisible()
        await expect(darkSlot.getByText(t.platformSettings.logoDark, { exact: true })).toBeVisible()
        await expect(lightSlot.locator("img")).toHaveAttribute("src", LOGO.light)
        await expect(darkSlot.locator("img")).toHaveAttribute("src", LOGO.dark)
        const tile = async (slot: typeof lightSlot) =>
          slot.evaluate((el) => getComputedStyle(el).backgroundColor)
        expect(await tile(lightSlot)).not.toBe(await tile(darkSlot))

        await dialog.screenshot({ path: testInfo.outputPath(`dialog-${language}-${theme}-${width}.png`) })
      })
    }
  }
}
