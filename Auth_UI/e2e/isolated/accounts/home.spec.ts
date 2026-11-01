import { expect, test, type Page } from "@playwright/test"

import { expectNoShellOverflow } from "../layout-overflow"
import {
  fulfillJson,
  installAnonymousApi,
  loginResponse,
} from "./mock-anonymous-api"

/**
 * UI batch item 12 on the built accounts app: signing in lands on Home, a grid
 * of large tiles, one per application the person can open, the name under the
 * logo; a tile opens its application in a new tab.
 *
 * The owner's sentence: «إضافة قسم "الرئيسية" في تطبيق الحسابات، ويحتوي على
 * التطبيقات التي يمكن للمستخدم الدخول إليها على شكل أيقونات كبيرة وتحت كل أيقونة
 * اسم التطبيق. النقر على الأيقونة يفتح التطبيق في تبويب جديد.»
 */

type Tile = {
  id: string
  name: string
  logoUrl: string | null
  logoUrlDark: string | null
  launchUrl: string
}

function person(language: "en" | "ar") {
  return {
    id: "99999999-9999-9999-9999-999999999999",
    email: "person@example.test",
    firstName: "Jane",
    lastName: "Doe",
    preferredLanguage: language,
    timeZone: "UTC",
    roles: [],
    permissions: [],
  }
}

function tile(index: number, overrides: Partial<Tile> = {}): Tile {
  return {
    id: `00000000-0000-0000-0000-${String(index).padStart(12, "0")}`,
    name: `Application ${index}`,
    logoUrl: null,
    logoUrlDark: null,
    launchUrl: `https://app${index}.example.test/start`,
    ...overrides,
  }
}

const SEVEN: Tile[] = [
  tile(1, {
    name: "CRM",
    logoUrl: "https://auth.example.com/uploads/images/crm.svg",
  }),
  tile(2, {
    name: "Wiki",
    logoUrl: "https://auth.example.com/uploads/images/missing.svg",
  }),
  tile(3, { name: "Help desk" }),
  tile(4, { name: "Payroll and human resources self-service portal" }),
  tile(5, { name: "Fleet" }),
  tile(6, { name: "Inventory" }),
  tile(7, { name: "Learning" }),
]

const LOGO = `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="96" viewBox="0 0 256 96"><rect width="256" height="96" rx="12" fill="#888"/></svg>`

async function signIn(
  page: Page,
  applications: Tile[] | "fail",
  options: { language?: "en" | "ar"; delayMs?: number } = {}
) {
  const language = options.language ?? "en"
  const who = person(language)
  const asked: string[] = []

  await page.route("https://auth.example.com/uploads/images/**", (route) =>
    route.request().url().endsWith("/crm.svg")
      ? route.fulfill({ contentType: "image/svg+xml", body: LOGO })
      : route.fulfill({ status: 404, body: "" })
  )
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/login") {
      await fulfillJson(route, loginResponse(who))
      return true
    }
    const bearer = await route.request().headerValue("authorization")
    if (path === "/api/v1/auth/me" || path === "/api/v1/users/me") {
      await (bearer
        ? fulfillJson(route, who)
        : fulfillJson(route, { status: 401, code: "Http.Unauthorized" }, 401))
      return true
    }
    if (path === "/api/v1/users/me/applications") {
      asked.push(`${url.search}|${bearer ? "bearer" : "none"}`)
      if (options.delayMs)
        await new Promise((resolve) => setTimeout(resolve, options.delayMs))
      await (applications === "fail"
        ? fulfillJson(route, { status: 500, code: "General.Unexpected" }, 500)
        : fulfillJson(route, applications))
      return true
    }
    return false
  })

  await page.goto("/login")
  await page
    .getByLabel(language === "ar" ? "البريد الإلكتروني" : "Email")
    .fill(who.email)
  await page
    .getByLabel(language === "ar" ? "كلمة المرور" : "Password")
    .fill("Passw0rd!")
  await page
    .getByRole("button", {
      name: language === "ar" ? "تسجيل الدخول" : "Sign in",
      exact: true,
    })
    .click()
  await page.waitForURL((url) => url.pathname === "/")
  return asked
}

const grid = (page: Page, name = "My applications") =>
  page.getByRole("list", { name })

test("signing in lands on Home, first in the nav, and asks only for the caller's own list", async ({
  page,
}) => {
  const asked = await signIn(page, SEVEN)

  await expect(
    page.getByRole("heading", { level: 1, name: "My applications" })
  ).toBeVisible()
  await expect(grid(page).getByRole("link")).toHaveCount(7)
  expect(asked).toEqual(["|bearer"])

  const nav = page.getByRole("navigation", { name: "Account" })
  await expect(nav.getByRole("link").first()).toHaveText("Home")
  await expect(nav.getByRole("link", { name: "Home" })).toHaveAttribute(
    "aria-current",
    "page"
  )
})

