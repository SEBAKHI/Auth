import { test } from "@playwright/test"

import { installAuthenticatedApi } from "./mock-authenticated-api"
import {
  expectOrganizationControls,
  PLAIN_MEMBER_ORG_PERMISSIONS,
} from "./organization-page-controls"
import {
  answerOrganizationOwner,
  ORG,
  OWNER_ORG_PERMISSIONS,
  ownerApiRecord,
} from "./organization-owner-api"

/**
 * OI-197 on the built console: a plain member sees none of the organization
 * page's controls that the API would refuse them, and the owner sees them all.
 * Neither holds a platform permission.
 */

for (const [who, organizationPermissions, shown] of [
  ["a plain member", PLAIN_MEMBER_ORG_PERMISSIONS, false],
  ["the owner", OWNER_ORG_PERMISSIONS, true],
] as const) {
  test(`${who} sees ${shown ? "every" : "no"} control that changes the organization`, async ({
    page,
  }) => {
    const recorded = ownerApiRecord()
    await installAuthenticatedApi(
      page,
      [],
      (route, url) => answerOrganizationOwner(route, url, recorded),
      { organizationPermissions: [...organizationPermissions] }
    )

    await page.goto(`/organizations/${ORG}`)

    await expectOrganizationControls(page, shown)
  })
}
