import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * A revoked API key or webhook key says why in its row panel (owner,
 * 2026-10-31): the reason its administrator gave when revoking it. A live key,
 * and a revoked key whose reason was left empty, show no reason row at all.
 * Both key kinds, on the application's tabs, in Arabic and English.
 */

const catalogs = { en, ar } as const
const SHOP = "33333333-3333-3333-3333-333333333333"

const application = {
  id: SHOP,
  name: "Shop",
  code: "SHOP",
  isActive: true,
  accessMode: "Restricted",
  redirectUris: [],
  createdAt: "2026-07-19T16:32:00Z",
}

function keys(extra: Record<string, unknown>) {
  const key = (id: string, name: string, more: Record<string, unknown>) => ({
    id,
    applicationId: SHOP,
    applicationName: "Shop",
    name,
    keyPrefix: "ak_prod_",
    environment: "production",
    createdAt: "2026-10-01T09:00:00Z",
    createdBy: "55555555-5555-5555-5555-555555555555",
    isRevoked: false,
    revokedAt: null,
    revokeReason: null,
    scopes: [],
    ...extra,
    ...more,
  })
  return [
    key("20000000-0000-0000-0000-000000000001", "Leaked key", {
      isRevoked: true,
      revokedAt: "2026-10-20T09:00:00Z",
      revokeReason: "Committed to a public repository",
    }),
    key("20000000-0000-0000-0000-000000000002", "Quietly revoked", {
      isRevoked: true,
      revokedAt: "2026-10-21T09:00:00Z",
      revokeReason: null,
    }),
    key("20000000-0000-0000-0000-000000000003", "Live key", {}),
  ]
}

async function install(page: Page, language: keyof typeof catalogs) {
  await installAuthenticatedApi(
    page,
    ["applications:read", "apikeys:read", "webhookkeys:read"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/applications/${SHOP}`) {
        await fulfillJson(route, application)
        return true
      }
      if (path === "/api/v1/apikeys") {
        await fulfillJson(route, keys({}))
        return true
      }
      if (path === "/api/v1/webhookkeys") {
        await fulfillJson(route, keys({ keyPrefix: "whk_prod_", targetUrl: "https://shop.example.com/hooks" }))
        return true
      }
      if (path === "/api/v1/applications") {
        await fulfillJson(route, { applications: [], totalCount: 0, totalPages: 0 })
        return true
      }
      await fulfillJson(route, { items: [], users: [], organizations: [], totalCount: 0, totalPages: 0 })
      return true
    },
    { preferredLanguage: language }
  )
}

async function openRow(page: Page, details: string, name: string) {
  const row = page.getByRole("row", { name: details }).filter({ hasText: name })
  await row.focus()
  await page.keyboard.press("Enter")
  const sheet = page.getByRole("dialog")
  await expect(sheet).toBeVisible()
  return sheet
}

for (const language of ["en", "ar"] as const) {
  for (const tab of ["api-keys", "webhook-keys"] as const) {
    test(`a revoked key's panel names its reason; others have no reason row (${tab}, ${language})`, async ({
      page,
    }, testInfo) => {
      const t = catalogs[language] as typeof en
      await page.setViewportSize({ width: 1440, height: 1000 })
      await install(page, language)
      await page.goto(`/applications/${SHOP}?tab=${tab}`)
      await expect(page.getByText("Leaked key")).toBeVisible()

      const label = t.fields.revokeReason
      let sheet = await openRow(page, t.common.details, "Leaked key")
      await expect(sheet.getByText(label, { exact: true })).toBeVisible()
      await expect(sheet.getByText("Committed to a public repository")).toBeVisible()
      await page.screenshot({ path: testInfo.outputPath(`${tab}-revoke-reason-${language}.png`) })
      await page.keyboard.press("Escape")
      await expect(sheet).toBeHidden()

      sheet = await openRow(page, t.common.details, "Quietly revoked")
      await expect(sheet.getByText(t.fields.revokedAt, { exact: true })).toBeVisible()
      await expect(sheet.getByText(label, { exact: true })).toHaveCount(0)
      await page.keyboard.press("Escape")
      await expect(sheet).toBeHidden()

      sheet = await openRow(page, t.common.details, "Live key")
      await expect(sheet.getByText(label, { exact: true })).toHaveCount(0)
    })
  }
}
