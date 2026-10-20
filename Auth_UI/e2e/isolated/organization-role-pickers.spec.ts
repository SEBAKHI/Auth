import { expect, test, type Page } from "@playwright/test"

import { installAuthenticatedApi } from "./mock-authenticated-api"
import {
  answerOrganizationOwner,
  APP,
  MEMBER_ROLE,
  ORG,
  OWNER_ORG_PERMISSIONS,
  REP_ROLE,
} from "./organization-owner-api"

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
  await installAuthenticatedApi(
    page,
    [],
    (route, url) => answerOrganizationOwner(route, url, recorded, options),
    { organizationPermissions: OWNER_ORG_PERMISSIONS }
  )
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
  await expect(page.getByRole("option")).toHaveText(["Sales representative"])
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
  await expect(page.getByRole("option")).toHaveText(["Sales representative"])

  expect(
    recorded.platformListReads,
    "no picker asks the platform-wide lists"
  ).toEqual([])
})
