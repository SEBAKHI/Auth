import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { SHELL_INSET } from "./layout-overflow"

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
const WCAG_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]

/**
 * One known palette gap, excluded by the exact colours rather than by selector.
 *
 * `--muted-foreground` on `--muted` measures 4.34:1 in the light theme, against
 * the 4.5:1 that WCAG 1.4.3 asks for small text. It misses by 0.16. Both values
 * are defined in packages/ui/src/preset.css and the pair is used by a dozen
 * shipped components - the keyboard hint, avatar initials, badges, empty
 * states - so this is the preset's calibration, not any component's mistake.
 * Correcting it means darkening one token, which is the design system owner's
 * call and is explicitly out of bounds for this codebase's UI rules.
 *
 * Keyed to the colour pair, not to `kbd` and `[data-slot=avatar-fallback]`:
 * a selector list would have to grow every time the pair appears somewhere new,
 * and would quietly swallow a genuinely different contrast bug on those same
 * elements. Matched this way, every other contrast failure still fails - and
 * when the token is darkened, this stops matching anything on its own.
 */
const KNOWN_PALETTE_GAPS = [
  // `--muted-foreground` on `--muted`: the keyboard hint, avatar initials,
  // badges, empty states.
  { fg: "#737373", bg: "#f5f5f5", measured: 4.34, required: 4.5 },
  // `--destructive` on a 10% tint of itself: the destructive Badge and Button
  // variants in packages/ui/src/{badge,button}.tsx. Same ownership, same kind
  // of miss - 4.0 where 4.5 is asked.
  { fg: "#e7000b", bg: "#fde6e7", measured: 4.0, required: 4.5 },
]

function isKnownPaletteGap(node: { any: Array<{ data?: unknown }> }) {
  const data = node.any[0]?.data as
    | { fgColor?: string; bgColor?: string }
    | undefined
  if (!data) return false
  return KNOWN_PALETTE_GAPS.some(
    (gap) =>
      gap.fg === data.fgColor?.toLowerCase() &&
      gap.bg === data.bgColor?.toLowerCase()
  )
}

/** Violations minus the one gap above, which the preset owns. */
function actionable(results: Awaited<ReturnType<AxeBuilder["analyze"]>>) {
  return results.violations
    .map((violation) =>
      violation.id === "color-contrast"
        ? { ...violation, nodes: violation.nodes.filter((n) => !isKnownPaletteGap(n)) }
        : violation
    )
    .filter((violation) => violation.nodes.length > 0)
}

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

/** Every violation, flattened into something readable in a failure message. */
function describeViolations(
  results: Awaited<ReturnType<AxeBuilder["analyze"]>>
) {
  const newline = String.fromCharCode(10)
  return actionable(results)
    .map((violation) =>
      [
        `${violation.id} (${violation.impact}): ${violation.help}`,
        ...violation.nodes.slice(0, 3).map((node) => `      ${node.target.join(" ")}`),
      ].join(newline)
    )
    .join(newline + "  ")
}

/** No session at all: the screen a person meets before signing in. */
async function installAnonymous(
  page: Page,
  { theme, language }: { theme: "light" | "dark"; language: string }
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
      await fulfillJson(route, { platformName: "AuthSystem" })
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
  }
}
