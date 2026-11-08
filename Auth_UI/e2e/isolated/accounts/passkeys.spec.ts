import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { WCAG_TAGS, actionable, describeViolations } from "../axe-known-gaps"
import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * S20 PR C on the accounts app, the page relying parties send people to: the
 * passkey button while passkeys run, nothing while their options answer 404 (as
 * shipped), and the recovery page's passkey choice. The browser part of the
 * ceremony is the harness's (`e2e/harness/s20-passkeys.spec.ts`); here, what a
 * stranger sees, in both themes and both directions.
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
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() !== OPTIONS_PATH) return false
    if (passkeys) await fulfillJson(route, PASSKEY_OPTIONS)
    else await fulfillJson(route, { title: "Not Found" }, 404)
    return true
  })
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

test("the sign-in page offers the passkey under the sign-in button while passkeys run", async ({ page }) => {
  await install(page, { passkeys: true })

  await page.goto("/login")

  const passkey = page.getByTestId("passkey-sign-in").getByRole("button")
  await expect(passkey).toBeEnabled()
  await expect(passkey).toHaveText(/sign in with a passkey/i)
  const signIn = page.getByRole("button", { name: "Sign in", exact: true })
  const below = await signIn.evaluate(
    (submit, other) => Boolean(submit.compareDocumentPosition(other as Node) & Node.DOCUMENT_POSITION_FOLLOWING),
    await passkey.elementHandle()
  )
  expect(below).toBe(true)
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
      await page.goto("/login")
      await expect(page.getByTestId("passkey-sign-in").getByRole("button")).toBeEnabled()

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
