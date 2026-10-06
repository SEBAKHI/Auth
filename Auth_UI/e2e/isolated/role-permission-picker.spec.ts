import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * What a role may be given, and the case its code is stored in (OI-74, OI-73).
 *
 * The server refuses a permission whose application is not the role's
 * (Role.PermissionNotForApplication) and an inactive one (Permission.Inactive),
 * so the picker offers neither: offering a certain refusal is a dead end. The
 * list of what the role already holds still reads the whole catalogue, so a
 * mismatched row granted before the rule shows, with its id, and stays
 * removable. And a new role's code is stored lowercase, so the code field
 * lowercases as the operator types.
 */

const EDIS = "33333333-3333-3333-3333-333333333333"
const CRM = "44444444-4444-4444-4444-444444444444"
const APP_ROLE_ID = "55555555-5555-5555-5555-555555555555"
const PLATFORM_ROLE_ID = "66666666-6666-6666-6666-666666666666"

/**
 * The API leaves null properties out of every body (WhenWritingNull), so a
 * platform row carries no applicationId at all, never `null`.
 */
function scope(applicationId: string | null) {
  return applicationId ? { applicationId } : {}
}

function permission(
  id: string,
  code: string,
  applicationId: string | null,
  isActive = true
) {
  return {
    id,
    code,
    name: `Name of ${code}`,
    ...scope(applicationId),
    isWildcard: code.endsWith("*"),
    isActive,
    createdAt: "2026-10-01T09:00:00Z",
  }
}

// The list endpoint returns active permissions only; the two inactive rows
// exercise the picker's own filter, which backs that up.
const CATALOGUE = [
  permission("10000000-0000-0000-0000-000000000001", "*", null),
  permission("10000000-0000-0000-0000-000000000002", "users:read", null),
  permission("10000000-0000-0000-0000-000000000003", "roles:read", null),
  permission(
    "10000000-0000-0000-0000-000000000004",
    "profile:read",
    null,
    false
  ),
  permission("10000000-0000-0000-0000-000000000005", "edis:fairs:view", EDIS),
  permission("10000000-0000-0000-0000-000000000006", "edis:fairs:manage", EDIS),
  permission("10000000-0000-0000-0000-000000000007", "edis:profile:view", EDIS),
  permission(
    "10000000-0000-0000-0000-000000000008",
    "edis:legacy",
    EDIS,
    false
  ),
  permission("10000000-0000-0000-0000-000000000009", "crm:leads:read", CRM),
]

const USERS_READ_ID = "10000000-0000-0000-0000-000000000002"

function role(id: string, applicationId: string | null, permissions: string[]) {
  return {
    id,
    ...scope(applicationId),
    applicationName: applicationId ? "EDIS" : undefined,
    code: applicationId ? "institution_manager" : "support-agent",
    name: applicationId ? "Institution manager" : "Support agent",
    isSystem: false,
    isActive: true,
    createdAt: "2026-10-01T09:00:00Z",
    permissions,
  }
}

const ROLES = {
  // users:read is a platform code granted to an EDIS role before the rule.
  [APP_ROLE_ID]: role(APP_ROLE_ID, EDIS, ["edis:fairs:view", "users:read"]),
  [PLATFORM_ROLE_ID]: role(PLATFORM_ROLE_ID, null, ["roles:read"]),
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

async function installRoles(
  page: Page,
  calls: { method: string; path: string; body?: unknown }[]
) {
  await installAuthenticatedApi(
    page,
    ["roles:read", "roles:create", "roles:update", "permissions:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (method !== "GET") {
        calls.push({
          method,
          path,
          body: route.request().postData()
            ? route.request().postDataJSON()
            : undefined,
        })
      }

      if (path === "/api/v1/permissions") {
        await fulfillJson(route, CATALOGUE)
        return true
      }
      const detail = /^\/api\/v1\/roles\/([0-9a-f-]+)$/.exec(path)
      if (detail && method === "GET") {
        await fulfillJson(route, ROLES[detail[1] as keyof typeof ROLES])
        return true
      }
      if (
        /^\/api\/v1\/roles\/[0-9a-f-]+\/permissions\/[0-9a-f-]+$/.test(path)
      ) {
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === "/api/v1/roles" && method === "POST") {
        await fulfillJson(route, role(PLATFORM_ROLE_ID, null, []), 201)
        return true
      }
      if (path === "/api/v1/roles") {
        await fulfillJson(route, Object.values(ROLES))
        return true
      }
      await fulfillJson(route, EMPTY_LIST)
      return true
    }
  )
}

