import { expect, test } from "@playwright/test"

import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * UI batch item 12, owner decision D9, on the built console: the same page as
 * the accounts app's Home, at /my-applications (console supremacy). Since
 * 2026-11-02 the header's grid button opens the same tiles from any page, and
 * the user menu no longer lists the page. Nothing on it points at the accounts
 * origin.
 */

const APPLICATIONS = [
  {
    id: "a1",
    name: "CRM",
    logoUrl: null,
    logoUrlDark: null,
    launchUrl: "https://crm.example.test/start",
  },
  {
    id: "a2",
    name: "Wiki",
    logoUrl: null,
    logoUrlDark: null,
    launchUrl: "https://wiki.example.test/",
  },
]

async function install(page: import("@playwright/test").Page, language = "en") {
  const asked: string[] = []
  await installAuthenticatedApi(
    page,
    [],
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/users/me/applications")
        return false
      asked.push(url.search)
      await fulfillJson(route, APPLICATIONS)
      return true
    },
    { preferredLanguage: language }
  )
  return asked
}

test("the user menu no longer lists My applications", async ({ page }) => {
  await install(page)
  await page.goto("/")

  await page.getByRole("button", { name: "Isolated Operator" }).click()
  await expect(page.getByRole("menuitem", { name: "Sign out" })).toBeVisible()
  await expect(
    page.getByRole("menuitem", { name: "My applications" })
  ).toHaveCount(0)
})

test("My applications is still its own page, the shared Home page", async ({
  page,
}) => {
  const asked = await install(page)
  await page.goto("/my-applications")

  await expect(
    page.getByRole("heading", { level: 1, name: "My applications" })
  ).toBeVisible()
  const links = page
    .getByRole("list", { name: "My applications" })
    .getByRole("link")
  await expect(links).toHaveCount(2)
  await expect(links.first()).toHaveAttribute(
    "href",
    "https://crm.example.test/start"
  )
  await expect(links.first()).toHaveAttribute("target", "_blank")
  expect(asked).toEqual([""])

  // Console supremacy: nothing on the page sends the administrator to the accounts app.
  const hrefs = await page
    .locator("main a[href]")
    .evaluateAll((anchors) =>
      anchors.map((anchor) => (anchor as HTMLAnchorElement).href)
    )
  expect(hrefs.filter((href) => href.includes(":4176"))).toEqual([])
})

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    for (const width of [375, 1440]) {
      test(`screenshot: console My applications ${language} ${mode} ${width}px`, async ({
        page,
      }) => {
        await page.addInitScript(
          ([lang, theme]) => {
            localStorage.setItem("auth.language", lang)
            localStorage.setItem("theme", theme)
          },
          [language, mode] as const
        )
        await page.setViewportSize({ width, height: 900 })
        await install(page, language)
        await page.goto("/my-applications")

        const title = language === "ar" ? "تطبيقاتي" : "My applications"
        await expect(
          page.getByRole("heading", { level: 1, name: title })
        ).toBeVisible()
        await expect(
          page.getByRole("list", { name: title }).getByRole("link")
        ).toHaveCount(2)
        await expectNoShellOverflow(
          page,
          `console ${language} ${mode} ${width}`
        )
        await page.screenshot({
          path: test
            .info()
            .outputPath(`console-${language}-${mode}-${width}.png`),
        })
      })
    }
  }
}
