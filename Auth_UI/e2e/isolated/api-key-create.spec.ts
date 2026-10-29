import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { problem } from "./problem"

/**
 * Creating an API key (OI-128 PR B), as the console renders it from the production build.
 *
 * A key must carry at least one scope, each an active permission of the key's own application
 * that the creator may grant: the server refuses anything else (ApiKey.ScopesRequired,
 * ApiKey.ScopeNotForApplication, Permission.CannotGrantHigher), so the dialog offers nothing
 * else and cannot be submitted without a scope. The per-key rate limits are gone (they limited
 * nothing), and the address list is real. Key validation moved to the application's own machine
 * identity, so neither key page offers a validate control any more.
 */

const SHOP = "33333333-3333-3333-3333-333333333333"
const CRM = "44444444-4444-4444-4444-444444444444"
const ORDERS_READ = "10000000-0000-0000-0000-000000000005"
const ORDERS_WRITE = "10000000-0000-0000-0000-000000000006"

/** The API leaves null properties out of every body, so a platform row has no applicationId. */
function permission(
  id: string,
  code: string,
  applicationId: string | null,
  extra: { isActive?: boolean; canGrant?: boolean } = {}
) {
  return {
    id,
    code,
    name: `Name of ${code}`,
    ...(applicationId ? { applicationId } : {}),
    isWildcard: false,
    isActive: extra.isActive ?? true,
    canGrant: extra.canGrant ?? true,
    createdAt: "2026-10-01T09:00:00Z",
  }
}

const CATALOGUE = [
  permission("10000000-0000-0000-0000-000000000001", "users:read", null),
  permission(ORDERS_READ, "shop:orders:read", SHOP),
  permission(ORDERS_WRITE, "shop:orders:write", SHOP),
  permission(
    "10000000-0000-0000-0000-000000000007",
    "shop:orders:refund",
    SHOP,
    { canGrant: false }
  ),
  permission("10000000-0000-0000-0000-000000000008", "shop:legacy", SHOP, {
    isActive: false,
  }),
  permission("10000000-0000-0000-0000-000000000009", "crm:leads:read", CRM),
]

const APPLICATIONS = [
  { id: SHOP, code: "SHOP", name: "Shop", isActive: true },
  { id: CRM, code: "CRM", name: "CRM", isActive: true },
]

const CREATED_KEY =
  "ak_prod_Sel3ctor0000000A_ssssssssssssssssssssssssssssssssAbC123"

const KEY_PERMISSIONS = [
  "apikeys:read",
  "apikeys:create",
  "apikeys:rotate",
  "apikeys:revoke",
  // Still in the catalogue (retirement is a follow-up); it must open nothing.
  "apikeys:validate",
  "webhookkeys:read",
  "webhookkeys:create",
  "webhookkeys:validate",
  "permissions:read",
  "applications:read",
]

type Call = { method: string; path: string; body?: Record<string, unknown> }

async function install(
  page: Page,
  calls: Call[],
  options: { createAnswer?: () => { status: number; body: unknown } } = {}
) {
  await installAuthenticatedApi(page, KEY_PERMISSIONS, async (route, url) => {
    const path = url.pathname.toLowerCase()
    const method = route.request().method()
    if (method !== "GET") {
      calls.push({
        method,
        path,
        body: route.request().postData()
          ? route.request().postDataJSON()
          : undefined,
      })
    }

    if (path === "/api/v1/permissions") {
      await fulfillJson(route, CATALOGUE)
      return true
    }
    if (path === "/api/v1/apikeys" && method === "POST") {
      const answer = options.createAnswer?.() ?? {
        status: 201,
        body: {
          id: "20000000-0000-0000-0000-000000000001",
          apiKey: CREATED_KEY,
          keyPrefix: "ak_prod_",
          createdAt: "2026-10-29T12:00:00Z",
        },
      }
      await fulfillJson(route, answer.body, answer.status)
      return true
    }
    if (path === "/api/v1/apikeys" || path === "/api/v1/webhookkeys") {
      await fulfillJson(route, [])
      return true
    }
    if (path === "/api/v1/applications") {
      await fulfillJson(
        route,
        Object.assign([...APPLICATIONS], {
          items: APPLICATIONS,
          totalCount: APPLICATIONS.length,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 100,
        })
      )
      return true
    }
    return false
  })
}

async function openCreateDialog(page: Page, applicationId = SHOP) {
  await page.goto(`/api-keys?applicationId=${applicationId}`)
  await page.getByRole("button", { name: "New API key", exact: true }).click()
  const dialog = page.getByRole("dialog", { name: "Create API key" })
  await expect(dialog).toBeVisible()
  return dialog
}

