import type { Page } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * X01 in a real browser: the console can see and switch the replay protection of
 * authenticator-app codes, and a reused code is answered in words the user can
 * act on. On the S30a harness, so every page runs under its app's real web.config
 * CSP and the automatic csp fixture fails any test on a violation beyond the
 * register.
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. The server's refusal of a reused code is pinned by the xUnit
 * suite and proven live by Tools/probes/two-factor-race.mjs totp-replay. What is
 * proven here is what only a built bundle in a browser decides: that the TwoFactor
 * section has a title, a description, a label and a hint in English and in Arabic;
 * that switching it off asks first; and that the verify page shows the API's own
 * sentence for TwoFactor.CodeAlreadyUsed rather than the generic text it would
 * show for a code the client does not know.
 *
 * Deliberate breaks this file must catch: SECTION_I18N without TwoFactor (the card
 * shows the raw key), and TwoFactor.CodeAlreadyUsed removed from
 * docs/api/error-codes.json then `pnpm gen:error-codes` (the generic text shows).
 */

const ROW_VERSION = "AAAAAAAAB9E="

/** The TwoFactor section as SystemSettingsRegistry declares it, at the shipped default. */
const TWO_FACTOR_SECTION = {
  key: "TwoFactor",
  group: "security",
  editable: true,
  version: 0,
  rowVersion: ROW_VERSION,
  fields: [
    {
      path: "RejectReusedCodes",
      kind: "bool",
      effectiveValue: true,
      baselineValue: true,
      defaultValue: true,
      source: "file",
      restartRequired: false,
      isPendingRestart: false,
      readOnly: false,
      sensitive: false,
    },
  ],
}

/** The English sentence of TwoFactor.CodeAlreadyUsed (DomainErrors.resx). */
const CODE_ALREADY_USED =
  "This code was already used. Wait for the next code. If you did not just use it, change your password."

interface SettingsServer {
  puts: unknown[]
}

async function serveSettings(api: HarnessApi, state: SettingsServer, preferredLanguage = "en") {
  await api.useAuthenticated(
    ["system-settings:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/admin/system-settings") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            restartPending: false,
            dbOverridesUnavailable: false,
            sections: [TWO_FACTOR_SECTION],
          }),
        })
        return true
      }
      if (path.startsWith("/api/v1/admin/system-settings/") && route.request().method() === "PUT") {
        state.puts.push(route.request().postDataJSON())
        await route.fulfill({ status: 200, contentType: "application/json", body: "{}" })
        return true
      }
      return false
    },
    { preferredLanguage }
  )
}

const row = (page: Page) => page.locator('[id="setting-RejectReusedCodes"]')
const cardTitle = (page: Page) => page.locator('[data-slot="card-title"]')

async function openTwoFactorSection(page: Page) {
  await page.goto(`${ORIGINS.console}/admin/system-settings/TwoFactor`)
  await row(page).waitFor()
}

test("the TwoFactor card is titled, described, labelled and explained in English", async ({ page, api }) => {
  await serveSettings(api, { puts: [] })
  await openTwoFactorSection(page)

  await expect(cardTitle(page).filter({ hasText: "Two-factor authentication" })).toBeVisible()
  await expect(page.getByText("How the six-digit codes from an authenticator app are checked")).toBeVisible()
  await expect(row(page)).toContainText("Reject a reused code (replay protection)")
  await expect(row(page)).toContainText("Replay protection: each authenticator-app code is accepted only once.")
  // The raw key is what a missing SECTION_I18N entry renders as the title.
  await expect(cardTitle(page).filter({ hasText: /^TwoFactor$/ })).toHaveCount(0)
})

test("the TwoFactor card is titled, labelled and explained in Arabic", async ({ page, api }) => {
  await serveSettings(api, { puts: [] }, "ar")
  await openTwoFactorSection(page)

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await expect(cardTitle(page).filter({ hasText: "المصادقة الثنائية" })).toBeVisible()
  await expect(row(page)).toContainText("رفض الرمز المستعمل من قبل (الحماية من إعادة الاستعمال)")
  await expect(row(page)).toContainText("الحماية من إعادة الاستعمال (replay protection)")
  await expect(cardTitle(page).filter({ hasText: /^TwoFactor$/ })).toHaveCount(0)
})

test("switching replay protection off asks first and writes nothing until confirmed", async ({ page, api }) => {
  const state: SettingsServer = { puts: [] }
  await serveSettings(api, state)
  await openTwoFactorSection(page)

  await row(page).getByRole("switch").click()
  await page.locator('[data-slot="settings-unsaved-bar"]').locator('button[type="submit"]').click()

  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("Reject a reused code (replay protection)")
  expect(state.puts).toHaveLength(0)

  await dialog.getByRole("button", { name: "Confirm" }).click()
  await expect.poll(() => state.puts.length).toBe(1)
  expect(state.puts[0]).toEqual(expect.objectContaining({ overrides: { RejectReusedCodes: false } }))
})

test("a reused code is answered with the API's own sentence on the verify page", async ({ page, api }) => {
  const verifies: unknown[] = []
  await api.useAnonymous(async (route, url, body) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/login") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ twoFactorChallengeToken: "harness-challenge", requiresTwoFactor: true }),
      })
      return true
    }
    if (path === "/api/v1/auth/2fa/verify") {
      verifies.push(body)
      await route.fulfill({
        status: 400,
        contentType: "application/problem+json",
        body: JSON.stringify({
          type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
          title: "Bad Request",
          status: 400,
          code: "TwoFactor.CodeAlreadyUsed",
          detail: CODE_ALREADY_USED,
        }),
      })
      return true
    }
    return false
  })

  await page.goto(`${ORIGINS.accounts}/login`)
  await page.locator('input[type="email"]').fill("reuse@example.test")
  await page.locator('input[type="password"]').fill("Harness1!")
  await page.locator('button[type="submit"]').click()
  await expect(page).toHaveURL(/\/two-factor$/)

  await page.locator('[data-slot="input-otp"]').click()
  await page.keyboard.type("123456")

  await expect.poll(() => verifies.length).toBe(1)
  await expect(page.getByText(CODE_ALREADY_USED)).toBeVisible()
  // The field is cleared for the next code, and the user stays on the challenge.
  await expect(page).toHaveURL(/\/two-factor$/)
})
