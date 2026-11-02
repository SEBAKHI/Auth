import { mkdirSync } from "node:fs"

import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The account bar's code on the built console (owner, 2026-11-02): behind the application
 * page's actions menu, beside Edit; every option with more than one answer starts empty,
 * and the code appears once all are chosen. In both directions.
 */

const SCREENSHOTS = process.env.ACCOUNT_BAR_SCREENSHOTS

const APPLICATION = {
  id: "a1",
  code: "CMS",
  name: "Commissions",
  isActive: true,
  baseUrl: "https://cms.example.test/",
  initiateLoginUri: "https://cms.example.test/signin",
  redirectUris: [],
  postLogoutRedirectUris: ["http://localhost:8765/signed-out", "https://cms.example.test/signed-out"],
}

async function install(page: Page, language: "en" | "ar") {
  await installAuthenticatedApi(
    page,
    ["applications:read", "applications:update"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/applications/a1") {
        await fulfillJson(route, APPLICATION)
        return true
      }
      if (path.startsWith("/api/v1/applications/a1/")) {
        await fulfillJson(route, { items: [], users: [], roles: [], permissions: [], organizations: [], totalCount: 0 })
        return true
      }
      return false
    },
    { preferredLanguage: language }
  )
}

async function shot(page: Page, name: string) {
  if (!SCREENSHOTS) return
  mkdirSync(SCREENSHOTS, { recursive: true })
  await page.screenshot({ path: `${SCREENSHOTS}/${name}.png` })
}

for (const [language, t] of [["en", en], ["ar", ar]] as const) {
  test(`opens from the actions menu and writes the code once everything is chosen (${language})`, async ({ page }) => {
    await install(page, language)
    await page.setViewportSize({ width: 1440, height: 1000 })
    await page.goto("/applications/a1")

    await page.getByRole("button", { name: t.common.actions }).first().click()
    const menu = page.getByRole("menu")
    await expect(menu.getByRole("menuitem")).toHaveText([t.common.edit, t.applications.accountBar.menuItem])
    await shot(page, `menu-${language}`)
    await menu.getByRole("menuitem", { name: t.applications.accountBar.menuItem }).click()

    const a = t.applications.accountBar
    const dialog = page.getByRole("dialog", { name: a.title })
    await expect(dialog.getByText(a.incomplete)).toBeVisible()
    await shot(page, `dialog-empty-${language}`)

    await dialog.getByRole("radio", { name: a.showsOptions.both }).click()
    await dialog.getByRole("radio", { name: a.themeOptions.person }).click()
    await dialog.getByRole("textbox", { name: a.signOutHref }).fill("/auth/sign-out")
    await dialog.getByRole("combobox", { name: a.signIn }).click()
    await page.getByRole("option", { name: a.signInIam }).click()
    await dialog.getByRole("combobox", { name: a.returnUri }).click()
    await page.getByRole("option", { name: "https://cms.example.test/signed-out" }).click()

    await expect(dialog.getByRole("heading", { name: a.headerCode })).toBeVisible()
    await expect(dialog.locator("pre code").nth(1)).toContainText(
      "client_id=CMS&post_logout_redirect_uri=https%3A%2F%2Fcms.example.test%2Fsigned-out"
    )
    // Nothing of a long address is hidden: its box shows it whole, wrapped (no scroll).
    const endSession = dialog.locator("pre").nth(1)
    expect(await endSession.evaluate((element) => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(0)
    await endSession.scrollIntoViewIfNeeded()
    await shot(page, `dialog-done-${language}`)
  })
}
