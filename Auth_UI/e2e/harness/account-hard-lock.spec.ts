import { readFileSync } from "node:fs"
import { dirname, resolve } from "node:path"
import { fileURLToPath } from "node:url"

import type { Page } from "@playwright/test"

import { ORIGINS, expect, test, type HarnessApi } from "./fixtures"

/**
 * X12 in a real browser: an account that reached the consecutive-failure limit
 * (the hard lock) answers a sign-in with `User.AccountLockedResetRequired`, and
 * the accounts app shows that answer's own sentence, in the reader's language,
 * on a page that still offers the way out it names: the forgot-password link.
 * Waiting does not end this lock, so a page that hid the sentence behind a
 * generic "something went wrong", or lost the link, would be a dead end.
 *
 * On the S30a harness, so the bundle is the production build under its real
 * web.config CSP, and the API host is the harness model answering with the
 * sentence the API itself would send: read from the same resource files
 * (DomainErrors.resx and DomainErrors.ar.resx) and picked by the request's
 * Accept-Language, as the API's localizer picks it. That the API refuses with
 * this code under the hard lock is pinned by the xUnit suite
 * (LockResponseSurfaceTests); what only a built bundle in a browser decides is
 * shown here.
 *
 * The console half: the two settings sit in the lockout group with a label and a
 * hint in English and Arabic, and saving Legacy asks first.
 *
 * Deliberate breaks this file must catch: adding the code to OPAQUE_DETAIL_CODES
 * in packages/api/src/errors.ts, which replaces the server's sentence with local
 * copy; and ConsecutiveFailureMode removed from HIGH_IMPACT_PATHS (Legacy saves
 * without asking).
 */

const CODE = "User.AccountLockedResetRequired"

const here = dirname(fileURLToPath(import.meta.url))
const errorsDir = resolve(
  here,
  "../../../Auth/Auth_Localization/Resources/Errors"
)

/** The API's own sentence for CODE in one resource file. */
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
    forgot: "Forgot password?",
    dir: "ltr",
  },
  {
    code: "ar",
    sentence: catalogSentence("DomainErrors.ar.resx"),
    forgot: "هل نسيت كلمة المرور؟",
    dir: "rtl",
  },
] as const

/** For the first answer after a cold navigation only; see two-factor-required.spec.ts. */
const FIRST_LOAD = { timeout: 20_000 }

test("x12: the two catalog sentences are real and distinct", () => {
  // A floor for the cases below: an empty or shared sentence would let a page
  // that shows the wrong language, or nothing, pass.
  expect(LANGUAGES[0].sentence.length).toBeGreaterThan(20)
  expect(LANGUAGES[1].sentence.length).toBeGreaterThan(20)
  expect(LANGUAGES[0].sentence).not.toBe(LANGUAGES[1].sentence)
})

for (const language of LANGUAGES) {
  test(`x12: a hard-locked account sees the reset sentence and the way out (${language.code})`, async ({
    page,
    api,
  }) => {
    const languagesAsked: string[] = []
    await api.useAnonymous(async (route, url) => {
      if (
        url.pathname.toLowerCase() !== "/api/v1/auth/login" ||
        route.request().method() !== "POST"
      ) {
        return false
      }
      const asked = route.request().headers()["accept-language"] ?? ""
      languagesAsked.push(asked)
      const sentence = asked.toLowerCase().startsWith("ar")
        ? LANGUAGES[1].sentence
        : LANGUAGES[0].sentence
      await route.fulfill({
        status: 403,
        contentType: "application/problem+json",
        body: JSON.stringify({
          title: "Forbidden",
          status: 403,
          code: CODE,
          detail: sentence,
        }),
      })
      return true
    })

    // The language a visitor picked lives where the app reads it at boot.
    await page.addInitScript((code: string) => {
      localStorage.setItem("auth.language", code)
    }, language.code)

    await page.goto(`${ORIGINS.accounts}/login`)
    await page
      .locator('input[type="email"]')
      .fill("owner@example.com", FIRST_LOAD)
    await page.locator('input[type="password"]').fill("Wrong-but-irrelevant1!")
    await page.locator('input[type="password"]').press("Enter")

    // The server's sentence, as sent, in the reader's language.
    await expect(
      page.getByText(language.sentence, { exact: true })
    ).toBeVisible()
    expect(languagesAsked).toHaveLength(1)
    expect(languagesAsked[0].toLowerCase().startsWith(language.code)).toBe(true)

    // The exit the sentence names is still on the page, and leads there.
    const forgot = page.getByRole("link", { name: language.forgot })
    await expect(forgot).toBeVisible()
    expect(
      new URL(await forgot.evaluate((a) => (a as HTMLAnchorElement).href))
        .pathname
    ).toBe("/forgot-password")

    await expect(page.locator("html")).toHaveAttribute("dir", language.dir)
  })
}

// ── The console: the two settings, where they are and what saving them asks ────

const ROW_VERSION = "AAAAAAAAB9E="

