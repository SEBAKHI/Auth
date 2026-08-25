import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * That no table offers the same value twice.
 *
 * The shared table adds a hidden column for every record field a page has not
 * claimed, so the column menu lists the whole record and not just the curated
 * view. It could only tell what was claimed from a column's id, so a column
 * named for a CONCEPT — `actor` reading `performedByEmail`, `status` reading
 * `isActive`, `owner` reading `ownerName` — claimed nothing, and its own source
 * came back as a second column showing the same thing again, in English, beside
 * the translated one. `meta.covers` is the declaration that stops it.
 *
 * Only a rendered page settles this: the duplicate lives in a menu built at
 * runtime from the response body, so nothing short of opening it can see it.
 */

/** Every entry in the "Columns" menu, in display order. */
async function columnMenuEntries(page: Page): Promise<string[]> {
  await page.getByRole("button", { name: "Toggle columns" }).click()
  const items = page.getByRole("menuitemcheckbox")
  await expect(items.first()).toBeVisible()
  const entries = await items.allInnerTexts()
  await page.keyboard.press("Escape")
  return entries.map((entry) => entry.trim()).filter(Boolean)
}

function duplicates(entries: string[]): string[] {
  const seen = new Set<string>()
  const twice = new Set<string>()
  for (const entry of entries) {
    if (seen.has(entry)) twice.add(entry)
    seen.add(entry)
  }
  return [...twice]
}

test.describe("audit logs", () => {
  // Every field the read model can carry, on one row: auto-discovery works off
  // the keys the response actually has, so a sparse fixture would hide the very
  // columns this test exists to catch.
  const PAGE_ONE = {
    logs: [
      {
        id: "11111111-1111-1111-1111-111111111111",
        userId: "22222222-2222-2222-2222-222222222222",
        userName: "Employee One",
        userEmail: "employee@example.test",
        performedBy: "33333333-3333-3333-3333-333333333333",
        performedByName: "System Administrator",
        performedByEmail: "admin@example.test",
        action: "user.locked",
        actionType: "Security",
        entityType: "User",
        entityId: "22222222-2222-2222-2222-222222222222",
        ipAddress: "203.0.113.42",
        userAgent: "Mozilla/5.0",
        isSuccess: true,
        timestamp: "2026-08-01T09:00:00Z",
      },
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 25,
    totalPages: 1,
  }

  test.beforeEach(async ({ page }) => {
    await installAuthenticatedApi(
      page,
      ["auditlogs:read"],
      async (route: Route, url: URL) => {
        if (url.pathname.toLowerCase() === "/api/v1/audit-logs") {
          await fulfillJson(route, PAGE_ONE)
          return true
        }
        await fulfillJson(route, { items: [], totalCount: 0 })
        return true
      }
    )
    await page.goto("/audit-logs")
    await expect(page.getByText("Account locked", { exact: true })).toBeVisible()
  })

  test("offers each person and the outcome exactly once", async ({ page }) => {
    const entries = await columnMenuEntries(page)

    expect(duplicates(entries)).toEqual([])
    // The three curated columns are the only way to reach these fields.
    expect(entries).toContain("Actor")
    expect(entries).toContain("Subject")
    expect(entries).toContain("Result")
    // What the row also carries, under a heading that repeats one of the above.
    for (const shadow of [
      "Performed By",
      "Performed By Email",
      "User",
      "User Email",
      "Is Success",
    ]) {
      expect(entries, `${shadow} duplicates a curated column`).not.toContain(
        shadow
      )
    }
  })

  test("still offers the fields no column claimed", async ({ page }) => {
    // The point is to stop showing one field twice, never to hide the record:
    // anything undeclared must keep its column.
    const entries = await columnMenuEntries(page)
    expect(entries).toContain("Ip Address")
    expect(entries).toContain("User Agent")
    expect(entries).toContain("Entity Id")
  })
})

test.describe("organizations", () => {
  const PAGE_ONE = {
    organizations: [
      {
        id: "44444444-4444-4444-4444-444444444444",
        name: "Acme Inc",
        code: "acme",
        logoUrl: "https://example.test/acme.png",
        ownerId: "55555555-5555-5555-5555-555555555555",
        ownerName: "Jane Doe",
        ownerEmail: "jane@example.test",
        memberCount: 4,
        enabledAppCount: 2,
        isActive: true,
        createdAt: "2026-08-01T09:00:00Z",
      },
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 25,
    totalPages: 1,
  }

  test("does not offer a second column under the same heading", async ({
    page,
  }) => {
    await installAuthenticatedApi(
      page,
      ["organizations:read"],
      async (route: Route, url: URL) => {
        if (url.pathname.toLowerCase() === "/api/v1/organizations/all") {
          await fulfillJson(route, PAGE_ONE)
          return true
        }
        await fulfillJson(route, { items: [], totalCount: 0 })
        return true
      }
    )
    await page.goto("/organizations")
    await expect(page.getByText("Acme Inc")).toBeVisible()

    const entries = await columnMenuEntries(page)

    // This page produced the plainest form of the defect: two columns headed
    // "Owner", one of them the resolved name of the id the other showed.
    expect(duplicates(entries)).toEqual([])
    expect(entries).toContain("Owner")
    for (const shadow of ["Owner Email", "Code", "Is Active", "Logo Url"]) {
      expect(entries, `${shadow} duplicates a curated column`).not.toContain(
        shadow
      )
    }
  })
})
