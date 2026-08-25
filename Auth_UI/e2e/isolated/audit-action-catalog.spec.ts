import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The audit trail stores codes; the console shows names. Nothing that runs
 * outside a browser can tell whether the name actually reaches the screen — a
 * unit test proves the key exists, and the payload proves the code was sent,
 * and neither notices a page that renders `user.login` in Arabic because the
 * lookup key was derived one way and written another.
 */

const PAGE_ONE = {
  logs: [
    {
      id: "11111111-1111-1111-1111-111111111111",
      action: "user.login",
      actionType: "Authentication",
      entityType: "User",
      userEmail: "someone@example.test",
      timestamp: "2026-08-01T09:00:00Z",
    },
    {
      id: "22222222-2222-2222-2222-222222222222",
      action: "role.permission.granted",
      actionType: "Authorization",
      entityType: "Role",
      userEmail: "admin@example.test",
      timestamp: "2026-08-01T10:00:00Z",
    },
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 25,
  totalPages: 1,
}

/** Records every audit-log query the page makes, so a filter can be checked. */
function installAuditApi(page: Page, preferredLanguage = "en") {
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
    },
    { preferredLanguage }
  )
  return { queries, installed }
}

test("the table shows an action by name and keeps its stored code in view", async ({
  page,
}) => {
  const { installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")

  const row = page.getByRole("row").filter({ hasText: "user.login" })
  await expect(row.getByText("Signed in", { exact: true })).toBeVisible()
  // The code stays on screen: it is what a ticket, a URL filter and a SQL
  // query all need, and it is the same string in every language.
  await expect(row.getByText("user.login", { exact: true })).toBeVisible()
  await expect(row.getByText("Authentication", { exact: true })).toBeVisible()
})

test("the category filter reaches the server", async ({ page }) => {
  const { queries, installed } = installAuditApi(page)
  await installed
  await page.goto("/audit-logs")
  await expect(page.getByText("Signed in", { exact: true })).toBeVisible()

  const before = queries.length
  await page
    .getByRole("combobox")
    .filter({ hasText: "All action types" })
    .click()
  await page.getByRole("option", { name: "Authorization", exact: true }).click()

  // A filter that changes the control and not the request is the defect this
  // page has shipped twice; the assertion is on the query string, not the UI.
  await expect
    .poll(() =>
      queries
        .slice(before)
        .some((url) => url.searchParams.get("actionType") === "Authorization")
    )
    .toBe(true)
  await expect(page).toHaveURL(/actionType=Authorization/)
})

test("Arabic reads the action names, not the codes", async ({ page }) => {
  const { installed } = installAuditApi(page, "ar")
  await installed
  await page.goto("/audit-logs")

  await expect(page.getByText("تسجيل دخول", { exact: true })).toBeVisible()
  await expect(page.getByText("المصادقة", { exact: true })).toBeVisible()
  // Still the same stored code, unchanged by the page's direction.
  await expect(page.getByText("user.login", { exact: true })).toBeVisible()
})

test("the catalogue route renders the catalogue, not a settings section", async ({
  page,
}) => {
  // `audit-catalog` is a single static segment sitting beside `:sectionKey`.
  // React Router ranks static above dynamic, but that is the kind of assumption
  // that is cheap to hold and expensive to be wrong about: getting it wrong
  // renders the settings page, which would redirect to the first section and
  // leave the catalogue unreachable.
  const { installed } = installAuditApi(page)
  await installed
  await page.goto("/admin/system-settings/audit-catalog")

  await expect(
    page.getByRole("heading", { name: "Audit action catalogue" })
  ).toBeVisible()
  await expect(page).toHaveURL(/\/admin\/system-settings\/audit-catalog$/)

  const row = page.getByRole("row").filter({ hasText: "user.logout.all" })
  await expect(
    row.getByText("Signed out of every device", { exact: true })
  ).toBeVisible()
})
