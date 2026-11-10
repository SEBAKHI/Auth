import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { SHELL_INSET } from "./layout-overflow"
import { WCAG_TAGS, actionable, describeViolations } from "./axe-known-gaps"

/**
 * An automated WCAG 2.2 AA pass over the screens an administrator lives in.
 *
 * Be clear about what this is worth. axe finds roughly a third of WCAG failures
 * - contrast, missing accessible names, dangling ARIA references, wrong roles,
 * duplicate ids - and it finds them reliably. It cannot tell whether the tab
 * order makes sense, whether an error message is understandable when read
 * aloud, whether focus lands somewhere useful after a dialog closes, or whether
 * a colour carries meaning nothing else carries. Those stay a manual pass, and
 * the audit records them as still open.
 *
 * Both themes are scanned because the contrast rule is the one automation is
 * best at and it gives a different answer in each. Both directions are scanned
 * because RTL is a different layout path, not a mirrored one.
 */
const USER_ID = "0f8fad5b-d9cb-469f-a165-70867728950e"

const user = {
  id: USER_ID,
  email: "ada@example.test",
  firstName: "Ada",
  lastName: "Lovelace",
  displayName: "Ada Lovelace",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  isDeleted: false,
  roles: [],
  createdAt: "2026-08-01T09:00:00Z",
}

async function installConsole(
  page: Page,
  { theme, language }: { theme: "light" | "dark"; language: string }
) {
  await page.addInitScript((value) => {
    localStorage.setItem("theme", value)
  }, theme)
  await installAuthenticatedApi(
    page,
    [
      "users:read",
      "users:update",
      "users:manage",
      "users:delete",
      "applications:read",
      "auditlogs:read",
      "organizations:read",
      "notification-templates:read",
    ],
    async (route, url) => {
      if (url.pathname.toLowerCase() === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, user)
        return true
      }
      await fulfillJson(
        route,
        Object.assign([], {
          users: [user],
          items: [user],
          applications: [],
          organizations: [],
          logs: [],
          templates: [],
          totalCount: 1,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 20,
        })
      )
      return true
    },
    { preferredLanguage: language }
  )
}

/** The passkey options of a deployment that runs passkeys (S20). */
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

/** No session at all: the screen a person meets before signing in. */
async function installAnonymous(
  page: Page,
  {
    theme,
    language,
    staySignedInOffered = true,
    passkeys = false,
  }: {
    theme: "light" | "dark"
    language: string
    staySignedInOffered?: boolean
    /** Passkeys run here (S20); off as shipped, when their options answer 404. */
    passkeys?: boolean
  }
) {
  await page.addInitScript(
    ([themeValue, languageValue]) => {
      localStorage.setItem("theme", themeValue)
      localStorage.setItem("auth.language", languageValue)
    },
    [theme, language] as const
  )
  await page.route("**/api/v1/**", async (route, request) => {
    const path = new URL(request.url()).pathname.toLowerCase()
    if (path === "/api/v1/platform/branding") {
      // OI-280: the deployment's hint goes with passkeys that run.
      await fulfillJson(route, {
        platformName: "AuthSystem",
        staySignedInOffered,
        ...(passkeys
          ? {
              passkeySignInOffered: true,
              codeSignInOffered: true,
              primaryMethod: "password",
            }
          : {}),
      })
      return
    }
    if (path === "/api/v1/auth/passkeys/assertion/options") {
      if (passkeys) await fulfillJson(route, PASSKEY_OPTIONS)
      else await fulfillJson(route, { title: "Not Found" }, 404)
      return
    }
    if (path === "/api/v1/auth/passkeys/lookup") {
      // Every address has one here: the passkey screen is what is scanned.
      if (passkeys) await fulfillJson(route, { passkey: true })
      else await fulfillJson(route, { title: "Not Found" }, 404)
      return
    }
    await fulfillJson(route, { title: "Unauthenticated" }, 401)
  })
}

