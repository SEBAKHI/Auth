import { expect, test, type Page } from "@playwright/test"

import { installAuthenticatedApi } from "./mock-authenticated-api"
import {
  answerOrganizationOwner,
  APP,
  APP_CODE,
  MEMBER_ROLE,
  ORG,
  OWNER_ORG_PERMISSIONS,
  ownerApiRecord,
  REP_ROLE,
  type OwnerApiRecord,
} from "./organization-owner-api"

/**
 * An application sends its user to the organization page with the invite dialog
 * already filled: `?invite=1&role=…&application=…&applicationRole=…`. The link
 * fills, never submits, and never carries the invitee's address; a value the
 * server's lists do not hold leaves its field empty. Walked in the built app with
 * every request fulfilled and every invitation recorded.
 */

async function signedInOwner(page: Page): Promise<OwnerApiRecord> {
  const recorded = ownerApiRecord()
  await installAuthenticatedApi(
    page,
    [],
    (route, url) => answerOrganizationOwner(route, url, recorded),
    { organizationPermissions: OWNER_ORG_PERMISSIONS }
  )
  return recorded
}

const link = (query: string) => `/organizations/${ORG}?${query}`

test("a link opens the invite dialog filled with its choices, and sends only what the owner confirms", async ({
  page,
}) => {
  const recorded = await signedInOwner(page)

  // No tab parameter: the link itself opens the invitations tab.
  await page.goto(
    link(
      `invite=1&role=org-member&application=${APP_CODE}&applicationRole=sales_rep`
    )
  )
  const dialog = page.getByRole("dialog", { name: "Invite member" })
  await expect(dialog).toBeVisible()

  await expect(
    dialog.getByRole("combobox", { name: "Role", exact: true })
  ).toHaveText("Organization Member")
  await expect(
    dialog.getByRole("combobox", { name: "Application (optional)" })
  ).toHaveText("CRM")
  await expect(
    dialog.getByRole("combobox", { name: "Application role", exact: true })
  ).toHaveText("Sales representative")

  // The address is the owner's to type: the link cannot choose whom it invites.
  await expect(dialog.getByLabel("Email to invite")).toHaveValue("")
  const send = dialog.getByRole("button", { name: "Invite member" })
  await expect(send).toBeDisabled()
  expect(recorded.invitations, "a link never submits").toEqual([])

  await dialog.getByLabel("Email to invite").fill("rep@example.test")
  await send.click()
  await expect(dialog).toBeHidden()

  expect(recorded.invitations).toEqual([
    {
      email: "rep@example.test",
      roleId: MEMBER_ROLE,
      applicationRole: { applicationId: APP, roleId: REP_ROLE },
    },
  ])

  // Spent: the link's parameters are gone, the invitations tab stays.
  const url = new URL(page.url())
  expect(Object.fromEntries(url.searchParams)).toEqual({ tab: "invitations" })
  await expect(page.getByRole("tab", { name: "Invitations" })).toHaveAttribute(
    "aria-selected",
    "true"
  )
})

test("a value the server's lists do not hold leaves its field empty", async ({
  page,
}) => {
  await signedInOwner(page)

  // org-owner is never offered; an application the organization has not enabled
  // is not in its list.
  await page.goto(
    link(
      "invite=1&role=org-owner&application=BILLING&applicationRole=sales_rep"
    )
  )
  const dialog = page.getByRole("dialog", { name: "Invite member" })
  await expect(dialog).toBeVisible()

  await expect(
    dialog.getByRole("combobox", { name: "Role", exact: true })
  ).toHaveText("Role")
  await expect(
    dialog.getByRole("combobox", { name: "Application (optional)" })
  ).toHaveText("No application role")
  await expect(
    dialog.getByRole("combobox", { name: "Application role", exact: true })
  ).toHaveCount(0)
})

test("a role the owner may not assign leaves the application chosen and its role empty", async ({
  page,
}) => {
  await signedInOwner(page)

  await page.goto(
    link(
      `invite=1&role=org-member&application=${APP_CODE}&applicationRole=sales_manager`
    )
  )
  const dialog = page.getByRole("dialog", { name: "Invite member" })
  await expect(
    dialog.getByRole("combobox", { name: "Application (optional)" })
  ).toHaveText("CRM")
  await expect(
    dialog.getByRole("combobox", { name: "Application role", exact: true })
  ).toHaveText("Select role")
  await expect(
    dialog.getByRole("button", { name: "Invite member" })
  ).toBeDisabled()
})

test("a spent link does not reopen the dialog on refresh", async ({ page }) => {
  await signedInOwner(page)

  await page.goto(link(`invite=1&role=org-member`))
  const dialog = page.getByRole("dialog", { name: "Invite member" })
  await expect(dialog).toBeVisible()
  await dialog.getByRole("button", { name: "Cancel" }).click()
  await expect(dialog).toBeHidden()

  await page.reload()
  await expect(page.getByRole("tab", { name: "Invitations" })).toHaveAttribute(
    "aria-selected",
    "true"
  )
  await expect(page.getByRole("dialog")).toHaveCount(0)
})

test("without invite=1 the page ignores the other parameters", async ({
  page,
}) => {
  await signedInOwner(page)

  await page.goto(link("tab=invitations&role=org-member"))
  await expect(
    page.getByRole("button", { name: "Invite member" })
  ).toBeVisible()
  await expect(page.getByRole("dialog")).toHaveCount(0)
})
