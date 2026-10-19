import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { fulfillProblem, problem } from "./problem"

/**
 * An organization's owner holds no platform permission. The organization page's
 * role pickers used to fill themselves from the platform-wide role lists, which
 * answered that owner with 403 — and the pickers showed an empty list with no
 * message, so the owner could not invite anyone, change a member's role, or give
 * a member an application role.
 *
 * Here the platform lists answer 403 exactly as the API does for such an owner,
 * the organization's own lists answer, and every write is recorded, so the
 * assertions cover what the dialogs offered, what they SENT, and that they never
 * asked the platform again.
 */

const ORG = "11111111-1111-1111-1111-111111111111"
const ADMIN_ROLE = "22222222-2222-2222-2222-222222222221"
const MEMBER_ROLE = "22222222-2222-2222-2222-222222222222"
const APP = "33333333-3333-3333-3333-333333333333"
const REP_ROLE = "44444444-4444-4444-4444-444444444444"
const MEMBER_USER = "55555555-5555-5555-5555-555555555555"

interface Recorded {
  platformListReads: string[]
  invitations: unknown[]
}

async function installOwnerApi(
  page: Page,
  recorded: Recorded,
  options: { membershipRolesFail?: boolean } = {}
) {
  // No platform permission at all: an organization owner and nothing more.
  await installAuthenticatedApi(page, [], async (route, url) => {
    // The client sends /api/v1/Organizations/…: compare case-insensitively.
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    const org = `/api/v1/organizations/${ORG}`

    if (
      path === "/api/v1/roles" ||
      /^\/api\/v1\/applications\/[^/]+\/roles$/.test(path)
    ) {
      recorded.platformListReads.push(path)
      await fulfillProblem(route, problem(403, "Auth.PermissionDenied"))
      return true
    }
    if (path === org && method === "GET") {
      await fulfillJson(route, {
        id: ORG,
        code: "acme",
        name: "Acme Corporation",
        contactEmail: "owner@example.test",
        ownerId: "99999999-9999-9999-9999-999999999999",
        isActive: true,
        memberCount: 2,
        enabledAppCount: 1,
      })
      return true
    }
    if (path === `${org}/membership-roles`) {
      if (options.membershipRolesFail) {
        await fulfillProblem(route, problem(403, "Auth.PermissionDenied"))
        return true
      }
      await fulfillJson(route, [
        { id: ADMIN_ROLE, code: "org-admin", name: "Organization Admin" },
        { id: MEMBER_ROLE, code: "org-member", name: "Organization Member" },
      ])
      return true
    }
    if (path === `${org}/applications`) {
      await fulfillJson(route, [
        {
          applicationId: APP,
          applicationName: "CRM",
          isActive: true,
          assignedUserCount: 0,
        },
      ])
      return true
    }
    if (path === `${org}/applications/${APP}/assignable-roles`) {
      await fulfillJson(route, [
        {
          id: REP_ROLE,
          code: "sales_rep",
          name: "Sales representative",
        },
      ])
      return true
    }
    if (path === `${org}/invitations` && method === "GET") {
      await fulfillJson(route, [])
      return true
    }
    if (path === `${org}/invitations` && method === "POST") {
      recorded.invitations.push(route.request().postDataJSON())
      await fulfillJson(
        route,
        { id: "66666666-6666-6666-6666-666666666666" },
        201
      )
      return true
    }
    if (path === `${org}/members`) {
      await fulfillJson(route, {
        members: [
          {
            id: "77777777-7777-7777-7777-777777777777",
            userId: MEMBER_USER,
            email: "colleague@example.test",
            fullName: "A Colleague",
            roleId: MEMBER_ROLE,
            roleName: "Organization Member",
            isActive: true,
          },
        ],
        totalCount: 1,
        totalPages: 1,
      })
      return true
    }
    if (path === `${org}/members/${MEMBER_USER}/roles`) {
      await fulfillJson(route, [])
      return true
    }
    return false
  })
}