const SCREENS = [
  { name: "the user list", path: "/users" },
  { name: "a user's page", path: `/users/${USER_ID}` },
]

for (const screen of SCREENS) {
  for (const theme of ["light", "dark"] as const) {
    for (const language of ["en", "ar"] as const) {
      test(`${screen.name} passes WCAG AA in ${theme} ${language}`, async ({
        page,
      }) => {
        await installConsole(page, { theme, language })
        await page.goto(screen.path)
        await page.waitForSelector(SHELL_INSET)

        const results = await new AxeBuilder({ page })
          .withTags(WCAG_TAGS)
          .analyze()

        expect(actionable(results), describeViolations(results)).toEqual([])
      })
    }
  }
}

// The most-seen screen in the product, and the only one reached without a
// session - so it gets its own setup rather than the authenticated harness.
for (const theme of ["light", "dark"] as const) {
  for (const language of ["en", "ar"] as const) {
    test(`the sign-in screen passes WCAG AA in ${theme} ${language}`, async ({
      page,
    }) => {
      await installAnonymous(page, { theme, language })
      await page.goto("/login")
      await expect(page.getByRole("textbox").first()).toBeVisible()

      const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()

      expect(actionable(results), describeViolations(results)).toEqual([])
    })

    // OI-165: where the administrator decides, the same screen without its
    // "Stay signed in" box.
    test(`the sign-in screen without Stay signed in passes WCAG AA in ${theme} ${language}`, async ({
      page,
    }) => {
      await installAnonymous(page, { theme, language, staySignedInOffered: false })
      // The box is hidden while the branding is unknown too: wait for the answer.
      const answered = page.waitForResponse((response) =>
        new URL(response.url()).pathname.toLowerCase().endsWith("/platform/branding")
      )
      await page.goto("/login")
      await answered
      await expect(page.getByRole("textbox").first()).toBeVisible()
      // Gone only once the page has applied the answer.
      await expect(page.getByTestId("stay-signed-in-placeholder")).toHaveCount(0)
      await expect(page.getByRole("checkbox")).toHaveCount(0)

      const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()

      expect(actionable(results), describeViolations(results)).toEqual([])
    })
  }
}

// S20, as OI-280 PR A changed it: while passkeys run there is no passkey button
// for every visitor; an address that has a passkey leads to the passkey screen,
// whose "Try another way" opens the other ways in place (D-S20-16). Both states
// of that screen, scanned. Deliberate break, verified red: the list's password
// item left without its words (axe: a button with no name).
for (const theme of ["light", "dark"] as const) {
  for (const language of ["en", "ar"] as const) {
    test(`the passkey screen, and its open list, pass WCAG AA in ${theme} ${language}`, async ({
      page,
    }) => {
      await installAnonymous(page, { theme, language, passkeys: true })
      await page.goto("/login")
      await page.getByRole("textbox").first().fill("ada@example.test")
      // "Continue" in either language: the form's own submit.
      await page.getByRole("textbox").first().press("Enter")
      const passkey = page.getByTestId("passkey-sign-in").getByRole("button")
      await expect(passkey).toBeEnabled()
      await expect(passkey).toBeFocused()

      const screen = await new AxeBuilder({ page })
        .withTags(WCAG_TAGS)
        .analyze()
      expect(actionable(screen), describeViolations(screen)).toEqual([])

      await page.getByTestId("try-another-way").click()
      await expect(page.getByTestId("other-ways")).toBeVisible()
      await expect(
        page.getByTestId("other-ways").getByRole("button")
      ).toHaveCount(2)

      const list = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()
      expect(actionable(list), describeViolations(list)).toEqual([])
    })
  }
}