test("neither key page offers a validate control", async ({ page }) => {
  await install(page, [])

  await page.goto("/api-keys")
  // Floor: the page rendered its header actions, so the absence below is real.
  await expect(
    page.getByRole("button", { name: "New API key", exact: true })
  ).toBeVisible()
  await expect(
    page.getByRole("button", { name: "Validate", exact: true })
  ).toHaveCount(0)

  await page.goto("/webhook-keys")
  await expect(
    page.getByRole("button", { name: "New webhook key", exact: true })
  ).toBeVisible()
  await expect(
    page.getByRole("button", { name: "Validate", exact: true })
  ).toHaveCount(0)
})

test("the dialog offers only the application's own active permissions this administrator may grant", async ({
  page,
}) => {
  await install(page, [])
  const dialog = await openCreateDialog(page)

  const scopes = dialog
    .getByRole("group", { name: "Scopes" })
    .getByRole("checkbox")
  await expect(scopes.first()).toBeVisible()
  const offered = await dialog
    .getByRole("group", { name: "Scopes" })
    .locator("label")
    .allInnerTexts()

  expect(offered.length).toBeGreaterThan(0)
  expect([...offered].sort()).toEqual(["shop:orders:read", "shop:orders:write"])
  // The per-key rate limits are gone: they limited nothing.
  await expect(dialog.getByText(/rate limit/i)).toHaveCount(0)
})

test("the dialog cannot be submitted without a scope, and sends the scopes and addresses chosen", async ({
  page,
}) => {
  const calls: Call[] = []
  await install(page, calls)
  const dialog = await openCreateDialog(page)

  await dialog.getByLabel("Name").fill("Orders integration")
  await dialog.getByRole("button", { name: "Create", exact: true }).click()

  await expect(dialog.getByText("Choose at least one scope.")).toBeVisible()
  expect(calls.filter((call) => call.method === "POST")).toHaveLength(0)

  await dialog.getByRole("checkbox", { name: "shop:orders:read" }).check()
  await dialog
    .getByLabel("Allowed addresses")
    .fill("203.0.113.0/24\n\n  2001:db8::1  ")
  await dialog.getByRole("button", { name: "Create", exact: true }).click()

  // The key is shown once, in the read-only field of the reveal dialog.
  await expect(
    page
      .getByRole("dialog", { name: "Copy your API key now" })
      .getByRole("textbox")
  ).toHaveValue(CREATED_KEY)
  const post = calls.find(
    (call) => call.method === "POST" && call.path === "/api/v1/apikeys"
  )
  expect(post?.body).toMatchObject({
    applicationId: SHOP,
    name: "Orders integration",
    permissionIds: [ORDERS_READ],
    allowedIps: ["203.0.113.0/24", "2001:db8::1"],
  })
  expect(Object.keys(post?.body ?? {})).not.toContain("rateLimitPerMinute")
  expect(Object.keys(post?.body ?? {})).not.toContain("rateLimitPerDay")
})

test("an address the server refuses is shown under the address field", async ({
  page,
}) => {
  const calls: Call[] = []
  await install(page, calls, {
    createAnswer: () => ({
      status: 400,
      body: problem(400, "ApiKey.AllowedIpInvalid", {
        detail:
          "Each allowed address must be an IPv4 or IPv6 address, or a CIDR range whose host bits are zero.",
      }),
    }),
  })
  const dialog = await openCreateDialog(page)

  await dialog.getByLabel("Name").fill("Orders integration")
  await dialog.getByRole("checkbox", { name: "shop:orders:write" }).check()
  await dialog.getByLabel("Allowed addresses").fill("10.0.0.1/24")
  await dialog.getByRole("button", { name: "Create", exact: true }).click()

  await expect(dialog.getByLabel("Allowed addresses")).toHaveAttribute(
    "aria-invalid",
    "true"
  )
  await expect(dialog).toBeVisible()
  expect(calls.filter((call) => call.method === "POST")).toHaveLength(1)
})

test("an application with nothing this administrator may grant says so, rather than an empty list", async ({
  page,
}) => {
  await install(page, [])
  const UNKNOWN = "55555555-5555-5555-5555-555555555555"
  const dialog = await openCreateDialog(page, UNKNOWN)

  await expect(
    dialog.getByText(
      "This application has no active permission that you may grant.",
      { exact: false }
    )
  ).toBeVisible()
  await expect(dialog.getByRole("checkbox")).toHaveCount(0)
})
