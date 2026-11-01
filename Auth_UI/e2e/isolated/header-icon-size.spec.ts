import { expect, test, type Page } from "@playwright/test"

import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The header's icon size (owner, 2026-11-02) on the built console: Small or
 * Default (Google's size), chosen in Platform settings › Appearance. The
 * header stays 56px tall at both, and the avatar is as large as the buttons
 * beside it. The grid button beside the avatar opens the tiles panel.
 *
 * Measured, not trusted: every header icon button, its icon and the avatar are
 * read off the page against the owner's pixels.
 */

const PX = {
  small: { icon: 16, button: 36 },
  default: { icon: 24, button: 40 },
} as const
type Size = keyof typeof PX

const NAMES = ["CRM", "Wiki", "Help desk", "Payroll and human resources", "Fleet"]

const applications = (count: number) =>
  Array.from({ length: count }, (_, index) => ({
    id: `a${index}`,
    name: NAMES[index] ?? `Application ${index + 1}`,
    logoUrl: null,
    logoUrlDark: null,
    launchUrl: `https://app${index}.example.test/`,
  }))

async function install(
  page: Page,
  size: Size | null,
  language: "en" | "ar" = "en",
  count = 5
) {
  await installAuthenticatedApi(
    page,
    [],
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/users/me/applications") return false
      await fulfillJson(route, applications(count))
      return true
    },
    {
      preferredLanguage: language,
      // null: an appearance saved before the setting existed (no field).
      branding: {
        platformName: "AuthSystem",
        staySignedInOffered: true,
        theme: size === null ? {} : { headerIconSize: size },
      },
    }
  )
}

/** Every header icon button's box, its icon's, and the header's height. */
async function measure(page: Page) {
  return page.locator("header").first().evaluate((header) => {
    const box = (element: Element | null) => {
      const rect = element?.getBoundingClientRect()
      return rect ? { width: Math.round(rect.width), height: Math.round(rect.height) } : null
    }
    const buttons = [...header.querySelectorAll('[data-size="icon-header"]')].map((button) => ({
      name: button.getAttribute("aria-label") ?? button.textContent?.trim() ?? "",
      box: box(button),
      icon: box(button.querySelector('svg:not([data-slot="spinner"])')),
      avatar: box(button.querySelector('[data-slot="avatar"]')),
    }))
    return { header: Math.round(header.getBoundingClientRect().height), buttons }
  })
}

