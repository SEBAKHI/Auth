import type { Page, Route } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * S09 in a real browser: a new password the API refuses as breached is answered
 * under the password field, in the page's language, and saving any of the
 * breached-password settings asks first. On the S30a harness, so every page
 * runs under its app's real web.config CSP and the automatic csp fixture fails
 * any test on a violation beyond the register.
 *
 * What these prove and what they do not: the API host is the harness model, not
 * the real API. The refusal itself (the evaluator, the defaults, the hot switch)
 * is pinned by the xUnit suite and proven live by the owner's s09-hot-flip run.
 * What is proven here is what only a built bundle in a browser decides: that the
 * verdict lands on the field (described by it, marked invalid) rather than in a
 * toast, on both forms that reach it anonymously, in English and in Arabic; and
 * that the five breach fields are behind the confirmation.
 *
 * Deliberate breaks this file must catch: PASSWORD_VERDICT_CODES emptied in
 * packages/auth/src/password-rules.ts (the sentence goes to a toast), and the
 * breach paths removed from HIGH_IMPACT_PATHS.Password (the save goes out with
 * no dialog).
 */

/** User.PasswordBreached as the API's catalog words it (DomainErrors.resx, .ar.resx). */
const BREACHED = {
  en: "This password has appeared in a known data breach. Please choose a different password.",
  ar: "ظهرت كلمة المرور هذه في تسريب بيانات معروف. يرجى اختيار كلمة مرور مختلفة.",
} as const

type Language = keyof typeof BREACHED

/** The candidate is in HIBP; the harness model refuses it whatever it is. */
const CANDIDATE = "1qaz!QAZ2wsx@WSX"

async function refuseAsBreached(route: Route, language: Language) {
  await route.fulfill({
    status: 400,
    contentType: "application/problem+json",
    body: JSON.stringify({
      type: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
      title: "Bad Request",
      status: 400,
      code: "User.PasswordBreached",
      detail: BREACHED[language],
    }),
  })
}

async function useLanguage(page: Page, language: Language) {
  await page.addInitScript(
    (code) => localStorage.setItem("auth.language", code),
    language
  )
}

/**
 * The verdict is under the field when the field names it: the input is marked
 * invalid and its accessible description is the element that holds the sentence.
 */
async function expectVerdictUnderField(
  page: Page,
  fieldName: string,
  sentence: string
) {
  const field = page.locator(`input[name="${fieldName}"]`)
  await expect(field).toHaveAttribute("aria-invalid", "true")
  await expect(field).toHaveAccessibleDescription(
    new RegExp(escapeRegExp(sentence))
  )
  await expect(field).toBeFocused()
  await expect(page.locator("[data-sonner-toast]")).toHaveCount(0)
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
}

for (const language of ["en", "ar"] as const) {
  test(`refusal under the field: reset password (${language})`, async ({
    page,
    api,
  }) => {
    const resets: unknown[] = []
    await api.useAnonymous(async (route, url, body) => {
      if (url.pathname.toLowerCase() === "/api/v1/auth/reset-password") {
        resets.push(body)
        await refuseAsBreached(route, language)
        return true
      }
      return false
    })
    await useLanguage(page, language)

    await page.goto(
      `${ORIGINS.accounts}/reset-password?token=harness-reset-token`
    )
    await expect(page.locator("html")).toHaveAttribute(
      "dir",
      language === "ar" ? "rtl" : "ltr"
    )
    await page.locator('input[name="newPassword"]').fill(CANDIDATE)
    await page.locator('input[name="confirmNewPassword"]').fill(CANDIDATE)
    await page.locator('button[type="submit"]').click()

    await expect.poll(() => resets.length).toBe(1)
    await expectVerdictUnderField(page, "newPassword", BREACHED[language])
    // The person stays on the form, free to choose another password.
    await expect(page).toHaveURL(/\/reset-password/)
  })

  test(`refusal under the field: complete registration (${language})`, async ({
    page,
    api,
  }) => {
    const completions: unknown[] = []
    await api.useAnonymous(async (route, url, body) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/registration/start") {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            pendingId: "harness-pending",
            maskedEmail: "n***@example.test",
            expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
          }),
        })
        return true
      }
      if (path === "/api/v1/auth/registration/verify") {
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === "/api/v1/auth/registration/complete") {
        completions.push(body)
        await refuseAsBreached(route, language)
        return true
      }
      return false
    })
    await useLanguage(page, language)

    await page.goto(`${ORIGINS.accounts}/register`)
    await page.getByRole("textbox").fill("new.person@example.test")
    await page.locator('button[type="submit"]').click()
    await page.waitForURL("**/register/verify")
    await page.locator("[data-slot=input-otp]").click()
    await page.keyboard.type("123456")
    await page.waitForURL("**/register/complete")

    await page.locator('input[name="firstName"]').fill("New")
    await page.locator('input[name="lastName"]').fill("Person")
    await page.locator('input[name="password"]').fill(CANDIDATE)
    await page.locator('button[type="submit"]').click()

    await expect.poll(() => completions.length).toBe(1)
    await expectVerdictUnderField(page, "password", BREACHED[language])
    await expect(page).toHaveURL(/\/register\/complete/)
  })
}

