import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The email layout's card colours. The server builds the logo's background in
 * these, so they have to reach it on save (the draft) and on preview (the
 * buffer being edited) exactly as typed, and a value the server would refuse
 * must be caught before the round trip.
 */

const LAYOUT_ID = "44444444-4444-4444-4444-444444444444"
const REVISION = "2026-08-21T07:00:00Z"

const layout = {
  id: LAYOUT_ID,
  applicationId: "66666666-6666-6666-6666-666666666666",
  applicationName: "Customer portal",
  channel: "Email",
  name: "Default email layout",
  draftContent: "<html>{{ content | raw }}</html>",
  draftStringsJson: "{}",
  draftCardColorLight: null,
  draftCardColorDark: null,
  isPublished: true,
  hasUnpublishedChanges: false,
  publishedAt: REVISION,
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: REVISION,
}

async function openLayout(page: Page, overrides: Partial<typeof layout> = {}) {
  const saves: Record<string, unknown>[] = []
  const previews: Record<string, unknown>[] = []
  await installAuthenticatedApi(
    page,
    ["notification-templates:read", "notification-layouts:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-layouts/${LAYOUT_ID}`) {
        await fulfillJson(route, { ...layout, ...overrides })
        return true
      }
      if (path === `/api/v1/notification-layouts/${LAYOUT_ID}/draft`) {
        const body = route.request().postDataJSON() as Record<string, unknown>
        saves.push(body)
        await fulfillJson(route, { ...layout, ...overrides, ...body })
        return true
      }
      if (path === "/api/v1/notification-layouts/preview") {
        previews.push(route.request().postDataJSON() as Record<string, unknown>)
        await fulfillJson(route, {
          subject: "Layout preview",
          html: "<p>preview</p>",
          text: "preview",
          languageCode: "en",
          direction: "ltr",
        })
        return true
      }
      return false
    }
  )
  await page.goto(`/notifications/layouts/${LAYOUT_ID}`)
  return { saves, previews }
}

test("a card colour is saved and previewed as typed, and an empty one is saved as the default", async ({
  page,
}) => {
  const { saves, previews } = await openLayout(page)

  const dark = page.getByLabel("Card color, dark mode")
  await expect(dark).toHaveAttribute("placeholder", "#1A1A1C")
  await expect(page.getByLabel("Card color, light mode")).toHaveValue("")
  await dark.fill("#3c6e47")

  await expect
    .poll(() => previews.at(-1)?.cardColorDark)
    .toBe("#3c6e47")
  expect(previews.at(-1)?.cardColorLight).toBeNull()

  await page.getByRole("button", { name: "Save draft" }).click()
  await expect.poll(() => saves.length).toBe(1)
  // "" returns a colour to the default; null (or no field) would keep the saved one.
  expect(saves[0]).toMatchObject({
    draftCardColorLight: "",
    draftCardColorDark: "#3c6e47",
  })
})

test("a colour the server would refuse is flagged and cannot be saved", async ({
  page,
}) => {
  const { saves } = await openLayout(page)

  const light = page.getByLabel("Card color, light mode")
  await light.fill("#FFF")

  await expect(light).toHaveAttribute("aria-invalid", "true")
  await expect(
    page.getByText("Write # followed by six hexadecimal digits", {
      exact: false,
    })
  ).toBeVisible()
  await expect(page.getByRole("button", { name: "Save draft" })).toBeDisabled()

  await light.fill("#FFFFFF")
  await expect(light).toHaveAttribute("aria-invalid", "false")
  await expect(page.getByRole("button", { name: "Save draft" })).toBeEnabled()
  expect(saves).toHaveLength(0)
})

test("a layout that is not an email layout has no card colours", async ({
  page,
}) => {
  await openLayout(page, { channel: "Sms" })

  await expect(page.getByLabel("Layout name")).toBeVisible()
  await expect(page.getByLabel("Card color, light mode")).toHaveCount(0)
  await expect(page.getByLabel("Card color, dark mode")).toHaveCount(0)
})
