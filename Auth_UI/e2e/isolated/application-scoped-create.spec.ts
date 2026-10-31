import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The application page's Roles and Permissions tabs carry a create button, the
 * way its key tabs do (owner, 2026-10-31). It creates inside that application:
 * the dialog shows the application without letting it change, the request
 * names it, and the tab lists the new row at once. Without the create
 * permission there is no button. Arabic and English, 1440px and 375px.
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

type Kind = "roles" | "permissions"

const row = (kind: Kind, id: string, code: string, name: string) => ({
  id,
  code,
  name,
  applicationId: SHOP,
  isActive: true,
  ...(kind === "roles" ? { isSystem: false } : { isWildcard: false }),
  createdAt: "2026-10-01T09:00:00Z",
})

interface Posted {
  path: string
  body: Record<string, unknown>
}

async function install(
  page: Page,
  language: keyof typeof catalogs,
  permissions: string[],
  posted: Posted[]
) {
  const store: Record<Kind, ReturnType<typeof row>[]> = {
    roles: [row("roles", "40000000-0000-0000-0000-000000000001", "shop-clerk", "Clerk")],
    permissions: [
      row("permissions", "50000000-0000-0000-0000-000000000001", "shop:orders:read", "Read orders"),
    ],
  }
  await installAuthenticatedApi(
    page,
    ["applications:read", ...permissions],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path === `/api/v1/applications/${SHOP}`) {
        await fulfillJson(route, application)
        return true
      }
      for (const kind of ["roles", "permissions"] as const) {
        if (path === `/api/v1/applications/${SHOP}/${kind}`) {
          await fulfillJson(route, store[kind])
          return true
        }
        if (path === `/api/v1/${kind}` && method === "POST") {
          const body = route.request().postDataJSON() as Record<string, unknown>
          posted.push({ path, body })
          store[kind] = [
            ...store[kind],
            row(kind, `${kind === "roles" ? 4 : 5}0000000-0000-0000-0000-000000000099`, String(body.code), String(body.name)),
          ]
          await fulfillJson(route, store[kind].at(-1), 201)
          return true
        }
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

const CASES = [
  {
    kind: "roles" as const,
    permission: "roles:create",
    newLabel: (t: typeof en) => t.roles.newRole,
    title: (t: typeof en) => t.roles.createTitle,
    code: "shop-manager",
  },
  {
    kind: "permissions" as const,
    permission: "permissions:create",
    newLabel: (t: typeof en) => t.permissions.newPermission,
    title: (t: typeof en) => t.permissions.createTitle,
    code: "shop:orders:refund",
  },
]

for (const language of ["en", "ar"] as const) {
  for (const { kind, permission, newLabel, title, code } of CASES) {
    test(`the ${kind} tab creates inside its application (${language})`, async ({ page }, testInfo) => {
      const t = catalogs[language] as typeof en
      const posted: Posted[] = []
      await page.setViewportSize({ width: 1440, height: 1000 })
      await install(page, language, [`${kind}:read`, permission], posted)
      await page.goto(`/applications/${SHOP}?tab=${kind}`)

      const panel = page.getByRole("tabpanel")
      await panel.getByRole("button", { name: newLabel(t) }).click()

      const dialog = page.getByRole("dialog", { name: title(t) })
      const picker = dialog.getByRole("combobox")
      await expect(picker).toHaveText("Shop")
      await expect(picker).toBeDisabled()
      await page.screenshot({ path: testInfo.outputPath(`${kind}-create-dialog-${language}.png`) })

      await dialog.getByLabel(t.common.code).fill(code)
      await dialog.getByLabel(t.common.name).fill("Manager")
      await dialog.getByRole("button", { name: t.common.create }).click()
      await expect(dialog).toBeHidden()

      expect(posted).toHaveLength(1)
      expect(posted[0].body).toMatchObject({ applicationId: SHOP, code })
      // The tab reads its own list again: the new row is there without a reload.
      await expect(panel.getByText("Manager", { exact: true })).toBeVisible()

      await expectNoShellOverflow(page)
      await page.screenshot({ path: testInfo.outputPath(`${kind}-tab-after-create-${language}.png`), fullPage: true })
    })
  }

  test(`at 375px the create buttons fit, and without the create permission there are none (${language})`, async ({
    page,
  }, testInfo) => {
    const t = catalogs[language] as typeof en
    await page.setViewportSize({ width: 375, height: 812 })
    await install(page, language, ["roles:read", "roles:create", "permissions:read"], [])

    await page.goto(`/applications/${SHOP}?tab=roles`)
    await expect(page.getByRole("tabpanel").getByRole("button", { name: t.roles.newRole })).toBeVisible()
    await expectNoShellOverflow(page)
    await page.screenshot({ path: testInfo.outputPath(`roles-tab-375-${language}.png`), fullPage: true })

    await page.goto(`/applications/${SHOP}?tab=permissions`)
    await expect(page.getByRole("tabpanel").getByText("Read orders")).toBeVisible()
    await expect(
      page.getByRole("tabpanel").getByRole("button", { name: t.permissions.newPermission })
    ).toHaveCount(0)
  })
}
