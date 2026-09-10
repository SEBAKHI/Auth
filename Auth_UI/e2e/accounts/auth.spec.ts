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
    // Sign-up begins with the address alone; the name and password come only
    // after the mailed code has proven it (e2e/isolated/accounts/registration.spec.ts).
    await expect(page.getByRole("button", { name: /send code/i })).toBeVisible()
    await expect(page.getByLabel(/password/i)).toHaveCount(0)
  })

  test("the registration form validates before submitting", async ({
    page,
  }) => {
    await page.goto("/register")
    await page.getByRole("button", { name: /send code/i }).click()
    // No API call happens; zod surfaces the required-field message client-side.
    await expect(page.getByText(/required/i).first()).toBeVisible()
    await expect(page).toHaveURL(/\/register$/)
  })
})
