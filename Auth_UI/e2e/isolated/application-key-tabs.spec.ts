import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The application page's own API-key and webhook-key tabs (OI-264), as the
 * console renders them from the production build.
 *
 * Each tab is the main-menu table fixed to the open application: it asks the
 * server for that application's keys only, offers no application picker, and
 * creates for that application at once. The main-menu pages keep every
 * application in one list, so they now name each key's application. And both
 * surfaces read one cache, so a key made on the tab is already on the
 * main-menu page.
 */

const SHOP = "33333333-3333-3333-3333-333333333333"
const CRM = "44444444-4444-4444-4444-444444444444"
const ORDERS_READ = "10000000-0000-0000-0000-000000000005"

const application = {
  id: SHOP,
  name: "Shop",
  code: "SHOP",
  isActive: true,
  accessMode: "Restricted",
  redirectUris: [],
  createdAt: "2026-07-19T16:32:00Z",
}

const APPLICATIONS = [
  { id: SHOP, code: "SHOP", name: "Shop", isActive: true },
  { id: CRM, code: "CRM", name: "CRM", isActive: true },
]

function keyRow(
  id: string,
  name: string,
  applicationId: string,
  applicationName: string,
  extra: Record<string, unknown> = {}
) {
  return {
    id,
    applicationId,
    applicationName,
    name,
    keyPrefix: "ak_prod_",
    environment: "production",
    createdAt: "2026-10-01T09:00:00Z",
    createdBy: "55555555-5555-5555-5555-555555555555",
    isRevoked: false,
    scopes: [],
    ...extra,
  }
}

type Call = { method: string; path: string; query: string; body?: unknown }

/**
 * Answers both key lists from an in-memory store, so a create is visible on the
 * next read exactly as it would be on the server.
 */
async function install(page: Page, calls: Call[]) {
  const apiKeys = [
    keyRow("20000000-0000-0000-0000-000000000001", "Billing service", SHOP, "Shop"),
    keyRow("20000000-0000-0000-0000-000000000002", "Lead import", CRM, "CRM"),
  ]
  const webhookKeys = [
    keyRow("30000000-0000-0000-0000-000000000001", "Order events", SHOP, "Shop", {
      targetUrl: "https://shop.example.com/hooks/orders",
    }),
    keyRow("30000000-0000-0000-0000-000000000002", "Lead events", CRM, "CRM", {
      targetUrl: "https://crm.example.com/hooks/leads",
    }),
  ]

  await installAuthenticatedApi(
    page,
    [
      "applications:read",
      "permissions:read",
      "apikeys:read",
      "apikeys:create",
      "webhookkeys:read",
      "webhookkeys:create",
    ],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      const body = route.request().postData()
        ? route.request().postDataJSON()
        : undefined
      calls.push({ method, path, query: url.search, body })

      const appFilter = url.searchParams.get("applicationId")
      const filtered = <T extends { applicationId: string }>(rows: T[]) =>
        appFilter ? rows.filter((row) => row.applicationId === appFilter) : rows

      if (path === `/api/v1/applications/${SHOP}`) {
        await fulfillJson(route, application)
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
      if (path === "/api/v1/permissions") {
        await fulfillJson(route, [
          {
            id: ORDERS_READ,
            code: "shop:orders:read",
            name: "Read orders",
            applicationId: SHOP,
            isWildcard: false,
            isActive: true,
            canGrant: true,
            createdAt: "2026-10-01T09:00:00Z",
          },
        ])
        return true
      }
      if (path === "/api/v1/apikeys") {
        if (method === "POST") {
          const created = body as { applicationId: string; name: string }
          apiKeys.push(
            keyRow(
              "20000000-0000-0000-0000-000000000099",
              created.name,
              created.applicationId,
              "Shop"
            )
          )
          await fulfillJson(
            route,
            {
              id: "20000000-0000-0000-0000-000000000099",
              apiKey: "ak_prod_Isolated0000000A_tttttttttttttttttttttttttttttttt",
              keyPrefix: "ak_prod_",
              createdAt: "2026-10-30T12:00:00Z",
            },
            201
          )
          return true
        }
        await fulfillJson(route, filtered(apiKeys))
        return true
      }
      if (path === "/api/v1/webhookkeys") {
        if (method === "POST") {
          await fulfillJson(
            route,
            {
              id: "30000000-0000-0000-0000-000000000099",
              webhookKey: "whk_prod_Isolated0000000A_uuuuuuuuuuuuuuuuuuuuuuuuuu",
              keyPrefix: "whk_prod_",
              createdAt: "2026-10-30T12:00:00Z",
            },
            201
          )
          return true
        }
        await fulfillJson(route, filtered(webhookKeys))
        return true
      }
      return false
    }
  )
}

const listReads = (calls: Call[], path: string) =>
  calls.filter((call) => call.method === "GET" && call.path === path)

