import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Which of a role's codes the console offers to remove (OI-226 T4).
 *
 * The API refuses removing a code the remover does not hold
 * (Permission.CannotRemoveHigher) and the last full administrator's "*"
 * (User.LastFullAdministrator), and the role read says which codes the viewer
 * may remove. A refused one is offered without its remove control: a ✕ that can
 * only answer 403 is a dead end. Here the viewer holds every administration
 * code but "*", as the seeded Administrator role does.
 */

const ADMINISTRATOR_SET = [
  "users:*",
  "roles:*",
  "permissions:*",
  "applications:*",
]

const SUPER_ADMIN_ID = "10000000-0000-0000-0000-000000000001"
const CUSTOM_ROLE_ID = "66666666-6666-6666-6666-666666666666"
const USERS_READ_ID = "20000000-0000-0000-0000-000000000002"

/** The last case of the suite: it counts the others. */
const LAST = "ran every case above"

function permission(id: string, code: string) {
  return {
    id,
    code,
    name: `Name of ${code}`,
    isWildcard: code.endsWith("*"),
    isActive: true,
    createdAt: "2026-10-01T09:00:00Z",
  }
}

const CATALOGUE = [
  permission("20000000-0000-0000-0000-000000000001", "*"),
  permission(USERS_READ_ID, "users:read"),
  permission("20000000-0000-0000-0000-000000000003", "roles:read"),
]

function role(
  id: string,
  code: string,
  name: string,
  isSystem: boolean,
  permissions: string[],
  removablePermissions: string[]
) {
  return {
    id,
    code,
    name,
    isSystem,
    isActive: true,
    createdAt: "2026-10-01T09:00:00Z",
    permissions,
    removablePermissions,
  }
}

const ROLES = {
  // What the API computes for this viewer: it does not hold "*".
  [SUPER_ADMIN_ID]: role(
    SUPER_ADMIN_ID,
    "super-admin",
    "Super Administrator",
    true,
    ["*"],
    []
  ),
  [CUSTOM_ROLE_ID]: role(
    CUSTOM_ROLE_ID,
    "support-agent",
    "Support agent",
    false,
    ["users:read", "roles:read"],
    ["users:read", "roles:read"]
  ),
}

const EMPTY_LIST = Object.assign([], {
  items: [],
  users: [],
  applications: [],
  totalCount: 0,
  totalPages: 1,
  pageNumber: 1,
  pageSize: 20,
})

/** Every request that changes something, as `METHOD path`, in the order sent. */
async function installRoles(page: Page, writes: string[]) {
  await installAuthenticatedApi(page, ADMINISTRATOR_SET, async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (method !== "GET") {
      writes.push(`${method} ${path}`)
      await route.fulfill({ status: 204 })
      return true
    }
    if (path === "/api/v1/permissions") {
      await fulfillJson(route, CATALOGUE)
      return true
    }
    const detail = /^\/api\/v1\/roles\/([0-9a-f-]+)$/.exec(path)
    if (detail) {
      await fulfillJson(route, ROLES[detail[1] as keyof typeof ROLES])
      return true
    }
    if (path === "/api/v1/roles") {
      await fulfillJson(route, Object.values(ROLES))
      return true
    }
    await fulfillJson(route, EMPTY_LIST)
    return true
  })
}

async function openPermissions(page: Page, roleId: string) {
  await page.goto(`/roles/${roleId}?tab=permissions`)
  await page
    .getByRole("button", { name: "Manage permissions", exact: true })
    .click()
  return page.getByRole("dialog", { name: "Manage permissions" })
}

test.describe("what an administrator without the wildcard may remove from a role", () => {
  // In order, in one worker, so the last case can count the others: a spec whose
  // cases were all skipped must not pass for having run nothing.
  test.describe.configure({ mode: "serial" })
  const CASES = 2
  let ran = 0
  const writes: string[] = []

  // Each pass over the cases counts afresh (--repeat-each runs them again in the
  // same worker).
  test.beforeAll(() => {
    ran = 0
  })

  test.beforeEach(async ({ page }) => {
    writes.length = 0
    await installRoles(page, writes)
  })

  // eslint-disable-next-line no-empty-pattern -- Playwright requires the destructured first argument.
  test.afterEach(({}, testInfo) => {
    if (testInfo.title !== LAST && testInfo.status === "passed") ran += 1
  })

  test("the Super Administrator role shows its wildcard with no remove control", async ({
    page,
  }) => {
    const dialog = await openPermissions(page, SUPER_ADMIN_ID)

    const chip = dialog.locator('[data-slot="badge"]', { hasText: "*" })
    await expect(chip).toBeVisible()
    await expect(
      chip.getByRole("button", { name: "Remove", exact: true })
    ).toHaveCount(0)
  })

  test("a custom role's codes the viewer holds can be removed", async ({
    page,
  }) => {
    const dialog = await openPermissions(page, CUSTOM_ROLE_ID)

    for (const code of ["users:read", "roles:read"]) {
      await expect(
        dialog
          .locator('[data-slot="badge"]', { hasText: code })
          .getByRole("button", { name: "Remove", exact: true })
      ).toHaveCount(1)
    }

    // One of them, end to end: the removal is sent, addressed by the code's id.
    await dialog
      .locator('[data-slot="badge"]', { hasText: "users:read" })
      .getByRole("button", { name: "Remove", exact: true })
      .click()
    await dialog.getByRole("button", { name: "Save (1)", exact: true }).click()
    await page
      .getByRole("alertdialog", { name: "Apply these changes?" })
      .getByRole("button", { name: "Save", exact: true })
      .click()
    await expect
      .poll(() => writes)
      .toContain(
        `DELETE /api/v1/roles/${CUSTOM_ROLE_ID}/permissions/${USERS_READ_ID}`
      )
  })

  test(LAST, () => {
    expect(ran, "every case above ran and passed").toBe(CASES)
  })
})
