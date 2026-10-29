import { expect, test, type Locator, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The platform's control height and table row height (Platform settings ›
 * Appearance), measured where they land: the real height of a button, an
 * input, an input group, a select, a toggle and a table row on the page, for
 * each of the three steps and a custom value, in Arabic and English, light and
 * dark. Every case leaves a screenshot of the appearance card and of a table.
 */

type Size = { preset: string; px?: number | null }

const STEPS = {
  compact: { control: 32, row: 40 },
  default: { control: 36, row: 44 },
  comfortable: { control: 40, row: 48 },
} as const

/** A custom pair that no step produces, so a fallback to a step cannot pass. */
const CUSTOM = { control: 30, row: 61 }

const CASES: { name: string; controlSize: Size; tableRowHeight: Size; control: number; row: number }[] = [
  ...Object.entries(STEPS).map(([preset, px]) => ({
    name: preset,
    controlSize: { preset },
    tableRowHeight: { preset },
    ...px,
  })),
  {
    name: "custom",
    controlSize: { preset: "custom", px: CUSTOM.control },
    tableRowHeight: { preset: "custom", px: CUSTOM.row },
    ...CUSTOM,
  },
]

const catalogs = { en, ar } as const

const USER = {
  id: "0f8fad5b-d9cb-469f-a165-70867728950e",
  email: "ada@example.test",
  firstName: "Ada",
  lastName: "Lovelace",
  displayName: "Ada Lovelace",
  status: "Active",
  isDeleted: false,
  roles: [],
  createdAt: "2026-08-01T09:00:00Z",
}

function themeWith(controlSize: Size, tableRowHeight: Size) {
  return {
    base: { preset: "neutral" },
    theme: { preset: "neutral" },
    chart: { preset: "cyan" },
    radius: "default",
    menuAccent: "subtle",
    controlSize,
    tableRowHeight,
  }
}

async function install(
  page: Page,
  {
    mode,
    language,
    theme,
    onPut,
  }: {
    mode: "light" | "dark"
    language: keyof typeof catalogs
    theme: ReturnType<typeof themeWith>
    onPut?: (body: Record<string, unknown>) => void
  }
) {
  await page.addInitScript((value) => {
    localStorage.setItem("theme", value)
  }, mode)
  const settings = {
    platformName: "Isolated Platform",
    logoUrl: null,
    logoUrlDark: null,
    faviconUrl: null,
    theme,
  }
  await installAuthenticatedApi(
    page,
    ["platform-settings:manage", "users:read", "users:update", "users:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/admin/platform-settings/theme" && route.request().method() === "PUT") {
        const body = route.request().postDataJSON() as Record<string, unknown>
        onPut?.(body)
        await fulfillJson(route, { ...settings, theme: body })
        return true
      }
      if (path === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, settings)
        return true
      }
      if (path === "/api/v1/users") {
        await fulfillJson(route, { users: [USER], totalCount: 1, totalPages: 1, pageNumber: 1, pageSize: 20 })
        return true
      }
      await fulfillJson(route, { items: [], users: [], totalCount: 0 })
      return true
    },
    {
      preferredLanguage: language,
      branding: { platformName: "Isolated Platform", staySignedInOffered: true, theme },
    }
  )
}

/** The token the page is drawn with, once the platform's stylesheet (if any) applied. */
async function expectTokens(page: Page, control: number, row: number) {
  const read = (name: string) =>
    page.evaluate((n) => getComputedStyle(document.documentElement).getPropertyValue(n).trim(), name)
  await expect.poll(() => read("--control-height")).toBe(`${control / 16}rem`)
  await expect.poll(() => read("--table-row-height")).toBe(`${row / 16}rem`)
}

async function height(locator: Locator) {
  const box = await locator.boundingBox()
  expect(box, "measured element has a box").not.toBeNull()
  return box!.height
}

/**
 * Polled: buttons carry `transition-all`, so for 150ms after the platform's
 * stylesheet lands a button is between its old height and its new one.
 */
