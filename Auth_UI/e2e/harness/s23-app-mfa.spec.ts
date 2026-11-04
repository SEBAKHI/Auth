import type { Page, Route } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { fa } from "../../packages/i18n/src/locales/fa"
import { fr } from "../../packages/i18n/src/locales/fr"
import { tr } from "../../packages/i18n/src/locales/tr"
import { ur } from "../../packages/i18n/src/locales/ur"
import { zh } from "../../packages/i18n/src/locales/zh"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * S23 PR B in a real browser: an application that requires two-step
 * verification sent the person back to the accounts sign-in page with a reason
 * (`reason=mfa_*`) and the server's method word (`method=password|code`). The
 * page says why, puts that method first, carries the pair across the sign-in,
 * opens the "set up an authenticator" screen in application mode for
 * `mfa_enroll`, and every way ends with one top-level navigation to the resume
 * URL. On the S30a harness at phone width, so each page runs under its app's
 * real web.config CSP and the automatic csp fixture fails any test on a
 * violation beyond the register (ARR-0780: zero undeclared violations).
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. That authorize sends the words, recomputes the requirement on
 * the resume and ends a return without progress with access_denied is pinned by
 * the xUnit suite (AuthorizeCommandHandlerAppMfaTests) and by the real run in
 * the PR (AppMfaDevRun). What is proven here is what only a built bundle in a
 * browser decides: the message, the method put first, every hop keeping the
 * pair, the application-mode screen with X02's emailed proof and the recovery
 * codes, the way out, and the titles whole in seven languages.
 *
 * Deliberate breaks this file must catch: the reason Alert removed from the
 * sign-in page; an inline style in the application-mode screen (a CSP
 * violation).
 */

test.use({ viewport: { width: 375, height: 812 } })

const HANDLE = "0123456789abcdef".repeat(4)
const PENDING = `client_id=CRM&auth_request=${HANDLE}`
const RESUME = `${ORIGINS.api}/api/v1/auth/authorize/resume?${PENDING}`

const USER = {
  id: "23232323-2323-2323-2323-232323232323",
  email: "person@example.test",
  firstName: "Second",
  lastName: "Step",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

const SETUP = {
  secret: "JBSWY3DPEHPK3PXP",
  qrCodeUri:
    "otpauth://totp/Harness:person%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=Harness",
  manualEntryKey: "JBSW Y3DP EHPK 3PXP",
  emailCodeRequired: true,
}

/** For the first answer after a cold navigation only; see two-factor-required.spec.ts. */
const FIRST_LOAD = { timeout: 20_000 }

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) })
}

interface JourneyServer {
  logins: number
  verifies: unknown[]
  enables: unknown[]
  emailSends: number
  resumes: string[]
}

const newServer = (): JourneyServer => ({
  logins: 0,
  verifies: [],
  enables: [],
  emailSends: 0,
  resumes: [],
})

/**
 * The account behind the sign-in: `challenged` answers the password with the
 * second-step challenge (an account with an authenticator); otherwise the
 * password signs in at once. `qualifyingMfa` is what /me says about the factor.
 * The resume is answered with a plain page, recorded: what the server decides
 * there is the xUnit suite's.
 */
