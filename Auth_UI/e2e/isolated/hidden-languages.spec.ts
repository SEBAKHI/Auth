import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { watchLanguageScripts } from "./language-chunks"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * UI batch phase P5 (item 11, owner decision D8), in a real browser: a language an administrator
 * hid is absent from the language menus, and its code is never downloaded: neither its strings
 * nor its date-fns locale. A visible language still loads both the moment it is chosen, which
 * is this spec's floor: the detector below sees Arabic arrive, so its silence about French is
 * evidence. The Languages tab saves the hidden codes, and the menus follow at once.
 *
 * The scripts are told apart by what they hold (`language-chunks.ts`), not by file names a
 * build may change.
 *
 * Deliberate breaks, each turning a case red: list `SUPPORTED_LANGUAGES` in the toggle (French
 * is offered); import the date-fns locales statically again (French dates ride in the date formatter);
 * drop `visibleOrDefault` from the initial language (the French strings load).
 */

const SETTINGS = (hiddenLanguages: string[]) => ({
  platformName: "AuthSystem",
  logoUrl: null,
  logoUrlDark: null,
  faviconUrl: null,
  logoHeightPx: null,
  fonts: {},
  hiddenLanguages,
})

interface Seen {
  method: string
  body: unknown
}

async function install(
  page: Page,
  options: { hidden: string[]; seen?: Seen[]; mode?: "light" | "dark"; language?: string }
) {
  let hidden = options.hidden
  if (options.mode) await page.addInitScript((value) => localStorage.setItem("theme", value), options.mode)
  await installAuthenticatedApi(page, ["platform-settings:manage"], async (route, url) => {
    const request = route.request()
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/admin/platform-settings/hidden-languages" && request.method() === "PUT") {
      const body = request.postDataJSON() as { hiddenLanguages: string[] }
      options.seen?.push({ method: "PUT", body })
      hidden = body.hiddenLanguages
      await fulfillJson(route, SETTINGS(hidden))
      return true
    }
    if (path === "/api/v1/admin/platform-settings") {
      await fulfillJson(route, SETTINGS(hidden))
      return true
    }
    if (path === "/api/v1/users/me" && request.method() === "PUT") {
      await fulfillJson(route, {})
      return true
    }
    return false
  }, { preferredLanguage: options.language })
  // After the catch-all, so it answers first; matched on the lower-cased path, because the
  // client writes /api/v1/Platform/branding and a route glob is case-sensitive.
  await page.route(
    (url) => url.pathname.toLowerCase() === "/api/v1/platform/branding",
    (route) => fulfillJson(route, { platformName: "AuthSystem", staySignedInOffered: true, hiddenLanguages: hidden })
  )
}

/** The Languages tab, found by its title (a card title is not a heading). */
function languagesCard(page: Page, title: string) {
  return page.locator("[data-slot=card]").filter({ hasText: title })
}

async function languageMenu(page: Page, label: string) {
  await page.getByRole("button", { name: label }).first().click()
  const menu = page.getByRole("menu")
  await expect(menu).toBeVisible()
  return menu
}

test("a stored language hidden since opens in English, and neither its strings nor its dates are downloaded", async ({ page }) => {
  // This browser has seen the branding hide French (the copy it keeps), and French is the
  // language it stored for the person.
  await page.addInitScript(() => {
    localStorage.setItem("auth.language", "fr")
    localStorage.setItem("auth.languages.hidden", JSON.stringify(["fr"]))
  })
  await install(page, { hidden: ["fr"] })
  const scripts = watchLanguageScripts(page)

  await page.goto("/admin/platform-settings?tab=languages")
  await expect(languagesCard(page, en.platformSettings.languages.title)).toBeVisible()

  await expect(page.locator("html")).toHaveAttribute("lang", "en")
  expect(await page.evaluate(() => localStorage.getItem("auth.language"))).toBe("fr")
  const menu = await languageMenu(page, en.common.language)
  await expect(menu.getByRole("menuitem")).toHaveText(["English", "العربية", "Türkçe", "中文", "اردو", "فارسی"])

  // The floor: a visible language loads its strings and its dates when chosen.
  await menu.getByRole("menuitem", { name: "العربية" }).click()
  await expect(page.locator("html")).toHaveAttribute("lang", "ar")
  await expect(languagesCard(page, ar.platformSettings.languages.title)).toBeVisible()

  const seen = await scripts.seen()
  expect([...seen].sort()).toEqual(["arDates", "arStrings"])
})

