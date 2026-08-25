import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Two facts that only a rendered table can settle.
 *
 * The first is which person a column shows. The audit row carries both — who
 * performed the action and who it happened to — and for the life of this screen
 * the column headed "Actor" read the second one. Every assertion available
 * short of the DOM passed throughout: the DTO carried both names, the request
 * was well formed, the handler tests were green. What was wrong was which field
 * the cell under that heading was bound to.
 *
 * The second is that an outcome has three states. A row whose outcome was never
 * recorded must not render as a success, and "renders as nothing" is the same
 * mistake with a quieter face.
 */

const ADMIN = "admin@example.test"
const EMPLOYEE = "employee@example.test"

const PAGE_ONE = {
  logs: [
    {
      // The case the whole distinction exists for: one person acted on another.
      id: "11111111-1111-1111-1111-111111111111",
      action: "user.locked",
      actionType: "Security",
      entityType: "User",
      userEmail: EMPLOYEE,
      performedByEmail: ADMIN,
      isSuccess: true,
      timestamp: "2026-08-01T09:00:00Z",
    },
    {
      id: "22222222-2222-2222-2222-222222222222",
      action: "user.login",
      actionType: "Authentication",
      entityType: "User",
      userEmail: EMPLOYEE,
      performedByEmail: EMPLOYEE,
      isSuccess: false,
      errorMessage: "Locked out",
      timestamp: "2026-08-01T10:00:00Z",
    },
    {
      // Written before the column existed: the outcome is not known.
      id: "33333333-3333-3333-3333-333333333333",
      action: "role.created",
      actionType: "Authorization",
      entityType: "Role",
      userEmail: null,
      performedByEmail: null,
      isSuccess: null,
      timestamp: "2026-08-01T11:00:00Z",
    },
  ],
  totalCount: 3,
  pageNumber: 1,
  pageSize: 25,
  totalPages: 1,
}

function installAuditApi(page: Page) {
  const queries: URL[] = []
  const installed = installAuthenticatedApi(
    page,
    ["auditlogs:read"],
    async (route: Route, url: URL) => {
      if (url.pathname.toLowerCase() === "/api/v1/audit-logs") {
        queries.push(url)
        await fulfillJson(route, PAGE_ONE)
        return true
      }
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    }
  )
  return { queries, installed }
}

/**
 * The cell of one row under one column HEADING, found by the heading's position
 * rather than by a fixed index — so the assertion keeps meaning if the columns
 * are ever reordered, and fails loudly if the heading disappears.
 */
async function cellUnder(page: Page, rowText: string, heading: string) {
  const headers = page.getByRole("columnheader")
  const count = await headers.count()
  let index = -1
  for (let i = 0; i < count; i++) {
    if ((await headers.nth(i).innerText()).trim() === heading) {
      index = i
      break
    }
  }
  expect(index, `no column headed "${heading}"`).toBeGreaterThan(-1)

  return page
    .getByRole("row")
    .filter({ hasText: rowText })
    .getByRole("cell")
    .nth(index)
}

test("the actor column names who acted, and the subject column who it happened to", async ({
  page,
}) => {
  const { installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")
  await expect(page.getByText("Account locked", { exact: true })).toBeVisible()

  await expect(await cellUnder(page, "user.locked", "Actor")).toHaveText(ADMIN)
  await expect(await cellUnder(page, "user.locked", "Subject")).toHaveText(
    EMPLOYEE
  )
})

test("a system action with neither person says so instead of guessing", async ({
  page,
}) => {
  const { installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")
  await expect(page.getByText("Role created", { exact: true })).toBeVisible()

  await expect(await cellUnder(page, "role.created", "Actor")).toHaveText("—")
  await expect(await cellUnder(page, "role.created", "Subject")).toHaveText("—")
})

test("an outcome renders as one of three states, never as a blank", async ({
  page,
}) => {
  const { installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")
  await expect(page.getByText("Account locked", { exact: true })).toBeVisible()

  await expect(await cellUnder(page, "user.locked", "Result")).toHaveText(
    "Succeeded"
  )
  await expect(await cellUnder(page, "user.login", "Result")).toHaveText(
    "Failed"
  )
  // The row that predates the column. Showing this one as a success is the
  // defect the nullable column was introduced to end.
  await expect(await cellUnder(page, "role.created", "Result")).toHaveText(
    "Not recorded"
  )
})

test("the result filter reaches the server and never asks for the unknown", async ({
  page,
}) => {
  const { queries, installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")
  await expect(page.getByText("Account locked", { exact: true })).toBeVisible()

  const before = queries.length
  await page.getByRole("combobox").filter({ hasText: "All results" }).click()

  // Two options and no more: the API matches the outcome on equality, so rows
  // whose outcome was never recorded cannot be asked for, and offering them
  // would be a filter that always returns nothing.
  const options = page.getByRole("option")
  await expect(options).toHaveCount(3) // All results, Succeeded, Failed
  await expect(page.getByRole("option", { name: "Not recorded" })).toHaveCount(
    0
  )

  await page.getByRole("option", { name: "Failed", exact: true }).click()

  await expect
    .poll(() =>
      queries
        .slice(before)
        .some((url) => url.searchParams.get("isSuccess") === "false")
    )
    .toBe(true)
  await expect(page).toHaveURL(/result=false/)
})

test("the detail dialog keeps the two people apart", async ({ page }) => {
  const { installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")

  const row = page.getByRole("row").filter({ hasText: "user.locked" })
  await row.getByRole("button", { name: "View" }).click()

  const dialog = page.getByRole("dialog")
  await expect(dialog.getByText("Actor", { exact: true })).toBeVisible()
  await expect(dialog.getByText(ADMIN, { exact: true })).toBeVisible()
  await expect(dialog.getByText("Subject", { exact: true })).toBeVisible()
  await expect(dialog.getByText(EMPLOYEE, { exact: true })).toBeVisible()
  await expect(dialog.getByText("Succeeded", { exact: true })).toBeVisible()
})
