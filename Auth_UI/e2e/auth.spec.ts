import { expect, test } from "@playwright/test"

test.describe("authentication", () => {
  test("unauthenticated users are redirected to the sign-in page", async ({
    page,
  }) => {
    await page.goto("/")
    await expect(page).toHaveURL(/\/login$/)
    await expect(
      page.getByRole("button", { name: /sign in/i })
    ).toBeVisible()
  })

  test("the language toggle switches the document direction to RTL", async ({
    page,
  }) => {
    await page.goto("/login")
    // Open the language menu and choose Arabic.
    await page.getByRole("button", { name: /language/i }).click()
    await page.getByRole("menuitem", { name: "العربية" }).click()
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl")
  })

  // Full login flow — enable once a seeded admin account is available:
  //
  // test("an admin can sign in", async ({ page }) => {
  //   await page.goto("/login")
  //   await page.getByLabel(/email/i).fill(process.env.E2E_EMAIL!)
  //   await page.getByLabel(/password/i).fill(process.env.E2E_PASSWORD!)
  //   await page.getByRole("button", { name: /sign in/i }).click()
  //   await expect(page).toHaveURL(/\/$/)
  // })
})
