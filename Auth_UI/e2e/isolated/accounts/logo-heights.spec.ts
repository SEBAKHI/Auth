import { expect, type Page, test } from "@playwright/test"

import { expectNoCrushedContent } from "../layout-overflow"
import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * The logos' heights on the sign-in pages (UI batch P3, owner decisions D5, D6, D12): each logo
 * takes the height its owner chose (24–120px; the platform's 60px and an application's 48px by
 * default), on every page that shows it, with its proportions kept. Measured in the built app,
 * because a height, a width cap and an overflow are CSS facts a unit test cannot see.
 *
 * Every page reaches its logos through `AuthLayout`, the one place a logo box is sized; this spec
 * walks every route that renders it. A route that needs a state a cold visit does not have (the
 * second-factor code, a signed-in session) lands on another page of the same layout, and the
 * table below names where, so nothing passes by measuring a page it did not mean to.
 */

const AUTHORIZE = "https://auth.example.com/api/v1/auth/authorize?client_id=app&state=xyz"
const APP_LOGO = "https://auth.example.com/uploads/images/app.svg"
const PLATFORM_LOGO = "https://auth.example.com/uploads/images/platform.svg"

/** An image of the given proportions; its content is irrelevant, its shape is the test. */
const svg = (width: number, height: number) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><rect width="${width}" height="${height}" fill="#777"/></svg>`

const SHAPES = {
  /** At the width cap exactly: 4 × its height. */
  wordmark: { width: 256, height: 64 },
  square: { width: 64, height: 64 },
} as const
type Shape = keyof typeof SHAPES

/**
 * Every route that renders `AuthLayout` in this app (re-run `git grep "<AuthLayout"` when one is
 * added), and where a cold visit lands. A route that redirects is still measured where it lands.
 */
const ROUTES: { path: string; lands: string }[] = [
  { path: "/login", lands: "/login" },
  { path: "/register", lands: "/register" },
  { path: "/register/verify", lands: "/register" },
  { path: "/forgot-password", lands: "/forgot-password" },
  { path: "/reset-password?token=t0k3n", lands: "/reset-password" },
  { path: "/two-factor", lands: "/login" },
  { path: "/two-factor/required", lands: "/login" },
  { path: "/force-password-change", lands: "/login" },
  { path: "/verify-email", lands: "/login" },
  { path: "/accept-invitation?token=t0k3n", lands: "/accept-invitation" },
  { path: "/account-recovery", lands: "/account-recovery" },
  { path: "/create-organization", lands: "/create-organization" },
  { path: "/delete-account", lands: "/delete-account" },
  { path: "/deletion-scheduled", lands: "/login" },
  { path: "/logout", lands: "/logout" },
  { path: "/signed-out", lands: "/signed-out" },
  { path: "/sign-in-expired", lands: "/sign-in-expired" },
]

/** The pages that name an application, each the way its address carries it. */
const APP_PAGES = [
  `/login?returnTo=${encodeURIComponent(AUTHORIZE)}`,
  `/register?returnTo=${encodeURIComponent(AUTHORIZE)}`,
  `/forgot-password?returnTo=${encodeURIComponent(AUTHORIZE)}`,
  "/logout?client_id=app",
  "/signed-out?client_id=app",
  "/sign-in-expired?client_id=app",
]

async function open(
  page: Page,
  path: string,
  options: { platformPx?: number | null; appPx?: number | null; shape?: Shape; width?: number } = {}
) {
  const shape = SHAPES[options.shape ?? "wordmark"]
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() !== "/api/v1/applications/app/public-branding") return false
    await fulfillJson(route, {
      name: "Partner App",
      logoUrl: APP_LOGO,
      logoUrlDark: null,
      logoHeightPx: options.appPx ?? null,
      homeUrl: null,
    })
    return true
  })
  // Registered after the defaults, so it answers first.
  await page.route("**/api/v1/Platform/branding", (route) =>
    fulfillJson(route, {
      platformName: "AuthSystem",
      logoUrl: PLATFORM_LOGO,
      logoHeightPx: options.platformPx ?? null,
      staySignedInOffered: true,
    })
  )
  await page.route("https://auth.example.com/uploads/images/*.svg", (route) =>
    route.fulfill({ contentType: "image/svg+xml", body: svg(shape.width, shape.height) })
  )
  await page.setViewportSize({ width: options.width ?? 1440, height: 900 })
  await page.goto(path)
  await expect(page.getByRole("img", { name: "AuthSystem" })).toBeVisible()
}

/** A logo as drawn: its box, the image's own size, and how it is fitted. */
async function measure(page: Page, scope: "header" | "main", name: string) {
  const logo = page.locator(scope).getByRole("img", { name })
  await expect(logo).toBeVisible()
  await logo.evaluate((img: HTMLImageElement) => img.decode())
  return logo.evaluate((img: HTMLImageElement) => {
    const box = img.getBoundingClientRect()
    // The room the logo's column leaves it: the form column above the title, the header less
    // its toggles (and their gap) in the corner.
    const header = img.closest("header")
    const room = header
      ? header.clientWidth - (header.lastElementChild as HTMLElement).offsetWidth - 16
      : img.parentElement!.clientWidth
    return {
      height: box.height,
      width: box.width,
      right: box.right,
      left: box.left,
      room,
      naturalRatio: img.naturalWidth / img.naturalHeight,
      fit: getComputedStyle(img).objectFit,
    }
  })
}

/**
 * The height drawn, and the box's width against the image's own proportions (±1px): height ×
 * ratio, unless the column is narrower, when the box takes the column and the drawing inside it
 * keeps its proportions (object-fit: contain).
 */
function expectDrawn(drawn: Awaited<ReturnType<typeof measure>>, heightPx: number, where: string) {
  expect(drawn.height, `${where}: height`).toBeCloseTo(heightPx, 0)
  const width = Math.min(heightPx * drawn.naturalRatio, drawn.room)
  expect(Math.abs(drawn.width - width), `${where}: width keeps the proportions`).toBeLessThanOrEqual(1)
  expect(drawn.fit, `${where}: never stretched or cropped`).toBe("contain")
}

test("covers every route that renders the sign-in layout", () => {
  // A floor, so an emptied table cannot pass every case below.
  expect(ROUTES).toHaveLength(17)
})

for (const { path, lands } of ROUTES) {
  test(`${path}: the platform's mark at 24, 60 (the default) and 120px, in both shapes`, async ({ page }) => {
    for (const [platformPx, expected] of [
      [24, 24],
      [null, 60],
      [120, 120],
    ] as const) {
      for (const shape of ["wordmark", "square"] as const) {
        await open(page, path, { platformPx, shape })
        expect(new URL(page.url()).pathname).toBe(lands)
        expectDrawn(await measure(page, "header", "AuthSystem"), expected, `${path} ${shape} ${platformPx}`)
        await page.unrouteAll({ behavior: "wait" })
      }
    }
  })
}

