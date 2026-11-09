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
 * published code Auth.ReauthenticationRequired opens the titled window that proves the
 * person where they stand, with nothing signed out, rather than a toast; that the
 * change goes on from there; that its way out, signing out and back in, lands on
 * /profile?tab=security; that
 * the disable form offers the recovery-code toggle; that the code the user just
 * signed in with is answered with X01's own sentence; and that all of it reads
 * right to left in Arabic.
 *
 * Deliberate breaks this file must catch: the Auth.ReauthenticationRequired branch
 * removed from the card's error handler (a toast shows and no window opens); the window
 * signing out as it opens (the person is taken away, so it is never seen).
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
  proofs?: unknown[]
  /** The next code of the app, once the proof spent the one before it. */
  nextCode?: string
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
        const body = route.request().postDataJSON() as { code: string }
        state.disables.push(body)
        if (state.nextCode && body.code === state.nextCode) {
          await route.fulfill({ status: 204 })
        } else if (state.signedInAgain) {
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

      if (path === "/api/v1/auth/reauthentication" && method === "POST") {
        state.proofs?.push(route.request().postDataJSON())
        state.signedInAgain = true
        await route.fulfill({ status: 204 })
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
async function staleDisableAsksInPlace(
  page: Page,
  api: HarnessApi,
  origin: string
) {
  const state: LifecycleServer = { disables: [], signedInAgain: false, proofs: [], nextCode: "111222" }
  await serveProfile(api, state)
  await openSecurityTab(page, origin)

  // The toggle for a user whose phone is gone, and back.
  await page.getByRole("button", { name: "Use a recovery code" }).click()
  await expect(disableCode(page, "Recovery code")).toBeVisible()
  await page.getByRole("button", { name: "Use an authenticator code" }).click()

  // What switching off does is said before it is done.
  await expect(
    page.getByText("Turning it off signs you out everywhere else")
  ).toBeVisible()

  await disableCode(page, "Verification code").fill("123456")
  await page.getByRole("button", { name: "Turn off the authenticator app" }).click()

  const dialog = page.getByRole("dialog", { name: "Confirm it's you" })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("You stay on this page.")
  expect(state.disables).toEqual([{ code: "123456", useRecoveryCode: false }])

  await dialog.getByLabel("Password", { exact: true }).fill("Harness1!")
  await dialog.getByLabel("Verification code", { exact: true }).fill("654321")
  await dialog.getByRole("button", { name: "Confirm", exact: true }).click()

  // The window closed and the person never left. The proof spent the app's time step, so the
  // code the page held can never pass: sending it would be counted and answered as a code someone
  // else used. So nothing is sent, and the person is asked for the next one.
  await expect(dialog).toHaveCount(0)
  await expect(page.getByText("A code works only once, so enter the next code from your app.")).toBeVisible()
  await expect(page).toHaveURL(`${origin}/profile?tab=security`)
  expect(state.proofs).toEqual([
    { password: "Harness1!", emailCode: null, secondStepCode: "654321", useRecoveryCode: false },
  ])
  expect(state.disables).toHaveLength(1)
  await expect(disableCode(page, "Verification code")).toHaveValue("")
  await expect(page.getByText(CODE_ALREADY_USED)).toHaveCount(0)

  await disableCode(page, "Verification code").fill("111222")
  await page.getByRole("button", { name: "Turn off the authenticator app" }).click()
  await expect(page.getByText("The authenticator app is off.")).toBeVisible()
  expect(state.disables).toEqual([
    { code: "123456", useRecoveryCode: false },
    { code: "111222", useRecoveryCode: false },
  ])
  expect(api.firstParty.logouts).toHaveLength(0)
}

test("x02: a stale disable proves the person where they stand, and goes on", async ({
  page,
  api,
}) => {
  await staleDisableAsksInPlace(page, api, ORIGINS.console)
})

test("x02: in the accounts app, the same profile asks in place too", async ({
  page,
  api,
}) => {
  await staleDisableAsksInPlace(page, api, ORIGINS.accounts)
})

test("x02: the window's way out signs out, and the sign-in returns to the security tab", async ({
  page,
  api,
}) => {
  const origin = ORIGINS.console
  const state: LifecycleServer = { disables: [], signedInAgain: false }
  await serveProfile(api, state)
  await openSecurityTab(page, origin)

  await disableCode(page, "Verification code").fill("123456")
  await page.getByRole("button", { name: "Turn off the authenticator app" }).click()

  const proof = page.getByRole("dialog", { name: "Confirm it's you" })
  await proof.getByRole("button", { name: "Can't confirm it here? Sign out and sign in again" }).click()
  expect(api.firstParty.logouts).toHaveLength(0)

  const signInAgain = page.getByRole("dialog", { name: "Sign in again to continue" })
  await expect(signInAgain).toContainText(
    "changing your 2-Step Verification needs a recent sign-in"
  )
  await signInAgain.getByRole("button", { name: "Sign in again" }).click()
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
})

test("x02: the window and the disable form read right to left in Arabic", async ({
  page,
  api,
}) => {
  const state: LifecycleServer = { disables: [], signedInAgain: false }
  await serveProfile(api, state, "ar")
  await openSecurityTab(page)

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await expect(page.getByText("تعطيلها يُنهي كل جلساتك الأخرى")).toBeVisible()

  await disableCode(page, "رمز التحقق").fill("123456")
  await page.getByRole("button", { name: "عطّل تطبيق المصادقة" }).click()

  const dialog = page.getByRole("dialog", { name: "أكّد أنك أنت" })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("ستبقى في هذه الصفحة")
  await expect(dialog.getByRole("button", { name: "تأكيد", exact: true })).toBeVisible()
  expect(
    await dialog.evaluate((element) => getComputedStyle(element).direction)
  ).toBe("rtl")

  // Cancel leaves everything as it was: still signed in, still on the tab.
  await dialog.getByRole("button", { name: "إلغاء" }).click()
  await expect(page.getByRole("dialog")).toHaveCount(0)
  await expect(page).toHaveURL(`${ORIGINS.console}/profile?tab=security`)
  expect(api.firstParty.logouts).toHaveLength(0)
})

/*
 * S08 PR B on the same harness: the recovery layer of an enabled factor. New
 * recovery codes and a new authenticator app each prove the factor once more in a
 * titled dialog and show the new codes once; a session that is not a recent
 * two-factor one opens the window that proves the person, with its reason; the security tab
 * says how many codes are left and warns while few are (AM-S08-1). And in the
 * console, the administrator's reset asks in a dialog whose accessible name is its
 * title, offered only with users:reset-two-factor.
 *
 * Deliberate breaks this part must catch: the reset dialog's title removed (no
 * accessible name), and the reset offered without the permission.
 */

const SECRET = {
  secret: "JBSWY3DPEHPK3PXP",
  qrCodeUri: "otpauth://totp/AuthSystem:isolated@example.test?secret=JBSWY3DPEHPK3PXP",
  manualEntryKey: "JBSW Y3DP EHPK 3PXP",
  emailCodeRequired: false,
}

interface RecoveryServer {
  remaining: number
  reauthenticate: boolean
  posts: { path: string; body: unknown }[]
}

const RECOVERY_ANSWERS: Record<string, unknown> = {
  "/api/v1/auth/2fa/recovery-codes": { recoveryCodes: ["NEW1-AAAA", "NEW2-BBBB"] },
  "/api/v1/auth/2fa/replace": SECRET,
  "/api/v1/auth/2fa/replace/confirm": { recoveryCodes: ["NEW3-CCCC"] },
}

async function serveRecoveryLayer(api: HarnessApi, state: RecoveryServer) {
  await api.useAuthenticated([], async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()

    if (path === "/api/v1/users/me" && method === "GET") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ ...PROFILE, preferredLanguage: "en" }),
      })
      return true
    }

    if (path === "/api/v1/auth/2fa/status" && method === "GET") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ recoveryCodesRemaining: state.remaining }),
      })
      return true
    }

    if (method === "POST" && path === "/api/v1/auth/reauthentication") {
      state.reauthenticate = false
      await route.fulfill({ status: 204 })
      return true
    }

    if (method === "POST" && path in RECOVERY_ANSWERS) {
      state.posts.push({ path, body: route.request().postDataJSON() })
      if (state.reauthenticate) {
        await problem(route, 403, "Auth.ReauthenticationRequired", REAUTHENTICATION_REQUIRED)
      } else {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify(RECOVERY_ANSWERS[path]),
        })
      }
      return true
    }

    return false
  })
}

