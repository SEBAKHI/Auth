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

/** No session at all: the screen a person meets before signing in. */
async function installAnonymous(
  page: Page,
  {
    theme,
    language,
    staySignedInOffered = true,
  }: { theme: "light" | "dark"; language: string; staySignedInOffered?: boolean }
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
      await fulfillJson(route, { platformName: "AuthSystem", staySignedInOffered })
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
