import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { clickPageAction } from "./page-actions"

/**
 * The pickers offer only what this administrator may grant (OI-223). The role and
 * permission lists carry `canGrant`, computed by the server from the caller's live
 * grants; the API reports here what it reports for an account holding the seeded
 * Administrator set (every administration code but `*`). Picking a role or a code
 * the administrator does not hold used to be refused with
 * Permission.CannotGrantHigher after the choice: a visible dead end.
 *
 * Deliberate break: offer every catalogue entry again (ignore `canGrant`).
 */

const USER_ID = "11111111-1111-1111-1111-111111111111"
const PLATFORM_ROLE_ID = "66666666-6666-6666-6666-666666666666"
const AT = "2026-10-24T09:00:00Z"

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

/** A platform role, as the list reports it (null properties are left out). */
function role(id: string, code: string, name: string, canGrant: boolean) {
  return {
    id,
    code,
    name,
    isSystem: code === "super-admin",
    isActive: true,
    createdAt: AT,
    permissions: [],
    canGrant,
  }
}

const ROLES = [
  role("70000000-0000-0000-0000-000000000001", "super-admin", "Super Administrator", false),
  role("70000000-0000-0000-0000-000000000002", "user-manager", "User Manager", true),
  role("70000000-0000-0000-0000-000000000003", "support", "Support", true),
]

function permission(id: string, code: string, canGrant: boolean) {
  return {
    id,
    code,
    name: `Name of ${code}`,
    isWildcard: code.endsWith("*"),
    isActive: true,
    createdAt: AT,
    canGrant,
  }
}

const CATALOGUE = [
  permission("10000000-0000-0000-0000-000000000001", "*", false),
  permission("10000000-0000-0000-0000-000000000002", "users:read", true),
  permission("10000000-0000-0000-0000-000000000003", "roles:read", true),
]

const PLATFORM_ROLE = {
  id: PLATFORM_ROLE_ID,
  code: "support-agent",
  name: "Support agent",
  isSystem: false,
  isActive: true,
  createdAt: AT,
  permissions: [],
}

async function install(page: Page) {
  await installAuthenticatedApi(
    page,
    [
      "users:read",
      "users:manage-roles",
      "users:manage-permissions",
      "roles:read",
      "roles:update",
      "permissions:read",
    ],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, user)
        return true
      }
      if (path.startsWith(`/api/v1/users/${USER_ID}/`)) {
        await fulfillJson(route, [])
        return true
      }
      if (path === "/api/v1/roles") {
        await fulfillJson(route, ROLES)
        return true
      }
      if (path === `/api/v1/roles/${PLATFORM_ROLE_ID}`) {
        await fulfillJson(route, PLATFORM_ROLE)
        return true
      }
      if (path === "/api/v1/permissions") {
        await fulfillJson(route, CATALOGUE)
        return true
      }
      return false
    }
  )
}

/** The entries the open dialog's picker offers, by their first line. */
async function offered(page: Page, dialogName: string) {
  const dialog = page.getByRole("dialog", { name: dialogName })
  await dialog.getByRole("combobox").click()
  const options = page.getByRole("option")
  // Floor: an empty list would satisfy every "does not offer" below.
  await expect(options.first()).toBeVisible()
  const texts = await options.allInnerTexts()
  return texts.map((text) => text.split("\n")[0].trim())
}

test("a user's role picker does not offer a role the administrator cannot assign", async ({
  page,
}) => {
  await install(page)
  await page.goto(`/users/${USER_ID}`)
  await clickPageAction(page, "Manage roles")

  const roles = await offered(page, "Manage roles")

  expect(roles.length).toBeGreaterThan(0)
  expect(roles.some((label) => label.startsWith("Super Administrator"))).toBe(false)
  expect(roles.some((label) => label.startsWith("User Manager"))).toBe(true)
})

test("a role's permission picker does not offer the wildcard to an administrator without it", async ({
  page,
}) => {
  await install(page)
  await page.goto(`/roles/${PLATFORM_ROLE_ID}?tab=permissions`)
  await page
    .getByRole("button", { name: "Manage permissions", exact: true })
    .click()

  const codes = await offered(page, "Manage permissions")

  expect(codes.length).toBeGreaterThan(0)
  expect(codes).not.toContain("*")
  expect([...codes].sort()).toEqual(["roles:read", "users:read"])
})
