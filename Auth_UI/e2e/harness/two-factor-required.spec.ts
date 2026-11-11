import type { Page } from "@playwright/test"

import { en } from "../../packages/i18n/src/locales/en"
import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"
import { loginResponse } from "../isolated/accounts/mock-anonymous-api"
import { fulfillJson } from "../isolated/mock-authenticated-api"
import {
  PASSKEY_RP_ID,
  USER_HANDLE,
  assertionOptions,
  attachVirtualAuthenticator,
  challenge,
  clientData,
} from "./passkey-authenticator"

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

test("s08: switched on while the console is open, the next refused page leads to the step-up", async ({ page, api }) => {
  // The console was opened with nothing owed; then a background refresh minted a
  // token without the platform authority. Every permission-guarded call is now
  // refused, and only the refusal tells the tab.
  const state = { requirement: "none" }
  await api.useAuthenticated(
    ["users:read", "roles:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (state.requirement !== "none" && !path.startsWith("/api/v1/auth/")) {
        await route.fulfill({
          status: 403,
          contentType: "application/problem+json",
          headers: { "WWW-Authenticate": 'Bearer error="insufficient_user_authentication"' },
          body: JSON.stringify({ status: 403, code: "TwoFactor.RequiredByPolicy" }),
        })
        return true
      }
      return false
    },
    { mfaRequirement: () => state.requirement }
  )

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page.locator('[data-slot="sidebar-inset"]')).toBeVisible(FIRST_LOAD)
  expect(pathOf(page)).toBe("/users")

  state.requirement = "step_up"
  // An in-app navigation, not a reload: the user info the tab holds still says none.
  await page.evaluate(() => {
    window.history.pushState({}, "", "/roles")
    window.dispatchEvent(new PopStateEvent("popstate"))
  })

  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`)
  await expect(page.getByLabel("Verification code", { exact: true })).toBeVisible()
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
  await expect(page.getByRole("button", { name: "Turn on the authenticator app" })).toBeVisible()
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible()
})

test("s08: a session whose sign-in is unknown proves the person where they stand", async ({ page, api }) => {
  await serveConsole(api, { requirement: "reauthenticate", stepUps: [] })

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await page.getByRole("button", { name: "Confirm" }).click()

  const dialog = page.getByRole("dialog", { name: "Confirm it's you" })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("Confirm it's you with your password and your second factor.")
  await expect(dialog.getByLabel("Password")).toBeVisible()
  expect(api.firstParty.logouts).toHaveLength(0)
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

/**
 * S20 PR C: an administrator whose only qualifying factor is a passkey
 * (`mfa_req: passkey`). Nothing upgrades the session: a passkey sign-in
 * replaces it with one that proved two factors, adopted like any other sign-in,
 * and the page leaves through the completion rule, back where they were going.
 * Deliberate break this test must catch: `refreshSessionNow` in place of the
 * adoption (the step would only renew the session that lacks the factor).
 */
test("s20: the passkey step signs in with the passkey, adopts that session and leaves", async ({ page, api }) => {
  const authenticator = await attachVirtualAuthenticator(page)
  await authenticator.addCredential()
  const state = { requirement: "passkey" }
  const asserted: Array<{ origin: string; type: string }> = []
  await api.useAuthenticated(
    ["users:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/passkeys/assertion/options") {
        await fulfillJson(route, assertionOptions())
        return true
      }
      if (path === "/api/v1/auth/passkeys/assertion/verify") {
        const body = route.request().postDataJSON() as { credential: unknown }
        asserted.push(clientData(body.credential))
        // The new session proved two factors: nothing is owed any more.
        state.requirement = "none"
        await fulfillJson(
          route,
          loginResponse({
            id: "99999999-9999-9999-9999-999999999999",
            email: "isolated@example.test",
            firstName: "Isolated",
            lastName: "Operator",
            preferredLanguage: "en",
            timeZone: "UTC",
            roles: [],
            permissions: ["users:read"],
          })
        )
        return true
      }
      return false
    },
    { mfaRequirement: () => state.requirement }
  )

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await expect(page.getByTestId("passkey-lost-way-out")).toBeVisible()
  const passkey = page
    .getByTestId("passkey-sign-in")
    .getByRole("button", { name: "Sign in with your passkey" })
  await expect(passkey).toBeEnabled()
  await passkey.click()

  await expect.poll(() => asserted.length).toBe(1)
  expect(asserted[0]).toMatchObject({ origin: ORIGINS.console, type: "webauthn.get" })
  await expect.poll(() => pathOf(page)).toBe("/users")
})

// ── OI-280 PR B: a passkey beside the app ─────────────────────────────────

/**
 * OI-280 PR B (T-B8) in the console: an administrator with no second factor, while
 * passkeys run here and email sending is on, is offered a passkey beside the app. It is
 * added through the addition every page shares (the emailed code first: a password
 * sign-in proved no mailbox, X02), then used once in place, since the registration
 * raised no session (D-B5). The proof raises this session, the page refreshes it, and
 * the administrator is back where they were going.
 * Deliberate break this test must catch: the in-place use skipped (the page goes on
 * without the proof, the refreshed session still owes the factor, and the page stays).
 */
test("oi280b: the set-up page adds a passkey beside the app, uses it once in place, and goes back", async ({
  page,
  api,
}) => {
  await attachVirtualAuthenticator(page)
  const state = {
    requirement: "enroll",
    registered: [] as string[],
    codeSends: 0,
    proofs: [] as Array<{ id: string; origin: string; type: string }>,
  }
  await api.useAuthenticated(
    ["users:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path === "/api/v1/users/me" && method === "GET") {
        await fulfillJson(route, {
          id: "99999999-9999-9999-9999-999999999999",
          email: "isolated@example.test",
          firstName: "Isolated",
          lastName: "Operator",
          displayName: "Isolated Operator",
          preferredLanguage: "en",
          timeZone: "UTC",
          hasPassword: true,
          emailConfirmed: true,
          twoFactorEnabled: false,
          totpEnabled: false,
          emailCodeEnabled: false,
          qualifyingMfa: false,
          passkeyCount: state.registered.length,
          emailCodeAvailable: true,
          roles: [],
          permissions: ["users:read"],
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/options" && method === "POST") {
        await fulfillJson(route, {
          options: {
            rp: { id: PASSKEY_RP_ID, name: "AuthSystem" },
            user: { id: USER_HANDLE, name: "isolated@example.test", displayName: "Isolated Operator" },
            challenge: challenge(),
            pubKeyCredParams: [
              { type: "public-key", alg: -7 },
              { type: "public-key", alg: -257 },
            ],
            timeout: 300_000,
            attestation: "none",
            excludeCredentials: [],
            authenticatorSelection: { residentKey: "required", requireResidentKey: true, userVerification: "required" },
          },
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
          emailCodeRequired: true,
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/email-code" && method === "POST") {
        state.codeSends += 1
        await fulfillJson(route, {
          emailCodeRequired: true,
          sentTo: "i******d@example.test",
          expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/verify" && method === "POST") {
        const body = route.request().postDataJSON() as { credential: { id: string }; name: string | null }
        state.registered.push(body.credential.id)
        await fulfillJson(route, {
          id: "aaaaaaaa-0000-0000-0000-000000000002",
          name: body.name ?? "Passkey",
          createdAt: "2026-11-11T09:00:00Z",
          lastUsedAt: null,
          isBackupEligible: false,
          isBackedUp: false,
          lastCounterRegressionAt: null,
          credentialId: body.credential.id,
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys/assertion/options" && method === "POST") {
        await fulfillJson(route, assertionOptions())
        return true
      }
      if (path === "/api/v1/auth/reauthentication/passkey" && method === "POST") {
        const body = route.request().postDataJSON() as { credential: { id: string } }
        const signed = clientData(body.credential)
        state.proofs.push({ id: body.credential.id, origin: signed.origin, type: signed.type })
        // The proof raised this session: its next token owes nothing.
        state.requirement = "none"
        await route.fulfill({ status: 204, body: "" })
        return true
      }
      return false
    },
    {
      mfaRequirement: () => state.requirement,
      branding: { platformName: "AuthSystem", staySignedInOffered: true, passkeySignInOffered: true },
    }
  )

  await page.goto(`${ORIGINS.console}/users`)
  await expect(page).toHaveURL(`${ORIGINS.console}${TWO_FACTOR_REQUIRED}`, FIRST_LOAD)
  await expect(page.getByText(en.auth.mfaRequiredEnrollWithPasskey)).toBeVisible()
  await expect(page.getByRole("button", { name: en.profile.enableTwoFactor })).toBeVisible()

  await page.getByRole("button", { name: en.auth.mfaRequiredAddPasskey }).click()
  const dialog = page.getByRole("dialog", { name: en.profile.passkeys.addTitle })
  await dialog.getByRole("button", { name: en.auth.emailMeACode }).click()
  await expect.poll(() => state.codeSends).toBe(1)
  await dialog.locator("[data-slot=input-otp]").click()
  await page.keyboard.type("654321")
  await dialog.getByRole("button", { name: en.profile.passkeys.create }).click()

  // Added, not used: until the touch, the page stays and the factor is still owed.
  const useIt = page
    .getByTestId("passkey-sign-in")
    .getByRole("button", { name: en.auth.mfaRequiredUsePasskeyAction })
  await expect(useIt).toBeEnabled(FIRST_LOAD)
  expect(state.registered).toHaveLength(1)
  expect(pathOf(page)).toBe(TWO_FACTOR_REQUIRED)
  await useIt.click()

  await expect.poll(() => pathOf(page), FIRST_LOAD).toBe("/users")
  expect(state.proofs).toEqual([{ id: state.registered[0], origin: ORIGINS.console, type: "webauthn.get" }])
})