test("a tile is a link that opens its application in a new tab, with no opener and no referrer", async ({
  page,
  context,
}) => {
  await context.route("https://app1.example.test/**", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: "<!doctype html><title>CRM</title><script>document.title = JSON.stringify({ opener: window.opener === null, referrer: document.referrer })</script>",
    })
  )
  await signIn(page, SEVEN)

  const crm = grid(page).getByRole("link", { name: "CRM (opens in a new tab)" })
  await expect(crm).toHaveAttribute("href", "https://app1.example.test/start")
  await expect(crm).toHaveAttribute("target", "_blank")
  expect((await crm.getAttribute("rel"))?.split(" ").sort()).toEqual([
    "noopener",
    "noreferrer",
  ])

  const [popup] = await Promise.all([context.waitForEvent("page"), crm.click()])
  await popup.waitForLoadState()
  await expect.poll(() => popup.title()).toContain("opener")
  expect(JSON.parse(await popup.title())).toEqual({
    opener: true,
    referrer: "",
  })
  expect(page.url()).toMatch(/\/$/)
  await popup.close()
})

test("the keyboard reaches every tile in order and Enter opens it", async ({
  page,
  context,
}) => {
  await context.route("https://app*.example.test/**", (route) =>
    route.fulfill({
      contentType: "text/html",
      body: "<!doctype html><title>opened</title>",
    })
  )
  await signIn(page, SEVEN.slice(0, 3))

  const links = grid(page).getByRole("link")
  await links.first().focus()
  await expect(links.first()).toBeFocused()
  await page.keyboard.press("Tab")
  await expect(links.nth(1)).toBeFocused()

  const [popup] = await Promise.all([
    context.waitForEvent("page"),
    page.keyboard.press("Enter"),
  ])
  await popup.waitForLoadState()
  expect(popup.url()).toBe("https://app2.example.test/start")
  await popup.close()
})

test("a logo that fails to load shows the name's initials", async ({
  page,
}) => {
  await signIn(page, SEVEN)

  const wiki = grid(page).getByRole("link", { name: /^Wiki/ })
  await expect(wiki.getByText("W", { exact: true })).toBeVisible()
  const crm = grid(page).getByRole("link", { name: /^CRM/ })
  await expect(crm.locator("img")).toHaveCSS("object-fit", "contain")
})

test("a long name stops at two lines, so its row stays as short as the others, and a tile has no border", async ({
  page,
}) => {
  await signIn(page, SEVEN)

  const name = (pattern: RegExp) =>
    grid(page)
      .getByRole("link", { name: pattern })
      .locator("[data-slot=item-title]")
  const lines = (box: { height: number } | null, lineHeight: number) =>
    Math.round(box!.height / lineHeight)
  const lineHeight = await name(/^Wiki/).evaluate((element) =>
    parseFloat(getComputedStyle(element).lineHeight)
  )

  // Floor: a one-line name measures one line, so the count below can move.
  expect(lines(await name(/^Wiki/).boundingBox(), lineHeight)).toBe(1)
  expect(lines(await name(/^Payroll/).boundingBox(), lineHeight)).toBe(2)
  await expect(name(/^Payroll/)).toHaveAttribute(
    "title",
    "Payroll and human resources self-service portal"
  )

  // A name that wraps grows downwards: its logo stays level with the logos
  // beside it (owner, 2026-11-01).
  const logoOffsets = await grid(page)
    .getByRole("link")
    .evaluateAll((elements) =>
      elements.map((element) => {
        const media = element.querySelector("[data-slot=item-media]")!
        return Math.round(
          media.getBoundingClientRect().top -
            element.getBoundingClientRect().top
        )
      })
    )
  expect(logoOffsets).toHaveLength(SEVEN.length)
  expect(new Set(logoOffsets).size, "logo offset inside each tile").toBe(1)

  const crm = grid(page).getByRole("link", { name: /^CRM/ })
  await expect(crm).toHaveCSS("border-top-color", "rgba(0, 0, 0, 0)")
})

test("placeholder tiles while the list loads, then the tiles", async ({
  page,
}) => {
  await signIn(page, SEVEN, { delayMs: 1500 })

  await expect(page.getByRole("list", { name: "Loading…" })).toHaveAttribute(
    "aria-busy",
    "true"
  )
  await expect(grid(page).getByRole("link")).toHaveCount(7)
  await expect(page.getByRole("list", { name: "Loading…" })).toHaveCount(0)
})