const ROW_VERSION = "AAAAAAAAB9E="

/** The breach fields of the Password section as SystemSettingsRegistry declares them, at the defaults. */
const PASSWORD_SECTION = {
  key: "Password",
  group: "security",
  editable: true,
  version: 0,
  rowVersion: ROW_VERSION,
  fields: [
    { path: "BreachedPasswordCheck:Enabled", kind: "bool", value: true },
    {
      path: "BreachedPasswordCheck:Mode",
      kind: "enum",
      value: "Enforce",
      allowedValues: ["Enforce", "Warn"],
    },
    { path: "BreachedPasswordCheck:FailOpen", kind: "bool", value: true },
    {
      path: "BreachedPasswordCheck:RejectThreshold",
      kind: "int",
      value: 1,
      min: 1,
      max: 1000,
    },
    {
      path: "BreachedPasswordCheck:TimeoutMs",
      kind: "int",
      value: 2000,
      min: 100,
      max: 30000,
    },
  ].map(({ value, ...field }) => ({
    ...field,
    effectiveValue: value,
    baselineValue: value,
    defaultValue: value,
    source: "file",
    restartRequired: false,
    isPendingRestart: false,
    readOnly: false,
    sensitive: false,
  })),
}

async function serveSettings(api: HarnessApi, puts: unknown[]) {
  await api.useAuthenticated(["system-settings:manage"], async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/admin/system-settings") {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          restartPending: false,
          dbOverridesUnavailable: false,
          sections: [PASSWORD_SECTION],
        }),
      })
      return true
    }
    if (
      path.startsWith("/api/v1/admin/system-settings/") &&
      route.request().method() === "PUT"
    ) {
      puts.push(route.request().postDataJSON())
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: "{}",
      })
      return true
    }
    return false
  })
}

test("breach fields ask to confirm, and cancelling writes nothing", async ({
  page,
  api,
}) => {
  const puts: unknown[] = []
  await serveSettings(api, puts)

  await page.goto(`${ORIGINS.console}/admin/system-settings/Password`)
  const enabled = page.locator('[id="setting-BreachedPasswordCheck:Enabled"]')
  await enabled.waitFor()

  await enabled.getByRole("switch").click()
  await page
    .locator('[data-slot="settings-unsaved-bar"]')
    .locator('button[type="submit"]')
    .click()

  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("Save settings that affect security?")
  await expect(dialog).toContainText("Breached-password check")

  await dialog.getByRole("button", { name: "Cancel" }).click()
  await expect(dialog).toBeHidden()
  expect(puts).toHaveLength(0)
})

test("a lower breach-check timeout asks to confirm too", async ({
  page,
  api,
}) => {
  const puts: unknown[] = []
  await serveSettings(api, puts)

  await page.goto(`${ORIGINS.console}/admin/system-settings/Password`)
  const timeout = page.locator('[id="setting-BreachedPasswordCheck:TimeoutMs"]')
  await timeout.waitFor()

  await timeout.locator("input").fill("100")
  await page
    .locator('[data-slot="settings-unsaved-bar"]')
    .locator('button[type="submit"]')
    .click()

  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toContainText("Breach check timeout (ms)")
  expect(puts).toHaveLength(0)

  await dialog.getByRole("button", { name: "Confirm" }).click()
  await expect.poll(() => puts.length).toBe(1)
  expect(puts[0]).toEqual(
    expect.objectContaining({
      overrides: { BreachedPasswordCheck: { TimeoutMs: 100 } },
    })
  )
})
