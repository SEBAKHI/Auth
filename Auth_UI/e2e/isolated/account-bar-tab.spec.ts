import { mkdirSync } from "node:fs"

import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Platform settings › Account bar on the built console (owner, 2026-11-02): the code a
 * linked application adds, written from choices, with the application's own end-session
 * address once an application is picked. In both directions, with no shell overflow.
 */

const SCREENSHOTS = process.env.ACCOUNT_BAR_SCREENSHOTS

const APPLICATION = {
  id: "a1",
  code: "CMS",
  name: "Commissions",
  isActive: true,
  postLogoutRedirectUris: ["https://cms.example.test/signed-out"],
}

async function install(page: Page, language: "en" | "ar") {
  await installAuthenticatedApi(
    page,
    ["platform-settings:manage", "applications:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, { platformName: "AuthSystem", hiddenLanguages: [] })
        return true
      }
      if (path === "/api/v1/applications") {
        await fulfillJson(route, { applications: [APPLICATION], totalCount: 1, totalPages: 1, pageNumber: 1, pageSize: 100 })
        return true
      }
      if (path === "/api/v1/applications/a1") {
        await fulfillJson(route, APPLICATION)
        return true
      }
      return false
    },
    { preferredLanguage: language }
  )
}

for (const [language, t] of [["en", en], ["ar", ar]] as const) {
  test(`writes the code from choices and the application's end-session address (${language})`, async ({ page }) => {
    await install(page, language)
    await page.setViewportSize({ width: 1440, height: 1100 })
    await page.goto("/admin/platform-settings?tab=account-bar")

    const a = t.platformSettings.accountBar
    await expect(page.getByRole("tab", { name: t.platformSettings.tabAccountBar })).toHaveAttribute("aria-selected", "true")
    await page.getByRole("radio", { name: a.showsOptions.avatar }).click()
    await page.getByRole("radio", { name: a.themeOptions.dark }).click()
    await expect(page.locator("pre code").first()).toContainText('hide="apps" theme="dark"')

    await page.getByRole("combobox", { name: a.application }).click()
    await page.getByRole("option", { name: "Commissions" }).click()
    await expect(page.getByRole("heading", { name: a.endSessionCode })).toBeVisible()
    await expect(page.locator("pre code").nth(1)).toContainText(
      "/api/v1/auth/end-session?client_id=CMS&post_logout_redirect_uri=https%3A%2F%2Fcms.example.test%2Fsigned-out"
    )

    await expectNoShellOverflow(page, `account bar tab, ${language}`)
    if (SCREENSHOTS) {
      mkdirSync(SCREENSHOTS, { recursive: true })
      await page.screenshot({ path: `${SCREENSHOTS}/tab-${language}.png` })
      await page.getByRole("heading", { name: a.cspCode }).scrollIntoViewIfNeeded()
      await page.screenshot({ path: `${SCREENSHOTS}/tab-${language}-lower.png` })
    }
  })
}
