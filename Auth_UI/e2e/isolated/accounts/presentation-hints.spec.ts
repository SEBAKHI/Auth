import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * An application sends its user to the accounts sign-in page with its language
 * and colour mode (`ui_locales`, `ui_theme`, resolved by the authorize endpoint).
 * Walked against the production build: the page must open in them from its
 * first drawn frame, keep them for the flow, take them off the address bar, and
 * write nothing onto the browser that outlives the tab.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const SIGN_IN = `/login?client_id=app&auth_request=${HANDLE}`

/**
 * What <html> said when the app first put content into #root: the language
 * and direction are stamped before the first render, and the class by the time
 * that render's content is in the page.
 */
async function recordFirstContent(page: Page) {
  await page.addInitScript(() => {
    const record = () => {
      const root = document.getElementById("root")
      if (!root) return
      new MutationObserver((_, observer) => {
        if (!root.firstChild) return
        const html = document.documentElement
        sessionStorage.setItem(
          "test.firstContent",
          JSON.stringify({ lang: html.lang, dir: html.dir, dark: html.classList.contains("dark") })
        )
        observer.disconnect()
      }).observe(root, { childList: true })
    }
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", record)
    } else {
      record()
    }
  })
}

async function installApi(page: Page) {
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() === "/api/v1/applications/app/public-branding") {
      await fulfillJson(route, { name: "Partner App", logoUrl: null, homeUrl: null })
      return true
    }
    return false
  })
}

test("opens in the application's language and colour mode from the first frame", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" })
  await recordFirstContent(page)
  await installApi(page)

  await page.goto(`${SIGN_IN}&ui_locales=ar&ui_theme=dark`)

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await expect(page.locator("html")).toHaveAttribute("lang", "ar")
  await expect(page.locator("html")).toHaveClass(/\bdark\b/)
  const first = await page.evaluate(() => JSON.parse(sessionStorage.getItem("test.firstContent") ?? "null"))
  expect(first).toEqual({ lang: "ar", dir: "rtl", dark: true })

  // Off the address bar, the request itself untouched.
  expect(new URL(page.url()).search).toBe(`?client_id=app&auth_request=${HANDLE}`)

  // Nothing the user owns on this browser was written.
  const stored = await page.evaluate(() => ({
    language: localStorage.getItem("auth.language"),
    theme: localStorage.getItem("theme"),
  }))
  expect(stored).toEqual({ language: null, theme: null })
})

test("keeps them across a reload of the same flow", async ({ page }) => {
  await installApi(page)
  await page.goto(`${SIGN_IN}&ui_locales=fa&ui_theme=dark`)
  await expect(page.locator("html")).toHaveAttribute("lang", "fa")

  await page.reload()

  await expect(page.locator("html")).toHaveAttribute("lang", "fa")
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await expect(page.locator("html")).toHaveClass(/\bdark\b/)
})

test("shows English for a language it does not have, whatever the browser had stored", async ({ page }) => {
  await installApi(page)
  await page.addInitScript(() => {
    if (!localStorage.getItem("auth.language")) localStorage.setItem("auth.language", "ar")
  })

  await page.goto(`${SIGN_IN}&ui_locales=de`)

  await expect(page.locator("html")).toHaveAttribute("lang", "en")
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr")
})

test("a flow that names no language keeps the one the browser had stored", async ({ page }) => {
  await installApi(page)
  await page.addInitScript(() => {
    if (!localStorage.getItem("auth.language")) localStorage.setItem("auth.language", "ar")
  })

  await page.goto(SIGN_IN)

  await expect(page.locator("html")).toHaveAttribute("lang", "ar")
})
