import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { clickPageAction } from "./page-actions"

/**
 * Removing a user's application-scoped role said "Changes saved" and the role
 * stayed: the dialog keyed a chip by the role alone and its DELETE carried no
 * application, so the API removed the platform assignment or nothing. Here the
 * same role and the same permission are each held platform-wide and inside
 * SHOP, as the API reports them, and the spec reads the DELETE that leaves the
 * browser for the chip that was clicked.
 */

const USER_ID = "11111111-1111-1111-1111-111111111111"
const ROLE_ID = "22222222-2222-2222-2222-222222222222"
const PERMISSION_ID = "33333333-3333-3333-3333-333333333333"
const SHOP = "44444444-4444-4444-4444-444444444444"
const AT = "2026-10-23T16:35:00Z"

const user = {
  id: USER_ID,
  email: "member@example.test",
  displayName: "Example Member",
  firstName: "Example",
  lastName: "Member",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: AT,
}

/**
 * The API leaves null properties out of every body (WhenWritingNull), so a
 * platform-wide row carries no applicationId at all, never `null`.
 */
function scope(applicationId: string | null) {
  return applicationId
    ? { applicationId, applicationName: "SHOP", applicationCode: "SHOP" }
    : {}
}

const ROLE_ROWS = [SHOP, null].map((applicationId, index) => ({
  id: `50000000-0000-0000-0000-00000000000${index}`,
  userId: USER_ID,
  roleId: ROLE_ID,
  roleName: "Store manager",
  roleCode: "store_manager",
  ...scope(applicationId),
  isActive: true,
  createdAt: AT,
}))

const PERMISSION_ROWS = [SHOP, null].map((applicationId, index) => ({
  id: `60000000-0000-0000-0000-00000000000${index}`,
  userId: USER_ID,
  permissionId: PERMISSION_ID,
  permissionCode: "reports:read",
  permissionName: "Read reports",
  ...scope(applicationId),
  isActive: true,
  createdAt: AT,
}))

async function installUser(page: Page, deletes: string[]) {
  await installAuthenticatedApi(
    page,
    ["users:read", "users:manage-roles", "users:manage-permissions"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (method === "DELETE") {
        deletes.push(`${path}${url.search}`)
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, user)
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/roles`) {
        await fulfillJson(route, ROLE_ROWS)
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/permissions`) {
        await fulfillJson(route, PERMISSION_ROWS)
        return true
      }
      if (path === "/api/v1/roles" || path === "/api/v1/permissions") {
        await fulfillJson(route, [])
        return true
      }
      if (path.startsWith(`/api/v1/users/${USER_ID}/`)) {
        await fulfillJson(route, [])
        return true
      }
      return false
    }
  )
}

async function removeAndSave(page: Page, dialogName: string, chip: string) {
  await page.goto(`/users/${USER_ID}`)
  await clickPageAction(page, dialogName)
  const dialog = page.getByRole("dialog", { name: dialogName })

  // Both scopes are on screen as their own chips before either is touched.
  const chips = dialog.locator('[data-slot="badge"]')
  await expect(chips).toHaveCount(2)

  await chips
    .filter({ hasText: chip })
    .getByRole("button", { name: "Remove" })
    .click()
  await dialog.getByRole("button", { name: "Save (1)", exact: true }).click()

  // The confirmation names the scope too: it is the last thing read before Save.
  await expect(page.getByText(chip, { exact: true }).last()).toBeVisible()
  await page.getByRole("button", { name: "Save", exact: true }).click()
  await expect(page.getByText("Changes saved")).toBeVisible()
}

test("removing an application-scoped role names that application in the request", async ({
  page,
}) => {
  const deletes: string[] = []
  await installUser(page, deletes)

  await removeAndSave(page, "Manage roles", "Store manager (SHOP)")

  expect(deletes).toEqual([
    `/api/v1/users/${USER_ID}/roles/${ROLE_ID}?applicationId=${SHOP}`,
  ])
})

test("removing the platform-wide role sends no application", async ({
  page,
}) => {
  const deletes: string[] = []
  await installUser(page, deletes)

  // hasText is a substring: the platform chip's text is the role name alone,
  // so it is picked as the chip that does not name SHOP.
  await page.goto(`/users/${USER_ID}`)
  await clickPageAction(page, "Manage roles")
  const dialog = page.getByRole("dialog", { name: "Manage roles" })
  const chips = dialog.locator('[data-slot="badge"]')
  await expect(chips).toHaveCount(2)
  await chips
    .filter({ hasNotText: "(SHOP)" })
    .getByRole("button", { name: "Remove" })
    .click()
  await dialog.getByRole("button", { name: "Save (1)", exact: true }).click()
  await page.getByRole("button", { name: "Save", exact: true }).click()
  await expect(page.getByText("Changes saved")).toBeVisible()

  expect(deletes).toEqual([`/api/v1/users/${USER_ID}/roles/${ROLE_ID}`])
})

test("revoking an application-scoped permission names that application in the request", async ({
  page,
}) => {
  const deletes: string[] = []
  await installUser(page, deletes)

  await removeAndSave(page, "Manage permissions", "reports:read (SHOP)")

  expect(deletes).toEqual([
    `/api/v1/users/${USER_ID}/permissions/${PERMISSION_ID}?applicationId=${SHOP}`,
  ])
})
