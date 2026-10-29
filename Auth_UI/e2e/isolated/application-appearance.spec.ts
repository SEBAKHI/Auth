import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * An application's own appearance (an application › Appearance), in the
 * production build: off by default and taking no edit, switched on it edits
 * the same choices as the platform's, its preview tiles are painted by the
 * draft, and the console around it keeps the platform's colours. Saving sends
 * the switch and the whole appearance. Arabic and English, light and dark,
 * 1440px and 375px; every case leaves a screenshot of the card.
 */

const catalogs = { en, ar } as const
const APP_ID = "5d1b7f2e-8c4a-4e6b-9f3d-2a7c1e9b4d60"

async function install(
  page: Page,
  { mode, language, onPut }: { mode: "light" | "dark"; language: keyof typeof catalogs; onPut?: (body: unknown) => void }
) {
  await page.addInitScript((value) => localStorage.setItem("theme", value), mode)
  const application = {
    id: APP_ID,
    code: "crm",
    name: "Example CRM",
    isActive: true,
    accessMode: "Restricted",
    themeEnabled: false,
    theme: null,
    redirectUris: [],
    postLogoutRedirectUris: [],
    confidentialRedirectUris: [],
    allowedScopes: [],
    createdAt: "2026-08-01T09:00:00Z",
  }
  await installAuthenticatedApi(
    page,
    ["applications:read", "applications:update"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/applications/${APP_ID}/appearance` && route.request().method() === "PUT") {
        const body = route.request().postDataJSON() as { enabled: boolean; theme: unknown }
        onPut?.(body)
        await fulfillJson(route, body)
        return true
      }
      if (path === `/api/v1/applications/${APP_ID}`) {
        await fulfillJson(route, application)
        return true
      }
      if (path.endsWith("/roles") || path.endsWith("/permissions")) {
        await fulfillJson(route, [])
        return true
      }
      await fulfillJson(route, { items: [], users: [], organizations: [], totalCount: 0, totalPages: 0 })
      return true
    },
    { preferredLanguage: language }
  )
}

const cssVar = (page: Page, name: string) =>
  page.evaluate((n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim(), name)

async function openTab(page: Page, language: keyof typeof catalogs) {
  const t = catalogs[language]
  await page.goto(`/applications/${APP_ID}?tab=appearance`)
  const card = page
    .locator('[data-slot="card"]')
    .filter({ has: page.getByText(t.applications.appearance.subtitle, { exact: true }) })
  await expect(card).toBeVisible()
  await expect(page.locator("html")).toHaveAttribute("dir", language === "ar" ? "rtl" : "ltr")
  return { card, t }
}

async function chooseTheme(page: Page, label: string, option: string) {
  await page.getByRole("combobox", { name: label }).click()
  await page.getByRole("option", { name: option, exact: true }).click()
}

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    test(`switched on, the application's appearance paints its preview only (${language}, ${mode})`, async ({
      page,
    }, testInfo) => {
      await page.setViewportSize({ width: 1440, height: 1200 })
      const sent: unknown[] = []
      await install(page, { mode, language, onPut: (body) => sent.push(body) })
      const { card, t } = await openTab(page, language)

      // Off: what would apply, taking no edit.
      const toggle = card.getByRole("switch", { name: t.applications.appearance.enable })
      await expect(toggle).not.toBeChecked()
      await expect(card.getByRole("combobox", { name: t.platformSettings.appearance.theme })).toBeDisabled()
      await expect(card.getByText(t.applications.appearance.disabledHint)).toBeVisible()
      await card.screenshot({ path: testInfo.outputPath(`tab-off-${language}-${mode}.png`) })

      const consolePrimary = await cssVar(page, "--primary")
      const tile = card.locator(`[data-appearance-scope] [data-mode="${mode}"]`)
      const tileSave = tile.locator('[data-slot="button"][data-size="default"]')
      const tilePrimary = () => tileSave.evaluate((element) => getComputedStyle(element).backgroundColor)
      const before = await tilePrimary()

      await toggle.click()
      await expect(card.getByText(t.applications.appearance.enabledHint)).toBeVisible()
      await chooseTheme(page, t.platformSettings.appearance.theme, "Violet")

      // The preview takes the draft; the console does not.
      await expect.poll(tilePrimary).not.toBe(before)
      expect(await cssVar(page, "--primary")).toBe(consolePrimary)
      const outside = page.getByRole("button", { name: t.common.save, exact: true })
      expect(await outside.evaluate((element) => getComputedStyle(element).backgroundColor)).toBe(before)

      const results = await new AxeBuilder({ page })
        .include('[data-slot="card"]')
        // The preview is the administrator's colours, hidden from assistive technology.
        .exclude("[data-appearance-scope]")
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
        .analyze()
      expect(results.violations.map((violation) => `${violation.id}: ${violation.nodes[0]?.target}`)).toEqual([])
      await card.screenshot({ path: testInfo.outputPath(`tab-on-${language}-${mode}.png`) })

      await outside.click()
      await expect.poll(() => sent.length).toBe(1)
      expect(sent[0]).toMatchObject({
        enabled: true,
        theme: { base: { preset: "neutral" }, theme: { preset: "violet" }, chart: { preset: "cyan" } },
      })
      await expect(page.getByText(t.applications.appearance.saved)).toBeVisible()
    })

    test(`the tab fits a phone (${language}, ${mode})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 375, height: 900 })
      await install(page, { mode, language })
      const { card, t } = await openTab(page, language)

      await card.getByRole("switch", { name: t.applications.appearance.enable }).click()
      await expectNoShellOverflow(page, `appearance tab, ${language} ${mode}, 375px`)
      await page.screenshot({ path: testInfo.outputPath(`tab-375-${language}-${mode}.png`), fullPage: true })
    })
  }
}

test("switching tabs keeps an unsaved draft", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1200 })
  await install(page, { mode: "light", language: "en" })
  const { card, t } = await openTab(page, "en")

  await card.getByRole("switch", { name: t.applications.appearance.enable }).click()
  await chooseTheme(page, t.platformSettings.appearance.theme, "Violet")
  await page.getByRole("tab", { name: t.nav.roles }).click()
  await page.getByRole("tab", { name: t.applications.appearance.tab }).click()

  await expect(card.getByRole("switch", { name: t.applications.appearance.enable })).toBeChecked()
  await expect(card.getByRole("combobox", { name: t.platformSettings.appearance.theme })).toHaveText("Violet")
})
