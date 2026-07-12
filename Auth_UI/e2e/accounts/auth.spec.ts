import { expect, test } from "@playwright/test"

test.describe("accounts app authentication", () => {
  test("unauthenticated users are redirected to the sign-in page", async ({
    page,
  }) => {
    await page.goto("/")
    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole("button", { name: /sign in/i })).toBeVisible()
  })

  test("the sign-in page links to self-registration", async ({ page }) => {
    await page.goto("/login")
    await page.getByRole("link", { name: /sign up/i }).click()
    await expect(page).toHaveURL(/\/register$/)
    await expect(
      page.getByRole("button", { name: /create account/i })
    ).toBeVisible()
  })

  test("the registration form validates before submitting", async ({
    page,
  }) => {
    await page.goto("/register")
    await page.getByRole("button", { name: /create account/i }).click()
    // No API call happens; zod surfaces required-field messages client-side.
    await expect(page.getByText(/required/i).first()).toBeVisible()
    await expect(page).toHaveURL(/\/register$/)
  })
})
