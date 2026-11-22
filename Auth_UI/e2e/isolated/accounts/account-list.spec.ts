import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { WCAG_TAGS, actionable, describeViolations } from "../axe-known-gaps"
import { fulfillJson, installAnonymousApi, type SeenRequest } from "./mock-anonymous-api"

/**
 * The sign-in page's "Pick an account" list on the built accounts app (owner,
 * 2026-11-22, reversing D-215-4): a sign-in keeps its account, as the token
 * names it; the next visit lists it; a row is that address and "Continue"; and
 * the list holds up in both directions and both themes, at a phone's width.
 */

const ACCOUNTS_KEY = "auth.signIn.accounts"

const ADA = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "ada.lovelace@analytical-engine.example",
  firstName: "Ada",
  lastName: "Lovelace",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

const SEEDED = [
  { id: ADA.id, email: ADA.email, name: "Ada Lovelace" },
  { id: "22222222-2222-2222-2222-222222222222", email: "grace@example.com", name: null },
]

/** A sign-in answer whose token names the account, as the API's does. */
function signedIn() {
  const encode = (value: object) => Buffer.from(JSON.stringify(value)).toString("base64url")
  const accessToken = `${encode({ alg: "none", typ: "JWT" })}.${encode({
    exp: Math.floor(Date.now() / 1000) + 3600,
    sub: ADA.id,
    email: ADA.email,
    name: "Ada Lovelace",
  })}.signature`
  return { token: { accessToken, refreshToken: "__cookie__" }, user: ADA, requiresPasswordChange: false }
}

async function installApi(page: Page, seen: SeenRequest[]) {
  await installAnonymousApi(
    page,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/login") {
        await fulfillJson(route, signedIn())
        return true
      }
      if (path === "/api/v1/auth/me") {
        const bearer = await route.request().headerValue("authorization")
        await (bearer
          ? fulfillJson(route, ADA)
          : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
        return true
      }
      return false
    },
    { seen }
  )
}

function accountList(page: Page) {
  return page.getByRole("list", { name: /Accounts used on this browser|الحسابات المستخدمة/ })
}

test("a sign-in keeps its account, and the next visit picks it instead of typing it", async ({ page }) => {
  const seen: SeenRequest[] = []
  await installApi(page, seen)

  // First visit: nothing remembered, the address field as before.
  await page.goto("/login")
  await expect(page.getByRole("textbox", { name: "Email" })).toBeVisible()
  await expect(accountList(page)).toHaveCount(0)
  await page.getByRole("textbox", { name: "Email" }).fill(ADA.email)
  await page.getByLabel("Password").fill("pw")
  await page.getByRole("button", { name: "Sign in" }).click()

  // Kept as the token names it, not as the field read.
  await expect
    .poll(() => page.evaluate((key) => window.localStorage.getItem(key), ACCOUNTS_KEY))
    .toBe(JSON.stringify([{ id: ADA.id, email: ADA.email, name: "Ada Lovelace" }]))

  // Next visit, signed out: the list, and no address field.
  await page.evaluate(() => window.localStorage.removeItem("auth.refreshToken"))
  seen.length = 0
  await page.goto("/login")
  await expect(page.getByRole("heading", { name: "Pick an account" })).toBeVisible()
  const row = accountList(page).getByRole("button", { name: /^Ada Lovelace/ })
  await expect(row).toBeFocused()
  await expect(page.getByRole("textbox", { name: "Email" })).toHaveCount(0)

  // The row is the address and "Continue": the password step, with the chip
  // and the hidden username a password manager pairs with the password.
  await row.click()
  await expect(page.getByTestId("address-chip")).toContainText(ADA.email)
  await expect(page.getByTestId("address-chip-username")).toHaveValue(ADA.email)
  await page.getByLabel("Password").fill("pw")
  await page.getByRole("button", { name: "Sign in" }).click()

  await expect.poll(() => seen.filter((request) => request.path === "/api/v1/auth/login").length).toBe(1)
  expect(seen.find((request) => request.path === "/api/v1/auth/login")?.body).toMatchObject({
    email: ADA.email,
  })
})

