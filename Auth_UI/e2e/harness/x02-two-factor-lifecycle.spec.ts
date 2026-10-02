import type { Page, Route } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * X02 in a real browser: switching two-factor off from a session whose sign-in is
 * too old asks the user to sign in again, and brings them back to the security
 * tab afterwards. On the S30a harness, so every page runs under its app's real
 * web.config CSP and the automatic csp fixture fails any test on a violation
 * beyond the register.
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. That the server measures the session's sign-in time, revokes the
 * other sessions and emails the owner is pinned by the xUnit suite and proven live
 * by Tools/probes/two-factor-race.mjs (lifecycle-disable, lifecycle-enable). What
 * is proven here is what only a built bundle in a browser decides: that the
 * published code Auth.ReauthenticationRequired opens a titled dialog rather than a
 * toast; that signing out from it and back in lands on /profile?tab=security; that
 * the disable form offers the recovery-code toggle; that the code the user just
 * signed in with is answered with X01's own sentence; and that all of it reads
 * right to left in Arabic.
 *
 * Deliberate break this file must catch: the Auth.ReauthenticationRequired branch
 * removed from the card's error handler (a toast shows and no dialog opens).
 */

const PROFILE = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "isolated@example.test",
  firstName: "Isolated",
  lastName: "Operator",
  hasPassword: true,
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: true,
  timeZone: "UTC",
}

/** The sign-in response's user, as the S01 spec gives it. */
const SIGNED_IN_USER = {
  id: PROFILE.id,
  email: PROFILE.email,
  firstName: PROFILE.firstName,
  lastName: PROFILE.lastName,
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

/** X01's sentence for TwoFactor.CodeAlreadyUsed (DomainErrors.resx). */
const CODE_ALREADY_USED =
  "This code was already used. Wait for the next code. If you did not just use it, change your password."

/** The English sentence of Auth.ReauthenticationRequired (DomainErrors.resx). */
const REAUTHENTICATION_REQUIRED =
  "For your security, sign in again to change two-factor authentication."

interface LifecycleServer {
  disables: unknown[]
  signedInAgain: boolean
}

async function problem(
  route: Route,
  status: number,
  code: string,
  detail: string
) {
  await route.fulfill({
    status,
    contentType: "application/problem+json",
    body: JSON.stringify({ title: "Refused", status, code, detail }),
  })
}

/**
 * A signed-in session too old to change two-factor: disable answers
 * Auth.ReauthenticationRequired until the user signs in again; after that it
 * answers what the real API answers for the code the user just signed in with.
 */
async function serveProfile(
  api: HarnessApi,
  state: LifecycleServer,
  preferredLanguage = "en"
) {
  await api.useAuthenticated(
    [],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()

      if (path === "/api/v1/users/me" && method === "GET") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ ...PROFILE, preferredLanguage }),
        })
        return true
      }

      if (path === "/api/v1/auth/2fa/disable" && method === "POST") {
        state.disables.push(route.request().postDataJSON())
        if (state.signedInAgain) {
          await problem(
            route,
            400,
            "TwoFactor.CodeAlreadyUsed",
            CODE_ALREADY_USED
          )
        } else {
          await problem(
            route,
            403,
            "Auth.ReauthenticationRequired",
            REAUTHENTICATION_REQUIRED
          )
        }
        return true
      }

      if (path === "/api/v1/auth/login" && method === "POST") {
        state.signedInAgain = true
        await api.firstParty.answerSignIn(
          route,
          SIGNED_IN_USER,
          "x02-fresh-sign-in"
        )
        return true
      }

      return false
    },
    { preferredLanguage }
  )
}

const disableCode = (page: Page, label: string) =>
  page.getByLabel(label, { exact: true })

async function openSecurityTab(page: Page, origin: string = ORIGINS.console) {
  await page.goto(`${origin}/profile?tab=security`)
}

/**
 * The whole flow, in the app at `origin`. Both apps serve the same profile
 * package under their own RequireAuth and login page, so each must bring the
 * user back to its own security tab.
 */
async function staleDisableReturns(
  page: Page,
  api: HarnessApi,
  origin: string
) {
  const state: LifecycleServer = { disables: [], signedInAgain: false }
  await serveProfile(api, state)
  await openSecurityTab(page, origin)

  // The toggle for a user whose phone is gone, and back.
  await page.getByRole("button", { name: "Use a recovery code" }).click()
  await expect(disableCode(page, "Recovery code")).toBeVisible()
  await page.getByRole("button", { name: "Use an authenticator code" }).click()

  // What switching off does is said before it is done.
  await expect(
    page.getByText("Turning it off signs out your other devices.")
  ).toBeVisible()

  await disableCode(page, "Verification code").fill("123456")
  await page.getByRole("button", { name: "Disable two-factor" }).click()

  const dialog = page.getByRole("alertdialog", {
    name: "Sign in again to continue",
  })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(
    "changing two-factor authentication needs a recent sign-in"
  )
  expect(state.disables).toEqual([{ code: "123456", useRecoveryCode: false }])

  // Signing out from the dialog, then in, lands back on the security tab.
  await dialog.getByRole("button", { name: "Sign in again" }).click()
  await expect(page).toHaveURL(/\/login(\?|$)/)
  await expect.poll(() => api.firstParty.logouts.length).toBe(1)

  await page.locator('input[type="email"]').fill(PROFILE.email)
  await page.locator('input[type="password"]').fill("Harness1!")
  await page.locator('button[type="submit"]').click()
  await expect(page).toHaveURL(`${origin}/profile?tab=security`)
  await expect(page.getByRole("tab", { name: "Security" })).toHaveAttribute(
    "aria-selected",
    "true"
  )

  // The code just used to sign in is refused in X01's own words: wait for the next.
  await disableCode(page, "Verification code").fill("123456")
  await page.getByRole("button", { name: "Disable two-factor" }).click()
  await expect(page.getByText(CODE_ALREADY_USED)).toBeVisible()
  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  expect(state.disables).toHaveLength(2)
}

test("x02: stale disable re-authenticates and returns", async ({
  page,
  api,
}) => {
  await staleDisableReturns(page, api, ORIGINS.console)
})

test("x02: in the accounts app, the same profile returns to its own security tab", async ({
  page,
  api,
}) => {
  await staleDisableReturns(page, api, ORIGINS.accounts)
})

test("x02: the sign-in-again dialog and the disable form read right to left in Arabic", async ({
  page,
  api,
}) => {
  const state: LifecycleServer = { disables: [], signedInAgain: false }
  await serveProfile(api, state, "ar")
  await openSecurityTab(page)

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await expect(
    page.getByText("تعطيلها يُخرج أجهزتك الأخرى من الحساب.")
  ).toBeVisible()

  await disableCode(page, "رمز التحقق").fill("123456")
  await page.getByRole("button", { name: "تعطيل المصادقة الثنائية" }).click()

  const dialog = page.getByRole("alertdialog", {
    name: "سجّل الدخول مجددًا للمتابعة",
  })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(
    "يتطلّب تغيير المصادقة الثنائية تسجيل دخول حديثًا"
  )
  await expect(
    dialog.getByRole("button", { name: "تسجيل الدخول مجددًا" })
  ).toBeVisible()
  expect(
    await dialog.evaluate((element) => getComputedStyle(element).direction)
  ).toBe("rtl")

  // Cancel leaves everything as it was: still signed in, still on the tab.
  await dialog.getByRole("button", { name: "إلغاء" }).click()
  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  await expect(page).toHaveURL(`${ORIGINS.console}/profile?tab=security`)
  expect(api.firstParty.logouts).toHaveLength(0)
})