test("the API-keys tab lists only this application's keys, has no picker, and creates for this application", async ({
  page,
}) => {
  const calls: Call[] = []
  await install(page, calls)

  await page.goto(`/applications/${SHOP}?tab=api-keys`)
  await expect(page.getByRole("tab", { name: "API Keys" })).toHaveAttribute(
    "aria-selected",
    "true"
  )
  await expect(page.getByText("Billing service")).toBeVisible()
  await expect(page.getByText("Lead import")).toHaveCount(0)
  await expect(
    page.getByText(/You give an API key to another system/)
  ).toBeVisible()

  const reads = listReads(calls, "/api/v1/apikeys")
  expect(reads.length).toBeGreaterThan(0)
  for (const read of reads) expect(read.query).toBe(`?applicationId=${SHOP}`)
  await expect(page.getByRole("combobox")).toHaveCount(0)

  const create = page.getByRole("button", { name: "New API key", exact: true })
  await expect(create).toBeEnabled()
  await create.click()
  const dialog = page.getByRole("dialog", { name: "Create API key" })
  await dialog.getByLabel("Name").fill("Orders integration")
  await dialog.getByRole("checkbox", { name: "shop:orders:read" }).check()
  await dialog.getByRole("button", { name: "Create", exact: true }).click()
  await expect(
    page.getByRole("dialog", { name: "Copy your API key now" })
  ).toBeVisible()

  const post = calls.find(
    (call) => call.method === "POST" && call.path === "/api/v1/apikeys"
  )
  expect(post?.body).toMatchObject({
    applicationId: SHOP,
    name: "Orders integration",
  })
})

test("the webhook-keys tab lists only this application's keys, has no picker, and creates for this application", async ({
  page,
}) => {
  const calls: Call[] = []
  await install(page, calls)

  await page.goto(`/applications/${SHOP}?tab=webhook-keys`)
  await expect(page.getByText("Order events")).toBeVisible()
  await expect(page.getByText("Lead events")).toHaveCount(0)
  await expect(
    page.getByText(/You give a webhook key to an external system/)
  ).toBeVisible()

  const reads = listReads(calls, "/api/v1/webhookkeys")
  expect(reads.length).toBeGreaterThan(0)
  for (const read of reads) expect(read.query).toBe(`?applicationId=${SHOP}`)
  await expect(page.getByRole("combobox")).toHaveCount(0)

  const create = page.getByRole("button", {
    name: "New webhook key",
    exact: true,
  })
  await expect(create).toBeEnabled()
  await create.click()
  const dialog = page.getByRole("dialog", { name: "Create webhook key" })
  await expect(
    dialog.getByText(/This platform stores it and returns it/)
  ).toBeVisible()
  await dialog.getByLabel("Name").fill("Payment events")
  await dialog
    .getByLabel("Target URL")
    .fill("https://shop.example.com/hooks/payments")
  await dialog.getByRole("button", { name: "Create", exact: true }).click()
  await expect(
    page.getByRole("dialog", { name: "Copy your webhook key now" })
  ).toBeVisible()

  const post = calls.find(
    (call) => call.method === "POST" && call.path === "/api/v1/webhookkeys"
  )
  expect(post?.body).toMatchObject({
    applicationId: SHOP,
    name: "Payment events",
  })
})

test("the main-menu pages, across every application, name each key's application", async ({
  page,
}) => {
  await install(page, [])

  await page.goto("/api-keys")
  await expect(
    page.getByRole("columnheader", { name: "Application" })
  ).toBeVisible()
  await expect(
    page.getByRole("row").filter({ hasText: "Billing service" })
  ).toContainText("Shop")
  await expect(
    page.getByRole("row").filter({ hasText: "Lead import" })
  ).toContainText("CRM")

  await page.goto("/webhook-keys")
  await expect(
    page.getByRole("columnheader", { name: "Application" })
  ).toBeVisible()
  await expect(
    page.getByRole("row").filter({ hasText: "Lead events" })
  ).toContainText("CRM")
})

test("a key made on the tab is on the main-menu page for that application without a reload", async ({
  page,
}) => {
  const calls: Call[] = []
  await install(page, calls)

  await page.goto(`/applications/${SHOP}?tab=api-keys`)
  await page.getByRole("button", { name: "New API key", exact: true }).click()
  const dialog = page.getByRole("dialog", { name: "Create API key" })
  await dialog.getByLabel("Name").fill("Orders integration")
  await dialog.getByRole("checkbox", { name: "shop:orders:read" }).check()
  await dialog.getByRole("button", { name: "Create", exact: true }).click()
  await page
    .getByRole("dialog", { name: "Copy your API key now" })
    .getByRole("button", { name: "Close" })
    .first()
    .click()
  await expect(page.getByText("Orders integration")).toBeVisible()

  // A client-side navigation: the document, and with it the query cache, stays.
  const readsBefore = listReads(calls, "/api/v1/apikeys").length
  await page.evaluate((id) => {
    window.history.pushState({}, "", `/api-keys?applicationId=${id}`)
    window.dispatchEvent(new PopStateEvent("popstate"))
  }, SHOP)

  await expect(page.getByRole("heading", { name: "API Keys" })).toBeVisible()
  await expect(page.getByText("Orders integration")).toBeVisible()
  // Served from the cache entry the tab filled: one key shape on both surfaces.
  expect(listReads(calls, "/api/v1/apikeys").length).toBe(readsBefore)
})