for (const size of [null, "small", "default"] as const) {
  // 768 and 1024: the narrowest desktop headers, with the sidebar open.
  for (const width of [375, 768, 1024, 1440]) {
    test(`header icons: ${size ?? "no field"} at ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 })
      await install(page, size)
      await page.goto("/")
      await expect(page.getByRole("button", { name: "Applications" })).toBeVisible()

      // An appearance without the field draws the default.
      const chosen: Size = size ?? "default"
      const drawn = PX[chosen]
      // The platform's appearance arrives with the branding answer; a cold
      // load (nothing cached yet) draws the preset's until then.
      await expect
        .poll(async () => (await measure(page)).buttons.at(-1)?.box?.width)
        .toBe(drawn.button)
      const { header, buttons } = await measure(page)

      expect(header).toBe(56)
      // Sidebar trigger (phone only), language, theme, apps, avatar.
      expect(buttons.map((b) => b.name)).toEqual([
        ...(width < 768 ? ["Toggle navigation"] : []),
        "Language",
        "Theme",
        "Applications",
        "Isolated Operator",
      ])
      for (const button of buttons) {
        expect(button.box, button.name).toEqual({ width: drawn.button, height: drawn.button })
        if (button.avatar) {
          // As large as the button (owner, 2026-11-02).
          expect(button.avatar, button.name).toEqual({ width: drawn.button, height: drawn.button })
        } else {
          expect(button.icon, button.name).toEqual({ width: drawn.icon, height: drawn.icon })
        }
      }
      await expectNoShellOverflow(page, `header ${chosen} ${width}`)
    })
  }
}

test("the grid button opens the tiles in a panel that scrolls inside the screen", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 400 })
  await install(page, "default", "en", 15)
  await page.goto("/")

  await page.getByRole("button", { name: "Applications" }).click()
  const panel = page.getByRole("list", { name: "Applications" })
  await expect(panel.getByRole("link")).toHaveCount(15)

  // A short screen: the panel stops at the screen's edge and scrolls instead.
  const content = page.locator('[data-slot="popover-content"]')
  const geometry = await content.evaluate((element) => {
    element.scrollTop = 9999
    return {
      bottom: element.getBoundingClientRect().bottom,
      scrolled: element.scrollTop > 0,
    }
  })
  expect(geometry.bottom).toBeLessThanOrEqual(400)
  expect(geometry.scrolled).toBe(true)
  await expect(panel.getByRole("link").last()).toBeInViewport()

  await page.keyboard.press("Escape")
  await expect(panel).toHaveCount(0)
})

for (const size of ["small", "default"] as const) {
  for (const language of ["en", "ar"] as const) {
    for (const mode of ["light", "dark"] as const) {
      for (const width of [375, 1440]) {
        test(`screenshot: header ${size} ${language} ${mode} ${width}px`, async ({ page }) => {
          await page.addInitScript(
            ([lang, theme]) => {
              localStorage.setItem("auth.language", lang)
              localStorage.setItem("theme", theme)
            },
            [language, mode] as const
          )
          await page.setViewportSize({ width, height: 640 })
          await install(page, size, language)
          await page.goto("/")
          const label = language === "ar" ? "التطبيقات" : "Applications"
          await page.getByRole("button", { name: label }).click()
          await expect(
            page.getByRole("list", { name: label }).getByRole("link")
          ).toHaveCount(5)
          await page.screenshot({
            path: test.info().outputPath(`header-${size}-${language}-${mode}-${width}.png`),
          })
        })
      }
    }
  }
}

for (const language of ["en", "ar"] as const) {
  test(`Platform settings › Appearance offers Small and Default, Default chosen, and saves the choice (${language})`, async ({
    page,
  }) => {
    const theme = {
      base: { preset: "neutral" },
      theme: { preset: "neutral" },
      chart: { preset: "cyan" },
      radius: "default",
      menuAccent: "subtle",
      controlSize: { preset: "default" },
      tableRowHeight: { preset: "default" },
    }
    const settings = { platformName: "AuthSystem", logoUrl: null, logoUrlDark: null, faviconUrl: null, theme }
    const saved: Record<string, unknown>[] = []
    await page.setViewportSize({ width: 1440, height: 1200 })
    await installAuthenticatedApi(
      page,
      ["platform-settings:manage"],
      async (route, url) => {
        const path = url.pathname.toLowerCase()
        if (path === "/api/v1/admin/platform-settings/theme" && route.request().method() === "PUT") {
          const body = route.request().postDataJSON() as Record<string, unknown>
          saved.push(body)
          await fulfillJson(route, { ...settings, theme: body })
          return true
        }
        if (path === "/api/v1/admin/platform-settings") {
          await fulfillJson(route, settings)
          return true
        }
        return false
      },
      { preferredLanguage: language, branding: { platformName: "AuthSystem", staySignedInOffered: true, theme } }
    )
    await page.goto("/admin/platform-settings?tab=appearance")

    const name = language === "ar" ? "حجم أيقونات الشريط العلوي" : "Header icon size"
    const group = page.getByRole("radiogroup", { name })
    // No "Google" in the copy, and no larger size (owner, 2026-11-02).
    await expect(group.getByRole("radio")).toHaveText(
      language === "ar" ? ["صغير", "افتراضي"] : ["Small", "Default"]
    )
    await expect(
      group.getByRole("radio", { name: language === "ar" ? "افتراضي" : "Default" })
    ).toBeChecked()

    // The platform's card previews on the console itself: the header changes at once.
    await group.getByRole("radio", { name: language === "ar" ? "صغير" : "Small" }).click()
    await expect
      .poll(async () => (await measure(page)).buttons.map((b) => b.box?.width))
      .toEqual([36, 36, 36, 36])
    expect((await measure(page)).header).toBe(56)
    await group.scrollIntoViewIfNeeded()
    await page.screenshot({ path: test.info().outputPath(`appearance-control-${language}.png`) })

    await page.getByRole("button", { name: language === "ar" ? "حفظ" : "Save", exact: true }).click()
    await expect.poll(() => saved.at(-1)?.headerIconSize).toBe("small")
  })
}