test("s08: new recovery codes from the warning, in a titled dialog, shown once", async ({
  page,
  api,
}) => {
  const state: RecoveryServer = { remaining: 2, reauthenticate: false, posts: [] }
  await serveRecoveryLayer(api, state)
  await openSecurityTab(page)

  await expect(page.getByText("On. Signing in asks for a six-digit code from the authenticator app on your phone: the safer of the two methods. Recovery codes left: 2.")).toBeVisible()
  const warning = page.getByRole("alert").filter({ hasText: "Few recovery codes left" })
  await expect(warning).toBeVisible()
  await warning.getByRole("button", { name: "Generate new codes" }).click()

  const dialog = page.getByRole("dialog", { name: "Generate new recovery codes" })
  await expect(dialog).toBeVisible()
  await dialog.getByLabel("Verification code", { exact: true }).fill("123456")
  await dialog.getByRole("button", { name: "Generate new codes" }).click()

  await expect(page.getByRole("dialog", { name: "Save your recovery codes" })).toBeVisible()
  expect(state.posts).toEqual([
    { path: "/api/v1/auth/2fa/recovery-codes", body: { code: "123456", useRecoveryCode: false } },
  ])
})

test("s08: a new authenticator app — prove, scan, confirm", async ({ page, api }) => {
  const state: RecoveryServer = { remaining: 8, reauthenticate: false, posts: [] }
  await serveRecoveryLayer(api, state)
  await openSecurityTab(page)

  await expect(page.getByText("On. Signing in asks for a six-digit code from the authenticator app on your phone: the safer of the two methods. Recovery codes left: 8.")).toBeVisible()
  await expect(page.getByRole("alert")).toHaveCount(0)
  await page.getByRole("button", { name: "Replace authenticator app" }).click()
  const dialog = page.getByRole("dialog", { name: "Replace your authenticator app" })
  await expect(dialog).toBeVisible()

  // A recovery code proves the factor when the phone is gone.
  await dialog.getByRole("button", { name: "Use a recovery code" }).click()
  await dialog.getByLabel("Recovery code", { exact: true }).fill("ABCD-1234")
  await dialog.getByRole("button", { name: "Next" }).click()

  await expect(dialog.getByLabel("Manual entry key")).toHaveValue(SECRET.manualEntryKey)
  await dialog.getByLabel("Verification code", { exact: true }).fill("654321")
  await dialog.getByRole("button", { name: "Confirm new app" }).click()

  await expect(page.getByRole("dialog", { name: "Save your recovery codes" })).toBeVisible()
  expect(state.posts.map((post) => post.path)).toEqual([
    "/api/v1/auth/2fa/replace",
    "/api/v1/auth/2fa/replace/confirm",
  ])
  expect(state.posts[0].body).toEqual({ code: "ABCD-1234", useRecoveryCode: true })
  expect(state.posts[1].body).toEqual({ code: "654321" })
})