test("the browser's Back, and the page's, return from 'Use a different account' to the list, on the sign-in page", async ({
  page,
}) => {
  await installApi(page, [])
  await page.addInitScript(
    ([key, value]) => window.localStorage.setItem(key, value),
    [ACCOUNTS_KEY, JSON.stringify([SEEDED[0]])] as const
  )
  await page.goto("/login?returnTo=%2Fprofile")
  const row = accountList(page).getByRole("button", { name: /^Ada Lovelace/ })

  await page.getByRole("button", { name: "Use a different account" }).click()
  await expect(page.getByRole("textbox", { name: "Email" })).toBeFocused()
  // A same-document step, not a reload: Back returns without a load event.
  await page.evaluate(() => history.back())
  await expect(row).toBeFocused()
  await expect(page).toHaveURL(/\/login\?returnTo=%2Fprofile$/)

  await page.getByRole("button", { name: "Use a different account" }).click()
  await page.getByRole("button", { name: "Back", exact: true }).click()
  await expect(row).toBeVisible()
  await expect(page.getByRole("textbox", { name: "Email" })).toHaveCount(0)
})

test("removing an account sends nothing, and the last one gone leaves the address field", async ({ page }) => {
  const seen: SeenRequest[] = []
  await installApi(page, seen)
  await page.addInitScript(
    ([key, value]) => window.localStorage.setItem(key, value),
    [ACCOUNTS_KEY, JSON.stringify([SEEDED[1]])] as const
  )

  await page.goto("/login")
  await page.getByRole("button", { name: "More options for grace@example.com" }).click()
  await page.getByRole("menuitem", { name: "Remove from this list" }).click()

  await expect(page.getByRole("textbox", { name: "Email" })).toBeFocused()
  expect(await page.evaluate((key) => window.localStorage.getItem(key), ACCOUNTS_KEY)).toBeNull()
  // The page stays usable: a menu closed by its row's removal leaves no lock.
  await page.getByRole("textbox", { name: "Email" }).fill("someone@example.com")
  await expect(page.getByRole("textbox", { name: "Email" })).toHaveValue("someone@example.com")
  expect(seen).toEqual([])
})

for (const theme of ["light", "dark"] as const) {
  for (const language of ["en", "ar"] as const) {
    test(`the account list passes WCAG AA and fits a phone in ${theme} ${language}`, async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 812 })
      await installApi(page, [])
      await page.addInitScript(
        ([key, value, themeValue, languageValue]) => {
          window.localStorage.setItem(key, value)
          window.localStorage.setItem("theme", themeValue)
          window.localStorage.setItem("auth.language", languageValue)
        },
        [ACCOUNTS_KEY, JSON.stringify(SEEDED), theme, language] as const
      )

      await page.goto("/login")
      const rows = accountList(page).getByRole("listitem")
      await expect(rows).toHaveCount(2)

      const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()
      expect(actionable(results), describeViolations(results)).toEqual([])

      // Nothing wider than the page, and every row inside it: a long address is
      // cut short within its row, never the page.
      const overflow = await page.evaluate(() => {
        const root = document.scrollingElement ?? document.documentElement
        const rowEdges = [...document.querySelectorAll("[data-account]")].map((element) => {
          const box = element.getBoundingClientRect()
          return { left: box.left, right: box.right }
        })
        return { scroll: root.scrollWidth - root.clientWidth, width: window.innerWidth, rowEdges }
      })
      expect(overflow.rowEdges).toHaveLength(2)
      expect(overflow.scroll).toBe(0)
      for (const edge of overflow.rowEdges) {
        expect(edge.left).toBeGreaterThanOrEqual(0)
        expect(edge.right).toBeLessThanOrEqual(overflow.width)
      }
    })
  }
}
