import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

const USER_ID = "0f8fad5b-d9cb-469f-a165-70867728950e"

const USER = {
  id: USER_ID,
  email: "ada@example.test",
  firstName: "Ada",
  lastName: "Lovelace",
  displayName: "Ada Lovelace",
  status: "Active",
  isDeleted: false,
  roles: [],
  createdAt: "2026-08-01T09:00:00Z",
}

async function installUsersApi(page: Page) {
  await installAuthenticatedApi(
    page,
    ["users:read", "users:update", "users:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/users") {
        await fulfillJson(route, {
          users: [USER],
          totalCount: 1,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 20,
        })
        return true
      }
      if (path === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, USER)
        return true
      }
      await fulfillJson(route, { items: [], users: [], totalCount: 0 })
      return true
    }
  )
}

test("a record name is an address the browser can act on", async ({ page }) => {
  await installUsersApi(page)
  await page.goto("/users")

  const name = page.getByRole("link", { name: "Ada Lovelace" })
  await expect(name).toHaveAttribute("href", `/users/${USER_ID}`)

  // The plain click still navigates inside the SPA.
  await name.click()
  await expect(page).toHaveURL(new RegExp(`/users/${USER_ID}$`))

  // …and Back returns to the list with its query state intact.
  await page.goBack()
  await expect(page).toHaveURL(/\/users$/)
})

test("Ctrl-click opens the record beside the list instead of replacing it", async ({
  page,
  context,
}) => {
  await installUsersApi(page)
  await page.goto("/users?q=ada")

  const opened = context.waitForEvent("page")
  await page
    .getByRole("link", { name: "Ada Lovelace" })
    .click({ modifiers: ["ControlOrMeta"] })

  const second = await opened
  await second.waitForURL(`**/users/${USER_ID}`)
  expect(new URL(second.url()).pathname).toBe(`/users/${USER_ID}`)

  // The list is untouched: same page, same query state.
  await expect(page).toHaveURL(/\/users\?q=ada$/)
  await second.close()
})

test("a middle click does the same", async ({ page, context }) => {
  await installUsersApi(page)
  await page.goto("/users")

  const opened = context.waitForEvent("page")
  await page.getByRole("link", { name: "Ada Lovelace" }).click({ button: "middle" })

  const second = await opened
  await second.waitForURL(`**/users/${USER_ID}`)
  expect(new URL(second.url()).pathname).toBe(`/users/${USER_ID}`)
  await second.close()
})

test("the section strip is navigation, not a tablist", async ({ page }) => {
  await installAuthenticatedApi(
    page,
    ["notification-templates:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/notification-templates") {
        await fulfillJson(route, {
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 20,
        })
        return true
      }
      if (path === "/api/v1/notification-types") {
        await fulfillJson(route, [])
        return true
      }
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    }
  )
  await page.goto("/notifications/templates")

  const strip = page.getByRole("navigation", { name: "Notification sections" })
  await expect(strip).toBeVisible()
  // A tab announces a panel it controls; this strip has no panel, so it must
  // not claim one.
  await expect(page.getByRole("tab")).toHaveCount(0)

  const layouts = strip.getByRole("link", { name: "Layouts" })
  await expect(layouts).toHaveAttribute("href", "/notifications/layouts")
  await expect(
    strip.getByRole("link", { name: "Templates" })
  ).toHaveAttribute("aria-current", "page")

  await layouts.click()
  await expect(page).toHaveURL(/\/notifications\/layouts$/)
})
