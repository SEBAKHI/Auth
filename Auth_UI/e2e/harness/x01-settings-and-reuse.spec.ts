import type { Page } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * X01 in a real browser: the TwoFactor card of the console, and a reused code
 * answered in words the user can act on. On the S30a harness, so every page runs
 * under its app's real web.config CSP and the automatic csp fixture fails any test
 * on a violation beyond the register.
 *
 * Since OI-47 a reused authenticator-app code is always refused and a first factor
 * always needs the emailed code while email is on: the card holds no row for
 * either. The section model below is the card as SystemSettingsRegistry now
 * declares it, reduced to the row these tests read.
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. The server's refusal of a reused code is pinned by the xUnit
 * suite and proven live by Tools/probes/two-factor-race.mjs totp-replay. What is
 * proven here is what only a built bundle in a browser decides: that the TwoFactor
 * section has a title and a description in English and in Arabic, that the row it
 * shows is labelled, and that the verify page shows the API's own sentence for
 * TwoFactor.CodeAlreadyUsed rather than the generic text it would show for a code
 * the client does not know. Each absence of a retired row is asserted in the same
 * render as the presence of a row that stays, so an empty page cannot pass it.
 *
 * Deliberate breaks this file must catch: SECTION_I18N without TwoFactor (the card
 * shows the raw key); a RejectReusedCodes field put back into TWO_FACTOR_SECTION
 * (the absence check fails while the presence check holds); and
 * TwoFactor.CodeAlreadyUsed removed from docs/api/error-codes.json then
 * `pnpm gen:error-codes` (the generic text shows).
 */

const ROW_VERSION = "AAAAAAAAB9E="

/** The TwoFactor section's administrator-enforcement row, at the shipped default. */
const TWO_FACTOR_SECTION = {
  key: "TwoFactor",
  group: "security",
  editable: true,
  version: 0,
  rowVersion: ROW_VERSION,
  fields: [
    {
      path: "EnforceForPlatformAdmins",
      kind: "bool",
      effectiveValue: false,
      baselineValue: false,
      defaultValue: false,
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

async function serveSettings(api: HarnessApi, preferredLanguage = "en") {
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
      return false
    },
    { preferredLanguage }
  )
}

const enforcementRow = (page: Page) => page.locator('[id="setting-EnforceForPlatformAdmins"]')
const replayRow = (page: Page) => page.locator('[id="setting-RejectReusedCodes"]')
const firstFactorEmailRow = (page: Page) => page.locator('[id="setting-RequireEmailCodeForFirstFactor"]')
const cardTitle = (page: Page) => page.locator('[data-slot="card-title"]')

async function openTwoFactorSection(page: Page) {
  await page.goto(`${ORIGINS.console}/admin/system-settings/TwoFactor`)
  await enforcementRow(page).waitFor()
}

/** Neither retired switch has a row, on the card that still shows the row that stays. */
async function expectNoRetiredSwitchRows(page: Page) {
  await expect(enforcementRow(page)).toBeVisible()
  await expect(replayRow(page)).toHaveCount(0)
  await expect(firstFactorEmailRow(page)).toHaveCount(0)
}

test("the TwoFactor card is titled, described and labelled in English, with no row for the retired switches", async ({ page, api }) => {
  await serveSettings(api)
  await openTwoFactorSection(page)

  await expect(cardTitle(page).filter({ hasText: "Two-factor authentication" })).toBeVisible()
  await expect(page.getByText("An authenticator-app code is always accepted once only")).toBeVisible()
  await expect(enforcementRow(page)).toContainText("Require two-factor authentication for platform administrators")
  await expectNoRetiredSwitchRows(page)
  // The raw key is what a missing SECTION_I18N entry renders as the title.
  await expect(cardTitle(page).filter({ hasText: /^TwoFactor$/ })).toHaveCount(0)
})

test("the TwoFactor card is titled, described and labelled in Arabic, with no row for the retired switches", async ({ page, api }) => {
  await serveSettings(api, "ar")
  await openTwoFactorSection(page)

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await expect(cardTitle(page).filter({ hasText: "المصادقة الثنائية" })).toBeVisible()
  await expect(page.getByText("ويُقبل رمز تطبيق المصادقة مرة واحدة فقط دائمًا")).toBeVisible()
  await expect(enforcementRow(page)).toContainText("فرض المصادقة الثنائية على مسؤولي المنصّة")
  await expectNoRetiredSwitchRows(page)
  await expect(cardTitle(page).filter({ hasText: /^TwoFactor$/ })).toHaveCount(0)
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