test("with no application, a plain sentence instead of an empty grid", async ({
  page,
}) => {
  await signIn(page, [])

  await expect(page.getByText("No applications yet")).toBeVisible()
  await expect(
    page.getByText("Applications you can sign in to will appear here.")
  ).toBeVisible()
  await expect(grid(page)).toHaveCount(0)
})

test("a failed list offers a retry, never an empty Home that looks final", async ({
  page,
}) => {
  await signIn(page, "fail")

  await expect(page.getByText("Failed to load data.")).toBeVisible()
  await expect(page.getByRole("button", { name: "Retry" })).toBeVisible()
  await expect(page.getByText("No applications yet")).toHaveCount(0)
})

for (const count of [1, 7, 100]) {
  for (const width of [375, 1440]) {
    test(`${count} application(s) at ${width}px: uniform tiles, nothing spills`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      const list =
        count === 7
          ? SEVEN
          : Array.from({ length: count }, (_, index) => tile(index + 1))
      await signIn(page, list)

      const tiles = grid(page).getByRole("link")
      await expect(tiles).toHaveCount(count)
      const boxes = await tiles.evaluateAll((elements) =>
        elements.map((element) => {
          const rect = element.getBoundingClientRect()
          return {
            width: Math.round(rect.width),
            top: Math.round(rect.top),
            height: Math.round(rect.height),
          }
        })
      )
      // One width for every tile, and tiles of one row share a height: a long
      // name wraps inside its tile, never widens or misaligns it.
      expect(new Set(boxes.map((box) => box.width)).size, "tile widths").toBe(1)
      const rows = new Map<number, Set<number>>()
      for (const box of boxes)
        rows.set(box.top, (rows.get(box.top) ?? new Set()).add(box.height))
      for (const heights of rows.values())
        expect(heights.size, "heights in one row").toBe(1)
      // A single tile stays tile-sized; it does not stretch across the page.
      expect(boxes[0]!.width).toBeLessThanOrEqual(width === 375 ? 200 : 260)

      await expectNoShellOverflow(page, `${count} tiles at ${width}px`)
    })
  }
}

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    for (const width of [375, 1440]) {
      test(`screenshot: Home ${language} ${mode} ${width}px`, async ({
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
        await signIn(page, SEVEN, { language })

        const title = language === "ar" ? "تطبيقاتي" : "My applications"
        await expect(
          page.getByRole("heading", { level: 1, name: title })
        ).toBeVisible()
        await expect(grid(page, title).getByRole("link")).toHaveCount(7)
        await expect(page.locator("html")).toHaveAttribute(
          "dir",
          language === "ar" ? "rtl" : "ltr"
        )
        if (language === "ar") {
          await expect(
            grid(page, title).getByRole("link", {
              name: "CRM (يفتح في تبويب جديد)",
            })
          ).toBeVisible()
          // A Latin name on an Arabic page keeps its own direction, so the
          // two-line cut drops its end, never its first letters.
          await expect(
            grid(page, title)
              .getByRole("link", { name: /^Payroll/ })
              .locator("[data-slot=item-title]")
          ).toHaveCSS("direction", "ltr")
        }
        await expectNoShellOverflow(page, `${language} ${mode} ${width}`)
        await page.screenshot({
          path: test.info().outputPath(`home-${language}-${mode}-${width}.png`),
        })
      })
    }
  }
}

/**
 * The header's grid button (owner, 2026-11-02) in the accounts app too: beside
 * the avatar, it opens the same tiles in a panel.
 */
for (const language of ["en", "ar"] as const) {
  for (const width of [375, 1440]) {
    test(`the header's grid button opens the tiles in a panel (${language}, ${width}px)`, async ({
      page,
    }) => {
      await page.addInitScript((lang) => localStorage.setItem("auth.language", lang), language)
      await page.setViewportSize({ width, height: 900 })
      await signIn(page, SEVEN, { language })

      const launcher = page.getByRole("button", {
        name: language === "ar" ? "التطبيقات" : "Applications",
      })
      await launcher.click()
      const panel = page.getByRole("list", {
        name: language === "ar" ? "التطبيقات" : "Applications",
      })
      await expect(panel.getByRole("link")).toHaveCount(7)
      await expect(panel.getByRole("link").first()).toHaveAttribute("target", "_blank")
      await page.screenshot({
        path: test.info().outputPath(`accounts-panel-${language}-${width}.png`),
      })
    })
  }
}
