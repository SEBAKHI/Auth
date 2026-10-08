import type { Page } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * S08 in a real browser: while the server withholds a platform administrator's
 * authority until the session proves a second factor, both consoles send them to
 * /two-factor/required, from any page but the profile, on a deep link and on a
 * reload — and the step the server names takes them back where they were going.
 * On the S30a harness, so every page runs under its app's real web.config CSP and
 * the automatic csp fixture fails any test on a violation beyond the register.
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. That the token lacks the permissions, that refreshes re-decide,
 * and that step-up settles the factor and upgrades the session in one transaction
 * are pinned by the xUnit suite (PlatformMfaPolicyTests, RefreshTokenMfaPolicyTests,
 * StepUpTwoFactorCommandHandlerTests). What is proven here is what only a built
 * bundle in a browser decides: the guard in the route tree, the exempt profile,
 * the page's three steps and its way back, Arabic right to left without overflow,
 * and the switch asking before it is saved.
 *
 * Deliberate breaks this file must catch: RequireMfaSatisfied removed from the
 * console's route tree (/users renders instead of redirecting), and
 * EnforceForPlatformAdmins removed from HIGH_IMPACT_PATHS (the switch saves
 * without asking).
 */

const TWO_FACTOR_REQUIRED = "/two-factor/required"

/**
 * For the first answer after a cold navigation only. The redirect is what is
 * measured, not the speed: with every worker starting its own TLS servers at
 * once, the first bundle load alone has taken more than the 5 s default here.
 */
const FIRST_LOAD = { timeout: 20_000 }

interface MfaServer {
  requirement: string
  stepUps: unknown[]
}

async function serveConsole(
  api: HarnessApi,
  state: MfaServer,
  preferredLanguage = "en"
) {
  await api.useAuthenticated(
    ["users:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/2fa/step-up" && route.request().method() === "POST") {
        state.stepUps.push(route.request().postDataJSON())
        // The server upgraded the session: the next token and /me carry it.
        state.requirement = "none"
        await route.fulfill({ status: 204, body: "" })
        return true
      }
      return false
    },
    { preferredLanguage, mfaRequirement: () => state.requirement }
  )
}

const pathOf = (page: Page) => new URL(page.url()).pathname

test("s08: a guarded page sends the administrator to the step-up, which brings them back", async ({ page, api }) => {
  const state: MfaServer = { requirement: "step_up", stepUps: [] }
  await serveConsole(api, state)

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await expect(page.getByRole("heading", { name: "Two-factor authentication required" })).toBeVisible()

  // A reload, and a deep link typed again, do not get round it.
  await page.reload()
  await expect(page.getByLabel("Verification code", { exact: true })).toBeVisible()
  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`)

  await page.getByLabel("Verification code", { exact: true }).fill("123456")
  await page.getByRole("button", { name: "Verify" }).click()

  await expect.poll(() => state.stepUps).toEqual([{ code: "123456", useRecoveryCode: false }])
  // Back where they were going: the page the guard turned them away from.
  await expect.poll(() => pathOf(page)).toBe("/users")
})

test("s08: the profile stays open while a second factor is still owed", async ({ page, api }) => {
  await serveConsole(api, { requirement: "enroll", stepUps: [] })

  await page.goto(`${ORIGINS.console}/profile`)
  // Given time to redirect if it were going to: the shell has loaded.
  await expect(page.locator('[data-slot="sidebar-inset"]')).toBeVisible(FIRST_LOAD)
  expect(pathOf(page)).toBe("/profile")
})

test("s08: an account without a factor is offered enrolment", async ({ page, api }) => {
  await serveConsole(api, { requirement: "enroll", stepUps: [] })

  await page.goto(`${ORIGINS.console}/`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await expect(page.getByText("Your account has no second factor yet.")).toBeVisible()
  await expect(page.getByRole("button", { name: "Enable two-factor" })).toBeVisible()
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible()
})

test("s08: a session whose sign-in is unknown is asked to sign in again", async ({ page, api }) => {
  await serveConsole(api, { requirement: "reauthenticate", stepUps: [] })

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await page.getByRole("button", { name: "Sign in again" }).click()

  const dialog = page.getByRole("alertdialog", { name: "Sign in again to continue" })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("Sign out and sign back in with your password and your authenticator code.")
})

test("s08: the accounts app guards its own shell the same way", async ({ page, api }) => {
  await serveConsole(api, { requirement: "step_up", stepUps: [] })

  await page.goto(`${ORIGINS.accounts}/organizations`)
  await expect(page).toHaveURL(`${ORIGINS.accounts}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)

  await page.goto(`${ORIGINS.accounts}/profile`)
  await expect(page.getByRole("tab", { name: "Security" })).toBeVisible()
  expect(pathOf(page)).toBe("/profile")
})

test("s08: the page reads right to left in Arabic, with nothing spilling out", async ({ page, api }) => {
  await serveConsole(api, { requirement: "step_up", stepUps: [] }, "ar")

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  const heading = page.getByRole("heading", { name: "المصادقة الثنائية مطلوبة" })
  await expect(heading).toBeVisible()
  expect(await heading.evaluate((element) => getComputedStyle(element).direction)).toBe("rtl")
  await expect(page.getByText("أدخل الرمز من تطبيق المصادقة، أو رمز استرداد، للمتابعة.")).toBeVisible()

  // Measured at the card that holds the step, and at the document: a floor first,
  // so a selector that matches nothing cannot pass.
  const card = page.locator('[data-slot="card"]').first()
  await expect(card).toBeVisible()
  const spill = await card.evaluate((element) => ({
    width: element.clientWidth,
    card: element.scrollWidth - element.clientWidth,
    document: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  }))
  expect(spill.width).toBeGreaterThan(0)
  expect(spill.card).toBe(0)
  expect(spill.document).toBe(0)
})

// ── The switch asks before it is saved ────────────────────────────────────

const ROW_VERSION = "AAAAAAAAB9E="

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

test("s08: turning enforcement on asks first, says who it applies to, and writes nothing until confirmed", async ({ page, api }) => {
  const puts: unknown[] = []
  await api.useAuthenticated(["system-settings:manage"], async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/admin/system-settings") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ restartPending: false, dbOverridesUnavailable: false, sections: [TWO_FACTOR_SECTION] }),
      })
      return true
    }
    if (path.startsWith("/api/v1/admin/system-settings/") && route.request().method() === "PUT") {
      puts.push(route.request().postDataJSON())
      await route.fulfill({ status: 200, contentType: "application/json", body: "{}" })
      return true
    }
    return false
  })

  await page.goto(`${ORIGINS.console}/admin/system-settings/TwoFactor`)
  const row = page.locator('[id="setting-EnforceForPlatformAdmins"]')
  await row.waitFor(FIRST_LOAD)
  await expect(row).toContainText("Require two-factor authentication for platform administrators")
  await expect(row).toContainText("applies to platform administrators only")

  await row.getByRole("switch").click()
  await page.locator('[data-slot="settings-unsaved-bar"]').locator('button[type="submit"]').click()

  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("Require two-factor authentication for platform administrators")
  expect(puts).toHaveLength(0)

  await dialog.getByRole("button", { name: "Confirm" }).click()
  await expect.poll(() => puts.length).toBe(1)
  expect(puts[0]).toEqual(expect.objectContaining({ overrides: { EnforceForPlatformAdmins: true } }))
})
