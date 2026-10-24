import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { PROMOTED, clickPageAction } from "./page-actions"

/**
 * OI-224 on the built console: an administrator who manages users but holds
 * less than the platform administrator sees none of the controls the API would
 * refuse on that administrator's account — no Edit, Lock, Deactivate or
 * Delete, no image change, no ✕ in the role and permission dialogs, and no
 * Deactivate in the list row — while an ordinary account keeps every one. The
 * API answers what the rules decide (`allowedActions`, `canRemove`); this reads
 * what the console makes of it. Deliberate break: drop one gate in a page.
 */

const ADMIN_ID = "11111111-1111-1111-1111-111111111111"
const MEMBER_ID = "22222222-2222-2222-2222-222222222222"
const SUPER_ADMIN_ROLE = "33333333-3333-3333-3333-333333333333"
const AGENT_ROLE = "44444444-4444-4444-4444-444444444444"
const WILDCARD = "55555555-5555-5555-5555-555555555555"
const CRM = "66666666-6666-6666-6666-666666666666"
const AT = "2026-10-23T16:35:00Z"

const USER_MANAGER = [
  "users:read",
  "users:update",
  "users:delete",
  "users:manage",
  "users:manage-roles",
  "users:manage-permissions",
]

const allowed = (value: boolean) => ({
  update: value,
  profileImage: value,
  lock: value,
  unlock: value,
  activate: value,
  deactivate: value,
  delete: value,
  permanentDelete: value,
})

function account(id: string, name: string, coveredByCaller: boolean) {
  const [firstName, lastName] = name.split(" ")
  return {
    id,
    email: `${firstName.toLowerCase()}@example.test`,
    displayName: name,
    firstName,
    lastName,
    status: "Active",
    emailConfirmed: true,
    phoneConfirmed: false,
    twoFactorEnabled: false,
    preferredLanguage: "en",
    timeZone: "UTC",
    createdAt: AT,
    isDeleted: false,
    roles: coveredByCaller ? ["crm-agent"] : ["super-admin"],
    permissions: coveredByCaller ? [] : ["*"],
    allowedActions: allowed(coveredByCaller),
  }
}

/** The last case of the suite: it counts the others. */
const LAST = "ran every case above"

const ADMIN = account(ADMIN_ID, "Platform Administrator", false)
const MEMBER = account(MEMBER_ID, "Ordinary Member", true)

const ROLES: Record<string, unknown[]> = {
  [ADMIN_ID]: [
    {
      id: "70000000-0000-0000-0000-000000000001",
      userId: ADMIN_ID,
      roleId: SUPER_ADMIN_ROLE,
      roleName: "Super Admin",
      roleCode: "super-admin",
      isActive: true,
      createdAt: AT,
      canRemove: false,
    },
  ],
  [MEMBER_ID]: [
    {
      id: "70000000-0000-0000-0000-000000000002",
      userId: MEMBER_ID,
      roleId: AGENT_ROLE,
      roleName: "Agent",
      roleCode: "crm-agent",
      applicationId: CRM,
      applicationName: "CRM",
      applicationCode: "CRM",
      isActive: true,
      createdAt: AT,
      canRemove: true,
    },
  ],
}

const PERMISSIONS: Record<string, unknown[]> = {
  [ADMIN_ID]: [
    {
      id: "80000000-0000-0000-0000-000000000001",
      userId: ADMIN_ID,
      permissionId: WILDCARD,
      permissionCode: "*",
      permissionName: "Everything",
      isActive: true,
      createdAt: AT,
      canRemove: false,
    },
  ],
  [MEMBER_ID]: [
    {
      id: "80000000-0000-0000-0000-000000000002",
      userId: MEMBER_ID,
      permissionId: WILDCARD,
      permissionCode: "crm:leads:read",
      permissionName: "Read leads",
      applicationId: CRM,
      applicationName: "CRM",
      applicationCode: "CRM",
      isActive: true,
      createdAt: AT,
      canRemove: true,
    },
  ],
}

/** Every request that changes something, as `METHOD path`, in the order sent. */
async function installAccounts(page: Page, writes: string[] = []) {
  await installAuthenticatedApi(page, USER_MANAGER, async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (method !== "GET") {
      writes.push(`${method} ${path}`)
      await route.fulfill({ status: 204 })
      return true
    }
    if (path === "/api/v1/users") {
      await fulfillJson(route, {
        users: [ADMIN, MEMBER],
        totalCount: 2,
        pageNumber: 1,
        pageSize: 20,
        totalPages: 1,
      })
      return true
    }
    for (const subject of [ADMIN, MEMBER]) {
      const base = `/api/v1/users/${subject.id}`
      if (path === base) {
        await fulfillJson(route, subject)
        return true
      }
      if (path === `${base}/roles`) {
        await fulfillJson(route, ROLES[subject.id])
        return true
      }
      if (path === `${base}/permissions`) {
        await fulfillJson(route, PERMISSIONS[subject.id])
        return true
      }
      if (path.startsWith(`${base}/`)) {
        await fulfillJson(route, [])
        return true
      }
    }
    if (path === "/api/v1/roles" || path === "/api/v1/permissions") {
      await fulfillJson(route, [])
      return true
    }
    if (path.startsWith("/api/v1/me/") || path.startsWith("/api/v1/users/me/")) {
      await fulfillJson(route, {})
      return true
    }
    return false
  })
}

