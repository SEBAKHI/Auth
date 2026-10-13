import { readFileSync } from "node:fs"
import { dirname, resolve } from "node:path"
import { fileURLToPath } from "node:url"

import { ORIGINS, expect, test } from "./fixtures"

/**
 * X12 PR B in a real browser: a second factor at the consecutive-failure limit
 * answers every code with `TwoFactor.LockedAdminResetRequired`, and the accounts
 * app's two-factor page then stops asking for a code and keeps the server's own
 * sentence on screen, in the reader's language, with the first way out it names:
 * the forgot-password link. A toast that faded over a field that still took codes
 * would be a dead end — no code will ever be accepted again.
 *
 * On the S30a harness: the production bundle under its real web.config CSP; the
 * API host is the harness model answering with the sentence the API would send,
 * read from DomainErrors.resx and DomainErrors.ar.resx by Accept-Language. That
 * the API refuses with this code at the limit is the xUnit suite's
 * (TwoFactorHardLockTests); what only a built bundle in a browser decides is here.
 *
 * Deliberate break: drop the LockedAdminResetRequired branch from
 * two-factor-verify.tsx (the sentence becomes a toast and the field stays on).
 */

const CODE = "TwoFactor.LockedAdminResetRequired"

const here = dirname(fileURLToPath(import.meta.url))
const errorsDir = resolve(here, "../../../Auth/Auth_Localization/Resources/Errors")

function catalogSentence(file: string): string {
  const resx = readFileSync(resolve(errorsDir, file), "utf8")
  const match = new RegExp(
    `<data name="${CODE.replace(".", "\\.")}" xml:space="preserve">\\s*<value>([^<]+)</value>`
  ).exec(resx)
  if (!match) throw new Error(`${file} has no entry for ${CODE}`)
  return match[1]
}

const LANGUAGES = [
  {
    code: "en",
    sentence: catalogSentence("DomainErrors.resx"),
    title: "Two-factor authentication is locked",
    forgot: "Forgot password?",
    dir: "ltr",
  },
  {
    code: "ar",
    sentence: catalogSentence("DomainErrors.ar.resx"),
    title: "المصادقة الثنائية مقفلة",
    forgot: "هل نسيت كلمة المرور؟",
    dir: "rtl",
  },
] as const

const FIRST_LOAD = { timeout: 20_000 }

test("x12b: the two catalog sentences are real and distinct", () => {
  expect(LANGUAGES[0].sentence.length).toBeGreaterThan(20)
  expect(LANGUAGES[1].sentence.length).toBeGreaterThan(20)
  expect(LANGUAGES[0].sentence).not.toBe(LANGUAGES[1].sentence)
})

for (const language of LANGUAGES) {
  test(`x12b: a second factor at the limit shows the way out and takes no more codes (${language.code})`, async ({
    page,
    api,
  }) => {
    const verifies: string[] = []
    await api.useAnonymous(async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/login" && route.request().method() === "POST") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ twoFactorChallengeToken: "harness-challenge", requiresTwoFactor: true }),
        })
        return true
      }
      if (path === "/api/v1/auth/2fa/verify" && route.request().method() === "POST") {
        const asked = route.request().headers()["accept-language"] ?? ""
        verifies.push(asked)
        await route.fulfill({
          status: 403,
          contentType: "application/problem+json",
          body: JSON.stringify({
            title: "Forbidden",
            status: 403,
            code: CODE,
            detail: asked.toLowerCase().startsWith("ar") ? LANGUAGES[1].sentence : LANGUAGES[0].sentence,
          }),
        })
        return true
      }
      return false
    })

    await page.addInitScript((code: string) => {
      localStorage.setItem("auth.language", code)
    }, language.code)

    await page.goto(`${ORIGINS.accounts}/login`)
    await page.locator('input[type="email"]').fill("owner@example.com", FIRST_LOAD)
    await page.locator('input[type="password"]').fill("Harness1!")
    await page.locator('input[type="password"]').press("Enter")
    await expect(page).toHaveURL(/\/two-factor$/)

    await page.locator('[data-slot="input-otp"]').click()
    await page.keyboard.type("123456")
    await expect.poll(() => verifies.length).toBe(1)
    expect(verifies[0].toLowerCase().startsWith(language.code)).toBe(true)

    // Persistent, not a toast: the alert with its title and the server's sentence.
    const alert = page.getByRole("alert").filter({ hasText: language.sentence })
    await expect(alert).toBeVisible()
    await expect(alert).toContainText(language.title)

    // The first step out, on the page, leading there.
    const forgot = alert.getByRole("link", { name: language.forgot })
    await expect(forgot).toBeVisible()
    expect(new URL(await forgot.evaluate((a) => (a as HTMLAnchorElement).href)).pathname).toBe("/forgot-password")

    // Nothing left to submit: no code is accepted any more.
    await expect(page.locator('input[data-slot="input-otp"]')).toBeDisabled()
    await page.waitForTimeout(500)
    expect(verifies).toHaveLength(1)

    await expect(page.locator("html")).toHaveAttribute("dir", language.dir)
  })
}