for (const path of APP_PAGES) {
  test(`${path}: both logos at their owners' heights, the application's above the title`, async ({ page }) => {
    for (const [platformPx, appPx, expectedApp] of [
      [null, null, 48],
      [24, 24, 24],
      [120, 120, 120],
      [90, 72, 72],
    ] as const) {
      for (const shape of ["wordmark", "square"] as const) {
        await open(page, path, { platformPx, appPx, shape })
        const where = `${path} ${shape} ${platformPx}/${appPx}`
        expectDrawn(await measure(page, "main", "Partner App"), expectedApp, `${where} application`)
        expectDrawn(await measure(page, "header", "AuthSystem"), platformPx ?? 60, `${where} platform`)
        await page.unrouteAll({ behavior: "wait" })
      }
    }
  })
}

for (const language of ["en", "ar"] as const) {
  test(`${language} at 375px: a 120px wordmark in each place stays inside the page`, async ({ page }) => {
    await page.addInitScript((lang) => localStorage.setItem("auth.language", lang), language)
    await open(page, APP_PAGES[0]!, { platformPx: 120, appPx: 120, width: 375 })

    for (const [scope, name] of [
      ["header", "AuthSystem"],
      ["main", "Partner App"],
    ] as const) {
      const drawn = await measure(page, scope, name)
      // The box keeps its height; a column narrower than 4 × 120px shrinks the width instead,
      // and the drawing inside it keeps its proportions (object-fit: contain).
      expect(drawn.height, `${name} height`).toBeCloseTo(120, 0)
      expect(drawn.fit).toBe("contain")
      expect(drawn.left, `${name} start`).toBeGreaterThanOrEqual(0)
      expect(drawn.right, `${name} end`).toBeLessThanOrEqual(375)
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0)
    const header = await page.locator("header").evaluate((el) => el.scrollWidth - el.clientWidth)
    expect(header).toBe(0)
    await expectNoCrushedContent(page, `${language} at 375px with 120px logos`)
    await page.screenshot({ path: test.info().outputPath(`logos-120-${language}-375.png`), fullPage: true })
  })
}

test("a design that hides the platform's mark leaves the application's logo above the title at its height", async ({
  page,
}) => {
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() !== "/api/v1/applications/app/public-branding") return false
    await fulfillJson(route, {
      name: "Partner App",
      logoUrl: APP_LOGO,
      logoHeightPx: 100,
      loginDesign: { id: "d1", contentHash: "a".repeat(64), showPlatformLogo: false },
    })
    return true
  })
  await page.route("https://auth.example.com/uploads/images/*.svg", (route) =>
    route.fulfill({ contentType: "image/svg+xml", body: svg(256, 64) })
  )
  await page.setViewportSize({ width: 900, height: 900 })
  await page.goto(APP_PAGES[0]!)

  expectDrawn(await measure(page, "main", "Partner App"), 100, "application, platform mark hidden")
  await expect(page.locator("header").getByRole("img")).toHaveCount(0)
})

// The proof the owner looks at: the sign-in page of an application's flow at the default
// heights, in each language, mode and width.
for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    for (const width of [375, 1440]) {
      test(`screenshot: ${language} ${mode} ${width}px, both logos at their defaults`, async ({ page }) => {
        await page.addInitScript(
          ([lang, theme]) => {
            localStorage.setItem("auth.language", lang)
            localStorage.setItem("theme", theme)
          },
          [language, mode] as const
        )
        await open(page, APP_PAGES[0]!, { width })
        expectDrawn(await measure(page, "main", "Partner App"), 48, `${language} ${mode} ${width} application`)
        expectDrawn(await measure(page, "header", "AuthSystem"), 60, `${language} ${mode} ${width} platform`)
        expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0)
        await page.screenshot({ path: test.info().outputPath(`signin-${language}-${mode}-${width}.png`) })
      })
    }
  }
}
