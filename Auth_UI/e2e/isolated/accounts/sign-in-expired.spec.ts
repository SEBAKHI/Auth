import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * OI-145: where a pending sign-in request ends when its handle can no longer be
 * resumed. The resume endpoint sends the browser here with `?client_id=` only
 * when that code is an active application's. The page names the application
 * from its public branding, says to start again from it, and offers the way
 * back only when the application publishes a home address that is safe to link
 * to: an absolute https URL.
 */

const LOGO_URL = "https://auth.example.com/uploads/images/partner-app.png"
// A 1x1 PNG: enough for the browser to decode.
const PNG = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=",
  "base64"
)

async function installBrandingApi(page: Page, homeUrl: string | null) {
  await page.route(LOGO_URL, (route) =>
    route.fulfill({ status: 200, contentType: "image/png", body: PNG })
  )
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() === "/api/v1/applications/app/public-branding") {
      await fulfillJson(route, { name: "Partner App", logoUrl: LOGO_URL, homeUrl })
      return true
    }
    return false
  })
}

test("names the application, says to start again there, and links home to its https address", async ({
  page,
}) => {
  await installBrandingApi(page, "https://partner.example.com/")

  await page.goto("/sign-in-expired?client_id=app")

  await expect(page.getByRole("heading", { name: "Sign-in request expired" })).toBeVisible()
  await expect(page.getByRole("img", { name: "Partner App" })).toBeVisible()
  await expect(
    page.getByText("The sign-in request from Partner App has expired or is no longer valid.", {
      exact: false,
    })
  ).toBeVisible()
  await expect(page.getByRole("link", { name: "Back to Partner App" })).toHaveAttribute(
    "href",
    "https://partner.example.com/"
  )
})

for (const homeUrl of [null, "http://partner.example.com/", "javascript:alert(1)", "/relative"]) {
  test(`offers no link for a home address that is not https: ${String(homeUrl)}`, async ({
    page,
  }) => {
    await installBrandingApi(page, homeUrl)

    await page.goto("/sign-in-expired?client_id=app")

    await expect(page.getByRole("img", { name: "Partner App" })).toBeVisible()
    await expect(page.getByText(/has expired or is no longer valid/)).toBeVisible()
    await expect(page.getByRole("link", { name: /Back to/ })).toHaveCount(0)
  })
}

test("without a client, says the same in the platform's own header", async ({ page }) => {
  const requests: string[] = []
  page.on("request", (request) => requests.push(new URL(request.url()).pathname))
  await installAnonymousApi(page, async () => false)

  await page.goto("/sign-in-expired")

  await expect(page.getByRole("heading", { name: "Sign-in request expired" })).toBeVisible()
  await expect(page.getByText(/request from the application has expired/)).toBeVisible()
  await expect(page.getByRole("link", { name: /Back to/ })).toHaveCount(0)
  expect(requests.filter((path) => path.includes("public-branding"))).toEqual([])
})

test("renders right-to-left in Arabic with its own copy", async ({ page }, testInfo) => {
  await installBrandingApi(page, "https://partner.example.com/")
  await page.addInitScript(() => localStorage.setItem("auth.language", "ar"))

  await page.goto("/sign-in-expired?client_id=app")

  await expect(page.getByRole("heading", { name: "انتهت صلاحية طلب الدخول" })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.dir)).toBe("rtl")
  await expect(page.getByRole("link", { name: "العودة إلى Partner App" })).toBeVisible()
  const logo = page.getByRole("img", { name: "Partner App" })
  await expect(logo).toBeVisible()
  await expect
    .poll(() => logo.evaluate((img: HTMLImageElement) => (img.complete ? img.naturalWidth : 0)))
    .toBeGreaterThan(0)

  await page.screenshot({ path: testInfo.outputPath("sign-in-expired-ar.png"), fullPage: true })
})
