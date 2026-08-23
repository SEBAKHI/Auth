import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import {
  expectCardOutlinesVisible,
  expectNoCrushedContent,
  expectNoShellOverflow,
  SHELL_INSET,
} from "./layout-overflow"

/**
 * Every list screen, at every width the audit named, in both writing directions.
 *
 * Deliberately one parametrised spec rather than Playwright projects: the
 * isolated config has no `testMatch`, so ten projects would run all 21 existing
 * tests ten times over. And a project's `locale` option would do nothing here -
 * the app takes its language from the mocked `/auth/me` response, never from the
 * browser.
 */

const USER_ID = "11111111-1111-1111-1111-111111111111"

const user = {
  id: USER_ID,
  email: "operator@example.test",
  displayName: "Console Operator",
  firstName: "Console",
  lastName: "Operator",
  status: "Active",
  isDeleted: false,
  roles: [],
  createdAt: "2026-08-22T08:00:00Z",
}

/**
 * The five sizes the audit listed, plus 1279.
 *
 * 1279 is not padding. Tailwind's `xl` is 80rem = 1280px exactly, and the action
 * surface switches on it: at 1279 the named menu is shown and the button row is
 * `display:none`; at 1280 they swap. Testing only 1280 tests one side of a
 * boundary and calls it a boundary test.
 */
const WIDTHS = [
  { width: 320, height: 568, name: "320 (smallest phone)" },
  { width: 375, height: 667, name: "375 (phone)" },
  { width: 768, height: 1024, name: "768 (tablet)" },
  { width: 1279, height: 720, name: "1279 (just under xl)" },
  { width: 1280, height: 720, name: "1280 (exactly xl)" },
  { width: 1440, height: 900, name: "1440 (desktop)" },
] as const

/**
 * One language per writing direction. `ar` stands in for the three RTL locales
 * (ar, ur, fa) - they share one direction and one layout path.
 */
const LOCALES = [
  { code: "en", dir: "ltr" },
  { code: "ar", dir: "rtl" },
] as const

const ROUTES = ["/users", "/applications", "/audit-logs", "/organizations"]

async function installConsole(page: Page, preferredLanguage: string) {
  await installAuthenticatedApi(
    page,
    [
      "users:read",
      "users:update",
      "users:manage-roles",
      "users:manage-permissions",
      "users:manage",
      "users:delete",
      "applications:read",
      "auditlogs:read",
      "organizations:read",
    ],
    async (route, url) => {
      // The detail route answers with the record itself; every list answers
      // with an envelope. One row is enough - this is about layout, not data.
      if (url.pathname.toLowerCase() === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, user)
        return true
      }
      // Some endpoints answer with a bare list and some with a paged envelope,
      // and this one handler stands in for all of them.
      await fulfillJson(
        route,
        Object.assign([], {
          users: [user],
          items: [user],
          applications: [user],
          organizations: [user],
          logs: [],
          roles: [],
          permissions: [],
          totalCount: 1,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 20,
        })
      )
      return true
    },
    { preferredLanguage }
  )
}

for (const locale of LOCALES) {
  for (const size of WIDTHS) {
    test(`${locale.code}: no screen spills at ${size.name}`, async ({ page }) => {
      await page.setViewportSize({ width: size.width, height: size.height })
      await installConsole(page, locale.code)

      for (const route of ROUTES) {
        await page.goto(route)
        await page.waitForSelector(SHELL_INSET)
        const where = `${route} at ${size.width} ${locale.code}`
        await expectNoShellOverflow(page, where)
        // Horizontal spill is only half of "the screen holds together". A
        // scroll pane that squeezes its children, or clips the ring that bounds
        // them, loses content just as completely and reports zero overflow
        // while doing it - which is how a five-card editor shipped with every
        // card rendering at 48px. These two cost nothing to carry here.
        await expectNoCrushedContent(page, where)
        await expectCardOutlinesVisible(page, where)
      }
    })
  }

  test(`${locale.code}: the page reads in the right direction`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 })
    await installConsole(page, locale.code)
    await page.goto("/users")
    await page.waitForSelector(SHELL_INSET)

    await expect(page.locator("html")).toHaveAttribute("dir", locale.dir)

    // Direction is not a document attribute alone: the shell has to lay out
    // against it. In RTL the sidebar sits on the right of the content, in LTR
    // on the left - and getting that wrong is invisible to a `dir` assertion.
    const geometry = await page.evaluate((selector) => {
      const inset = document.querySelector(selector)!.getBoundingClientRect()
      const sidebar = document
        .querySelector('[data-slot="sidebar"], [data-sidebar="sidebar"]')
        ?.getBoundingClientRect()
      return { insetLeft: inset.left, sidebarLeft: sidebar?.left ?? null }
    }, SHELL_INSET)

    expect(geometry.sidebarLeft).not.toBeNull()
    if (locale.dir === "rtl") {
      expect(geometry.sidebarLeft!).toBeGreaterThan(geometry.insetLeft)
    } else {
      expect(geometry.sidebarLeft!).toBeLessThan(geometry.insetLeft)
    }
  })
}

/**
 * The action surface switches at xl and nowhere else.
 *
 * Both halves are always in the DOM - one is hidden by CSS - so this asserts on
 * what is actually visible, which is what an admin experiences.
 */
