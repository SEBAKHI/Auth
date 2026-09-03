import { expect, test } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The two halves of the password contract, on the one screen the isolated
 * suite can reach: every rule the live policy enables is shown and ticked as
 * the person types, and every reason the server refuses is shown at once —
 * not the first sentence in a toast, one rule per submit.
 *
 * The refusal is deliberately one the list could not have predicted (a common
 * pattern) plus one that contradicts the fetched policy (a stricter minimum,
 * as after an operator's change): both must land under the control.
 */
test("a new password shows its rules live and every server refusal at once", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await installAuthenticatedApi(
    page,
    ["users:read", "users:create"],
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/users") return false
      if (route.request().method() === "GET") {
        await fulfillJson(route, {
          users: [],
          totalCount: 0,
          totalPages: 0,
          pageNumber: 1,
          pageSize: 20,
        })
        return true
      }
      await fulfillJson(
        route,
        {
          status: 400,
          title: "Password.CommonPattern",
          detail: "Password contains a common pattern that is easy to guess.",
          errors: [
            {
              code: "Password.CommonPattern",
              description:
                "Password contains a common pattern that is easy to guess.",
            },
            {
              code: "Password.TooShort",
              description: "Password must be at least 12 characters long.",
            },
          ],
        },
        400
      )
      return true
    }
  )

  await page.goto("/users")
  await page.getByRole("button", { name: "New user" }).click()
  const dialog = page.getByRole("dialog", { name: "Create user" })
  const password = dialog.getByLabel("Password", { exact: true })
  const rules = dialog.locator('[data-slot="password-requirement"]')
  const metRules = dialog.locator(
    '[data-slot="password-requirement"][data-met="true"]'
  )

  // The mocked policy enables all five rules; nothing is met before typing.
  await expect(rules).toHaveCount(5)
  await expect(metRules).toHaveCount(0)
  await expect(
    dialog.getByRole("list", { name: "Password requirements" })
  ).toContainText("At least 8 characters")

  // Live: four of five after a symbol-less password, five once it has one.
  await password.fill("Passw0rd")
  await expect(metRules).toHaveCount(4)
  await expect(
    rules.filter({ hasText: "At least one symbol" })
  ).toHaveAttribute("data-met", "false")
  await password.fill("Passw0rd!")
  await expect(metRules).toHaveCount(5)

  await dialog.getByRole("textbox", { name: "Email" }).fill("new@example.test")
  await dialog.getByRole("textbox", { name: "First name" }).fill("New")
  await dialog.getByRole("textbox", { name: "Last name" }).fill("Person")
  await dialog.getByRole("button", { name: "Create" }).click()

  // Both sentences, under the control, with the control marked and focused.
  const message = dialog.getByRole("alert")
  await expect(message).toContainText(
    "Password contains a common pattern that is easy to guess."
  )
  await expect(message).toContainText(
    "Password must be at least 12 characters long."
  )
  await expect(password).toHaveAttribute("aria-invalid", "true")
  await expect(password).toBeFocused()
  await expect(dialog).toBeVisible()
})
