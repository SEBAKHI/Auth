import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * That no table offers the same value twice, and that it offers all of them in
 * the reader's language.
 *
 * The shared table adds a hidden column for every record field a page has not
 * claimed, so the column menu lists the whole record and not just the curated
 * view. Two defects lived in that mechanism:
 *
 * It could only tell what was claimed from a column's id, so a column named for
 * a CONCEPT — `actor` reading `performedByEmail`, `status` reading `isActive`,
 * `owner` reading `ownerName` — claimed nothing, and its own source came back as
 * a second column showing the same thing again beside the translated one.
 * `meta.covers` is the declaration that stops it.
 *
 * And a discovered column had no heading anyone had written, so it wore whatever
 * its field identifier humanized to — English, in all seven languages. The
 * `fields` catalogue gives those names a translation.
 *
 * Only a rendered page settles either: the menu is built at runtime out of the
 * response body, so nothing short of opening it can see what it holds.
 */

/** Every field the audit read model can carry, on one row. */
const AUDIT_PAGE = {
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

const ORGANIZATIONS_PAGE = {
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

/**
 * A page serving one list endpoint. The language is fixed per browser context —
 * the console adopts the profile's language once, on the first load — so each
 * test that reads in another language installs its own.
 */
function installList(
  page: Page,
  options: {
    permission: string
    path: string
    body: unknown
    preferredLanguage?: string
  }
) {
  return installAuthenticatedApi(
    page,
    [options.permission],
    async (route: Route, url: URL) => {
      if (url.pathname.toLowerCase() === options.path) {
        await fulfillJson(route, options.body)
        return true
      }
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    },
    { preferredLanguage: options.preferredLanguage ?? "en" }
  )
}

function installAuditApi(page: Page, preferredLanguage?: string) {
  return installList(page, {
    permission: "auditlogs:read",
    path: "/api/v1/audit-logs",
    body: AUDIT_PAGE,
    preferredLanguage,
  })
}

const AUDIT_USER_ID = AUDIT_PAGE.logs[0].userId

const AUDIT_USER = {
  id: AUDIT_USER_ID,
  email: "employee@example.test",
  firstName: "Employee",
  lastName: "One",
  displayName: "Employee One",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  isDeleted: false,
  createdAt: "2026-08-01T08:00:00Z",
}

/** The same audit rows, reached through a user's page instead of the full list. */
function installUserAuditApi(page: Page) {
  return installAuthenticatedApi(
    page,
    ["auditlogs:read", "users:read"],
    async (route: Route, url: URL) => {
      if (url.pathname.toLowerCase() === "/api/v1/audit-logs") {
        await fulfillJson(route, AUDIT_PAGE)
        return true
      }
      if (url.pathname.toLowerCase() === `/api/v1/users/${AUDIT_USER_ID}`) {
        await fulfillJson(route, AUDIT_USER)
        return true
      }
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    }
  )
}

/** Every entry in the "Columns" menu, in display order. */
async function columnMenuEntries(
  page: Page,
  trigger = "Toggle columns"
): Promise<string[]> {
  await page.getByRole("button", { name: trigger }).click()
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
  test.beforeEach(async ({ page }) => {
    await installAuditApi(page)
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
      "Actor email",
      "User",
      "User email",
      "Succeeded",
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
    expect(entries).toContain("IP address")
    expect(entries).toContain("User agent")
    expect(entries).toContain("Entity id")
  })
})

/**
 * The same menu, on a user's page.
 *
 * This spec only ever opened `/audit-logs`, which is exactly how the copy of
 * the table one route away kept shipping without a single `covers` declaration:
 * it offered a raw "Succeeded" column reading Yes/No/— while having no outcome
 * column at all, and a separate heading for each of the six fields naming the
 * two people. Both tables read one column set now, and both are checked.
 */
test.describe("a user's audit trail", () => {
  test.beforeEach(async ({ page }) => {
    await installUserAuditApi(page)
    await page.goto(`/users/${AUDIT_USER_ID}?tab=audit`)
    await expect(page.getByText("Account locked", { exact: true })).toBeVisible()
  })

  test("offers each person and the outcome exactly once", async ({ page }) => {
    const entries = await columnMenuEntries(page)

    expect(duplicates(entries)).toEqual([])
    expect(entries).toContain("Actor")
    // Hidden by default here — every row is this user — but still the only
    // column that reaches those three fields, and still reachable from the menu.
    expect(entries).toContain("Subject")
    expect(entries).toContain("Result")
    for (const shadow of ["Actor email", "User", "User email", "Succeeded"]) {
      expect(entries, `${shadow} duplicates a curated column`).not.toContain(
        shadow
      )
    }
  })

  test("still offers the fields no column claimed", async ({ page }) => {
    const entries = await columnMenuEntries(page)
    expect(entries).toContain("IP address")
    expect(entries).toContain("User agent")
    expect(entries).toContain("Entity id")
  })
})

test.describe("audit logs, read in Arabic", () => {
  test("names the discovered fields in the reader's language", async ({
    page,
  }) => {
    await installAuditApi(page, "ar")
    await page.goto("/audit-logs")
    await expect(page.getByText("قفل الحساب", { exact: true })).toBeVisible()

    const entries = await columnMenuEntries(page, "إظهار/إخفاء الأعمدة")

    expect(entries).toContain("عنوان IP")
    expect(entries).toContain("وكيل المستخدم")
    expect(entries).toContain("معرّف الكيان")

    // And nothing left reading as an identifier. `IP` is a Latin token the
    // Arabic locale keeps on purpose, so a lone Latin run is fine — two Latin
    // words in a row is what a humanized field name looks like, and none may
    // survive.
    const humanized = entries.filter((entry) =>
      /[A-Za-z]+ [A-Za-z]+/.test(entry)
    )
    expect(humanized).toEqual([])
  })
})

test.describe("organizations", () => {
  test("does not offer a second column under the same heading", async ({
    page,
  }) => {
    await installList(page, {
      permission: "organizations:read",
      path: "/api/v1/organizations/all",
      body: ORGANIZATIONS_PAGE,
    })
    await page.goto("/organizations")
    await expect(page.getByText("Acme Inc")).toBeVisible()

    const entries = await columnMenuEntries(page)

    // This page produced the plainest form of the defect: two columns headed
    // "Owner", one of them the resolved name of the id the other showed.
    expect(duplicates(entries)).toEqual([])
    expect(entries).toContain("Owner")
    for (const shadow of ["Owner email", "Code", "Active", "Logo"]) {
      expect(entries, `${shadow} duplicates a curated column`).not.toContain(
        shadow
      )
    }
  })
})
