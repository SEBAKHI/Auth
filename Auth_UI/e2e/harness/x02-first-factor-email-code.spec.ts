import type { Page, Route } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * X02 PR B in a real browser: an account turning on its FIRST second factor while
 * email is on also enters a code emailed to its confirmed address. On the S30a
 * harness, so every page runs under its app's real web.config CSP and the
 * automatic csp fixture fails any test on a violation beyond the register.
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. That the server mints, hashes, caps and spends the code, and
 * checks it under the pending factor's five-failure lock, is pinned by the xUnit
 * suite (FirstFactorEmailProofTests, SendTwoFactorEmailCodeCommandHandlerTests,
 * TwoFactorStateStoreSqlTests). What is proven here is what only a built bundle in
 * a browser decides: that setup's emailCodeRequired shows the step; that the send
 * names the masked address the code went to; that a wrong code is answered with
 * the server's own sentence while the step stays; that "Send a new code" works;
 * that the enable body carries the emailed code; that the recovery codes follow;
 * and that it all reads right to left in Arabic.
 *
 * Deliberate break this file must catch: emailCode dropped from the enable body
 * (the recorded bodies no longer carry it).
 */

const PROFILE = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "isolated@example.test",
  firstName: "Isolated",
  lastName: "Operator",
  hasPassword: true,
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  timeZone: "UTC",
}

const SETUP = {
  secret: "JBSWY3DPEHPK3PXP",
  qrCodeUri:
    "otpauth://totp/Harness:isolated%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=Harness",
  manualEntryKey: "JBSW Y3DP EHPK 3PXP",
  emailCodeRequired: true,
}

const MASKED = "i****d@example.test"
const RIGHT_EMAIL_CODE = "654321"

/** The English sentence of TwoFactor.EmailCodeInvalid (DomainErrors.resx). */
const EMAIL_CODE_INVALID =
  "The email code is incorrect or is no longer valid. Send a new code and try again."

interface BindServer {
  sends: number
  enables: unknown[]
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

async function json(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  })
}

/**
 * An account without a second factor: setup asks for the email step; every send
 * answers with the masked address and a 15-minute expiry; enable refuses a wrong
 * emailed code with the server's sentence and accepts the right one.
 */
async function serveProfile(
  api: HarnessApi,
  state: BindServer,
  preferredLanguage = "en"
) {
  await api.useAuthenticated(
    [],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()

      if (path === "/api/v1/users/me" && method === "GET") {
        await json(route, { ...PROFILE, preferredLanguage })
        return true
      }

      if (path === "/api/v1/auth/2fa/setup" && method === "POST") {
        await json(route, SETUP)
        return true
      }

      if (path === "/api/v1/auth/2fa/email-code" && method === "POST") {
        state.sends += 1
        await json(route, {
          emailCodeRequired: true,
          sentTo: MASKED,
          expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
        })
        return true
      }

      if (path === "/api/v1/auth/2fa/enable" && method === "POST") {
        const body = route.request().postDataJSON() as { emailCode?: string }
        state.enables.push(body)
        if (body.emailCode === RIGHT_EMAIL_CODE) {
          await json(route, { recoveryCodes: ["AAAA-1111", "BBBB-2222"] })
        } else {
          await problem(
            route,
            400,
            "TwoFactor.EmailCodeInvalid",
            EMAIL_CODE_INVALID
          )
        }
        return true
      }

      return false
    },
    { preferredLanguage }
  )
}

const field = (page: Page, label: string) =>
  page.getByLabel(label, { exact: true })

async function openSecurityTab(page: Page, origin: string = ORIGINS.console) {
  await page.goto(`${origin}/profile?tab=security`)
}

test("x02: first factor asks for the email code", async ({ page, api }) => {
  const state: BindServer = { sends: 0, enables: [] }
  await serveProfile(api, state)
  await openSecurityTab(page)

  await page.getByRole("button", { name: "Enable two-factor" }).click()

  // The step is there before anything is sent, and says why.
  await expect(field(page, "Code from your email")).toBeVisible()
  await expect(
    page.getByText("Because this is your first second factor")
  ).toBeVisible()
  await field(page, "Verification code").fill("123456")
  await expect(page.getByRole("button", { name: "Verify" })).toBeDisabled()

  // Sent: the address it went to, masked, and when it dies.
  await page.getByRole("button", { name: "Send code" }).click()
  await expect(
    page.getByText(`We sent a code to ${MASKED}. It expires in 15 minutes.`)
  ).toBeVisible()
  expect(state.sends).toBe(1)

  // A wrong code: the server's own sentence, and the step stays.
  await field(page, "Code from your email").fill("111111")
  await page.getByRole("button", { name: "Verify" }).click()
  await expect(page.getByText(EMAIL_CODE_INVALID)).toBeVisible()
  await expect(field(page, "Code from your email")).toBeVisible()

  // A new code, then the right one: the factor is on and the codes are shown.
  await page.getByRole("button", { name: "Send a new code" }).click()
  await expect.poll(() => state.sends).toBe(2)
  await field(page, "Code from your email").fill(RIGHT_EMAIL_CODE)
  await page.getByRole("button", { name: "Verify" }).click()
  await expect(
    page.getByRole("dialog", { name: "Save your recovery codes" })
  ).toBeVisible()

  expect(state.enables).toEqual([
    { code: "123456", emailCode: "111111" },
    { code: "123456", emailCode: RIGHT_EMAIL_CODE },
  ])
})

test("x02: the email step reads right to left in Arabic", async ({
  page,
  api,
}) => {
  const state: BindServer = { sends: 0, enables: [] }
  await serveProfile(api, state, "ar")
  await openSecurityTab(page)

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await page.getByRole("button", { name: "تفعيل المصادقة الثنائية" }).click()
  await expect(field(page, "الرمز المرسَل إلى بريدك")).toBeVisible()

  await page.getByRole("button", { name: "إرسال الرمز" }).click()
  const sent = page.getByText(`أرسلنا رمزًا إلى ${MASKED}.`)
  await expect(sent).toBeVisible()
  // The masked address sits in an Arabic sentence with no direction forced on it.
  await expect(sent).not.toHaveAttribute("dir")
  expect(
    await sent.evaluate((element) => getComputedStyle(element).direction)
  ).toBe("rtl")
  await expect(
    page.getByRole("button", { name: "إرسال رمز جديد" })
  ).toBeVisible()
})
