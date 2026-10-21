import { expect, type Page } from "@playwright/test"

import { ORG } from "./organization-owner-api"

/**
 * OI-197, shared by the console and accounts projects (the organization page is
 * one page): each control shows only to a person whose token lets its endpoint
 * through. The seeded plain member reads members and applications and may do
 * nothing else; the owner holds `org:*`.
 */

/** The seeded `org-member` role's claims, as the API mints them (`org_perm`). */
export const PLAIN_MEMBER_ORG_PERMISSIONS = [
  `${ORG}:org:members:read`,
  `${ORG}:org:apps:read`,
]

/**
 * Walks the page's controls once the members list has loaded: the header, the
 * members' row menu, then the applications tab's button and row menu. `shown`
 * says whether a person may use them all (the owner) or none (a plain member).
 */
export async function expectOrganizationControls(page: Page, shown: boolean) {
  const memberRow = page.getByRole("row", { name: /colleague@example\.test/ })
  await expect(memberRow).toBeVisible()
  await expect(
    page.getByRole("heading", { name: "Acme Corporation" })
  ).toBeVisible()

  await expect(page.getByRole("button", { name: "Edit" })).toHaveCount(
    shown ? 1 : 0
  )
  await expect(page.getByRole("button", { name: "Avatar" })).toHaveCount(
    shown ? 1 : 0
  )

  const memberActions = memberRow.getByRole("button", { name: "Actions" })
  if (shown) {
    await memberActions.click()
    await expect(page.getByRole("menuitem")).toHaveText([
      "Change role",
      "Manage app roles",
      "Remove member",
    ])
    await page.keyboard.press("Escape")
  } else {
    await expect(memberActions).toHaveCount(0)
  }

  await page.getByRole("tab", { name: "Applications" }).click()
  const appRow = page.getByRole("row", { name: /CRM/ })
  await expect(appRow).toBeVisible()
  await expect(
    page.getByRole("button", { name: "Enable application" })
  ).toHaveCount(shown ? 1 : 0)

  const appActions = appRow.getByRole("button", { name: "Actions" })
  if (shown) {
    await appActions.click()
    await expect(page.getByRole("menuitem")).toHaveText([
      "Edit subscription",
      "Remove",
    ])
    await page.keyboard.press("Escape")
  } else {
    await expect(appActions).toHaveCount(0)
  }
}
