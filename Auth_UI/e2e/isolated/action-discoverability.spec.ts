import { expect, test } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { expectNoShellOverflow } from "./layout-overflow"

const USER_ID = "11111111-1111-1111-1111-111111111111"
const TEMPLATE_ID = "22222222-2222-2222-2222-222222222222"
const DRAFT_ID = "33333333-3333-3333-3333-333333333333"
const PUBLISHED_ID = "44444444-4444-4444-4444-444444444444"
const REVISION = "2026-08-22T08:00:00Z"

const user = {
  id: USER_ID,
  email: "operator@example.test",
  displayName: "Console Operator",
  firstName: "Console",
  lastName: "Operator",
  status: "Active",
  emailConfirmed: false,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: REVISION,
}

const template = {
  id: TEMPLATE_ID,
  notificationTypeId: "55555555-5555-5555-5555-555555555555",
  typeCode: "password-reset",
  typeName: "Password reset",
  typeIsSystem: false,
  typeVariablesJson: "[]",
  typeSampleDataJson: "{}",
  applicationId: null,
  applicationName: null,
  channel: "Email",
  defaultLanguage: "en",
  draftVersionId: DRAFT_ID,
  publishedVersionId: PUBLISHED_ID,
  draftVersion: {
    id: DRAFT_ID,
    versionNumber: 2,
    changeNote: null,
    createdAt: REVISION,
    translations: [],
  },
  publishedVersion: {
    id: PUBLISHED_ID,
    versionNumber: 1,
    changeNote: null,
    createdAt: REVISION,
    translations: [],
  },
  versions: [],
  createdAt: REVISION,
  modifiedAt: REVISION,
}

test("user detail promotes one action and keeps the rest reachable, danger apart", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await installAuthenticatedApi(
    page,
    [
      "users:read",
      "users:update",
      "users:manage-roles",
      "users:manage-permissions",
      "users:manage",
      "users:delete",
    ],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, user)
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/organizations`) {
        await fulfillJson(route, [])
        return true
      }
      return false
    }
  )

  await page.goto(`/users/${USER_ID}`)

  // The primary action is the only one out front. Everything else - including
  // Delete - is one click away in the named menu, at this width and every other.
  const promoted = page.locator('[data-slot="page-action-surface-action"]')
  await expect(promoted).toHaveText(["Edit"])

  await page.getByRole("button", { name: "Actions" }).click()
  const menu = page.getByRole("menu")

  for (const label of [
    "Manage roles",
    "Manage permissions",
    "Send password reset email",
    "Resend confirmation email",
    "Lock",
    "Deactivate",
    "Delete",
  ]) {
    await expect(menu.getByRole("menuitem", { name: label })).toBeVisible()
  }
  // The promoted action is not repeated inside the menu beside it.
  await expect(menu.getByRole("menuitem", { name: "Edit" })).toHaveCount(0)
  await expect(menu.getByRole("menuitem", { name: "Delete" })).toHaveAttribute(
    "data-variant",
    "destructive"
  )
  await expect(
    menu.locator('[data-slot="dropdown-menu-separator"]')
  ).toBeVisible()
  await expect(menu).toHaveScreenshot("user-actions-menu-desktop-en.png", {
    animations: "disabled",
  })

  await page.keyboard.press("Escape")
  await promoted.click()
  await expect(page.getByRole("dialog", { name: "Edit user" })).toBeVisible()
  await expectNoShellOverflow(page, "user actions, 1440 en")
})

test("Arabic mobile template uses a named menu with the complete action contract", async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 667 })
  await installAuthenticatedApi(
    page,
    [
      "notification-templates:read",
      "notification-templates:manage",
      "notification-templates:publish",
    ],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}`) {
        await fulfillJson(route, template)
        return true
      }
      if (path === "/api/v1/notification-templates/preview") {
        await fulfillJson(route, {
          subject: "Preview",
          bodyHtml: "<p>Preview</p>",
          bodyText: "Preview",
        })
        return true
      }
      return false
    },
    { preferredLanguage: "ar" }
  )

  await page.goto(`/notifications/templates/${TEMPLATE_ID}`)
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")

  // Each page promotes its OWN working actions. An editor keeps both the thing
  // it exists for and the thing you do between every other action - so "حفظ
  // المسودة" and "نشر" are buttons here where a user page promotes only
  // "تعديل". Contract order, not sorted: saving comes before publishing.
  await expect(
    page.locator('[data-slot="page-action-surface-action"]')
  ).toHaveText(["حفظ المسودة", "نشر"])

  await page.getByRole("button", { name: "إجراءات" }).click()
  const menu = page.getByRole("menu")
  for (const label of [
    "إرسال تجريبي",
    "سجل الإصدارات",
    "إلغاء النشر",
    "تجاهل المسودة",
    "حذف",
  ]) {
    await expect(
      menu.getByRole("menuitem", { name: label, exact: true })
    ).toBeVisible()
  }
  await expect(menu.getByRole("menuitem", { name: "حذف" })).toHaveAttribute(
    "data-variant",
    "destructive"
  )
  await expect(menu).toHaveScreenshot("template-actions-mobile-ar.png", {
    animations: "disabled",
  })
  await menu.getByRole("menuitem", { name: "إرسال تجريبي" }).click()
  await expect(
    page.getByRole("alertdialog", { name: "إرسال رسالة تجريبية" })
  ).toBeVisible()
  await expectNoShellOverflow(page, "template actions, 375 ar")
})
