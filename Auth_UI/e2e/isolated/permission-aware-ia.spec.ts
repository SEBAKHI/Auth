import { expect, test } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

const TEMPLATE_READ = "notification-templates:read"

/** The notification section strip: links in a nav, not tabs in a tablist. */
function sections(page: Parameters<typeof installAuthenticatedApi>[0]) {
  return page
    .getByRole("navigation", { name: "Notification sections" })
    .getByRole("link")
}
const POLICY_READ = "privacy-policy:read"

async function installIaApi(
  page: Parameters<typeof installAuthenticatedApi>[0],
  permissions: string[]
) {
  await installAuthenticatedApi(page, permissions, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/privacy-policy/versions") {
      await fulfillJson(route, [])
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
    if (path === "/api/v1/notification-types") {
      await fulfillJson(route, [])
      return true
    }
    // Record search is irrelevant to this IA check; static surfaces remain
    // available while every unrelated source deterministically returns empty.
    await fulfillJson(route, { items: [], totalCount: 0 })
    return true
  })
}

test("privacy-only lands on Policy and sees it in sidebar, tabs, search, and direct URL", async ({
  page,
}) => {
  await installIaApi(page, [POLICY_READ])

  await page.goto("/notifications")
  await expect(page).toHaveURL(/\/notifications\/policy$/)
  await expect(
    sections(page).filter({ hasText: "Privacy Policy" })
  ).toBeVisible()
  await expect(sections(page)).toHaveCount(1)
  await expect(
    page.locator('[data-sidebar="menu-button"][href="/notifications/policy"]')
  ).toBeVisible()
  await expect(
    page
      .getByRole("navigation", { name: "breadcrumb" })
      .getByRole("link", { name: "Notifications" })
  ).toHaveAttribute("href", "/notifications")

  await page.getByRole("button", { name: "Search" }).click()
  await page.getByPlaceholder("Search the console…").fill("privacy policy")
  const search = page.getByRole("dialog", { name: "Search" })
  await expect(
    search.getByText("Privacy policy versions", { exact: true })
  ).toBeVisible()
  await expect(
    search.getByText("Notification Templates", { exact: true })
  ).toHaveCount(0)

  await page.goto("/notifications/policy/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
  await expect(page).toHaveURL(/\/notifications\/policy\/aaaaaaaa-/)
  await expect(page).not.toHaveURL(/\/403$/)
})

test("templates-only sees notification branches but Policy direct URLs are forbidden", async ({
  page,
}) => {
  await installIaApi(page, [TEMPLATE_READ])

  await page.goto("/notifications/templates")
  await expect(
    page.locator('[data-sidebar="menu-button"][href="/notifications"]')
  ).toBeVisible()
  await expect(sections(page)).toHaveCount(4)
  await expect(
    sections(page).filter({ hasText: "Privacy Policy" })
  ).toHaveCount(0)
  await expect(
    page
      .getByRole("navigation", { name: "breadcrumb" })
      .getByRole("link", { name: "Notifications" })
  ).toHaveAttribute("href", "/notifications")

  await page.goto("/notification-templates")
  await expect(page).toHaveURL(/\/notifications\/templates$/)

  await page.goto("/notifications/policy")
  await expect(page).toHaveURL(/\/403$/)
})

test("both sees every branch while neither sees no section and receives 403", async ({
  page,
}) => {
  await installIaApi(page, [TEMPLATE_READ, POLICY_READ])
  await page.goto("/notifications/policy")
  await expect(sections(page)).toHaveCount(5)
  await expect(
    page.locator('[data-sidebar="menu-button"][href="/notifications"]')
  ).toBeVisible()

  await page.evaluate(() => localStorage.clear())
  await page.context().clearCookies()
  await page.unrouteAll({ behavior: "wait" })
  await installIaApi(page, [])
  await page.goto("/notifications")
  await expect(page).toHaveURL(/\/403$/)
  await expect(
    page.locator('[data-sidebar="menu-button"][href^="/notifications"]')
  ).toHaveCount(0)

  await page.goto("/notifications/templates")
  await expect(page).toHaveURL(/\/403$/)
  await page.goto("/notifications/policy")
  await expect(page).toHaveURL(/\/403$/)
})