/** The codes the picker offers, in the order it lists them. */
async function offeredCodes(page: Page, roleId: string) {
  await page.goto(`/roles/${roleId}?tab=permissions`)
  await page
    .getByRole("button", { name: "Manage permissions", exact: true })
    .click()
  const dialog = page.getByRole("dialog", { name: "Manage permissions" })
  await dialog.getByRole("combobox").click()
  const options = page.getByRole("option")
  // Floor: an empty list would satisfy every "does not offer" below.
  await expect(options.first()).toBeVisible()
  const texts = await options.allInnerTexts()
  return { dialog, codes: texts.map((text) => text.split("\n")[0].trim()) }
}

test("an application role is offered only its application's active codes", async ({
  page,
}) => {
  await installRoles(page, [])
  const { codes } = await offeredCodes(page, APP_ROLE_ID)

  expect(codes.length).toBeGreaterThan(0)
  // edis:fairs:view is already held; edis:legacy is inactive.
  expect([...codes].sort()).toEqual(["edis:fairs:manage", "edis:profile:view"])
  for (const absent of [
    "*",
    "users:read",
    "roles:read",
    "crm:leads:read",
    "edis:legacy",
  ]) {
    expect(codes).not.toContain(absent)
  }
})

test("a platform role is offered the platform's active codes, the wildcard included", async ({
  page,
}) => {
  await installRoles(page, [])
  const { codes } = await offeredCodes(page, PLATFORM_ROLE_ID)

  expect(codes.length).toBeGreaterThan(0)
  // roles:read is already held; profile:read is inactive.
  expect([...codes].sort()).toEqual(["*", "users:read"])
})

test("a mismatched code granted earlier still shows and can be removed", async ({
  page,
}) => {
  const calls: { method: string; path: string }[] = []
  await installRoles(page, calls)
  await page.goto(`/roles/${APP_ROLE_ID}?tab=permissions`)
  await page
    .getByRole("button", { name: "Manage permissions", exact: true })
    .click()
  const dialog = page.getByRole("dialog", { name: "Manage permissions" })

  const chip = dialog.locator('[data-slot="badge"]', { hasText: "users:read" })
  await expect(chip).toBeVisible()
  await chip.getByRole("button", { name: "Remove", exact: true }).click()
  await dialog.getByRole("button", { name: "Save (1)", exact: true }).click()
  await page
    .getByRole("alertdialog", { name: "Apply these changes?" })
    .getByRole("button", { name: "Save", exact: true })
    .click()

  // The removal is addressed by the permission's id, which only the whole
  // catalogue could supply for a code the picker no longer offers.
  await expect
    .poll(() => calls.map((call) => `${call.method} ${call.path}`))
    .toContain(
      `DELETE /api/v1/roles/${APP_ROLE_ID}/permissions/${USERS_READ_ID}`
    )
})

test("the code field lowercases as the operator types, and the role is created lowercase", async ({
  page,
}) => {
  const calls: { method: string; path: string; body?: unknown }[] = []
  await installRoles(page, calls)
  await page.goto("/roles")
  await page.getByRole("button", { name: "New role", exact: true }).click()
  const dialog = page.getByRole("dialog", { name: "Create role" })

  const code = dialog.getByLabel("Code")
  await code.pressSequentially("Agent")
  await expect(code).toHaveValue("agent")
  // Typing before the existing text: the caret must stay where the operator
  // put it, or the prefix would land at the end.
  await code.press("Home")
  await code.pressSequentially("Support-")
  await expect(code).toHaveValue("support-agent")

  await dialog.getByLabel("Name").fill("Support agent")
  await dialog.getByRole("button", { name: "Create", exact: true }).click()

  await expect
    .poll(() => calls.find((call) => call.method === "POST")?.body)
    .toMatchObject({ code: "support-agent" })
})