async function serveJourney(
  api: HarnessApi,
  state: JourneyServer,
  account: { challenged: boolean; qualifyingMfa: boolean },
  branding?: Record<string, unknown>
) {
  await api.useAuthenticated(
    [],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()

      if (path === "/api/v1/auth/authorize/resume" && method === "GET") {
        state.resumes.push(url.search)
        await route.fulfill({ status: 200, contentType: "text/html", body: "<p>resumed</p>" })
        return true
      }
      if (path === "/api/v1/auth/login" && method === "POST") {
        state.logins += 1
        if (account.challenged) {
          await json(route, {
            requiresTwoFactor: true,
            twoFactorChallengeToken: "harness-challenge",
            twoFactorMethods: ["totp", "recovery"],
          })
        } else {
          await api.firstParty.answerSignIn(route, USER, "s23-secret")
        }
        return true
      }
      if (path === "/api/v1/auth/2fa/verify" && method === "POST") {
        state.verifies.push(route.request().postDataJSON())
        await api.firstParty.answerSignIn(route, USER, "s23-secret")
        return true
      }
      if (path === "/api/v1/users/me" && method === "GET") {
        await json(route, {
          ...USER,
          hasPassword: true,
          emailConfirmed: true,
          phoneConfirmed: false,
          twoFactorEnabled: account.qualifyingMfa,
          totpEnabled: account.qualifyingMfa,
          emailCodeEnabled: false,
          qualifyingMfa: account.qualifyingMfa,
          emailCodeAvailable: true,
        })
        return true
      }
      if (path === "/api/v1/auth/2fa/setup" && method === "POST") {
        await json(route, SETUP)
        return true
      }
      if (path === "/api/v1/auth/2fa/email-code" && method === "POST") {
        state.emailSends += 1
        await json(route, {
          emailCodeRequired: true,
          sentTo: "p****n@example.test",
          expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/2fa/enable" && method === "POST") {
        state.enables.push(route.request().postDataJSON())
        await json(route, { recoveryCodes: ["AAAA-1111", "BBBB-2222"] })
        return true
      }
      return false
    },
    { session: "none", branding }
  )
}

const reasonAlert = (page: Page) => page.getByTestId("sign-in-reason")

async function signInWithPassword(page: Page) {
  await page.locator('input[type="email"]').fill(USER.email)
  await page.locator('input[type="password"]').fill("Harness-Password-1!")
  await page.locator('button[type="submit"]').click()
}

test("s23: mfa_step_up says why, puts the password first, and the second step resumes the application once", async ({ page, api }) => {
  const state = newServer()
  await serveJourney(api, state, { challenged: true, qualifyingMfa: true })

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_step_up&method=password`)
  await expect(reasonAlert(page)).toContainText(en.auth.signInReasonStepUpTitle, FIRST_LOAD)
  await expect(reasonAlert(page)).toContainText(en.auth.signInReasonStepUpBody)
  await expect(page.locator('input[type="password"]')).toBeVisible()

  await signInWithPassword(page)
  await expect(page).toHaveURL(/\/two-factor(\?|$)/)
  // Six digits submit by themselves.
  await page.getByLabel(en.auth.twoFactorCode, { exact: true }).fill("123456")

  await expect.poll(() => state.resumes).toEqual([`?${PENDING}`])
  await expect(page).toHaveURL(RESUME)
  expect(state.verifies).toHaveLength(1)
})

test("s23: mfa_enroll signs in, sets up the authenticator with the emailed proof, shows the codes, and resumes once", async ({ page, api }) => {
  const state = newServer()
  await serveJourney(api, state, { challenged: false, qualifyingMfa: false })

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_enroll&method=password`)
  await expect(reasonAlert(page)).toContainText(en.auth.signInReasonEnrollTitle, FIRST_LOAD)
  await signInWithPassword(page)

  // The application mode: the platform requires nothing of this account, and
  // the page still waits for the authenticator.
  await expect(page).toHaveURL(`${ORIGINS.accounts}/two-factor/required`)
  await expect(page.getByText(en.auth.mfaAppRequiredSubtitle)).toBeVisible()
  expect(state.resumes).toEqual([])

  await page.getByRole("button", { name: en.profile.enableTwoFactor }).click()
  // X02: the first factor needs the code emailed to the confirmed address.
  await expect(page.getByLabel("Code from your email", { exact: true })).toBeVisible()
  await page.getByLabel(en.auth.twoFactorCode, { exact: true }).fill("123456")
  await page.getByRole("button", { name: "Send code" }).click()
  await expect.poll(() => state.emailSends).toBe(1)
  await page.getByLabel("Code from your email", { exact: true }).fill("654321")
  await page.getByRole("button", { name: en.auth.verify }).click()

  const codes = page.getByRole("dialog", { name: "Save your recovery codes" })
  await expect(codes).toBeVisible()
  expect(state.resumes).toEqual([])
  await codes.getByRole("button", { name: en.common.close }).first().click()

  await expect.poll(() => state.resumes).toEqual([`?${PENDING}`])
  expect(state.enables).toEqual([{ code: "123456", emailCode: "654321" }])
})

