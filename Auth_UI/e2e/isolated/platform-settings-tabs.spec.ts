import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * UI batch phase P2, in a real browser: platform settings as URL-synced tabs (Identity,
 * Appearance, Templates), the platform's sign-in designs as a table, and Delete in the row
 * menus of sign-in designs, email templates, layouts and the privacy policy. A row the
 * server would refuse keeps its Delete, disabled. Arabic and English, light and dark, 1440px
 * and 375px, each with a screenshot.
 */

const catalogs = { en, ar } as const
const fill = (text: string, values: Record<string, string>) =>
  Object.entries(values).reduce((s, [k, v]) => s.replace(`{{${k}}}`, v), text)

const DESIGNS = [
  { id: "d1", applicationId: null, name: "Autumn", isActive: true, showPlatformLogo: false, contentHash: "a".repeat(64), createdAt: "2026-10-01T10:00:00Z", modifiedAt: "2026-10-20T10:00:00Z" },
  { id: "d2", applicationId: null, name: "Spring", isActive: false, showPlatformLogo: true, contentHash: "b".repeat(64), createdAt: "2026-10-02T10:00:00Z", modifiedAt: null },
]
const TEMPLATES = [
  { id: "t-sys", typeName: "Password reset", typeCode: "password-reset", typeIsSystem: true, applicationId: null, channel: "Email", isPublished: true, publishedVersionNumber: 3, translationCount: 7, defaultLanguage: "en", createdAt: "2026-08-01T10:00:00Z" },
  { id: "t-app", typeName: "Billing receipt", typeCode: "billing-receipt", typeIsSystem: false, applicationId: "app-1", applicationName: "Billing", channel: "Email", isPublished: false, translationCount: 1, defaultLanguage: "en", createdAt: "2026-09-01T10:00:00Z" },
]
const LAYOUTS = [
  { id: "l-global", name: "Default email", applicationId: null, channel: "Email", isPublished: true, createdAt: "2026-08-01T10:00:00Z" },
  { id: "l-app", name: "Billing email", applicationId: "app-1", applicationName: "Billing", channel: "Email", isPublished: true, createdAt: "2026-09-01T10:00:00Z" },
]
const VERSIONS = [
  { id: "v-live", version: "2026.09", isPublished: true, canDelete: false, languages: ["en", "ar"], effectiveDateUtc: "2026-09-01T00:00:00Z", notifiedAtUtc: "2026-09-02T00:00:00Z", notifiedCount: 12 },
  { id: "v-draft", version: "2026.11", isPublished: false, canDelete: true, languages: [], effectiveDateUtc: "2026-11-01T00:00:00Z" },
]

interface Seen {
  method: string
  path: string
}

async function install(page: Page, language: keyof typeof catalogs, mode: "light" | "dark", seen: Seen[]) {
  await page.addInitScript((value) => localStorage.setItem("theme", value), mode)
  await installAuthenticatedApi(
    page,
    [
      "platform-settings:manage",
      "notification-templates:read",
      "notification-templates:manage",
      "notification-layouts:manage",
      "privacy-policy:read",
      "privacy-policy:manage",
    ],
    async (route, url) => {
      const request = route.request()
      const path = url.pathname.toLowerCase()
      if (request.method() === "DELETE") {
        seen.push({ method: "DELETE", path })
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, { platformName: "Isolated Platform", logoUrl: null, logoUrlDark: null, faviconUrl: null, theme: null })
        return true
      }
      if (path === "/api/v1/admin/platform-settings/login-designs") {
        await fulfillJson(route, DESIGNS)
        return true
      }
      if (path === "/api/v1/notification-templates") {
        await fulfillJson(route, { templates: TEMPLATES, totalCount: 2, totalPages: 1 })
        return true
      }
      if (path === "/api/v1/notification-types") {
        await fulfillJson(route, [])
        return true
      }
      if (path === "/api/v1/notification-layouts") {
        await fulfillJson(route, LAYOUTS)
        return true
      }
      if (path === "/api/v1/privacy-policy/versions") {
        await fulfillJson(route, VERSIONS)
        return true
      }
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    },
    { preferredLanguage: language }
  )
}

/** Waits for the open dialog to finish its entrance, so a screenshot shows it, not its fade. */
async function settledDialog(page: Page) {
  const dialog = page.getByRole("alertdialog")
  await dialog.evaluate((el) => Promise.all(el.getAnimations({ subtree: true }).map((a) => a.finished)))
  return dialog
}