test("s08: a session that is not a recent two-factor one proves the person in place, and is told why", async ({
  page,
  api,
}) => {
  const state: RecoveryServer = { remaining: 6, reauthenticate: true, posts: [] }
  await serveRecoveryLayer(api, state)
  await openSecurityTab(page)

  await page.getByRole("button", { name: "Generate new codes" }).click()
  const dialog = page.getByRole("dialog", { name: "Generate new recovery codes" })
  await dialog.getByLabel("Verification code", { exact: true }).fill("123456")
  await dialog.getByRole("button", { name: "Generate new codes" }).click()

  const proof = page.getByRole("dialog", { name: "Confirm it's you" })
  await expect(proof).toBeVisible()
  await expect(proof).toContainText(
    "This needs you to confirm it's you with your authenticator app or a recovery code"
  )
  await expect(page.getByRole("dialog", { name: "Generate new recovery codes" })).toHaveCount(0)

  // Given, the dialog opens again, empty, and the page behind it still answers a click: one
  // dialog closing as another opens in the same tick must not leave the page dead.
  await proof.getByLabel("Password", { exact: true }).fill("Harness1!")
  await proof.getByLabel("Verification code", { exact: true }).fill("654321")
  await proof.getByRole("button", { name: "Confirm", exact: true }).click()
  const again = page.getByRole("dialog", { name: "Generate new recovery codes" })
  await expect(again).toBeVisible()
  await expect(again.getByLabel("Verification code", { exact: true })).toHaveValue("")
  await expect(proof).toHaveCount(0)
  await again.getByRole("button", { name: "Cancel" }).click()
  await expect(again).toHaveCount(0)
  expect(await page.evaluate(() => document.body.style.pointerEvents)).not.toBe("none")
  await page.getByRole("button", { name: "Generate new codes" }).click()
  await expect(again).toBeVisible()
})

