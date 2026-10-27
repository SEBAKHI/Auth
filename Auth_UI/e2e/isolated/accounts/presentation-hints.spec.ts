import { expect, test, type Page, type Route } from "@playwright/test"

import { installAuthenticatedApi } from "../mock-authenticated-api"
import { fulfillJson, installAnonymousApi, loginResponse } from "./mock-anonymous-api"

/**
 * An application sends its user to the accounts pages with its language and
 * colour mode (`ui_locales`, `ui_theme`; the authorize endpoint resolves them for
 * the sign-in pages, and a direct link may carry them). Walked against the
 * production build: the pages show in them, keep them for the flow, take them
 * off the address bar for good, never let the profile's or an earlier choice
 * override them silently, never pass them on, and write nothing onto the
 * browser that outlives the tab — until the user picks for themself.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const SIGN_IN = `/login?client_id=app&auth_request=${HANDLE}`

/** The person behind the signed-in cases: their profile says French. */
const PERSON = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "person@example.test",
  firstName: "Hint",
  lastName: "Person",
  preferredLanguage: "fr",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

const PROFILE = { ...PERSON, phoneNumber: "", theme: "light", displayName: null }

/**
 * What <html> said when the app first inserted content into #root. A
 * MutationObserver record is not a paint: it shows the values were on <html>
 * when the first render's content went in, not what the browser drew first.
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

async function installApi(page: Page, extra?: (route: Route, path: string, url: URL) => Promise<boolean>) {
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/applications/app/public-branding") {
      await fulfillJson(route, { name: "Partner App", logoUrl: null, homeUrl: null })
      return true
    }
    return extra ? extra(route, path, url) : false
  })
}

/** Signed in as PERSON (profile French), with the profile endpoints the account page reads. */
async function installSignedIn(page: Page, writes: unknown[]) {
  await installAuthenticatedApi(
    page,
    [],
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/users/me") return false
      if (route.request().method() === "PUT") {
        writes.push(route.request().postDataJSON())
        await route.fulfill({ status: 204 })
        return true
      }
      await fulfillJson(route, PROFILE)
      return true
    },
    { preferredLanguage: "fr" }
  )
}

async function storedChoices(page: Page) {
  return page.evaluate(() => ({
    language: localStorage.getItem("auth.language"),
    theme: localStorage.getItem("theme"),
  }))
}

test("is in the application's language and colour mode when its content first appears", async ({ page }) => {
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
  expect(await storedChoices(page)).toEqual({ language: null, theme: null })
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

test("signing in neither adopts the profile's language over the hint nor sends the hint on", async ({ page }) => {
  const resumed: string[] = []
  await installApi(page, async (route, path, url) => {
    if (path === "/api/v1/auth/login") {
      await fulfillJson(route, loginResponse(PERSON))
      return true
    }
    if (path === "/api/v1/auth/me") {
      const bearer = await route.request().headerValue("authorization")
      await (bearer ? fulfillJson(route, PERSON) : fulfillJson(route, { code: "Http.Unauthorized" }, 401))
      return true
    }
    if (path === "/api/v1/auth/authorize/resume") {
      resumed.push(url.toString())
      await route.fulfill({ status: 200, contentType: "text/html", body: "<p>resumed</p>" })
      return true
    }
    return false
  })

  await page.goto(`${SIGN_IN}&ui_locales=ar&ui_theme=dark`)
  await page.getByLabel("البريد الإلكتروني").fill(PERSON.email)
  await page.getByLabel("كلمة المرور").fill("Passw0rd!")
  await page.getByRole("button", { name: "تسجيل الدخول", exact: true }).click()
  await expect.poll(() => resumed.length).toBe(1)

  const resume = new URL(resumed[0])
  expect(resume.pathname).toBe("/api/v1/auth/authorize/resume")
  expect([...resume.searchParams.keys()]).toEqual(["client_id", "auth_request"])

  // The profile's French was not written onto this browser on the way out.
  await page.goto("/")
  expect(await storedChoices(page)).toEqual({ language: null, theme: null })
})

test("a direct link to the profile shows the link's language over the profile's, until the user picks one", async ({
  page,
}) => {
  const writes: unknown[] = []
  await installSignedIn(page, writes)

  await page.goto("/profile?ui_locales=ar-EG&ui_theme=dark")

  await expect(page.locator("html")).toHaveAttribute("lang", "ar")
  await expect(page.locator("html")).toHaveClass(/\bdark\b/)
  await expect(page.getByLabel("رقم الهاتف")).toBeVisible()
  expect(new URL(page.url()).search).toBe("")
  expect(await storedChoices(page)).toEqual({ language: null, theme: null })

  // Saving a phone number is not a choice of language or theme.
  await page.getByLabel("رقم الهاتف").fill("+15550100")
  await page.getByRole("button", { name: "تحديث الملف الشخصي" }).click()
  await expect.poll(() => writes.length).toBe(1)
  await expect(page.getByText("تم تحديث الملف الشخصي.")).toBeVisible()
  await expect(page.locator("html")).toHaveAttribute("lang", "ar")
  await expect(page.locator("html")).toHaveClass(/\bdark\b/)
  expect(await storedChoices(page)).toEqual({ language: null, theme: null })

  // Changing the language field is. Found through its group: the trigger has no
  // accessible name of its own (LanguageSelect does not take FormControl's id).
  await page.getByRole("group").filter({ hasText: "اللغة المفضلة" }).getByRole("combobox").first().click()
  await page.getByRole("option", { name: "Türkçe" }).click()
  await page.getByRole("button", { name: "تحديث الملف الشخصي" }).click()
  await expect(page.locator("html")).toHaveAttribute("lang", "tr")
  expect((await storedChoices(page)).language).toBe("tr")
})

test("a language the user picks survives a tab change and a reload", async ({ page }) => {
  await installSignedIn(page, [])

  await page.goto("/profile?ui_locales=ar&ui_theme=dark")
  await expect(page.locator("html")).toHaveAttribute("lang", "ar")

  await page.getByRole("button", { name: "اللغة" }).click()
  await page.getByRole("menuitem", { name: "Français" }).click()
  await expect(page.locator("html")).toHaveAttribute("lang", "fr")

  // The tab is written into the address by the router: it must not bring the
  // application's parameters back with it.
  await page.getByRole("tab").nth(2).click()
  await expect(page).toHaveURL(/[?&]tab=/)
  expect(new URL(page.url()).searchParams.has("ui_locales")).toBe(false)
  expect(new URL(page.url()).searchParams.has("ui_theme")).toBe(false)

  await page.reload()

  await expect(page.locator("html")).toHaveAttribute("lang", "fr")
})