test.describe("the action surface switches exactly at xl", () => {
  for (const width of [1279, 1280] as const) {
    test(`at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 720 })
      await installConsole(page, "en")
      await page.goto(`/users/${USER_ID}`)
      await page.waitForSelector(SHELL_INSET)

      const desktopRow = page.locator('[data-slot="page-action-surface-desktop"]')
      const narrowMenu = page.getByRole("button", { name: "Actions", exact: true })

      if (width >= 1280) {
        await expect(desktopRow).toBeVisible()
        await expect(narrowMenu).toBeHidden()
      } else {
        await expect(desktopRow).toBeHidden()
        await expect(narrowMenu).toBeVisible()
      }
    })
  }
})

/**
 * At the narrowest width every command is still reachable - through the named
 * menu, not by scrolling sideways to find a button that fell off the edge.
 */
test("every user action is still reachable at 320px", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 568 })
  await installConsole(page, "en")
  await page.goto(`/users/${USER_ID}`)
  await page.waitForSelector(SHELL_INSET)

  const menu = page.getByRole("button", { name: "Actions", exact: true })
  await expect(menu).toBeVisible()
  await menu.click()

  const items = page.getByRole("menuitem")
  await expect(items.first()).toBeVisible()
  expect(await items.count()).toBeGreaterThan(3)
  await expectNoShellOverflow(page, "user detail with the action menu open at 320")
})

/**
 * Long content must push a layout around, not out of it.
 *
 * Real records carry names nobody sized a column for - a department that spells
 * itself out, an Arabic name with its full patronymic. German and Arabic labels
 * also run considerably longer than the English the layout was drawn against.
 * The failure this catches is a name shouldering the action controls off the
 * edge, where the shell then clips them out of reach entirely.
 */
const LONG_NAME_EN =
  "Alexandra Constantina Featherstonehaugh-Wolseley, Regional Director of Operations"
const LONG_NAME_AR =
  "عبد الرحمن بن محمد بن عبد الله الشريف الحسيني، مدير العمليات الإقليمية للشرق الأوسط"

for (const [locale, longName] of [
  ["en", LONG_NAME_EN],
  ["ar", LONG_NAME_AR],
] as const) {
  for (const width of [320, 1440] as const) {
    test(`${locale}: a very long name does not push the actions off the page at ${width}px`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 720 })
      await installAuthenticatedApi(
        page,
        ["users:read", "users:update", "users:manage", "users:delete"],
        async (route, url) => {
          const longUser = { ...user, displayName: longName, firstName: longName }
          if (url.pathname.toLowerCase() === `/api/v1/users/${USER_ID}`) {
            await fulfillJson(route, longUser)
            return true
          }
          await fulfillJson(
            route,
            Object.assign([], {
              users: [longUser],
              items: [longUser],
              totalCount: 1,
              totalPages: 1,
              pageNumber: 1,
              pageSize: 20,
            })
          )
          return true
        },
        { preferredLanguage: locale }
      )

      await page.goto(`/users/${USER_ID}`)
      await page.waitForSelector(SHELL_INSET)

      // The name is long enough to matter, and the commands survived it.
      await expect(page.getByRole("heading", { level: 1 })).toContainText(
        longName.slice(0, 20)
      )
      const commands =
        width >= 1280
          ? page.locator('[data-slot="page-action-surface-desktop"]')
          : page.getByRole("button", { name: /actions|إجراءات/i })
      await expect(commands).toBeVisible()

      await expectNoShellOverflow(page, `long ${locale} name at ${width}`)
      await expectNoShellOverflow(page, `long ${locale} name on the list at ${width}`)
    })
  }
}

/**
 * Where you are in the hierarchy stays readable at every width.
 *
 * The overflow guard above cannot catch this on its own: a crumb squeezed to
 * two characters does not spill, it truncates, and truncation is silent. So
 * this asserts legibility directly - the label's own scrollWidth against its
 * clientWidth - which is the only thing that separates "shown" from "readable".
 */
const TRAIL = '[data-slot="breadcrumb"]'
const PARENT_LABEL = '[data-slot="parent-link-label"]'

for (const locale of LOCALES) {
  for (const size of WIDTHS) {
    test(`${locale.code}: the way back is readable at ${size.name}`, async ({
      page,
    }) => {
      await page.setViewportSize({ width: size.width, height: size.height })
      await installConsole(page, locale.code)
      await page.goto(`/users/${USER_ID}`)
      await page.waitForSelector(SHELL_INSET)

      const wide = size.width >= 1024
      const shown = wide ? TRAIL : PARENT_LABEL

      // Exactly one of the two is offered - never both, never neither.
      await expect(page.locator(shown)).toBeVisible()
      await expect(page.locator(wide ? PARENT_LABEL : TRAIL)).toBeHidden()

      const clipped = await page.evaluate((selector) => {
        const nodes = [...document.querySelectorAll(selector)]
        const parts = selector.includes("breadcrumb")
          ? nodes.flatMap((n) => [...n.querySelectorAll("a, span")])
          : nodes
        return parts
          .filter((n) => (n.textContent ?? "").trim().length > 0)
          .filter((n) => n.scrollWidth > n.clientWidth + 1)
          .map((n) => (n.textContent ?? "").trim())
      }, shown)

      expect(clipped, `clipped labels at ${size.width}: ${clipped.join(" | ")}`).toEqual([])
    })
  }
}

test("the way back actually goes back", async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 })
  await installConsole(page, "en")
  await page.goto(`/users/${USER_ID}`)
  await page.waitForSelector(SHELL_INSET)

  await expect(page.locator(PARENT_LABEL)).toHaveText("Users")
  await page.locator(PARENT_LABEL).click()
  await expect(page).toHaveURL(/\/users$/)
})