/** Every action the record's surface offers: the promoted ones, then the menu. */
async function pageActionLabels(page: Page) {
  const menu = page.getByRole("button", { name: "Actions" })
  await expect(page.locator(PROMOTED).first().or(menu).first()).toBeVisible()
  const labels = (await page.locator(PROMOTED).allTextContents()).map((label) => label.trim())
  if ((await menu.count()) > 0) {
    await menu.click()
    labels.push(
      ...(await page.getByRole("menu").getByRole("menuitem").allTextContents()).map((label) => label.trim())
    )
    await page.keyboard.press("Escape")
  }
  return labels
}

async function removeControls(page: Page, dialogName: string) {
  await clickPageAction(page, dialogName)
  const dialog = page.getByRole("dialog", { name: dialogName })
  const chips = dialog.locator('[data-slot="badge"]')
  await expect(chips).toHaveCount(1)
  const count = await chips.getByRole("button", { name: "Remove" }).count()
  await page.keyboard.press("Escape")
  await expect(dialog).toBeHidden()
  return count
}

test.describe("what a user manager is offered", () => {
  // In order, in one worker, so the last case can count the others: a spec whose
  // cases were all skipped must not pass for having run nothing.
  test.describe.configure({ mode: "serial" })
  const CASES = 3
  let ran = 0
  const writes: string[] = []

  test.beforeEach(async ({ page }) => {
    writes.length = 0
    await installAccounts(page, writes)
  })

  // eslint-disable-next-line no-empty-pattern -- Playwright requires the destructured first argument.
  test.afterEach(({}, testInfo) => {
    if (testInfo.title !== LAST && testInfo.status === "passed") ran += 1
  })

  test("on the platform administrator's page: nothing the API would refuse", async ({ page }) => {
    await page.goto(`/users/${ADMIN_ID}`)
    await expect(page.getByRole("heading", { name: "Platform Administrator" })).toBeVisible()

    const labels = await pageActionLabels(page)
    expect(labels.length).toBeGreaterThan(1)
    expect(labels).toEqual(expect.arrayContaining(["Manage roles", "Manage permissions"]))
    for (const refused of ["Edit", "Lock", "Deactivate", "Delete"]) {
      expect(labels).not.toContain(refused)
    }
    // The image menu (View, Change, Remove) opens from the avatar itself.
    await expect(page.getByRole("button", { name: "Avatar", exact: true })).toHaveCount(0)

    expect(await removeControls(page, "Manage roles")).toBe(0)
    expect(await removeControls(page, "Manage permissions")).toBe(0)
  })

  test("on an ordinary account's page: every control, and they work", async ({ page }) => {
    await page.goto(`/users/${MEMBER_ID}`)
    await expect(page.getByRole("heading", { name: "Ordinary Member" })).toBeVisible()

    const labels = await pageActionLabels(page)
    expect(labels).toEqual(
      expect.arrayContaining(["Edit", "Manage roles", "Manage permissions", "Lock", "Deactivate", "Delete"])
    )
    await expect(page.getByRole("button", { name: "Avatar", exact: true })).toHaveCount(1)

    expect(await removeControls(page, "Manage roles")).toBe(1)
    expect(await removeControls(page, "Manage permissions")).toBe(1)

    // One of them, end to end: deactivating acts on the click and sends its request.
    await clickPageAction(page, "Deactivate")
    await expect.poll(() => writes).toContain(`POST /api/v1/users/${MEMBER_ID}/deactivate`)
  })

  test("in the list: no Deactivate on the administrator's row, Deactivate on the member's", async ({ page }) => {
    await page.goto("/users")
    const rows = page.getByRole("row")
    await expect(rows.filter({ hasText: "Platform Administrator" })).toHaveCount(1)

    const menuOf = async (name: string) => {
      await rows.filter({ hasText: name }).getByRole("button", { name: "Actions" }).click()
      const items = (await page.getByRole("menu").getByRole("menuitem").allTextContents()).map((item) => item.trim())
      await page.keyboard.press("Escape")
      return items
    }

    const administrator = await menuOf("Platform Administrator")
    const member = await menuOf("Ordinary Member")

    expect(administrator.length).toBeGreaterThan(0)
    expect(administrator).not.toContain("Deactivate")
    expect(administrator).not.toContain("Lock")
    expect(administrator).not.toContain("Delete")
    expect(member).toEqual(expect.arrayContaining(["Lock", "Deactivate", "Delete"]))
  })

  test(LAST, () => {
    expect(ran, "every case above ran and passed").toBe(CASES)
  })
})
