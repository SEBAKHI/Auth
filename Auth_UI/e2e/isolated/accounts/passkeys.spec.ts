import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { WCAG_TAGS, actionable, describeViolations } from "../axe-known-gaps"
import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * S20 PR C on the accounts app, the page relying parties send people to, as
 * OI-280 PR A changed it: no passkey button for every visitor while passkeys
 * run (D-S20-11) — the address first, and the passkey screen for an address that
 * has one (the isolated accessibility spec and the harness walk it) — nothing
 * while their options answer 404 (as shipped), and the recovery page's passkey
 * choice. The browser part of the ceremony is the harness's
 * (`e2e/harness/s20-passkeys.spec.ts`); here, what a stranger sees, in both
 * themes and both directions.
 *
 * Deliberate break, verified red: the button for every visitor brought back.
 */

const OPTIONS_PATH = "/api/v1/auth/passkeys/assertion/options"

const PASSKEY_OPTIONS = {
  options: {
    challenge: "c2lnbi1pbi1jaGFsbGVuZ2U",
    rpId: "localhost",
    allowCredentials: [],
    userVerification: "required",
    timeout: 300_000,
  },
  expiresAt: "2099-01-01T00:00:00Z",
}

async function install(
  page: Page,
  { passkeys, theme = "light", language = "en" }: { passkeys: boolean; theme?: "light" | "dark"; language?: string }
) {
  await page.addInitScript(
    ([themeValue, languageValue]) => {
      localStorage.setItem("theme", themeValue)
      localStorage.setItem("auth.language", languageValue)
    },
    [theme, language] as const
  )
  await installAnonymousApi(
    page,
    async (route, url) => {
      if (url.pathname.toLowerCase() !== OPTIONS_PATH) return false
      if (passkeys) await fulfillJson(route, PASSKEY_OPTIONS)
      else await fulfillJson(route, { title: "Not Found" }, 404)
      return true
    },
    // OI-280: the deployment's hint, as the API sends it while passkeys run.
    { branding: passkeys ? { passkeySignInOffered: true } : undefined }
  )
}

test("the sign-in page shows no passkey control while passkeys do not run", async ({ page }) => {
  await install(page, { passkeys: false })
  const answered = page.waitForResponse((response) => response.url().endsWith(OPTIONS_PATH))

  await page.goto("/login")
  await answered

  await expect(page.getByRole("button", { name: "Sign in", exact: true })).toBeVisible()
  await expect(page.getByTestId("passkey-sign-in")).toHaveCount(0)
  await expect(page.getByLabel("Email")).toHaveAttribute("autocomplete", "username")
})

test("the sign-in page shows no passkey button to every visitor while passkeys run: the address first", async ({
  page,
}) => {
  await install(page, { passkeys: true })
  const answered = page.waitForResponse((response) =>
    response.url().endsWith(OPTIONS_PATH)
  )

  await page.goto("/login")
  await answered
  // Ready: the options answered 200 and the passkey helper came, so the address
  // field offers the passkeys. A passkey button for every visitor would be on the
  // screen by now (the same render), so the absence checks below can fail.
  await expect(page.getByLabel("Email")).toHaveAttribute(
    "autocomplete",
    "username webauthn"
  )

  // The address and "Continue", the password on the next step (D-S20-11, T-A5).
  await expect(
    page.getByRole("button", { name: "Continue", exact: true })
  ).toBeVisible()
  await expect(page.getByLabel("Password")).toHaveCount(0)
  await expect(page.getByTestId("passkey-sign-in")).toHaveCount(0)
  await expect(page.getByRole("button", { name: /passkey/i })).toHaveCount(0)
})

test("the recovery page offers to cancel the deletion with a passkey while passkeys run", async ({ page }) => {
  await install(page, { passkeys: true })

  await page.goto("/account-recovery")

  await expect(
    page.getByTestId("passkey-sign-in").getByRole("button", { name: /cancel the deletion with your passkey/i })
  ).toBeEnabled()
})

for (const theme of ["light", "dark"] as const) {
  for (const language of ["en", "ar"] as const) {
    test(`the sign-in page with passkeys passes WCAG AA in ${theme} ${language}`, async ({ page }) => {
      await install(page, { passkeys: true, theme, language })
      const answered = page.waitForResponse((response) =>
        response.url().endsWith(OPTIONS_PATH)
      )
      await page.goto("/login")
      await answered
      await expect(page.getByRole("textbox").first()).toBeVisible()

      const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()

      expect(actionable(results), describeViolations(results)).toEqual([])
    })

    test(`the recovery page with its passkey choice passes WCAG AA in ${theme} ${language}`, async ({ page }) => {
      await install(page, { passkeys: true, theme, language })
      await page.goto("/account-recovery")
      await expect(page.getByTestId("passkey-sign-in").getByRole("button")).toBeEnabled()

      const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()

      expect(actionable(results), describeViolations(results)).toEqual([])
    })
  }
}