async function expectHeight(locator: Locator, px: number, label: string) {
  await expect.poll(() => height(locator), { message: label }).toBeCloseTo(px, 1)
}

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    for (const size of CASES) {
      test(`control ${size.control}px and row ${size.row}px (${size.name}, ${language}, ${mode})`, async ({
        page,
      }, testInfo) => {
        const t = catalogs[language]
        await page.setViewportSize({ width: 1440, height: 1000 })
        await install(page, { mode, language, theme: themeWith(size.controlSize, size.tableRowHeight) })
        await page.goto("/admin/platform-settings")

        const card = page
          .locator('[data-slot="card"]')
          .filter({ has: page.getByText(t.platformSettings.appearance.title, { exact: true }) })
        await expect(card).toBeVisible()
        await expect(page.locator("html")).toHaveAttribute("dir", language === "ar" ? "rtl" : "ltr")
        await expectTokens(page, size.control, size.row)

        // The tile painted in the page's own mode, so it shows what visitors see.
        const tile = card.locator(`[data-mode="${mode}"]`)
        const save = tile.locator('[data-slot="button"][data-size="default"]')
        const group = tile.locator('[data-slot="input-group"]')
        await expect(save).toHaveCount(1)
        await expect(group).toHaveCount(1)

        await expectHeight(save, size.control, "button")
        await expectHeight(group, size.control, "input group")
        // The input group and the button beside it stay one row.
        expect(Math.abs((await height(group)) - (await height(save))), "group = button").toBeLessThan(0.5)
        await expectHeight(tile.locator('[data-slot="button"][data-size="sm"]'), size.control - 4, "small button")

        const select = card.locator('[data-slot="select-trigger"][data-size="default"]').first()
        await expectHeight(select, size.control, "select")
        const toggle = card.locator('[data-slot="toggle-group-item"]').first()
        await expectHeight(toggle, size.control, "toggle")
        const input = page.getByRole("textbox", { name: t.platformSettings.platformName, exact: true })
        await expectHeight(input, size.control, "input")
        // The sidebar keeps its own heights (its rail would not hold more).
        await expectHeight(page.locator('[data-slot="sidebar-trigger"]').first(), 32, "sidebar trigger")

        // A row of one text line is the row height. Measured inside the
        // cell's border: the 1px line between rows is shared half and half
        // by the rows it separates (collapsed borders), as it always was.
        // The second row is drawn as the list pages draw theirs: an avatar
        // (32px) and an icon-sm actions button (control − 4px). It is taller
        // than the setting by exactly the taller of the two, less a text line.
        const previewCells = tile.locator('[data-slot="table-row"] [data-slot="table-cell"]')
        await expect(previewCells).toHaveCount(4)
        await expect(tile.locator('[data-slot="table-row"]').nth(1).locator('[data-slot="avatar"]')).toHaveCount(1)
        const richRow = Math.max(32, size.control - 4) + size.row - 20
        await expect
          .poll(() => previewCells.evaluateAll((all) => all.map((cell) => cell.clientHeight)), {
            message: "table rows",
          })
          .toEqual([size.row, size.row, richRow, richRow])

        await card.screenshot({ path: testInfo.outputPath(`card-${size.name}-${language}-${mode}.png`) })

        // The DataTable on a real list: its cells are laid on the same token,
        // and the row's text cell measures the same as the preview's.
        await page.goto("/users")
        const row = page.locator('tbody [data-slot="table-row"]').filter({ hasText: USER.displayName })
        await expect(row).toBeVisible()
        await expectTokens(page, size.control, size.row)
        const cells = row.locator('[data-slot="table-cell"]')
        const padding = await cells
          .first()
          .evaluate((cell) => parseFloat(getComputedStyle(cell).paddingTop))
        expect(padding, "cell padding").toBeCloseTo((size.row - 20) / 2, 1)
        // A row holding something taller than a text line (the avatar) grows
        // by exactly that difference: the setting adds the same padding.
        const tallest = await cells.evaluateAll((all) =>
          Math.max(
            20,
            ...all.flatMap((cell) => [...cell.children].map((child) => child.getBoundingClientRect().height))
          )
        )
        expect(tallest, "tallest cell content").toBeGreaterThan(20)
        const rowHeight = await height(row)
        expect(rowHeight - (tallest + size.row - 20), "users row").toBeGreaterThanOrEqual(-0.5)
        expect(rowHeight - (tallest + size.row - 20), "users row").toBeLessThanOrEqual(1.5)
        await page
          .locator('[data-slot="table-container"]')
          .first()
          .screenshot({ path: testInfo.outputPath(`users-${size.name}-${language}-${mode}.png`) })
      })
    }
  }
}

