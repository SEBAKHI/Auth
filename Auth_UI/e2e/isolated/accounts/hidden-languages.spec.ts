import { expect, test } from "@playwright/test"

import { en } from "../../../packages/i18n/src/locales/en"
import { watchLanguageScripts } from "../language-chunks"
import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * UI batch phase P5 (item 11, owner decision D8) on the sign-in pages: an application asking
 * for a hidden language (`ui_locales`) gets English, the language menu leaves the hidden one
 * out, and the browser downloads neither its strings nor its dates. Arabic, still shown, loads
 * both when chosen: the floor that proves the detector sees what it looks for.
 */

for (const width of [1440, 375]) {
  test(`a sign-in link asking for a hidden language opens in English and never downloads it (${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 })
    await page.addInitScript(() => localStorage.setItem("auth.languages.hidden", JSON.stringify(["fr"])))
    await installAnonymousApi(page, async () => false)
    // Matched on the lower-cased path: the client writes /api/v1/Platform/branding.
    await page.route(
      (url) => url.pathname.toLowerCase() === "/api/v1/platform/branding",
      (route) => fulfillJson(route, { platformName: "AuthSystem", staySignedInOffered: true, hiddenLanguages: ["fr"] })
    )
    const scripts = watchLanguageScripts(page)

    await page.goto("/login?ui_locales=fr")
    await expect(page.getByRole("heading", { name: en.auth.signIn })).toBeVisible()
    await expect(page.locator("html")).toHaveAttribute("lang", "en")

    await page.getByRole("button", { name: en.common.language }).first().click()
    const menu = page.getByRole("menu")
    await expect(menu.getByRole("menuitem")).toHaveText(["English", "العربية", "Türkçe", "中文", "اردو", "فارسی"])
    await page.screenshot({ path: testInfo.outputPath(`sign-in-menu-${width}.png`), fullPage: true })

    await menu.getByRole("menuitem", { name: "العربية" }).click()
    await expect(page.locator("html")).toHaveAttribute("lang", "ar")
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl")

    expect([...(await scripts.seen())].sort()).toEqual(["arDates", "arStrings"])
  })
}