test("a fresh device fetches a hidden language at most once, then never again", async ({ page }) => {
  // No copy of the branding yet: the first load can only learn from the answer.
  await page.addInitScript(() => {
    if (!sessionStorage.getItem("visited")) {
      sessionStorage.setItem("visited", "1")
      localStorage.setItem("auth.language", "fr")
    }
  })
  await install(page, { hidden: ["fr"] })
  const scripts = watchLanguageScripts(page)

  await page.goto("/admin/platform-settings?tab=languages")
  // The branding's answer moves the page to English.
  await expect(page.locator("html")).toHaveAttribute("lang", "en")
  await expect(languagesCard(page, en.platformSettings.languages.title)).toBeVisible()

  await scripts.seen()
  scripts.reset()
  await page.reload()
  await expect(languagesCard(page, en.platformSettings.languages.title)).toBeVisible()
  await expect(page.locator("html")).toHaveAttribute("lang", "en")

  const seen = await scripts.seen()
  expect(seen.has("frStrings")).toBe(false)
  expect(seen.has("frDates")).toBe(false)
})

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    test(`the Languages tab hides a language from the menus at once (${language}, ${mode})`, async ({ page }, testInfo) => {
      const catalog = { en, ar }[language]
      const seen: Seen[] = []
      await install(page, { hidden: ["fr"], seen, mode, language })

      for (const width of [1440, 375]) {
        await page.setViewportSize({ width, height: 900 })
        await page.goto("/admin/platform-settings?tab=languages")
        const tab = languagesCard(page, catalog.platformSettings.languages.title)
        await expect(tab).toBeVisible()
        await expect(tab.getByRole("switch", { name: "Français" })).not.toBeChecked()
        await expect(tab.getByRole("switch", { name: "English" })).toBeDisabled()
        await expect(tab.getByText(catalog.platformSettings.languages.englishAlwaysShown)).toBeVisible()
        await expectNoShellOverflow(page)
        await page.screenshot({ path: testInfo.outputPath(`languages-tab-${language}-${mode}-${width}.png`), fullPage: true })
      }

      const tab = languagesCard(page, catalog.platformSettings.languages.title)
      await tab.getByRole("switch", { name: "Türkçe" }).click()
      await tab.getByRole("button", { name: catalog.common.save }).click()
      await expect.poll(() => seen.length).toBe(1)
      expect(seen[0].body).toEqual({ hiddenLanguages: ["tr", "fr"] })

      const menu = await languageMenu(page, catalog.common.language)
      await expect(menu.getByRole("menuitem", { name: "Türkçe" })).toHaveCount(0)
      await expect(menu.getByRole("menuitem", { name: "Français" })).toHaveCount(0)
      await expect(menu.getByRole("menuitem", { name: "English" })).toHaveCount(1)
    })
  }
}

test("the profile's language select leaves a hidden language out", async ({ page }) => {
  await install(page, { hidden: ["zh", "ur"] })
  await page.route((url) => url.pathname.toLowerCase() === "/api/v1/users/me", (route) =>
    route.request().method() === "GET"
      ? fulfillJson(route, { id: "99999999-9999-9999-9999-999999999999", email: "isolated@example.test", firstName: "Isolated", lastName: "Operator", preferredLanguage: "en", timeZone: "UTC", theme: "system" })
      : route.fallback()
  )

  await page.goto("/profile")
  const select = page.getByRole("combobox").filter({ hasText: "English" }).first()
  await expect(select).toBeVisible()
  await select.click()

  const options = page.getByRole("option")
  await expect(options).toHaveText(["English", "العربية", "Türkçe", "Français", "فارسی"])
})