for (const language of ["en", "ar"] as const) {
  for (const mode of ["light", "dark"] as const) {
    test(`the card at 375px with custom heights stays on screen (${language}, ${mode})`, async ({
      page,
    }, testInfo) => {
      const t = catalogs[language]
      await page.setViewportSize({ width: 375, height: 900 })
      await install(page, {
        mode,
        language,
        theme: themeWith({ preset: "custom", px: 56 }, { preset: "custom", px: 72 }),
      })
      await page.goto("/admin/platform-settings")
      const card = page
        .locator('[data-slot="card"]')
        .filter({ has: page.getByText(t.platformSettings.appearance.title, { exact: true }) })
      await expect(card).toBeVisible()
      await expectTokens(page, 56, 72)
      const a = t.platformSettings.appearance
      await card.getByText(a.controlSize, { exact: true }).scrollIntoViewIfNeeded()
      await expectNoShellOverflow(page, `appearance ${language} ${mode} 375`)

      // The number field is as wide as a height, not as the column, and
      // every control in the card stays inside the screen.
      const field = page.getByRole("spinbutton", { name: a.customPx.replace("{{name}}", a.tableRowHeight) })
      const group = card.locator('[data-slot="input-group"]').filter({ has: field })
      await expect(group).toHaveCount(1)
      expect((await group.boundingBox())!.width, "number field width").toBeLessThan(200)
      const outside = await card.evaluate((root) =>
        [...root.querySelectorAll('[data-slot="button"], [data-slot="toggle-group-item"], [data-slot="input-group"]')]
          .map((el) => el.getBoundingClientRect())
          .filter((box) => box.width > 0 && (box.left < 0 || box.right > window.innerWidth)).length
      )
      expect(outside, "controls outside the screen").toBe(0)
      await expectHeight(page.locator('[data-slot="sidebar-trigger"]').first(), 32, "sidebar trigger")

      // Viewport shots: the card is taller than the screen, and an element
      // shot of it inside the shell's scroll pane is stitched, not seen.
      await page.screenshot({ path: testInfo.outputPath(`card-375-sizes-${language}-${mode}.png`) })
      await card.getByText(a.preview, { exact: true }).scrollIntoViewIfNeeded()
      await page.screenshot({ path: testInfo.outputPath(`card-375-preview-${language}-${mode}.png`) })
    })
  }
}

for (const language of ["en", "ar"] as const) {
  test(`the header holds everything in it at the tallest control height (${language}, 1440px)`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1440, height: 900 })
    await install(page, {
      mode: "light",
      language,
      theme: themeWith({ preset: "custom", px: 56 }, { preset: "custom", px: 72 }),
    })
    await page.goto("/admin/platform-settings")
    await expectTokens(page, 56, 72)

    const header = page.locator("header").first()
    // The home link is navigation and keeps its 40px; the controls beside it
    // follow the setting (a floor: taller than any step would make them).
    const home = header.locator("a[href]").first()
    await expectHeight(home, 40, "home link")
    await expect
      .poll(
        () =>
          header
            .locator('[data-slot="button"]')
            .evaluateAll((all) => Math.max(0, ...all.map((el) => el.getBoundingClientRect().height))),
        { message: "tallest header control" }
      )
      .toBeGreaterThan(44)

    const boxes = await header.evaluate((root) => {
      const frame = root.getBoundingClientRect()
      const inside = [...root.querySelectorAll("*")]
        .map((el) => el.getBoundingClientRect())
        .filter((box) => box.width > 0 && box.height > 0)
      return {
        measured: inside.length,
        // Half a pixel: a 56px control is centred in the 55px left above
        // the header's bottom border.
        outside: inside.filter((box) => box.top < frame.top - 0.75 || box.bottom > frame.bottom + 0.75).length,
      }
    })
    // A floor: a header with nothing measured would hold everything.
    expect(boxes.measured, "elements measured").toBeGreaterThan(5)
    expect(boxes.outside, "elements taller than the header").toBe(0)
  })
}

test("in Arabic the preview cuts the end of a Latin name, never its start", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 })
  await install(page, { mode: "light", language: "ar", theme: themeWith({ preset: "default" }, { preset: "default" }) })
  await page.goto("/admin/platform-settings")
  const tile = page.locator('[data-mode="light"]')
  const names = tile.locator('[data-slot="table-cell"] .truncate')
  await expect(names).toHaveCount(2)

  const measured = await names.evaluateAll((all) =>
    all.map((el) => {
      const range = document.createRange()
      range.setStart(el.firstChild!, 0)
      range.setEnd(el.firstChild!, 1)
      const letter = range.getBoundingClientRect()
      const shown = el.getBoundingClientRect()
      return {
        cut: el.scrollWidth > el.clientWidth,
        firstLetterShown: letter.width > 0 && letter.left >= shown.left - 1 && letter.right <= shown.right + 1,
      }
    })
  )
  // A floor: with nothing cut there is nothing for the check to see.
  expect(measured.filter((name) => name.cut).length, "names cut").toBeGreaterThan(0)
  expect(measured.map((name) => name.firstLetterShown)).toEqual([true, true])
})