// S20: Profile › Security › Passkeys, its add dialog and its removal's
// confirmation. One passkey carries the counter's warning, and one name is in
// Arabic: the owner's own text, in any script.
const PASSKEY_LIST = {
  rpId: "localhost",
  userHandle: "dXNlci1oYW5kbGU",
  passkeys: [
    {
      id: "aaaaaaaa-0000-0000-0000-000000000001",
      name: "Work laptop",
      createdAt: "2026-10-01T09:00:00Z",
      lastUsedAt: "2026-11-01T09:00:00Z",
      isBackupEligible: true,
      isBackedUp: true,
      lastCounterRegressionAt: null,
      credentialId: "Y3JlZC1sYXB0b3A",
    },
    {
      id: "aaaaaaaa-0000-0000-0000-000000000002",
      name: "مفتاح الأمان",
      createdAt: "2026-10-02T09:00:00Z",
      lastUsedAt: null,
      isBackupEligible: false,
      isBackedUp: false,
      lastCounterRegressionAt: "2026-11-02T09:00:00Z",
      credentialId: "Y3JlZC1rZXk",
    },
  ],
}

async function installProfile(
  page: Page,
  { theme, language }: { theme: "light" | "dark"; language: string }
) {
  await page.addInitScript((value) => {
    localStorage.setItem("theme", value)
  }, theme)
  await installAuthenticatedApi(
    page,
    ["users:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/users/me") {
        await fulfillJson(route, {
          ...user,
          id: "99999999-9999-9999-9999-999999999999",
          hasPassword: true,
          twoFactorEnabled: true,
          totpEnabled: true,
          qualifyingMfa: true,
          emailCodeAvailable: true,
          passkeyCount: 2,
          preferredLanguage: language,
        })
        return true
      }
      if (path === "/api/v1/auth/passkeys") {
        await fulfillJson(route, PASSKEY_LIST)
        return true
      }
      if (path === "/api/v1/auth/passkeys/registration/options") {
        await fulfillJson(route, {
          options: {
            challenge: "cmVnaXN0cmF0aW9uLWNoYWxsZW5nZQ",
            rp: { id: "localhost", name: "AuthSystem" },
            user: { id: "dXNlci1oYW5kbGU", name: "ada@example.test", displayName: "Ada Lovelace" },
            pubKeyCredParams: [{ type: "public-key", alg: -7 }],
          },
          expiresAt: "2099-01-01T00:00:00Z",
          emailCodeRequired: false,
        })
        return true
      }
      if (path === "/api/v1/auth/2fa/status") {
        await fulfillJson(route, { recoveryCodesRemaining: 8 })
        return true
      }
      return false
    },
    { preferredLanguage: language }
  )
}

for (const theme of ["light", "dark"] as const) {
  for (const language of ["en", "ar"] as const) {
    test(`the profile's passkeys, their add dialog and their removal pass WCAG AA in ${theme} ${language}`, async ({
      page,
    }) => {
      await installProfile(page, { theme, language })
      await page.goto("/profile?tab=security")
      const card = page.getByTestId("passkeys-card")
      await expect(card.getByTestId("passkey-row")).toHaveCount(2)

      const scan = async () => {
        // An overlay measured mid-animation blends with the backdrop: let every
        // open animation finish first (as platform-settings-tabs.spec.ts does).
        await page.evaluate(() => Promise.all(document.getAnimations().map((animation) => animation.finished)))
        const results = await new AxeBuilder({ page }).withTags(WCAG_TAGS).analyze()
        expect(actionable(results), describeViolations(results)).toEqual([])
      }
      await scan()

      // The add dialog: its first click asks for the options and the helper.
      await card.locator('button[data-variant="outline"]').click()
      const dialog = page.getByRole("dialog")
      await expect(dialog).toBeVisible()
      await scan()
      await page.keyboard.press("Escape")
      await expect(dialog).toHaveCount(0)

      // The removal's confirmation.
      await card.getByTestId("passkey-row").first().getByRole("button").nth(1).click()
      const confirmation = page.getByRole("alertdialog")
      await expect(confirmation).toBeVisible()
      await scan()
    })
  }
}
