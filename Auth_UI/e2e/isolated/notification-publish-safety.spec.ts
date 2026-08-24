import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { clickPageAction } from "./page-actions"

const TEMPLATE_ID = "11111111-1111-1111-1111-111111111111"
const DRAFT_VERSION_ID = "22222222-2222-2222-2222-222222222222"
const PUBLISHED_VERSION_ID = "33333333-3333-3333-3333-333333333333"
const LAYOUT_ID = "44444444-4444-4444-4444-444444444444"
const REVISION = "2026-08-21T07:00:00Z"

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
  draftVersionId: DRAFT_VERSION_ID,
  publishedVersionId: PUBLISHED_VERSION_ID,
  draftVersion: {
    id: DRAFT_VERSION_ID,
    versionNumber: 2,
    changeNote: null,
    createdAt: REVISION,
    createdBy: "99999999-9999-9999-9999-999999999999",
    translations: [],
  },
  publishedVersion: {
    id: PUBLISHED_VERSION_ID,
    versionNumber: 1,
    changeNote: null,
    createdAt: REVISION,
    createdBy: "99999999-9999-9999-9999-999999999999",
    translations: [],
  },
  versions: [],
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: REVISION,
}

const layout = {
  id: LAYOUT_ID,
  applicationId: "66666666-6666-6666-6666-666666666666",
  applicationName: "Customer portal",
  channel: "Email",
  name: "Default email layout",
  draftContent: "<html>{{ content | raw }}</html>",
  draftStringsJson: "{}",
  isPublished: true,
  hasUnpublishedChanges: true,
  publishedAt: REVISION,
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: REVISION,
}

const overlays = (page: Page) =>
  page.locator(
    '[data-slot="alert-dialog-overlay"], [data-slot="dialog-overlay"]'
  )

test("template publish/unpublish are confirmed, single-flight, and leave no overlay", async ({
  page,
}) => {
  const publishBodies: unknown[] = []
  const unpublishBodies: unknown[] = []
  let releasePublish!: () => void
  const publishRelease = new Promise<void>((resolve) => {
    releasePublish = resolve
  })
  let currentTemplate = template

  await installAuthenticatedApi(
    page,
    ["notification-templates:read", "notification-templates:publish"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}`) {
        await fulfillJson(route, currentTemplate)
        return true
      }
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}/publish`) {
        publishBodies.push(route.request().postDataJSON())
        await publishRelease
        currentTemplate = {
          ...template,
          draftVersionId: null,
          draftVersion: null,
          publishedVersionId: DRAFT_VERSION_ID,
          publishedVersion: template.draftVersion,
        }
        await fulfillJson(route, currentTemplate)
        return true
      }
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}/unpublish`) {
        unpublishBodies.push(route.request().postDataJSON())
        currentTemplate = {
          ...currentTemplate,
          publishedVersionId: null,
          publishedVersion: null,
        }
        await fulfillJson(route, currentTemplate)
        return true
      }
      return false
    }
  )

  await page.goto(`/notifications/templates/${TEMPLATE_ID}`)
  const publish = page.getByRole("button", { name: "Publish", exact: true })
  await expect(publish).toBeVisible()

  await publish.click()
  await expect(
    page.getByRole("alertdialog", { name: "Publish Password reset?" })
  ).toBeVisible()
  await expect(page.getByText("Draft v2 ·", { exact: false })).toBeVisible()
  await expect(page.getByText("Global (all applications)")).toBeVisible()
  expect(publishBodies).toHaveLength(0)

  await page.getByRole("button", { name: "Cancel" }).click()
  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  expect(publishBodies).toHaveLength(0)

  await publish.click()
  const dialog = page.getByRole("alertdialog")
  const confirm = dialog.getByRole("button", { name: /Publish$/ })
  await confirm.dblclick()
  await expect.poll(() => publishBodies.length).toBe(1)
  await expect(confirm).toBeDisabled()
  await expect(dialog.getByRole("button", { name: "Cancel" })).toBeDisabled()
  await page.keyboard.press("Escape")
  await expect(dialog).toBeVisible()

  releasePublish()
  await expect(dialog).toHaveCount(0)
  await expect(overlays(page)).toHaveCount(0)
  expect(publishBodies).toEqual([
    { expectedDraftVersionId: DRAFT_VERSION_ID, expectedRevisionAt: REVISION },
  ])

  await clickPageAction(page, "Unpublish")
  await expect(
    page.getByRole("alertdialog").getByText("Published v2")
  ).toBeVisible()
  expect(unpublishBodies).toHaveLength(0)
  await page.getByRole("button", { name: "Cancel" }).click()
  expect(unpublishBodies).toHaveLength(0)

  await clickPageAction(page, "Unpublish")
  await page
    .getByRole("alertdialog")
    .getByRole("button", { name: "Unpublish" })
    .click()
  await expect.poll(() => unpublishBodies.length).toBe(1)
  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  await expect(overlays(page)).toHaveCount(0)
  expect(unpublishBodies).toEqual([
    { expectedPublishedVersionId: DRAFT_VERSION_ID },
  ])
})

test("a publish conflict keeps the reviewed template dialog open", async ({
  page,
}) => {
  await installAuthenticatedApi(
    page,
    ["notification-templates:read", "notification-templates:publish"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}`) {
        await fulfillJson(route, template)
        return true
      }
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}/publish`) {
        await fulfillJson(
          route,
          {
            status: 409,
            title: "Notification.PublishTargetChanged",
            detail: "The reviewed draft changed before publication.",
          },
          409
        )
        return true
      }
      return false
    }
  )

  await page.goto(`/notifications/templates/${TEMPLATE_ID}`)
  await page.getByRole("button", { name: "Publish", exact: true }).click()
  const dialog = page.getByRole("alertdialog")
  await dialog.getByRole("button", { name: /Publish$/ }).click()

  await expect(dialog).toBeVisible()
  await expect(
    dialog.getByRole("button", { name: /Publish$/ })
  ).toBeEnabled()
})

test("layout publish sends only the saved revision after confirmation", async ({
  page,
}) => {
  const bodies: unknown[] = []
  await installAuthenticatedApi(
    page,
    ["notification-templates:read", "notification-layouts:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-layouts/${LAYOUT_ID}`) {
        await fulfillJson(route, layout)
        return true
      }
      if (path === `/api/v1/notification-layouts/${LAYOUT_ID}/publish`) {
        bodies.push(route.request().postDataJSON())
        await fulfillJson(route, { ...layout, hasUnpublishedChanges: false })
        return true
      }
      return false
    }
  )

  await page.goto(`/notifications/layouts/${LAYOUT_ID}`)
  const publish = page.getByRole("button", { name: "Publish", exact: true })
  await expect(publish).toBeEnabled()
  await publish.click()
  await expect(page.getByText("Saved draft ·", { exact: false })).toBeVisible()
  await expect(page.getByText("Customer portal", { exact: true })).toBeVisible()
  expect(bodies).toHaveLength(0)

  await page.getByRole("button", { name: "Cancel" }).click()
  expect(bodies).toHaveLength(0)
  await publish.click()
  await page
    .getByRole("alertdialog")
    .getByRole("button", { name: /Publish$/ })
    .click()

  await expect.poll(() => bodies.length).toBe(1)
  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  await expect(overlays(page)).toHaveCount(0)
  expect(bodies).toEqual([{ expectedRevisionAt: REVISION }])
})