for (const [label, theme, fetched] of [
  ["the shipped heights download no theme registry", themeWith({ preset: "default" }, { preset: "default" }), false],
  // The floor: a sign-in page whose heights alone changed does compute a
  // stylesheet, so the detector below can see the registry when it comes.
  ["changed heights alone compute the stylesheet", themeWith({ preset: "compact" }, { preset: "default" }), true],
] as const) {
  test(`${label} (signed out, /login)`, async ({ page }) => {
    const scripts: string[] = []
    page.on("response", (response) => {
      if (response.url().endsWith(".js")) scripts.push(response.url())
    })
    await page.route("**/api/v1/**", async (route) => {
      const path = new URL(route.request().url()).pathname.toLowerCase()
      if (path === "/api/v1/platform/branding") {
        await fulfillJson(route, { platformName: "Isolated Platform", staySignedInOffered: true, theme })
        return
      }
      await fulfillJson(route, { title: "Unauthorized" }, 401)
    })

    await page.goto("/login")
    await expect(page.getByRole("textbox").first()).toBeVisible()
    await expectTokens(page, fetched ? 32 : 36, 44)
    const registry = scripts.filter((url) => /\/build-theme-[^/]*\.js$/.test(url))
    expect(registry.length > 0, `theme registry fetched: ${registry.join(", ")}`).toBe(fetched)
  })
}

for (const language of ["en", "ar"] as const) {
  test(`choosing heights previews them live and saves them (${language})`, async ({ page }) => {
    const t = catalogs[language]
    const a = t.platformSettings.appearance
    const bodies: Record<string, unknown>[] = []
    await page.setViewportSize({ width: 1440, height: 1000 })
    await install(page, {
      mode: "light",
      language,
      theme: themeWith({ preset: "default" }, { preset: "default" }),
      onPut: (body) => bodies.push(body),
    })
    await page.goto("/admin/platform-settings")
    await expectTokens(page, 36, 44)

    const card = page
      .locator('[data-slot="card"]')
      .filter({ has: page.getByText(a.title, { exact: true }) })
    const controls = card.getByRole("radiogroup", { name: a.controlSize, exact: true })
    const rows = card.getByRole("radiogroup", { name: a.tableRowHeight, exact: true })
    // The preview tiles are inert and hidden, so this is the card's own Save.
    const save = card.getByRole("button", { name: t.common.save, exact: true })
    await expect(save).toHaveCount(1)

    // A step previews at once, on this very page.
    await controls.getByRole("radio", { name: a.sizes.compact }).click()
    await expectTokens(page, 32, 44)

    // Custom starts from the height on screen, then follows the number.
    await controls.getByRole("radio", { name: a.sizes.custom }).click()
    const controlPx = card.getByRole("spinbutton", { name: a.customPx.replace("{{name}}", a.controlSize) })
    await expect(controlPx).toHaveValue("32")
    await controlPx.fill("50")
    await expectTokens(page, 50, 44)

    // Rows lower than the controls cannot be saved, and the page says why.
    await expect(page.getByText(a.rowBelowControls.replace("{{px}}", "50"))).toBeVisible()
    await expect(save).toBeDisabled()

    await rows.getByRole("radio", { name: a.sizes.custom }).click()
    const rowPx = card.getByRole("spinbutton", { name: a.customPx.replace("{{name}}", a.tableRowHeight) })
    await expect(rowPx).toHaveValue("44")
    await rowPx.fill("73")
    await expect(page.getByText(a.customPxRange.replace("{{min}}", "32").replace("{{max}}", "72"))).toBeVisible()
    await expect(save).toBeDisabled()

    await rowPx.fill("60")
    await expectTokens(page, 50, 60)
    await expect(save).toBeEnabled()
    await save.click()

    await expect.poll(() => bodies.length).toBe(1)
    expect(bodies[0].controlSize).toEqual({ preset: "custom", px: 50 })
    expect(bodies[0].tableRowHeight).toEqual({ preset: "custom", px: 60 })
  })
}
