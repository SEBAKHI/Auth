import { expect, test } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

const USER_ID = "99999999-9999-9999-9999-999999999999"
const RECENT_KEY = `authsystem.settingsSearch.recent.${USER_ID}`

async function installSearchApi(
  page: Parameters<typeof installAuthenticatedApi>[0],
  preferredLanguage = "en"
) {
  await installAuthenticatedApi(
    page,
    ["users:read", "applications:read", "auditlogs:read"],
    async (route) => {
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    },
    { preferredLanguage }
  )
}

test("history and quick navigation coexist; clear removes only history", async ({
  page,
}) => {
  await installSearchApi(page)
  await page.addInitScript(
    ({ key }) => {
      localStorage.setItem(
        key,
        JSON.stringify([{ id: "profile", route: "/profile" }])
      )
    },
    { key: RECENT_KEY }
  )
  await page.goto("/")

  await page.getByRole("button", { name: "Search" }).click()
  const dialog = page.getByRole("dialog", { name: "Search" })
  await expect(dialog.getByText("Recent", { exact: true })).toBeVisible()
  await expect(dialog.getByText("My profile", { exact: true })).toBeVisible()
  await expect(dialog.locator('[data-slot="command-separator"]')).toBeVisible()
  await expect(dialog.getByText("Jump to", { exact: true })).toBeVisible()

  await dialog.getByText("Clear recent", { exact: true }).click()
  await expect(dialog.getByText("Recent", { exact: true })).toHaveCount(0)
  await expect(dialog.getByText("Jump to", { exact: true })).toBeVisible()
  await expect
    .poll(() => page.evaluate((key) => localStorage.getItem(key), RECENT_KEY))
    .toBe("[]")

  await dialog.getByText("Users", { exact: true }).click()
  await expect(page).toHaveURL(/\/users$/)
})

test("no-history state starts with permission-filtered quick navigation", async ({
  page,
}) => {
  await installSearchApi(page)
  await page.goto("/")

  await page.getByRole("button", { name: "Search" }).click()
  const dialog = page.getByRole("dialog", { name: "Search" })
  await expect(dialog.getByText("Recent", { exact: true })).toHaveCount(0)
  await expect(dialog.getByText("Jump to", { exact: true })).toBeVisible()
  await expect(dialog.getByText("Users", { exact: true })).toBeVisible()
  await expect(dialog.getByText("Applications", { exact: true })).toBeVisible()
  await expect(dialog.getByText("Audit Logs", { exact: true })).toBeVisible()
  // Password settings are not rendered because this role cannot manage system
  // settings; the idle shortcut list therefore cannot leak the destination.
  await expect(dialog.getByText("Password", { exact: true })).toHaveCount(0)
})

test("the combined start state preserves RTL order and labels in Arabic", async ({
  page,
}) => {
  await installSearchApi(page, "ar")
  await page.addInitScript(
    ({ key }) => {
      localStorage.setItem("auth.language", "ar")
      localStorage.setItem(
        key,
        JSON.stringify([{ id: "profile", route: "/profile" }])
      )
    },
    { key: RECENT_KEY }
  )
  await page.goto("/")

  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  await page.getByRole("button", { name: "بحث" }).click()
  const dialog = page.getByRole("dialog", { name: "بحث" })
  const recent = dialog.getByText("الأحدث", { exact: true })
  const jump = dialog.getByText("انتقال سريع", { exact: true })
  await expect(recent).toBeVisible()
  await expect(dialog.locator('[data-slot="command-separator"]')).toBeVisible()
  await expect(jump).toBeVisible()
  const headings = dialog.locator("[cmdk-group-heading]")
  await expect(headings.nth(0)).toHaveText("الأحدث")
  await expect(headings.nth(1)).toHaveText("انتقال سريع")
})
