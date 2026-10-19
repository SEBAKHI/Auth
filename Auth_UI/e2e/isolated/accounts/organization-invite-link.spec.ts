import { expect, test } from "@playwright/test"

import {
  answerOrganizationOwner,
  APP_CODE,
  ORG,
  ownerApiRecord,
} from "../organization-owner-api"
import { fulfillJson } from "../mock-authenticated-api"
import { installAnonymousApi, loginResponse } from "./mock-anonymous-api"

/**
 * The accounts app is where an application's invite link lands, and its user is
 * often signed out of it: the application's sign-in and this app's are separate
 * sessions. The link must survive the sign-in — path AND query — or the owner
 * lands on the home page and the link did nothing.
 */

const OWNER = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "owner@example.test",
  firstName: "Org",
  lastName: "Owner",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

test("a signed-out owner who follows an invite link signs in and lands on the filled dialog", async ({
  page,
}) => {
  const recorded = ownerApiRecord()
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/login") {
      await fulfillJson(route, loginResponse(OWNER))
      return true
    }
    if (path === "/api/v1/auth/me") {
      const bearer = await route.request().headerValue("authorization")
      await (bearer
        ? fulfillJson(route, OWNER)
        : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
      return true
    }
    return answerOrganizationOwner(route, url, recorded)
  })

  await page.goto(
    `/organizations/${ORG}?invite=1&role=org-member&application=${APP_CODE}&applicationRole=sales_rep`
  )
  await expect(page).toHaveURL(/\/login/)

  await page.getByLabel("Email").fill(OWNER.email)
  await page.getByLabel("Password").fill("Passw0rd!")
  await page.getByRole("button", { name: "Sign in", exact: true }).click()

  const dialog = page.getByRole("dialog", { name: "Invite member" })
  await expect(dialog).toBeVisible()
  await expect(
    dialog.getByRole("combobox", { name: "Role", exact: true })
  ).toHaveText("Organization Member")
  await expect(
    dialog.getByRole("combobox", { name: "Application role", exact: true })
  ).toHaveText("Sales representative")
  expect(recorded.invitations).toEqual([])
})
