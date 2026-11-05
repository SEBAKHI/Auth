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
 * violation); the sign-in marker ignored (OI-275: the second sign-in comes
 * back); the skip taken whatever proved the first step (OI-275: an emailed code
 * alone, for an account with a password, reaches the set-up page).
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
  codeSignIns: number
  verifies: unknown[]
  enables: unknown[]
  emailSends: number
  resumes: string[]
}

const newServer = (): JourneyServer => ({
  logins: 0,
  codeSignIns: 0,
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
 * there is the xUnit suite's. With `bounce`, the FIRST resume is answered as the
 * server answers a session short of the application's second step: a redirect
 * to the accounts sign-in page with the request and those words (OI-275).
 */
async function serveJourney(
  api: HarnessApi,
  state: JourneyServer,
  account: { challenged: boolean; qualifyingMfa: boolean; bounce?: string },
  branding?: Record<string, unknown>
) {
  await api.useAuthenticated(
    [],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()

      if (path === "/api/v1/auth/authorize/resume" && method === "GET") {
        state.resumes.push(url.search)
        if (account.bounce && state.resumes.length === 1) {
          await route.fulfill({
            status: 302,
            headers: { location: `${ORIGINS.accounts}/login?${PENDING}${account.bounce}` },
          })
          return true
        }
        await route.fulfill({ status: 200, contentType: "text/html", body: "<p>resumed</p>" })
        return true
      }
      // The sign-in by emailed code (OI-215): start, check, then spend it.
      if (path === "/api/v1/auth/email-code/start" && method === "POST") {
        await json(route, {
          pendingId: "harness-pending",
          maskedEmail: "p****n@example.test",
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        })
        return true
      }
      if (path === "/api/v1/auth/email-code/verify" && method === "POST") {
        await json(route, { step: "signIn" })
        return true
      }
      if (path === "/api/v1/auth/email-code/sign-in" && method === "POST") {
        state.codeSignIns += 1
        await api.firstParty.answerSignIn(route, USER, "s23-secret")
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

// ── OI-275: one sign-in, not two, before the set-up page ─────────────────────

/**
 * Records, in each accounts document opened under `mfa_enroll`, whether the
 * sign-in form was ever put on the page there: "never painted" is observed, not
 * inferred from where the test ended up.
 */
async function watchForTheEnrollForm(page: Page) {
  await page.addInitScript((accounts: string) => {
    if (location.origin !== accounts || !location.search.includes("reason=mfa_enroll")) return
    const flag = () => {
      if (location.pathname === "/login" && document.querySelector('input[type="email"]')) {
        ;(window as unknown as { __enrollFormSeen?: boolean }).__enrollFormSeen = true
      }
    }
    new MutationObserver(flag).observe(document, { childList: true, subtree: true })
  }, ORIGINS.accounts)
}

const enrollFormSeen = (page: Page) =>
  page.evaluate(() => (window as unknown as { __enrollFormSeen?: boolean }).__enrollFormSeen ?? false)

test("s23: journey 2 in one sign-in: the bounce for set-up goes straight to the set-up page", async ({ page, api }) => {
  const state = newServer()
  await serveJourney(api, state, {
    challenged: false,
    qualifyingMfa: false,
    bounce: "&reason=mfa_enroll&method=password",
  })
  await watchForTheEnrollForm(page)

  // No session and no reason: the application sent the person to sign in.
  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await expect(page.locator('input[type="password"]')).toBeVisible(FIRST_LOAD)
  await expect(reasonAlert(page)).toHaveCount(0)
  await signInWithPassword(page)

  // The resume bounced once, for set-up, and the page skipped its form.
  await expect(page).toHaveURL(`${ORIGINS.accounts}/two-factor/required`, FIRST_LOAD)
  await expect(page.getByText(en.auth.mfaAppRequiredSubtitle)).toBeVisible()
  expect(state.resumes).toEqual([`?${PENDING}`])
  expect(state.logins).toBe(1)
  expect(await enrollFormSeen(page)).toBe(false)
  // Spent: nothing is left in the tab for a later page to act on.
  expect(await page.evaluate(() => sessionStorage.getItem("auth.signIn.marker"))).toBeNull()

  // The rest of the journey is the set-up as before: X02's emailed proof, then the codes.
  await page.getByRole("button", { name: en.profile.enableTwoFactor }).click()
  await page.getByLabel(en.auth.twoFactorCode, { exact: true }).fill("123456")
  await page.getByRole("button", { name: "Send code" }).click()
  await expect.poll(() => state.emailSends).toBe(1)
  await page.getByLabel("Code from your email", { exact: true }).fill("654321")
  await page.getByRole("button", { name: en.auth.verify }).click()
  const codes = page.getByRole("dialog", { name: "Save your recovery codes" })
  await expect(codes).toBeVisible()
  await codes.getByRole("button", { name: en.common.close }).first().click()

  await expect.poll(() => state.resumes).toEqual([`?${PENDING}`, `?${PENDING}`])
  expect(state.enables).toEqual([{ code: "123456", emailCode: "654321" }])
  expect(state.logins).toBe(1)
})

test("s23: a password account that signed in by emailed code alone gets the form after the bounce", async ({ page, api }) => {
  const state = newServer()
  await serveJourney(
    api,
    state,
    { challenged: false, qualifyingMfa: false, bounce: "&reason=mfa_enroll&method=password" },
    { platformName: "AuthSystem", staySignedInOffered: true, codeSignInOffered: true, primaryMethod: "code" }
  )

  await page.goto(`${ORIGINS.accounts}/login?${PENDING}`)
  await expect(page.getByRole("button", { name: en.auth.continueWithEmail })).toBeVisible(FIRST_LOAD)
  await page.locator('input[type="email"]').fill(USER.email)
  await page.getByRole("button", { name: en.auth.continueWithEmail }).click()
  await page.locator("[data-slot=input-otp]").click()
  await page.keyboard.type("123456")

  // The set-up would refuse a session that proved the mailbox only: the form,
  // with the password first, as the server named it.
  await expect(page).toHaveURL(`${ORIGINS.accounts}/login?${PENDING}&reason=mfa_enroll&method=password`, FIRST_LOAD)
  await expect(reasonAlert(page)).toContainText(en.auth.signInReasonEnrollTitle)
  await expect(page.locator('input[type="password"]')).toBeVisible()
  expect(state.codeSignIns).toBe(1)
  expect(state.resumes).toEqual([`?${PENDING}`])
  await expect(page).not.toHaveURL(/\/two-factor\/required/)
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
