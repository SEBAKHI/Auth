import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

async function installUsersApi(page: Page, requests: URL[]) {
  await installAuthenticatedApi(
    page,
    ["users:read", "users:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/users") {
        requests.push(new URL(url))
        await fulfillJson(route, {
          users: [],
          totalCount: 100,
          totalPages: 5,
          pageNumber: Number(url.searchParams.get("pageNumber") ?? 1),
          pageSize: Number(url.searchParams.get("pageSize") ?? 20),
        })
        return true
      }

      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    }
  )
}

test("a list deep link hydrates both controls and the bounded server query", async ({
  page,
}) => {
  const requests: URL[] = []
  await installUsersApi(page, requests)

  await page.goto(
    "/users?q=alice&page=3&pageSize=50&sort=name&direction=asc&includeDeleted=1"
  )

  await expect(page.getByPlaceholder("Search by name or email…")).toHaveValue(
    "alice"
  )
  await expect(page.getByRole("switch", { name: "Show deleted" })).toBeChecked()
  await expect(page.getByText("Page 3 of 5")).toBeVisible()
  await expect
    .poll(() => requests.at(-1)?.searchParams.get("pageNumber"))
    .toBe("3")

  const query = requests.at(-1)?.searchParams
  expect(query?.get("pageSize")).toBe("50")
  expect(query?.get("searchTerm")).toBe("alice")
  expect(query?.get("sortBy")).toBe("name")
  expect(query?.get("sortDirection")).toBe("0")
  expect(query?.get("includeDeleted")).toBe("true")
})

test("discrete list transitions reset pages atomically and survive browser history", async ({
  page,
}) => {
  const requests: URL[] = []
  await installUsersApi(page, requests)
  await page.goto("/users?page=2")

  await page.getByRole("button", { name: "Next" }).click()
  await expect(page).toHaveURL(/\/users\?page=3$/)

  await page.getByRole("switch", { name: "Show deleted" }).click()
  await expect(page).toHaveURL(/\/users\?includeDeleted=1$/)

  await page.goBack()
  await expect(page).toHaveURL(/\/users\?page=3$/)
  await page.goBack()
  await expect(page).toHaveURL(/\/users\?page=2$/)
  await page.goForward()
  await expect(page).toHaveURL(/\/users\?page=3$/)
})

test("malformed list parameters canonicalize before reaching the API", async ({
  page,
}) => {
  const requests: URL[] = []
  await installUsersApi(page, requests)

  await page.goto(
    "/users?keep=1&page=-2&pageSize=999&sort=__proto__&direction=sideways&includeDeleted=admin"
  )

  await expect(page).toHaveURL(/\/users\?keep=1$/)
  await expect
    .poll(() => requests.at(-1)?.searchParams.get("pageNumber"))
    .toBe("1")

  const query = requests.at(-1)?.searchParams
  expect(query?.get("pageSize")).toBe("20")
  expect(query?.get("sortBy")).toBe("createdAt")
  expect(query?.get("sortDirection")).toBe("1")
  expect(query?.has("includeDeleted")).toBe(false)
})
