import AxeBuilder from "@axe-core/playwright"
import type { Page, Route } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * S24 PR B in a real browser: the emailed code as the second factor, on the S30a
 * harness, so every page runs under its app's real web.config CSP and the automatic
 * csp fixture fails any test on a violation beyond the register.
 *
 * What these prove and what they do not: the API host is the harness model, not the
 * real API. That the server mints, hashes, caps, counts and spends the codes, and
 * switches the factors in one transaction, is pinned by the xUnit suite and by the
 * run on SQL Server through the API (the PR's table). What is proven here is what
 * only a built bundle in a browser decides: that sign-in sends a code only when
 * asked, with the resend after its cooldown; that no resend is offered once none can
 * succeed, and the way out keeps the relying party's request; that the profile's two
 * cards bind and switch off through titled dialogs; that the recovery page takes a
 * recovery code; and that the screens pass axe in both themes and both directions.
 *
 * Deliberate break this file must catch: the «Email me a code» button hidden.
 */

const USER = {
  id: "77777777-7777-7777-7777-777777777777",
  email: "owner@example.test",
  firstName: "Email",
  lastName: "Owner",
  hasPassword: true,
  emailConfirmed: true,
  phoneConfirmed: false,
  timeZone: "UTC",
}

const MASKED = "o***r@example.test"
const RIGHT = "246810"
const RETURN_TO = `${ORIGINS.api}/api/v1/auth/authorize?client_id=CRM&redirect_uri=${encodeURIComponent("https://crm.example.com/callback")}&response_type=code`

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) })
}

async function problem(route: Route, status: number, code: string, detail: string) {
  await route.fulfill({
    status,
    contentType: "application/problem+json",
    body: JSON.stringify({ title: "Refused", status, code, detail }),
  })
}

const sent = (sendsRemaining: number) => ({
  expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
  maskedEmail: MASKED,
  sendsRemaining,
})

interface SignInServer {
  sends: number
  verifies: unknown[]
}

/**
 * A password sign-in that the emailed code completes. `sentWithChallenge` is what
 * the server's own send left (D-243: the code goes out with the challenge), or null
 * for a challenge that came without one (a refused send, an older server).
 */
async function serveSignIn(
  api: HarnessApi,
  state: SignInServer,
  sendsLeft: number[],
  sentWithChallenge: number | null = null
) {
  await api.useAnonymous(async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (path === "/api/v1/auth/login" && method === "POST") {
      await json(route, {
        requiresTwoFactor: true,
        twoFactorChallengeToken: "harness-challenge",
        twoFactorMethods: ["email", "recovery"],
        ...(sentWithChallenge === null ? {} : { twoFactorEmailCode: sent(sentWithChallenge) }),
      })
      return true
    }
    if (path === "/api/v1/auth/2fa/verify/email-code" && method === "POST") {
      state.sends += 1
      await json(route, sent(sendsLeft[Math.min(state.sends, sendsLeft.length) - 1]))
      return true
    }
    if (path === "/api/v1/auth/2fa/verify" && method === "POST") {
      state.verifies.push(route.request().postDataJSON())
      await problem(route, 400, "User.InvalidTwoFactorCode", "Invalid two-factor authentication code.")
      return true
    }
    return false
  })
}

async function signIn(page: Page, search = "") {
  await page.goto(`${ORIGINS.accounts}/login${search}`)
  await page.locator('input[type="email"]').fill(USER.email, { timeout: 60_000 })
  await page.locator('input[type="password"]').fill("Harness-Password-1!")
  await page.locator('input[type="password"]').press("Enter")
  await expect(page).toHaveURL(/\/two-factor(\?|$)/)
}

test("oi-243: the emailed code arrives with the challenge, the page asks for it at once, and the resend waits", async ({ page, api }) => {
  const state: SignInServer = { sends: 0, verifies: [] }
  await serveSignIn(api, state, [1, 0], 2)

  await signIn(page)

  // The server sent the code with the challenge (D-243): the page sends nothing
  // itself, says where the code went, and offers the six boxes straight away.
  await expect(page.getByText(`We sent a code to ${MASKED}.`).first()).toBeVisible()
  await expect(page.locator('[data-slot="input-otp"]')).toBeVisible()
  await expect(page.getByRole("button", { name: "Email me a code" })).toHaveCount(0)
  await expect(page.getByRole("button", { name: /Resend in/ })).toBeDisabled()
  await page.waitForTimeout(500)
  expect(state.sends).toBe(0)

  await page.locator('[data-slot="input-otp"]').click()
  await page.keyboard.type(RIGHT)
  await expect.poll(() => state.verifies.length).toBe(1)
  expect(state.verifies[0]).toMatchObject({ challengeToken: "harness-challenge", code: RIGHT, method: "email" })
})

