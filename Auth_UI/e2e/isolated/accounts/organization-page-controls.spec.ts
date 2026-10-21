import { test } from "@playwright/test"

import { fulfillJson } from "../mock-authenticated-api"
import {
  expectOrganizationControls,
  PLAIN_MEMBER_ORG_PERMISSIONS,
} from "../organization-page-controls"
import {
  answerOrganizationOwner,
  ORG,
  OWNER_ORG_PERMISSIONS,
  ownerApiRecord,
} from "../organization-owner-api"
import { installAnonymousApi, loginResponse } from "./mock-anonymous-api"

/**
 * OI-197 on the built accounts app: a person signs in from the organization
 * page's link, and the page then shows only the controls their membership
 * lets them use; the owner sees them all.
 */

const PERSON = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "person@example.test",
  firstName: "Org",
  lastName: "Person",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

for (const [who, organizationPermissions, shown] of [
  ["a plain member", PLAIN_MEMBER_ORG_PERMISSIONS, false],
  ["the owner", OWNER_ORG_PERMISSIONS, true],
] as const) {
  test(`${who} sees ${shown ? "every" : "no"} control that changes the organization`, async ({
    page,
  }) => {
    await installAnonymousApi(page, async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/auth/login") {
        await fulfillJson(
          route,
          loginResponse(PERSON, [...organizationPermissions])
        )
        return true
      }
      if (path === "/api/v1/auth/me") {
        const bearer = await route.request().headerValue("authorization")
        await (bearer
          ? fulfillJson(route, PERSON)
          : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
        return true
      }
      return answerOrganizationOwner(route, url, ownerApiRecord())
    })

    await page.goto(`/organizations/${ORG}`)
    await page.getByLabel("Email").fill(PERSON.email)
    await page.getByLabel("Password").fill("Passw0rd!")
    await page.getByRole("button", { name: "Sign in", exact: true }).click()

    await expectOrganizationControls(page, shown)
  })
}
