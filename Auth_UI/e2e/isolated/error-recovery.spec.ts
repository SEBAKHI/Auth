import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"
import { clickPageAction } from "./page-actions"

const USER_ID = "77777777-7777-7777-7777-777777777777"

const user = {
  id: USER_ID,
  email: "recovery@example.test",
  displayName: "Recovery Operator",
  firstName: "Recovery",
  lastName: "Operator",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: "2026-08-22T08:00:00Z",
}

async function installUserDetail(
  page: Page,
  update: (route: Route) => Promise<void>,
  preferredLanguage = "en"
) {
  await installAuthenticatedApi(
    page,
    ["users:read", "users:update"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/users/${USER_ID}`) {
        if (route.request().method() === "PUT") {
          await update(route)
        } else {
          await fulfillJson(route, user)
        }
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/organizations`) {
        await fulfillJson(route, [])
        return true
      }
      return false
    },
    { preferredLanguage }
  )
}

test("server validation stays local, inline, and focused", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await installUserDetail(page, async (route) => {
    await fulfillJson(
      route,
      {
        status: 400,
        title: "FirstName",
        detail: "raw backend validation text",
        errors: [
          { code: "FirstName", description: "raw backend validation text" },
          { code: "FutureInternalField", description: "internal contract detail" },
        ],
      },
      400
    )
  })

  await page.goto(`/users/${USER_ID}`)
  await page.getByRole("button", { name: "Edit", exact: true }).click()
  const dialog = page.getByRole("dialog", { name: "Edit user" })
  const firstName = dialog.getByRole("textbox", { name: "First name" })
  await firstName.fill("Changed")
  await dialog.getByRole("button", { name: "Save" }).click()

  await expect(
    dialog.getByText("Check this value and try again.", { exact: true })
  ).toBeVisible()
  await expect(firstName).toHaveAttribute("aria-invalid", "true")
  await expect(firstName).toBeFocused()
  await expect(page.getByText("raw backend validation text")).toHaveCount(0)
  await expect(page.getByText("internal contract detail")).toHaveCount(0)
})

test("Arabic transient feedback offers one safe replay of the same update", async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 667 })
  const bodies: unknown[] = []
  await installUserDetail(
    page,
    async (route) => {
      bodies.push(route.request().postDataJSON())
      if (bodies.length === 1) {
        await fulfillJson(
          route,
          {
            status: 503,
            title: "System.DatabaseUnavailableException",
            detail: "private database host and stack trace",
          },
          503
        )
        return
      }
      await fulfillJson(route, {})
    },
    "ar"
  )

  await page.goto(`/users/${USER_ID}`)
  await clickPageAction(page, "تعديل")
  const dialog = page.getByRole("dialog", { name: "تعديل مستخدم" })
  await dialog.getByRole("textbox", { name: "الاسم الأول" }).fill("استرداد")
  await dialog.getByRole("button", { name: "حفظ" }).click()

  await expect(
    page.getByText("تعذّر إكمال الإجراء", { exact: true })
  ).toBeVisible()
  await expect(
    page.getByText(
      "تعذّر على الخدمة إكمال الإجراء. حاول مرة أخرى، وإن استمر الخطأ فتواصل مع الدعم.",
      { exact: true }
    )
  ).toBeVisible()
  await expect(page.getByText(/private database|stack trace/i)).toHaveCount(0)

  await page.getByRole("button", { name: "حاول مرة أخرى", exact: true }).click()
  await expect.poll(() => bodies.length).toBe(2)
  expect(bodies[1]).toEqual(bodies[0])
  await expect(dialog).toHaveCount(0)
})