/** One registry field as the API sends it, at the shipped value. */
const field = (
  path: string,
  kind: string,
  value: unknown,
  extra: Record<string, unknown> = {}
) => ({
  path,
  kind,
  effectiveValue: value,
  baselineValue: value,
  defaultValue: value,
  source: "file",
  restartRequired: false,
  isPendingRestart: false,
  readOnly: false,
  sensitive: false,
  ...extra,
})

/** The lock rows of the Password section, as SystemSettingsRegistry declares them. */
const PASSWORD_SECTION = {
  key: "Password",
  group: "security",
  editable: true,
  version: 0,
  rowVersion: ROW_VERSION,
  fields: [
    field("MinimumLength", "int", 8, { min: 6, max: 128 }),
    field("MaxFailedAttempts", "int", 5, { min: 1, max: 100 }),
    field("LockoutDurationMinutes", "int", 15, { min: 1, max: 1440 }),
    field("ConsecutiveFailureLimit", "int", 100, { min: 10, max: 100 }),
    field("ConsecutiveFailureMode", "enum", "Enforce", {
      allowedValues: ["Enforce", "Legacy"],
    }),
    // A row of the next group (hashing): without it, a lock row that fell out of
    // the lockout group would still render right after LockoutDurationMinutes,
    // and the order assertion below could not fail.
    field("Argon2MemorySize", "int", 19456, {
      min: 19456,
      max: 1048576,
      restartRequired: true,
    }),
  ],
}

async function serveSettings(
  api: HarnessApi,
  puts: unknown[],
  preferredLanguage = "en"
) {
  await api.useAuthenticated(
    ["system-settings:manage"],
    async (route, url) => {
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
    },
    { preferredLanguage }
  )
}

const settingRow = (page: Page, path: string) =>
  page.locator(`[id="setting-${path}"]`)

const CONSOLE = [
  {
    code: "en",
    group: "After repeated failed sign-ins",
    limit: "Consecutive failures before a hard lock",
    limitHint: "NIST SP 800-63B-4 allows at most 100",
    mode: "Consecutive-failure limit",
    modeHint: "exists only as a rollback for the first 16 days",
  },
  {
    code: "ar",
    group: "بعد محاولات دخول فاشلة متكرّرة",
    limit: "الإخفاقات المتتالية قبل القفل الدائم",
    limitHint: "ولا يسمح معيار NIST SP 800-63B-4 بأكثر من 100",
    mode: "حدّ الإخفاقات المتتالية",
    modeHint: "وهو للتراجع وحده في أول 16 يومًا",
  },
] as const

for (const language of CONSOLE) {
  test(`x12: the two lock settings sit with the lockout rows, labelled and explained (${language.code})`, async ({
    page,
    api,
  }) => {
    await serveSettings(api, [], language.code)
    await page.goto(`${ORIGINS.console}/admin/system-settings/Password`)
    await settingRow(page, "ConsecutiveFailureLimit").waitFor(FIRST_LOAD)

    await expect(page.getByText(language.group, { exact: true })).toBeVisible()
    await expect(settingRow(page, "ConsecutiveFailureLimit")).toContainText(
      language.limit
    )
    await expect(settingRow(page, "ConsecutiveFailureLimit")).toContainText(
      language.limitHint
    )
    await expect(settingRow(page, "ConsecutiveFailureMode")).toContainText(
      language.mode
    )
    await expect(settingRow(page, "ConsecutiveFailureMode")).toContainText(
      language.modeHint
    )

    // In the lockout group: after the timed-lock rows, before anything else.
    // The rows themselves (other elements carry ids that start the same way).
    const paths = PASSWORD_SECTION.fields.map((f) => f.path)
    const order = await page.evaluate(
      (known) =>
        [...document.querySelectorAll('[id^="setting-"]')]
          .map((el) => el.id.replace("setting-", ""))
          .filter((id) => known.includes(id)),
      paths
    )
    expect(order).toHaveLength(paths.length)
    expect(order.indexOf("ConsecutiveFailureLimit")).toBe(
      order.indexOf("LockoutDurationMinutes") + 1
    )
    expect(order.indexOf("ConsecutiveFailureMode")).toBe(
      order.indexOf("ConsecutiveFailureLimit") + 1
    )
    expect(order.indexOf("Argon2MemorySize")).toBe(
      order.indexOf("ConsecutiveFailureMode") + 1
    )
  })
}

test("x12: switching the limit to Legacy asks first and writes nothing until confirmed", async ({
  page,
  api,
}) => {
  const puts: unknown[] = []
  await serveSettings(api, puts)
  await page.goto(`${ORIGINS.console}/admin/system-settings/Password`)
  await settingRow(page, "ConsecutiveFailureMode").waitFor(FIRST_LOAD)

  await settingRow(page, "ConsecutiveFailureMode")
    .getByText("Legacy", { exact: true })
    .click()
  await page
    .locator('[data-slot="settings-unsaved-bar"]')
    .locator('button[type="submit"]')
    .click()

  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText("Consecutive-failure limit")
  expect(puts).toHaveLength(0)

  await dialog.getByRole("button", { name: "Confirm" }).click()
  await expect.poll(() => puts.length).toBe(1)
  expect(puts[0]).toEqual(
    expect.objectContaining({ overrides: { ConsecutiveFailureMode: "Legacy" } })
  )
})
