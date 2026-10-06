import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * An application's allowed OAuth scopes, as an administrator sets them (OI-58).
 *
 * The edit dialog is a full replacement, so what matters is the request body:
 * the boxes must start from the saved list, a save must send the whole list in
 * the server's canonical order, and unticking everything must send [] (clear to
 * openid only) — never omit the field, which the server reads as "unchanged".
 * openid is every application's: stated, never offered as a box.
 */

const APP_ID = "33333333-3333-3333-3333-333333333333"

const application = {
  id: APP_ID,
  name: "EDIS",
  code: "EDIS",
  isActive: true,
  accessMode: "Restricted",
  allowSelfRegistration: false,
  requireTwoFactor: false,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 60,
  maxConcurrentSessions: 5,
  reauthenticationMaxAgeMinutes: null,
  redirectUris: ["https://edis.example.com/callback"],
  allowedScopes: ["email"],
  createdAt: "2026-10-01T09:00:00Z",
  modifiedAt: "2026-10-02T09:00:00Z",
  createdByName: "Super System Administrator",
  modifiedByName: "Super System Administrator",
}

async function installApplication(
  page: Page,
  preferredLanguage: string,
  sentBodies: unknown[],
  allowedScopes: string[] = application.allowedScopes
) {
  await installAuthenticatedApi(
    page,
    ["applications:read", "applications:update"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === `/api/v1/applications/${APP_ID}`) {
        if (route.request().method() === "PUT") {
          sentBodies.push(route.request().postDataJSON())
        }
        await fulfillJson(route, { ...application, allowedScopes })
        return true
      }
      // The detail page's tabs each load a list; one empty envelope serves them all.
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
    },
    { preferredLanguage }
  )
}

async function openEditDialog(page: Page) {
  await page.goto(`/applications/${APP_ID}`)
  await page.getByRole("button", { name: /^(Edit|تعديل)$/ }).click()
  const dialog = page.getByRole("dialog")
  await expect(dialog.getByRole("checkbox", { name: "phone" })).toBeVisible()
  return dialog
}

test("the detail page lists openid and the allowed scopes", async ({
  page,
}) => {
  await installApplication(page, "en", [])
  await page.goto(`/applications/${APP_ID}`)

  const row = page
    .locator("dt", { hasText: "Allowed scopes" })
    .locator("xpath=following-sibling::dd[1]")
  await expect(row).toHaveText(/openid\s*email/)
  await expect(
    row.getByText(/already carries the user's email address and name/)
  ).toBeVisible()
})

test("the edit dialog starts from the saved scopes and saves the full canonical list", async ({
  page,
}) => {
  const sent: unknown[] = []
  await installApplication(page, "en", sent)
  const dialog = await openEditDialog(page)

  // Three boxes, no openid box: openid is stated in the description instead.
  // The boxes form one group named by the field's label, so a screen reader
  // hears the question before the answers.
  await expect(dialog.getByRole("checkbox")).toHaveCount(3)
  await expect(
    dialog.getByRole("group", { name: "Allowed scopes" }).getByRole("checkbox")
  ).toHaveCount(3)
  await expect(dialog.getByRole("checkbox", { name: "openid" })).toHaveCount(0)
  await expect(
    dialog.getByText(/openid, which identifies the user, is always included/)
  ).toBeVisible()
  // An unticked email or profile box must not read as "withheld": every
  // access token already carries the user's email address and name.
  await expect(
    dialog.getByText(
      /every application already carries the user's email address and name, whatever is ticked here/
    )
  ).toBeVisible()
  // Each scope says what UserInfo returns for it, as a fact: UserInfo filters by
  // scope (X11), so no hint promises it for a later release.
  await expect(
    dialog.getByText(
      "UserInfo returns the user's phone number to the application."
    )
  ).toBeVisible()
  await expect(dialog.getByText(/next release/)).toHaveCount(0)
  // With no consent screen this copy is the only disclosure of what profile releases.
  await expect(
    dialog.getByText(
      "UserInfo returns the user's name, profile picture, language and time zone to the application."
    )
  ).toBeVisible()
  await expect(
    dialog.getByRole("checkbox", { name: "profile" })
  ).not.toBeChecked()
  await expect(dialog.getByRole("checkbox", { name: "email" })).toBeChecked()
  await expect(
    dialog.getByRole("checkbox", { name: "phone" })
  ).not.toBeChecked()

  // Ticked out of order on purpose; the body must still be canonical.
  await dialog.getByRole("checkbox", { name: "phone" }).check()
  await dialog.getByRole("checkbox", { name: "profile" }).check()
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect.poll(() => sent.length).toBe(1)
  expect(sent[0]).toMatchObject({
    name: "EDIS",
    redirectUris: ["https://edis.example.com/callback"],
    allowedScopes: ["profile", "email", "phone"],
  })
})

test("unticking every scope sends an empty list, never an absent field", async ({
  page,
}) => {
  const sent: unknown[] = []
  await installApplication(page, "en", sent)
  const dialog = await openEditDialog(page)

  await dialog.getByRole("checkbox", { name: "email" }).uncheck()
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect.poll(() => sent.length).toBe(1)
  expect(sent[0]).toHaveProperty("allowedScopes", [])
})

test("a scope the console does not offer survives a save", async ({ page }) => {
  // A server newer than this console may allow a scope it has no box for.
  // Ticking another box must not drop it.
  const sent: unknown[] = []
  await installApplication(page, "en", sent, ["email", "future_scope"])
  const dialog = await openEditDialog(page)

  await dialog.getByRole("checkbox", { name: "phone" }).check()
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect.poll(() => sent.length).toBe(1)
  expect(sent[0]).toHaveProperty("allowedScopes", [
    "email",
    "phone",
    "future_scope",
  ])
})

test("ar: each scope keeps its standard name and reads its effect in Arabic", async ({
  page,
}) => {
  await installApplication(page, "ar", [])
  const dialog = await openEditDialog(page)

  // The scope names are protocol words and are not translated; what each one
  // allows is. No direction is forced on them (memory: never force LTR on codes).
  for (const scope of ["profile", "email", "phone"]) {
    const box = dialog.getByRole("checkbox", { name: scope })
    await expect(box).toBeVisible()
    await expect(
      dialog.locator(`label[for="${await box.getAttribute("id")}"]`)
    ).not.toHaveAttribute("dir", /.*/)
  }
  await expect(
    dialog.getByText("يُرجع UserInfo إلى التطبيق رقم هاتف المستخدم.")
  ).toBeVisible()
  // UserInfo filters by scope now (X11): no hint promises it for a later release.
  await expect(dialog.getByText(/الإصدار القادم/)).toHaveCount(0)
  await expect(
    dialog.getByText(
      "يُرجع UserInfo إلى التطبيق اسم المستخدم وصورته الشخصية ولغته ومنطقته الزمنية."
    )
  ).toBeVisible()
  await expect(
    dialog.getByText(/رمز الوصول لكل تطبيق يحمل أصلًا عنوان البريد الإلكتروني/)
  ).toBeVisible()
  await expect(dialog.getByText(/applications\.scope/)).toHaveCount(0)
})