test("s24: a challenge that came without a code offers the button, and its resend waits", async ({ page, api }) => {
  const state: SignInServer = { sends: 0, verifies: [] }
  await serveSignIn(api, state, [2, 1, 0])

  await signIn(page)

  // No code came with the challenge: nothing is sent on arrival, and no code is
  // asked for yet.
  const emailMe = page.getByRole("button", { name: "Email me a code" })
  await expect(emailMe).toBeVisible()
  await page.waitForTimeout(500)
  expect(state.sends).toBe(0)

  await emailMe.click()
  await expect.poll(() => state.sends).toBe(1)
  await expect(page.getByText(`We sent a code to ${MASKED}.`).first()).toBeVisible()
  await expect(page.getByText("Only the newest code works")).toBeVisible()
  // The resend waits out its cooldown.
  await expect(page.getByRole("button", { name: /Resend in/ })).toBeDisabled()

  await page.locator('[data-slot="input-otp"]').click()
  await page.keyboard.type(RIGHT)
  await expect.poll(() => state.verifies.length).toBe(1)
  expect(state.verifies[0]).toMatchObject({ challengeToken: "harness-challenge", code: RIGHT, method: "email" })
})

test("s24: with no send left, no resend is offered, and starting again keeps the relying party's request", async ({ page, api }) => {
  const state: SignInServer = { sends: 0, verifies: [] }
  await serveSignIn(api, state, [0])

  await signIn(page, `?returnTo=${encodeURIComponent(RETURN_TO)}`)
  await page.getByRole("button", { name: "Email me a code" }).click()

  await expect(page.getByText("No more codes can be emailed for this sign-in.")).toBeVisible()
  await expect(page.getByRole("button", { name: /Resend code/ })).toHaveCount(0)
  const again = page.getByRole("link", { name: "Start sign-in again" })
  await expect(again).toBeVisible()
  const href = new URL(await again.evaluate((a) => (a as HTMLAnchorElement).href))
  expect(href.pathname).toBe("/login")
  expect(href.searchParams.get("returnTo")).toBe(RETURN_TO)
})

/** The profile's security tab for an account in either state. */
async function serveProfile(api: HarnessApi, emailCodeEnabled: boolean, calls: { path: string; body: unknown }[]) {
  await api.useAuthenticated([], async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (path === "/api/v1/users/me" && method === "GET") {
      await json(route, {
        ...USER,
        preferredLanguage: "en",
        twoFactorEnabled: emailCodeEnabled,
        totpEnabled: false,
        emailCodeEnabled,
        qualifyingMfa: false,
        emailCodeAvailable: true,
      })
      return true
    }
    if (path.startsWith("/api/v1/auth/2fa/email/") && method === "POST") {
      const body = route.request().postDataJSON() ?? {}
      calls.push({ path, body })
      if (path.endsWith("/enable")) await json(route, sent(2))
      else if (path.endsWith("/confirm")) await json(route, { recoveryCodes: ["AAAA-BBBB", "CCCC-DDDD"] })
      else if ((body as { code?: string }).code) await route.fulfill({ status: 204 })
      else await json(route, sent(2), 202)
      return true
    }
    return false
  })
}

test("s24: the profile binds the emailed code through a titled dialog and shows the recovery codes", async ({ page, api }) => {
  const calls: { path: string; body: unknown }[] = []
  await serveProfile(api, false, calls)
  await page.goto(`${ORIGINS.console}/profile?tab=security`)

  await page.getByRole("button", { name: "Turn on code by email" }).click({ timeout: 60_000 })
  const dialog = page.getByRole("alertdialog", { name: "Enter the code we emailed you" })
  await expect(dialog).toBeVisible()
  await dialog.getByLabel("Code from the email").fill(RIGHT)
  await dialog.getByRole("button", { name: "Verify" }).click()

  await expect(page.getByRole("dialog", { name: "Save your recovery codes" })).toBeVisible()
  expect(calls.map((c) => c.path)).toEqual(["/api/v1/auth/2fa/email/enable", "/api/v1/auth/2fa/email/confirm"])
  expect(calls[1].body).toEqual({ code: RIGHT })
})

test("s24: an emailed-code account: the app card offers set-up, never a switch-off; the email card switches off with a code", async ({ page, api }) => {
  const calls: { path: string; body: unknown }[] = []
  await serveProfile(api, true, calls)
  await page.goto(`${ORIGINS.console}/profile?tab=security`)

  await expect(page.getByRole("button", { name: "Turn off code by email" })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByRole("button", { name: "Turn off the authenticator app" })).toHaveCount(0)
  await expect(page.getByRole("button", { name: "Turn on the authenticator app" })).toBeVisible()

  await page.getByRole("button", { name: "Turn off code by email" }).click()
  const dialog = page.getByRole("alertdialog", { name: "Turn off code by email?" })
  await expect(dialog).toContainText("your other sessions will be signed out")
  await dialog.getByRole("button", { name: "Email me a code" }).click()
  await dialog.getByLabel("Code from the email").fill(RIGHT)
  await dialog.getByRole("button", { name: "Turn off code by email" }).click()

  await expect.poll(() => calls.length).toBe(2)
  expect(calls).toEqual([
    { path: "/api/v1/auth/2fa/email/disable", body: {} },
    { path: "/api/v1/auth/2fa/email/disable", body: { code: RIGHT, useRecoveryCode: false } },
  ])
})