/** Opens a row's menu by a text in that row; the menu buttons carry the row's name or a generic label. */
async function rowMenu(page: Page, rowText: string, label: string) {
  const row = page.getByRole("row").filter({ hasText: rowText })
  await row.getByRole("button", { name: label }).click()
  return page.getByRole("menu")
}

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    for (const width of [1440, 375] as const) {
      const where = `${language}, ${mode}, ${width}px`

      test(`platform settings tabs and the designs table (${where})`, async ({ page }, testInfo) => {
        const t = catalogs[language]
        const seen: Seen[] = []
        await page.setViewportSize({ width, height: 1000 })
        await install(page, language, mode, seen)
        await page.goto("/admin/platform-settings")

        // Identity is the default and writes no parameter.
        await expect(page.getByRole("tab", { name: t.platformSettings.tabIdentity })).toHaveAttribute("aria-selected", "true")
        await expect(page.getByRole("tab")).toHaveText([
          t.platformSettings.tabIdentity,
          t.platformSettings.appearance.title,
          t.loginDesigns.tab,
        ])
        await expect(
          page.getByRole("tabpanel").locator('[data-slot="card-title"]').filter({ hasText: t.platformSettings.brandingTitle })
        ).toBeVisible()
        expect(new URL(page.url()).search).toBe("")
        await expectNoShellOverflow(page, `identity ${where}`)
        await page.screenshot({ path: testInfo.outputPath(`identity-${language}-${mode}-${width}.png`), fullPage: true })

        await page.getByRole("tab", { name: t.platformSettings.appearance.title }).click()
        await expect(page).toHaveURL(/\?tab=appearance$/)
        await expect(page.getByText(t.platformSettings.appearance.subtitle)).toBeVisible()
        await page.screenshot({ path: testInfo.outputPath(`appearance-${language}-${mode}-${width}.png`), fullPage: true })

        await page.getByRole("tab", { name: t.loginDesigns.tab }).click()
        await expect(page).toHaveURL(/\?tab=templates$/)
        const panel = page.getByRole("tabpanel")
        await expect(panel.getByRole("table")).toBeVisible()
        await expect(panel.getByText(fill(t.loginDesigns.visitorsSee, { name: "Autumn" }))).toBeVisible()
        await expect(panel.getByRole("link", { name: "Spring" })).toBeVisible()
        await expectNoShellOverflow(page, `templates ${where}`)

        // The active design keeps Delete, disabled; an inactive one is deleted after a named confirmation.
        const activeMenu = await rowMenu(page, "Autumn", fill(t.loginDesigns.actions, { name: "Autumn" }))
        await expect(activeMenu.getByRole("menuitem", { name: t.loginDesigns.delete })).toHaveAttribute("aria-disabled", "true")
        // The reason is written in the menu, without hovering, so keyboard and touch readers see it.
        const reason = activeMenu.getByText(t.loginDesigns.deleteActiveHint)
        await expect(reason).toBeVisible()
        await expect(activeMenu.getByRole("menuitem", { name: t.loginDesigns.delete })).toHaveAccessibleDescription(
          t.loginDesigns.deleteActiveHint
        )
        // Inside the menu's own box at a phone's width too: not clipped, not widening it.
        const [reasonBox, menuBox] = await Promise.all([reason.boundingBox(), activeMenu.boundingBox()])
        expect(reasonBox && menuBox).toBeTruthy()
        expect(reasonBox!.x).toBeGreaterThanOrEqual(menuBox!.x)
        expect(reasonBox!.x + reasonBox!.width).toBeLessThanOrEqual(menuBox!.x + menuBox!.width)
        await page.screenshot({ path: testInfo.outputPath(`templates-${language}-${mode}-${width}.png`), fullPage: true })
        await page.keyboard.press("Escape")
        await expect(page.getByRole("menu")).toBeHidden()

        const menu = await rowMenu(page, "Spring", fill(t.loginDesigns.actions, { name: "Spring" }))
        await menu.getByRole("menuitem", { name: t.loginDesigns.delete }).click()
        const dialog = page.getByRole("alertdialog")
        await expect(dialog).toContainText(fill(t.loginDesigns.deleteTitle, { name: "Spring" }))
        await dialog.getByRole("button", { name: t.loginDesigns.delete }).click()
        await expect(page.getByText(t.loginDesigns.deleted)).toBeVisible()
        expect(seen).toContainEqual({ method: "DELETE", path: "/api/v1/admin/platform-settings/login-designs/d2" })
      })

      test(`Delete in the notification lists' row menus (${where})`, async ({ page }, testInfo) => {
        const t = catalogs[language]
        const seen: Seen[] = []
        await page.setViewportSize({ width, height: 1000 })
        await install(page, language, mode, seen)

        // Email templates: the platform's system template keeps Delete, disabled.
        await page.goto("/notifications/templates")
        const system = await rowMenu(page, "Password reset", t.common.actions)
        await expect(system.getByRole("menuitem", { name: t.common.delete })).toHaveAttribute("aria-disabled", "true")
        await page.keyboard.press("Escape")
        const template = await rowMenu(page, "Billing receipt", t.common.actions)
        await template.getByRole("menuitem", { name: t.common.delete }).click()
        await expect(page.getByRole("alertdialog")).toContainText(fill(t.notifications.deleteNamedTitle, { name: "Billing receipt" }))
        await expectNoShellOverflow(page, `templates ${where}`)
        await settledDialog(page)
        await page.screenshot({ path: testInfo.outputPath(`template-delete-${language}-${mode}-${width}.png`), fullPage: true })
        await page.getByRole("alertdialog").getByRole("button", { name: t.common.delete }).click()
        await expect(page.getByText(t.notifications.templateDeleted)).toBeVisible()
        expect(seen).toContainEqual({ method: "DELETE", path: "/api/v1/notification-templates/t-app" })

        // Layouts: the global layout keeps Delete, disabled.
        await page.goto("/notifications/layouts")
        const global = await rowMenu(page, "Default email", t.common.actions)
        await expect(global.getByRole("menuitem", { name: t.common.delete })).toHaveAttribute("aria-disabled", "true")
        await page.keyboard.press("Escape")
        const layout = await rowMenu(page, "Billing email", t.common.actions)
        await layout.getByRole("menuitem", { name: t.common.delete }).click()
        await expect(page.getByRole("alertdialog")).toContainText(t.notifications.deleteLayoutBody)
        await settledDialog(page)
        await page.screenshot({ path: testInfo.outputPath(`layout-delete-${language}-${mode}-${width}.png`), fullPage: true })
        await page.getByRole("alertdialog").getByRole("button", { name: t.common.delete }).click()
        await expect.poll(() => seen.some((entry) => entry.path === "/api/v1/notification-layouts/l-app")).toBe(true)
        await expect(page.getByText(t.notifications.layoutDeleted)).toBeVisible()

        // Privacy policy: only the draft nobody was shown.
        await page.goto("/notifications/policy")
        const live = await rowMenu(page, "2026.09", t.common.actions)
        await expect(live.getByRole("menuitem", { name: t.common.delete })).toHaveAttribute("aria-disabled", "true")
        await page.keyboard.press("Escape")
        const draft = await rowMenu(page, "2026.11", t.common.actions)
        await draft.getByRole("menuitem", { name: t.common.delete }).click()
        await expect(page.getByRole("alertdialog")).toContainText(fill(t.notifications.policyDeleteTitle, { version: "2026.11" }))
        await expectNoShellOverflow(page, `policy ${where}`)
        await settledDialog(page)
        await page.screenshot({ path: testInfo.outputPath(`policy-delete-${language}-${mode}-${width}.png`), fullPage: true })
        await page.getByRole("alertdialog").getByRole("button", { name: t.common.delete }).click()
        await expect(page.getByText(t.notifications.policyDeleted)).toBeVisible()
        expect(seen).toContainEqual({ method: "DELETE", path: "/api/v1/privacy-policy/versions/v-draft" })
      })
    }
  }
}

test("the command palette reaches each platform settings tab", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await install(page, "en", "light", [])
  await page.goto("/")

  for (const [title, search] of [
    [en.platformSettings.appearance.title, "?tab=appearance"],
    [en.loginDesigns.tab, "?tab=templates"],
  ] as const) {
    await page.getByRole("button", { name: en.globalSearch.label, exact: true }).first().click()
    await page.getByPlaceholder(en.globalSearch.placeholder).fill(title)
    await page
      .getByRole("option")
      .filter({ hasText: title })
      .filter({ hasText: en.nav.platformSettings })
      .first()
      .click()
    await expect(page).toHaveURL(new RegExp(`/admin/platform-settings\\${search}$`))
    await expect(page.getByRole("tab", { name: title })).toHaveAttribute("aria-selected", "true")
  }
})
