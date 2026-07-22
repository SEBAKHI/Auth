import { expect, test } from "@playwright/test"

test("loads a fingerprinted production entry from a non-cacheable shell", async ({
  page,
}) => {
  const response = await page.goto("/login")

  expect(response?.ok()).toBe(true)
  await expect(page.locator("#root")).not.toBeEmpty()

  await expect(
    page.locator('meta[http-equiv="Cache-Control"]')
  ).toHaveAttribute("content", /no-store.*no-cache.*must-revalidate/)

  const entry = await page
    .locator('script[type="module"][src]')
    .first()
    .getAttribute("src")

  expect(entry).toMatch(/^\/assets\/index-[A-Za-z0-9_-]+\.js$/)
})
