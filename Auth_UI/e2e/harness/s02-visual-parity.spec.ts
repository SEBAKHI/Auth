import { ORIGINS, expect, test } from "./fixtures"
import {
  BLUE_PRIMARY_LIGHT,
  BLUE_THEME,
  openAccountsTwoFactor,
  openDashboardPeople,
  openTemplate,
  previewFrame,
  privacyFolder,
  serveConsoleSurfaces,
  withoutSchemeEmulation,
} from "./s02-surfaces"
import { clickPageAction } from "../isolated/page-actions"

/**
 * S02, target 16: the look does not move when inline styles leave style-src.
 *
 * Every snapshot here was taken on the code BEFORE any S02 change, while the
 * policy still carried 'unsafe-inline' (card S02, rollout step 2), and is
 * compared after the change WITHOUT --update-snapshots. A difference is fixed
 * at the rule that caused it; a difference whose cause is the preset is a stop
 * (the preset owns the look). Never update a snapshot to make one pass.
 *
 * Each surface is a place a style injector draws: the charts (ChartStyle),
 * the code field (input-otp), the editor (style-mod), a dialog with an open
 * Select (Radix Select, and the dialog's scroll lock: react-style-singleton),
 * a toast (sonner), the email preview in both schemes, /privacy (the renderer's
 * stylesheet) and a page under a non-default platform appearance (apply-theme).
 */

const SNAP = { animations: "disabled" as const, caret: "hide" as const }

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light", reducedMotion: "reduce" })
})

test("charts on the dashboard", async ({ page, api }) => {
  await serveConsoleSurfaces(api)
  await openDashboardPeople(page)
  const card = page.locator('[data-slot="card"]').filter({ has: page.locator('[data-slot="chart"]') })
  await expect(card.first().locator(".recharts-bar-rectangle").first()).toBeVisible()
  // Status share, activation funnel, signups and members per organisation.
  await expect(card).toHaveCount(4)
  for (let index = 0; index < 4; index++) {
    await expect(card.nth(index)).toHaveScreenshot(`chart-${index + 1}.png`, SNAP)
  }
})

test("the code field", async ({ page, api }) => {
  await openAccountsTwoFactor(page, api)
  await page.locator('[data-slot="input-otp"]').click()
  await page.keyboard.type("123")
  await expect(page.locator("[data-input-otp-container]")).toHaveScreenshot("otp.png", SNAP)
})

for (const scheme of ["light", "dark"] as const) {
  test(`the template editor, ${scheme}`, async ({ page, api }) => {
    await page.emulateMedia({ colorScheme: scheme })
    await serveConsoleSurfaces(api)
    await openTemplate(page)
    await expect(page.locator(".cm-content")).toContainText("Hello")
    await expect(page.locator(".cm-editor")).toHaveScreenshot(`editor-${scheme}.png`, SNAP)
  })

  test(`the email preview, ${scheme}`, async ({ page, api }) => {
    await withoutSchemeEmulation(page)
    await serveConsoleSurfaces(api)
    await openTemplate(page)
    const frame = await previewFrame(page, scheme)
    await expect(frame).toHaveScreenshot(`preview-${scheme}.png`, SNAP)
  })
}

test("a dialog with an open Select", async ({ page, api }) => {
  await serveConsoleSurfaces(api)
  await openTemplate(page)
  await clickPageAction(page, "Send test")
  const dialog = page.getByRole("alertdialog", { name: "Send test message" })
  await expect(dialog).toBeVisible()
  await dialog.locator("#test-send-language").click()
  await expect(page.getByRole("listbox")).toBeVisible()
  await expect(page).toHaveScreenshot("dialog-select.png", SNAP)
})

test("a toast", async ({ page, api }) => {
  await serveConsoleSurfaces(api)
  await openTemplate(page)
  await page.getByRole("button", { name: "Publish", exact: true }).click()
  await page.getByRole("alertdialog").getByRole("button", { name: /Publish$/ }).click()
  const toast = page.locator("[data-sonner-toast]")
  await expect(toast).toContainText("Published")
  await page.mouse.move(0, 0)
  await expect(toast).toHaveScreenshot("toast.png", SNAP)
})

test("/privacy, current and archived", async ({ page, api, privacy }) => {
  await api.useAnonymous()
  const folder = privacyFolder()
  try {
    privacy.mount(folder.dir)
    await page.goto(`${ORIGINS.accounts}/privacy/en`)
    await expect(page.locator("main")).toBeVisible()
    await expect(page).toHaveScreenshot("privacy-current.png", { ...SNAP, fullPage: true })
    await page.goto(`${ORIGINS.accounts}/privacy/v2026.09/en`)
    await expect(page.locator("main")).toBeVisible()
    await expect(page).toHaveScreenshot("privacy-archived.png", { ...SNAP, fullPage: true })
  } finally {
    folder.dispose()
  }
})

test("a page under a non-default platform appearance", async ({ page, api }) => {
  await serveConsoleSurfaces(api, { branding: { platformName: "AuthSystem", theme: BLUE_THEME } })
  await openTemplate(page)
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--primary").trim()))
    .toBe(BLUE_PRIMARY_LIGHT)
  await page.mouse.move(0, 0)
  await expect(page).toHaveScreenshot("theme-blue.png", SNAP)
})