test("s08: after a sign-in with a recovery code, the security tab says so once", async ({
  page,
  api,
}) => {
  const verifies: unknown[] = []
  await api.useAuthenticated(
    [],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()

      if (path === "/api/v1/auth/login" && method === "POST") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ twoFactorChallengeToken: "harness-challenge", requiresTwoFactor: true }),
        })
        return true
      }

      if (path === "/api/v1/auth/2fa/verify" && method === "POST") {
        verifies.push(route.request().postDataJSON())
        await api.firstParty.answerSignIn(route, SIGNED_IN_USER, "s08-recovery-sign-in")
        return true
      }

      if (path === "/api/v1/users/me" && method === "GET") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ ...PROFILE, preferredLanguage: "en" }),
        })
        return true
      }

      if (path === "/api/v1/auth/2fa/status" && method === "GET") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ recoveryCodesRemaining: 8 }),
        })
        return true
      }

      return false
    },
    { session: "none" }
  )

  await page.goto(`${ORIGINS.console}/login`)
  await page.locator('input[type="email"]').fill(PROFILE.email)
  await page.locator('input[type="password"]').fill("Harness1!")
  await page.locator('button[type="submit"]').click()
  await expect(page).toHaveURL(/\/two-factor$/)

  await page.getByRole("button", { name: "Use a recovery code" }).click()
  await page.getByLabel("Recovery code", { exact: true }).fill("ABCD-1234")
  await page.getByRole("button", { name: "Verify", exact: true }).click()
  await expect.poll(() => verifies.length).toBe(1)
  expect(verifies[0]).toEqual(expect.objectContaining({ code: "ABCD-1234", useRecoveryCode: true }))
  await expect(page).not.toHaveURL(/\/two-factor$/)

  // Eight codes left, so the only warning is the one the sign-in left.
  await openSecurityTab(page)
  await expect(page.getByText("On. Signing in asks for a six-digit code from the authenticator app on your phone: the safer of the two methods. Recovery codes left: 8.")).toBeVisible()
  const notice = page.getByRole("alert").filter({ hasText: "You signed in with a recovery code" })
  await expect(notice).toBeVisible()
  await expect(notice.getByRole("button", { name: "Generate new codes" })).toBeVisible()

  // Once: the next visit shows the count and no notice.
  await page.reload()
  await expect(page.getByText("On. Signing in asks for a six-digit code from the authenticator app on your phone: the safer of the two methods. Recovery codes left: 8.")).toBeVisible()
  await expect(page.getByRole("alert")).toHaveCount(0)
})

const TARGET = {
  id: "77777777-7777-7777-7777-777777777777",
  email: "lost.phone@example.test",
  displayName: "Lost Phone",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: true,
  createdAt: "2026-08-20T07:00:00Z",
}

async function serveUser(api: HarnessApi, permissions: string[], resets: string[]) {
  await api.useAuthenticated(permissions, async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (path === `/api/v1/users/${TARGET.id}` && method === "GET") {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(TARGET) })
      return true
    }
    if (path === `/api/v1/users/${TARGET.id}/two-factor/reset` && method === "POST") {
      resets.push(path)
      await route.fulfill({ status: 204 })
      return true
    }
    return false
  })
}

test("s08: the console's reset asks in a dialog named by its title", async ({ page, api }) => {
  const resets: string[] = []
  await serveUser(api, ["users:read", "users:reset-two-factor"], resets)
  await page.goto(`${ORIGINS.console}/users/${TARGET.id}`)

  await page.getByRole("button", { name: "Reset two-factor", exact: true }).click()
  const dialog = page.getByRole("alertdialog", { name: "Reset two-factor authentication?" })
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(TARGET.displayName)
  await dialog.getByRole("button", { name: "Reset", exact: true }).click()

  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  expect(resets).toHaveLength(1)
})

test("s08: without users:reset-two-factor the console offers no reset", async ({ page, api }) => {
  await serveUser(api, ["users:read"], [])
  await page.goto(`${ORIGINS.console}/users/${TARGET.id}`)

  await expect(page.getByText(TARGET.email).first()).toBeVisible()
  await expect(page.getByRole("button", { name: "Reset two-factor", exact: true })).toHaveCount(0)
})
