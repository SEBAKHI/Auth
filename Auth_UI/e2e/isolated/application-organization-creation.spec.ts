import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * An application's organization-creation settings in the console (OI-63), driven
 * through the real Select. The edit dialog is a full replacement, but these two
 * fields are sent only when they changed: null tells the API "unchanged", so a
 * rename never re-submits, or corrupts, a creator role nobody touched.
 */

const APP_ID = "44444444-4444-4444-4444-444444444444"
const INACTIVE_ROLE = "55555555-5555-5555-5555-555555555555"
const ACTIVE_ROLE = "66666666-6666-6666-6666-666666666666"

const application = {
  id: APP_ID,
  name: "EDIS",
  code: "EDIS",
  isActive: true,
  accessMode: "Everyone",
  allowSelfRegistration: false,
  requireTwoFactor: false,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 60,
  maxConcurrentSessions: 5,
  reauthenticationMaxAgeMinutes: null,
  redirectUris: ["https://edis.example.com/callback"],
  allowedScopes: [],
  allowOrganizationCreation: true,
  // Deactivated since it was saved: the case a Select would otherwise reset.
  organizationCreatorRoleId: INACTIVE_ROLE,
  createdAt: "2026-10-01T09:00:00Z",
  modifiedAt: "2026-10-02T09:00:00Z",
}

const roles = [
  { id: INACTIVE_ROLE, name: "Institution manager", isActive: false },
  { id: ACTIVE_ROLE, name: "Visitor", isActive: true },
]

async function install(page: Page, sent: unknown[]) {
  await installAuthenticatedApi(
    page,
    ["applications:read", "applications:update"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/applications/${APP_ID}`) {
        if (route.request().method() === "PUT") {
          sent.push(route.request().postDataJSON())
        }
        await fulfillJson(route, application)
        return true
      }
      if (path === `/api/v1/applications/${APP_ID}/roles`) {
        await fulfillJson(route, roles)
        return true
      }
      await fulfillJson(
        route,
        Object.assign([], {
          users: [],
          items: [],
          organizations: [],
          roles: [],
          permissions: [],
          totalCount: 0,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 20,
        })
      )
      return true
    }
  )
}

async function openEditDialog(page: Page) {
  await page.goto(`/applications/${APP_ID}`)
  await page.getByRole("button", { name: "Edit" }).click()
  const dialog = page.getByRole("dialog")
  await expect(dialog.getByRole("switch", { name: /Organization creation/ })).toBeVisible()
  return dialog
}

test("a rename leaves the organization-creation settings unchanged, even with an inactive stored role", async ({
  page,
}) => {
  const sent: unknown[] = []
  await install(page, sent)
  const dialog = await openEditDialog(page)

  await expect(dialog.getByRole("combobox", { name: "Creator role" })).toHaveText(
    /Institution manager \(Inactive\)/
  )
  await dialog.getByRole("textbox", { name: "Name" }).fill("EDIS renamed")
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect.poll(() => sent.length).toBe(1)
  expect(sent[0]).toMatchObject({
    name: "EDIS renamed",
    allowOrganizationCreation: null,
    organizationCreatorRoleId: null,
  })
})

test("choosing another role sends both settings", async ({ page }) => {
  const sent: unknown[] = []
  await install(page, sent)
  const dialog = await openEditDialog(page)

  await dialog.getByRole("combobox", { name: "Creator role" }).click()
  await page.getByRole("option", { name: "Visitor" }).click()
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect.poll(() => sent.length).toBe(1)
  expect(sent[0]).toMatchObject({
    allowOrganizationCreation: true,
    organizationCreatorRoleId: ACTIVE_ROLE,
  })
})

test("creation on without a role is refused at the field, and nothing is sent", async ({
  page,
}) => {
  const sent: unknown[] = []
  await install(page, sent)
  const dialog = await openEditDialog(page)

  await dialog.getByRole("combobox", { name: "Creator role" }).click()
  await page.getByRole("option", { name: "None" }).click()
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect(dialog.getByText("This field is required.")).toBeVisible()
  expect(sent).toHaveLength(0)
})

test("the detail page shows both settings", async ({ page }) => {
  await install(page, [])
  await page.goto(`/applications/${APP_ID}`)

  const value = (label: string) =>
    page.locator("dt", { hasText: label }).locator("xpath=following-sibling::dd[1]")
  await expect(value("Organization creation from this application")).toHaveText("Yes")
  await expect(value("Creator role")).toHaveText("Institution manager")
})