test("s23: Cancel on the set-up screen resumes the request unmet, for the application to refuse", async ({ page, api }) => {
  const state = newServer()
  await serveJourney(api, state, { challenged: false, qualifyingMfa: false })

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_enroll&method=password`)
  await expect(reasonAlert(page)).toBeVisible(FIRST_LOAD)
  await signInWithPassword(page)
  await expect(page).toHaveURL(`${ORIGINS.accounts}/two-factor/required`)

  await page.getByRole("button", { name: en.common.cancel }).click()

  await expect.poll(() => state.resumes).toEqual([`?${PENDING}`])
  expect(state.enables).toEqual([])
})

test("s23: an account that already has an authenticator is offered Continue", async ({ page, api }) => {
  const state = newServer()
  await serveJourney(api, state, { challenged: false, qualifyingMfa: true })

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_enroll&method=password`)
  await expect(reasonAlert(page)).toBeVisible(FIRST_LOAD)
  await signInWithPassword(page)

  await expect(page.getByText(en.auth.mfaAppAlreadyEnrolled)).toBeVisible()
  await page.getByRole("button", { name: en.auth.mfaAppContinue }).click()

  await expect.poll(() => state.resumes).toEqual([`?${PENDING}`])
})

test("s23: the server's code word beats the deployment's password, and the password stays one click away", async ({ page, api }) => {
  const state = newServer()
  // Code sign-in offered, with the password as the deployment's own first method.
  await serveJourney(api, state, { challenged: false, qualifyingMfa: false }, {
    platformName: "AuthSystem",
    staySignedInOffered: true,
    codeSignInOffered: true,
    primaryMethod: "password",
  })

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_enroll&method=code`)
  await expect(reasonAlert(page)).toBeVisible(FIRST_LOAD)
  await expect(page.getByRole("button", { name: en.auth.continueWithEmail })).toBeVisible()
  await expect(page.locator('input[type="password"]')).toHaveCount(0)

  await page.getByRole("button", { name: en.auth.signInWithPasswordInstead }).click()
  await expect(page.locator('input[type="password"]')).toBeVisible()
})

test("s23: a hard-locked person reaches the password reset with the request and the reason", async ({ page, api }) => {
  await api.useAnonymous(async (route, url) => {
    if (url.pathname.toLowerCase() !== "/api/v1/auth/login" || route.request().method() !== "POST") {
      return false
    }
    await route.fulfill({
      status: 403,
      contentType: "application/problem+json",
      body: JSON.stringify({
        title: "Forbidden",
        status: 403,
        code: "User.AccountLockedResetRequired",
        detail: "This account is locked. Reset your password to unlock it.",
      }),
    })
    return true
  })

  const search = `?${PENDING}&reason=mfa_step_up&method=password`
  await page.goto(`${ORIGINS.accounts}/login${search}`)
  await expect(reasonAlert(page)).toBeVisible(FIRST_LOAD)
  await signInWithPassword(page)
  await expect(page.getByText("This account is locked. Reset your password to unlock it.")).toBeVisible()

  await page.getByRole("link", { name: en.auth.forgotPassword }).click()
  await expect(page).toHaveURL(`${ORIGINS.accounts}/forgot-password${search}`)
  // And back: the sign-in page there says the same thing.
  const back = page.getByRole("link", { name: en.auth.backToSignIn }).first()
  expect(new URL(await back.evaluate((a) => (a as HTMLAnchorElement).href)).search).toBe(search)
})

// ── The titles, whole, in seven languages at phone width ─────────────────────

const CATALOGS = { en, ar, tr, fr, zh, ur, fa } as const
const RTL = new Set(["ar", "ur", "fa"])
const REASONS = [
  ["mfa_step_up", "signInReasonStepUpTitle"],
  ["mfa_enroll", "signInReasonEnrollTitle"],
  ["mfa_reauthenticate", "signInReasonReauthenticateTitle"],
] as const

for (const [code, catalog] of Object.entries(CATALOGS)) {
  test(`s23: the reason titles are whole at 375 px (${code})`, async ({ page, api }) => {
    await api.useAnonymous()
    await page.addInitScript((language: string) => {
      localStorage.setItem("auth.language", language)
    }, code)

    for (const [reason, key] of REASONS) {
      await page.goto(`${ORIGINS.accounts}/login?${PENDING}&reason=${reason}&method=password`)
      const title = reasonAlert(page).locator('[data-slot="alert-title"]')
      await expect(title).toHaveText(catalog.auth[key], FIRST_LOAD)
      await expect(page.locator("html")).toHaveAttribute("dir", RTL.has(code) ? "rtl" : "ltr")

      const box = await title.evaluate((element) => ({
        width: element.clientWidth,
        height: element.clientHeight,
        clipped: element.scrollHeight - element.clientHeight,
        spill: element.scrollWidth - element.clientWidth,
        direction: getComputedStyle(element).direction,
        alertSpill: element.parentElement!.scrollWidth - element.parentElement!.clientWidth,
        document: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      }))
      // A floor first: a title that rendered nothing cannot pass as unclipped.
      expect(box.width, `${code} ${reason}`).toBeGreaterThan(0)
      expect(box.height, `${code} ${reason}`).toBeGreaterThan(0)
      // The title is clamped to one line: anything longer is cut, not wrapped.
      expect(box.clipped, `${code} ${reason} clipped`).toBe(0)
      expect(box.spill, `${code} ${reason} spill`).toBe(0)
      expect(box.alertSpill, `${code} ${reason} alert`).toBe(0)
      expect(box.document, `${code} ${reason} document`).toBe(0)
      expect(box.direction).toBe(RTL.has(code) ? "rtl" : "ltr")
    }
  })
}

// ── The console offers Enforce (ConsoleReadinessCheck) ─────────────────────

/** The setting as SystemSettingsRegistry declares it, at its default. */
const IDENTITY_PROVIDER_SECTION = {
  key: "IdentityProvider",
  group: "access",
  editable: true,
  version: 0,
  rowVersion: "AAAAAAAAB9E=",
  fields: [
    {
      path: "ApplicationMfaEnforcement",
      kind: "enum",
      allowedValues: ["Monitor", "Enforce"],
      effectiveValue: "Monitor",
      baselineValue: "Monitor",
      defaultValue: "Monitor",
      source: "file",
      restartRequired: false,
      isPendingRestart: false,
      readOnly: false,
      sensitive: false,
    },
  ],
}

for (const [code, catalog, dir] of [
  ["en", en, "ltr"],
  ["ar", ar, "rtl"],
] as const) {
  test(`s23: the console offers Enforce with what it does (${code})`, async ({ page, api }) => {
    await api.useAuthenticated(
      ["system-settings:manage"],
      async (route, url) => {
        if (url.pathname.toLowerCase() !== "/api/v1/admin/system-settings") return false
        await json(route, {
          restartPending: false,
          dbOverridesUnavailable: false,
          sections: [IDENTITY_PROVIDER_SECTION],
        })
        return true
      },
      { preferredLanguage: code }
    )
    const texts = catalog.systemSettings.identityProvider

    await page.goto(`${ORIGINS.console}/admin/system-settings/IdentityProvider`)
    // The id is the control's. The section holds this one field, so its label and
    // hint are the only ones on the page.
    const control = page.locator('[id="setting-ApplicationMfaEnforcement"]')
    await control.waitFor(FIRST_LOAD)
    await expect(page.locator("html")).toHaveAttribute("dir", dir)
    await expect(page.getByText(texts.applicationMfaEnforcement, { exact: true })).toBeVisible()
    await expect(page.getByText(texts.applicationMfaEnforcementHint, { exact: true })).toBeVisible()

    // Two choices render as a radio group named by the setting.
    const choices = page.getByRole("radiogroup", { name: texts.applicationMfaEnforcement }).getByRole("radio")
    await expect(choices).toHaveText([
      texts.applicationMfaEnforcementOptions.Monitor,
      texts.applicationMfaEnforcementOptions.Enforce,
    ])
    await expect(choices.first()).toBeChecked()
  })
}