test("s24: an account waiting for deletion recovers with a recovery code (XXXX-XXXX)", async ({ page, api }) => {
  const recovers: Record<string, unknown>[] = []
  await api.useAnonymous(async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/deletion/recover" && route.request().method() === "POST") {
      const body = route.request().postDataJSON() as Record<string, unknown>
      recovers.push(body)
      if (!body.twoFactorCode) {
        await problem(route, 400, "TwoFactor.RecoveryEmailCodeRequired",
          "Enter the code sent to your email address, or one of your recovery codes. Ask for a code first if you have none.")
      } else {
        await problem(route, 400, "User.InvalidTwoFactorCode", "Invalid two-factor authentication code.")
      }
      return true
    }
    return false
  })

  await page.goto(`${ORIGINS.accounts}/account-recovery`)
  await page.locator('input[type="email"]').fill(USER.email, { timeout: 60_000 })
  await page.locator('input[type="password"]').fill("Harness-Password-1!")
  await page.getByRole("button", { name: /Restore/ }).click()

  // The emailed code's account: the field takes a recovery code too, and the
  // code is sent only on request.
  await expect(page.getByRole("button", { name: "Email me a code" })).toBeVisible()
  const code = page.getByLabel(/code/i).last()
  await expect(code).not.toHaveAttribute("inputmode", "numeric")
  await code.fill("ABCD-EFGH")
  await page.getByRole("button", { name: /Restore/ }).click()

  await expect.poll(() => recovers.length).toBe(2)
  expect(recovers[1]).toMatchObject({ twoFactorCode: "ABCD-EFGH" })
})

test("s24: account recovery offers no resend once no code can be emailed (OI-220 (1))", async ({ page, api }) => {
  await api.useAnonymous(async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/deletion/recover" && route.request().method() === "POST") {
      const body = route.request().postDataJSON() as Record<string, unknown>
      if (body.sendEmailCode) await json(route, sent(0), 202)
      else await problem(route, 400, "TwoFactor.RecoveryEmailCodeRequired",
        "Enter the code sent to your email address, or one of your recovery codes. Ask for a code first if you have none.")
      return true
    }
    return false
  })

  await page.goto(`${ORIGINS.accounts}/account-recovery`)
  await page.locator('input[type="email"]').fill(USER.email, { timeout: 60_000 })
  await page.locator('input[type="password"]').fill("Harness-Password-1!")
  await page.getByRole("button", { name: /Restore/ }).click()
  await page.getByRole("button", { name: "Email me a code" }).click()

  await expect(page.getByText("Only the newest code works")).toBeVisible()
  await expect(page.getByRole("button", { name: /Resend/ })).toHaveCount(0)
  await expect(page.getByRole("button", { name: "Email me a code" })).toHaveCount(0)
})

const AXE_CASES = [
  { language: "en", theme: "light" },
  { language: "en", theme: "dark" },
  { language: "ar", theme: "light" },
  { language: "ar", theme: "dark" },
] as const

// The two gaps the isolated accessibility suite registers (e2e/isolated/
// accessibility.spec.ts).
const KNOWN_PALETTE_GAPS = [
  { fg: "#737373", bg: "#f5f5f5" },
  { fg: "#e7000b", bg: "#fde6e7" },
]

for (const { language, theme } of AXE_CASES) {
  test(`s24: the emailed-code sign-in step passes axe (${language}, ${theme})`, async ({ page, api }) => {
    const state: SignInServer = { sends: 0, verifies: [] }
    await serveSignIn(api, state, [2])
    await page.addInitScript(([lang, mode]) => {
      localStorage.setItem("auth.language", lang)
      localStorage.setItem("theme", mode)
    }, [language, theme])

    await signIn(page)
    await page.locator("button").filter({ hasText: language === "ar" ? "أرسل لي رمزًا بالبريد" : "Email me a code" }).click()
    await expect.poll(() => state.sends).toBe(1)
    await expect(page.locator("html")).toHaveAttribute("dir", language === "ar" ? "rtl" : "ltr")

    // The shared toaster is left out: its "code sent" toast is still animating
    // when the page is scanned, so its sampled colours differ run to run. At rest
    // its success colours measure 3.16:1 (#2fa155 on #f0fdf5) on every success toast
    // of both apps — a finding reported in the PR, not this item's to restyle.
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa"])
      .exclude("[data-sonner-toaster]")
      .analyze()
    const actionable = results.violations
      .map((violation) =>
        violation.id === "color-contrast"
          ? {
              ...violation,
              nodes: violation.nodes.filter((node) => {
                const data = node.any[0]?.data as { fgColor?: string; bgColor?: string } | undefined
                return !KNOWN_PALETTE_GAPS.some(
                  (gap) => gap.fg === data?.fgColor?.toLowerCase() && gap.bg === data?.bgColor?.toLowerCase()
                )
              }),
            }
          : violation
      )
      .filter((violation) => violation.nodes.length > 0)
    expect(
      actionable.flatMap((v) =>
        v.nodes.map((n) => `${v.id}: ${n.target.join(" ")} ${JSON.stringify(n.any[0]?.data ?? null)}`)
      )
    ).toEqual([])
  })
}