test("template edits survive cancel and a save-completed navigation resumes safely", async ({
  page,
}) => {
  const saveBodies: Array<{
    translations?: Array<{ subject?: string }>
  }> = []
  let releaseSave!: () => void
  const saveRelease = new Promise<void>((resolve) => {
    releaseSave = resolve
  })
  let currentTemplate = template

  await installAuthenticatedApi(
    page,
    ["notification-templates:read", "notification-templates:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}`) {
        await fulfillJson(route, currentTemplate)
        return true
      }
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}/draft`) {
        const body = route.request().postDataJSON()
        saveBodies.push(body)
        await saveRelease
        currentTemplate = {
          ...template,
          modifiedAt: "2026-08-22T08:00:00Z",
          draftVersion: {
            ...template.draftVersion,
            translations: body.translations ?? [],
          },
        }
        await fulfillJson(route, currentTemplate)
        return true
      }
      if (path === "/api/v1/notification-templates") {
        await fulfillJson(route, {
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        })
        return true
      }
      return false
    }
  )

  await page.goto(`/notifications/templates/${TEMPLATE_ID}`)
  const subject = page.getByRole("textbox", { name: "Subject" })
  await subject.fill("Local subject")
  await expect(page.getByText("Unsaved changes")).toBeVisible()

  const notificationsCrumb = page
    .getByRole("navigation", { name: "breadcrumb" })
    .getByRole("link", { name: "Notifications" })
  await notificationsCrumb.click()
  const discardDialog = page.getByRole("alertdialog", {
    name: "Discard changes?",
  })
  await expect(discardDialog).toBeVisible()
  await discardDialog.getByRole("button", { name: "Cancel" }).click()
  await expect(page).toHaveURL(
    new RegExp(`/notifications/templates/${TEMPLATE_ID}$`)
  )
  await expect(subject).toHaveValue("Local subject")

  await subject.fill("Submitted subject")
  await clickPageAction(page, "Save draft")
  await expect.poll(() => saveBodies.length).toBe(1)
  await notificationsCrumb.click()
  const savingDialog = page.getByRole("alertdialog", {
    name: "Save in progress",
  })
  await expect(savingDialog).toBeVisible()
  await expect(
    savingDialog.getByRole("button", { name: "Discard" })
  ).toBeDisabled()

  releaseSave()
  await expect(page).toHaveURL(/\/notifications(?:\/templates)?$/)
  await expect(page.getByRole("alertdialog")).toHaveCount(0)
  expect(saveBodies[0]?.translations?.[0]?.subject).toBe("Submitted subject")
})