test("an owner without platform permissions invites a member with an application role in one step", async ({
  page,
}) => {
  const recorded: Recorded = { platformListReads: [], invitations: [] }
  await installOwnerApi(page, recorded)

  await page.goto(`/organizations/${ORG}?tab=invitations`)
  await page.getByRole("button", { name: "Invite member" }).click()
  const dialog = page.getByRole("dialog", { name: "Invite member" })

  await dialog.getByLabel("Email to invite").fill("rep@example.test")

  await dialog.getByRole("combobox", { name: "Role", exact: true }).click()
  await expect(page.getByRole("option")).toHaveText([
    "Organization Admin",
    "Organization Member",
  ])
  await page.getByRole("option", { name: "Organization Member" }).click()

  await dialog.getByRole("combobox", { name: "Application (optional)" }).click()
  await page.getByRole("option", { name: "CRM" }).click()

  // An application without its role is incomplete, so the invitation waits for it.
  const send = dialog.getByRole("button", { name: "Invite member" })
  await expect(send).toBeDisabled()

  await dialog
    .getByRole("combobox", { name: "Application role", exact: true })
    .click()
  await expect(page.getByRole("option")).toHaveText([
    "Sales representative",
  ])
  await page.getByRole("option", { name: "Sales representative" }).click()

  await expect(send).toBeEnabled()
  await send.click()
  await expect(dialog).toBeHidden()

  expect(recorded.invitations).toEqual([
    {
      email: "rep@example.test",
      roleId: MEMBER_ROLE,
      applicationRole: { applicationId: APP, roleId: REP_ROLE },
    },
  ])
  expect(
    recorded.platformListReads,
    "no picker asks the platform-wide lists"
  ).toEqual([])
})

test("a role list that fails to load says why instead of showing an empty list", async ({
  page,
}) => {
  const recorded: Recorded = { platformListReads: [], invitations: [] }
  await installOwnerApi(page, recorded, { membershipRolesFail: true })

  await page.goto(`/organizations/${ORG}?tab=invitations`)
  await page.getByRole("button", { name: "Invite member" }).click()
  const dialog = page.getByRole("dialog", { name: "Invite member" })

  const roleField = dialog.getByRole("combobox", { name: "Role", exact: true })
  await expect(roleField).toHaveAttribute("aria-invalid", "true")
  await expect(dialog.getByRole("alert")).not.toHaveText("")
  await expect(
    dialog.getByRole("button", { name: "Invite member" })
  ).toBeDisabled()
})

test("an owner changes a member's role and assigns an application role from the organization's own lists", async ({
  page,
}) => {
  const recorded: Recorded = { platformListReads: [], invitations: [] }
  await installOwnerApi(page, recorded)

  await page.goto(`/organizations/${ORG}`)
  const row = page.getByRole("row", { name: /colleague@example\.test/ })

  await row.getByRole("button", { name: "Actions" }).click()
  await page.getByRole("menuitem", { name: "Change role" }).click()
  const changeRole = page.getByRole("dialog", { name: "Change role" })
  await changeRole.getByRole("combobox", { name: "Role", exact: true }).click()
  await expect(page.getByRole("option")).toHaveText([
    "Organization Admin",
    "Organization Member",
  ])
  await page.keyboard.press("Escape")
  await changeRole.getByRole("button", { name: "Cancel" }).click()
  await expect(changeRole).toBeHidden()

  await row.getByRole("button", { name: "Actions" }).click()
  await page.getByRole("menuitem", { name: "Manage app roles" }).click()
  const appRoles = page.getByRole("dialog", { name: "Manage app roles" })
  await appRoles
    .getByRole("combobox")
    .filter({ hasText: "Select application" })
    .click()
  await page.getByRole("option", { name: "CRM" }).click()
  await appRoles
    .getByRole("combobox")
    .filter({ hasText: "Select role" })
    .click()
  await expect(page.getByRole("option")).toHaveText([
    "Sales representative",
  ])

  expect(
    recorded.platformListReads,
    "no picker asks the platform-wide lists"
  ).toEqual([])
})
