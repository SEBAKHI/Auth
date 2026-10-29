import AxeBuilder from "@axe-core/playwright"
import { expect, test, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import {
  firstMultipartPart,
  pngDimensions,
  pngOf,
  readPixels,
  type Rgba,
} from "./image-bytes"
import { WCAG_TAGS, actionable, describeViolations } from "./axe-known-gaps"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The frame editor between choosing an image and uploading it, run on the
 * production console build. What carries the meaning is read out of the bytes
 * that reach the (mocked) API: the uploaded PNG's own dimensions, and its
 * pixels decoded back in the browser - red where the left half of the source
 * was kept, blue for the right half, alpha 0 where the margin is.
 */

const RED: Rgba = [255, 0, 0, 255]
const BLUE: Rgba = [0, 0, 255, 255]

/** 400 x 200: the left half red, the right half blue. */
const SOURCE = pngOf(400, 200, (x) => (x < 200 ? RED : BLUE))
const PICKED = { name: "brand.png", mimeType: "image/png", buffer: SOURCE }

/** A 1 x 1 GIF: enough to be one. */
const GIF = Buffer.from("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", "base64")

const catalogs = { en, ar } as const

const USER_ID = "0f8fad5b-d9cb-469f-a165-70867728950e"
const USER = {
  id: USER_ID,
  email: "ada@example.test",
  displayName: "Ada Lovelace",
  firstName: "Ada",
  lastName: "Lovelace",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: "2026-08-01T09:00:00Z",
}

const SETTINGS = {
  platformName: "Isolated Platform",
  logoUrl: null,
  logoUrlDark: null,
  faviconUrl: null,
}

interface Captured {
  uploads: Buffer[]
  settingsWrites: unknown[]
}

async function install(
  page: Page,
  options: {
    language?: keyof typeof catalogs
    theme?: "light" | "dark"
    /** Holds every upload's response until it settles: a Save still in flight. */
    holdUploads?: Promise<void>
  } = {}
): Promise<Captured> {
  const captured: Captured = { uploads: [], settingsWrites: [] }
  if (options.theme) {
    await page.addInitScript((value) => localStorage.setItem("theme", value), options.theme)
  }
  await installAuthenticatedApi(
    page,
    ["users:read", "users:update", "users:manage", "platform-settings:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path === "/api/v1/images" && method === "POST") {
        captured.uploads.push(route.request().postDataBuffer()!)
        await options.holdUploads
        await fulfillJson(route, {
          key: "images/isolated.png",
          url: "http://localhost:4175/uploads/images/isolated.png",
        })
        return true
      }
      if (path === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, USER)
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/profile-image`) {
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/organizations`) {
        await fulfillJson(route, [])
        return true
      }
      if (path === "/api/v1/admin/platform-settings") {
        if (method === "PUT") captured.settingsWrites.push(route.request().postDataJSON())
        await fulfillJson(route, SETTINGS)
        return true
      }
      return false
    },
    { preferredLanguage: options.language ?? "en" }
  )
  return captured
}

async function openPhoto(page: Page, t: typeof en = en) {
  await page.goto(`/users/${USER_ID}`)
  await expect(page.getByRole("heading", { name: "Ada Lovelace" })).toBeVisible()
  await page.locator('input[type="file"]').setInputFiles(PICKED)
  const dialog = page.getByRole("dialog", { name: t.imageFrame.title })
  await expect(dialog).toBeVisible()
  await expect(dialog.locator('[data-slot="image-frame-stage"]')).toBeVisible()
  return dialog
}

async function openFavicon(page: Page, file = PICKED) {
  await page.goto("/admin/platform-settings")
  await expect(page.getByText(en.platformSettings.favicon, { exact: true })).toBeVisible()
  // The themed logo edits through its own dialog, so the favicon's is the only
  // file input on the page.
  await expect(page.locator('input[type="file"]')).toHaveCount(1)
  await page.locator('input[type="file"]').setInputFiles(file)
  const dialog = page.getByRole("dialog", { name: en.imageFrame.title })
  await expect(dialog).toBeVisible()
  return dialog
}

async function uploaded(captured: Captured) {
  await expect.poll(() => captured.uploads.length, { timeout: 15_000 }).toBe(1)
  return firstMultipartPart(captured.uploads[0])
}

test("a photo left as it was uploads the original file, byte for byte", async ({ page }) => {
  const captured = await install(page)
  const dialog = await openPhoto(page)

  await expect(dialog.getByText(en.imageFrame.description.photo)).toBeVisible()
  // A person's photo is previewed in the circle it is shown in.
  await expect(dialog.locator('[data-slot="image-frame-stage"] circle')).toHaveCount(2)
  // ...and is square: no free-aspect edge grips.
  await expect(dialog.locator("[data-edge]")).toHaveCount(0)
  await expect(dialog.getByText(en.imageFrame.original)).toBeVisible()

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  const part = await uploaded(captured)

  expect(part.headers).toMatch(/content-type:\s*image\/png/i)
  expect(part.headers).toMatch(/filename="brand\.png"/)
  expect(part.bytes.length).toBeGreaterThan(64)
  expect(part.bytes.equals(SOURCE)).toBe(true)
  await expect(dialog).toBeHidden()
})

test("cancelling the frame uploads nothing", async ({ page }) => {
  const captured = await install(page)
  const dialog = await openPhoto(page)
  await dialog.getByRole("button", { name: en.common.cancel, exact: true }).click()
  await expect(dialog).toBeHidden()
  // Give a wrongly wired upload the time it would take to arrive.
  await page.waitForTimeout(500)
  expect(captured.uploads).toHaveLength(0)
})

test("zooming a photo crops a centred square", async ({ page }) => {
  const captured = await install(page)
  const dialog = await openPhoto(page)

  const zoom = dialog.getByRole("slider", { name: en.imageFrame.zoom })
  await zoom.focus()
  await zoom.press("End")
  await expect(zoom).toHaveAttribute("aria-valuenow", "800")
  // The 400 px bounds square at 8x: 50 x 50, centred on the colour boundary.
  await expect(
    dialog.getByText(en.imageFrame.outputSize.replace("{{width}}", "50").replace("{{height}}", "50"))
  ).toBeVisible()

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  const part = await uploaded(captured)

  expect(part.headers).toMatch(/content-type:\s*image\/png/i)
  expect(pngDimensions(part.bytes)).toEqual({ width: 50, height: 50 })
  expect(await readPixels(page, part.bytes, [[5, 25], [45, 25]])).toEqual([RED, BLUE])
})

test("dragging the image moves what the frame keeps", async ({ page }) => {
  const captured = await install(page)
  const dialog = await openPhoto(page)

  const zoom = dialog.getByRole("slider", { name: en.imageFrame.zoom })
  await zoom.focus()
  await zoom.press("End")

  // Drag the image to the left: the frame then keeps more of its right side.
  const stage = dialog.locator('[data-slot="image-frame-stage"]')
  const box = (await stage.boundingBox())!
  const y = box.y + box.height / 2
  for (let i = 0; i < 2; i++) {
    await page.mouse.move(box.x + box.width - 10, y)
    await page.mouse.down()
    await page.mouse.move(box.x + 10, y, { steps: 8 })
    await page.mouse.up()
  }

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  const part = await uploaded(captured)

  expect(pngDimensions(part.bytes)).toEqual({ width: 50, height: 50 })
  // All of it from the blue half now.
  expect(await readPixels(page, part.bytes, [[2, 25], [25, 25], [47, 25]])).toEqual([
    BLUE,
    BLUE,
    BLUE,
  ])
})

test("a favicon is framed square, and its margin is transparent", async ({ page }) => {
  const captured = await install(page)
  const dialog = await openFavicon(page)

  await expect(dialog.getByText(en.imageFrame.description.favicon)).toBeVisible()
  await expect(dialog.locator("[data-edge]")).toHaveCount(0)

  // PageUp is ten steps: a 10% margin on the 400 px square.
  const margin = dialog.getByRole("slider", { name: en.imageFrame.margin })
  await margin.focus()
  await margin.press("PageUp")
  await expect(margin).toHaveAttribute("aria-valuenow", "10")

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  const part = await uploaded(captured)

  expect(pngDimensions(part.bytes)).toEqual({ width: 480, height: 480 })
  const [margin00, bar, red, blue] = await readPixels(page, part.bytes, [
    [5, 5],
    [240, 100], // the bar above a wide image: transparent, not a crop
    [100, 240],
    [380, 240],
  ])
  expect(margin00[3]).toBe(0)
  expect(bar[3]).toBe(0)
  expect(red).toEqual(RED)
  expect(blue).toEqual(BLUE)
  await expect.poll(() => captured.settingsWrites.length).toBe(1)
  // The uploaded key lands in the favicon's own field, not a logo's.
  expect(captured.settingsWrites[0]).toMatchObject({
    faviconUrl: "images/isolated.png",
    logoUrl: null,
  })
})

test("a logo's aspect is free: one edge trimmed, then a margin added", async ({
  page,
}, testInfo) => {
  const captured = await install(page)
  await page.goto("/admin/platform-settings")
  await page.getByRole("button", { name: en.platformSettings.logo, exact: true }).click()
  await page.getByRole("menuitem", { name: en.common.change, exact: true }).click()
  const logos = page.getByRole("dialog", { name: en.platformSettings.logoDialogTitle })
  await expect(logos).toBeVisible()
  await logos.locator('[data-mode="light"] input[type="file"]').setInputFiles(PICKED)

  const dialog = page.getByRole("dialog", { name: en.imageFrame.title })
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText(en.imageFrame.description.logo)).toBeVisible()
  await expect(dialog.locator("[data-edge]")).toHaveCount(4)

  // Shift+Left on the right edge: 10% of the 400 px crop off the right side.
  const right = dialog.locator('[data-edge="right"]')
  await right.focus()
  // Each grip is a focusable separator whose value is its place in the image.
  await expect(right).toHaveAttribute("role", "separator")
  await expect(right).toHaveAttribute("aria-orientation", "vertical")
  await expect(right).toHaveAttribute("aria-valuemax", "400")
  await expect(right).toHaveAttribute("aria-valuenow", "400")
  await right.press("Shift+ArrowLeft")
  await expect(right).toHaveAttribute("aria-valuenow", "360")
  await expect(right).toHaveAttribute("aria-valuetext", "360 px")
  await expect(
    dialog.getByText(en.imageFrame.outputSize.replace("{{width}}", "360").replace("{{height}}", "200"))
  ).toBeVisible()

  // A 10% margin of the 360 px crop on every side.
  const margin = dialog.getByRole("slider", { name: en.imageFrame.margin })
  await margin.focus()
  await margin.press("PageUp")
  await dialog.screenshot({ path: testInfo.outputPath("frame-logo.png"), animations: "disabled" })

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  const part = await uploaded(captured)

  expect(pngDimensions(part.bytes)).toEqual({ width: 432, height: 272 })
  const [corner, red, lastBlue, past] = await readPixels(page, part.bytes, [
    [5, 5],
    [36 + 10, 136],
    [36 + 355, 136],
    [36 + 365, 136], // the trimmed 40 px are gone: margin, not blue
  ])
  expect(corner[3]).toBe(0)
  expect(red).toEqual(RED)
  expect(lastBlue).toEqual(BLUE)
  expect(past[3]).toBe(0)
  await expect.poll(() => captured.settingsWrites.length).toBe(1)
  expect(captured.settingsWrites[0]).toMatchObject({
    logoUrl: "images/isolated.png",
    faviconUrl: null,
  })
})

test("a GIF warns that its animation is lost", async ({ page }) => {
  const captured = await install(page)
  const dialog = await openFavicon(page, {
    name: "spinner.gif",
    mimeType: "image/gif",
    buffer: GIF,
  })
  await expect(dialog.getByText(en.imageFrame.gifTitle)).toBeVisible()
  await expect(dialog.getByText(en.imageFrame.gifBody)).toBeVisible()

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  const part = await uploaded(captured)
  // Untouched: the GIF itself, not a re-encode.
  expect(part.bytes.equals(GIF)).toBe(true)
})

for (const language of ["en", "ar"] as const) {
  for (const theme of ["light", "dark"] as const) {
    for (const width of [375, 1440]) {
      test(`the frame editor fits and reads right (${language}, ${theme}, ${width}px)`, async ({
        page,
      }, testInfo) => {
        const t = catalogs[language]
        await page.setViewportSize({ width, height: 900 })
        await install(page, { language, theme })
        const dialog = await openPhoto(page, t)

        await expect(dialog.getByText(t.imageFrame.description.photo)).toBeVisible()
        await expect(dialog.getByRole("slider", { name: t.imageFrame.zoom })).toBeVisible()
        const margin = dialog.getByRole("slider", { name: t.imageFrame.margin })
        await margin.focus()
        await margin.press("PageUp")

        // The dialog stays inside the viewport, and nothing in it is wider than it.
        const fits = await dialog.evaluate((el) => {
          const rect = el.getBoundingClientRect()
          return {
            inside: rect.left >= 0 && rect.right <= window.innerWidth,
            overflow: el.scrollWidth - el.clientWidth,
          }
        })
        expect(fits).toEqual({ inside: true, overflow: 0 })
        const stage = (await dialog.locator('[data-slot="image-frame-stage"]').boundingBox())!
        expect(stage.width).toBeGreaterThan(200)
        expect(stage.height).toBeCloseTo(stage.width, 0)

        await dialog.screenshot({
          path: testInfo.outputPath(`frame-${language}-${theme}-${width}.png`),
          animations: "disabled",
        })
      })
    }
  }
}

/** Opens the editor the way a person does: the avatar's menu, then Change. */
async function chooseThroughMenu(page: Page, avatar: ReturnType<Page["getByRole"]>) {
  await avatar.click()
  const chooser = page.waitForEvent("filechooser")
  await page.getByRole("menuitem", { name: en.common.change, exact: true }).click()
  await (await chooser).setFiles(PICKED)
  const dialog = page.getByRole("dialog", { name: en.imageFrame.title })
  await expect(dialog.locator('[data-slot="image-frame-stage"]')).toBeVisible()
  return dialog
}

for (const close of ["Cancel", "Escape"] as const) {
  test(`closing the editor with ${close} hands focus back to the avatar`, async ({ page }) => {
    await install(page)
    await page.goto(`/users/${USER_ID}`)
    const avatar = page.getByRole("button", { name: en.common.avatar })
    const dialog = await chooseThroughMenu(page, avatar)

    if (close === "Cancel") {
      await dialog.getByRole("button", { name: en.common.cancel, exact: true }).click()
    } else {
      await page.keyboard.press("Escape")
    }
    await expect(dialog).toBeHidden()
    await expect(avatar).toBeFocused()
  })
}

test("saving hands focus back to the avatar, while the upload runs and after", async ({ page }) => {
  let release!: () => void
  const held = new Promise<void>((resolve) => (release = resolve))
  const captured = await install(page, { holdUploads: held })
  await page.goto(`/users/${USER_ID}`)
  const avatar = page.getByRole("button", { name: en.common.avatar })
  const dialog = await chooseThroughMenu(page, avatar)

  await dialog.getByRole("button", { name: en.common.save, exact: true }).click()
  await expect(dialog).toBeHidden()
  await expect.poll(() => captured.uploads.length).toBe(1)
  // In flight: the avatar keeps focus and says it is unavailable.
  await expect(avatar).toBeFocused()
  await expect(avatar).toHaveAttribute("aria-disabled", "true")
  // ...and its menu does not open meanwhile.
  await avatar.press("Enter")
  await expect(page.getByRole("menuitem", { name: en.common.change, exact: true })).toHaveCount(0)

  release()
  await expect(avatar).not.toHaveAttribute("aria-disabled", "true")
  await expect(avatar).toBeFocused()
})

test("inside the platform logo dialog, closing the editor keeps focus in that dialog", async ({
  page,
}) => {
  await install(page)
  await page.goto("/admin/platform-settings")
  await page.getByRole("button", { name: en.common.avatar }).first().click()
  await page.getByRole("menuitem", { name: en.common.change, exact: true }).click()
  const logos = page.getByRole("dialog", { name: en.platformSettings.logoDialogTitle })
  await expect(logos).toBeVisible()

  const light = logos.locator('[data-mode="light"]').getByRole("button", { name: en.common.avatar })
  const dialog = await chooseThroughMenu(page, light)
  await dialog.getByRole("button", { name: en.common.cancel, exact: true }).click()
  await expect(dialog).toBeHidden()

  await expect(logos).toBeVisible()
  await expect(light).toBeFocused()
  expect(await logos.evaluate((el) => el.contains(document.activeElement))).toBe(true)
})

test("the edge grips are at least 24 px on their thin side at a 320 px viewport", async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 720 })
  await install(page)
  await page.goto("/admin/platform-settings")
  await page.getByRole("button", { name: en.common.avatar }).first().click()
  await page.getByRole("menuitem", { name: en.common.change, exact: true }).click()
  const logos = page.getByRole("dialog", { name: en.platformSettings.logoDialogTitle })
  await logos.locator('[data-mode="light"] input[type="file"]').setInputFiles(PICKED)
  const dialog = page.getByRole("dialog", { name: en.imageFrame.title })
  const grips = dialog.locator("[data-edge]")
  await expect(grips).toHaveCount(4)

  for (const grip of await grips.all()) {
    const box = (await grip.boundingBox())!
    expect(Math.min(box.width, box.height), await grip.getAttribute("data-edge") ?? "").toBeGreaterThanOrEqual(24)
  }
})

for (const language of ["en", "ar"] as const) {
  for (const theme of ["light", "dark"] as const) {
    test(`axe finds nothing in the logo editor (${language}, ${theme})`, async ({ page }) => {
      const t = catalogs[language]
      await install(page, { language, theme })
      await page.goto("/admin/platform-settings")
      await page.getByRole("button", { name: t.common.avatar }).first().click()
      await page.getByRole("menuitem", { name: t.common.change, exact: true }).click()
      const logos = page.getByRole("dialog", { name: t.platformSettings.logoDialogTitle })
      await logos.locator('[data-mode="light"] input[type="file"]').setInputFiles(PICKED)
      const dialog = page.getByRole("dialog", { name: t.imageFrame.title })
      // Logo mode: the four grips are on screen to be judged.
      await expect(dialog.locator("[data-edge]")).toHaveCount(4)

      const results = await new AxeBuilder({ page })
        .withTags(WCAG_TAGS)
        .include('[data-slot="dialog-content"]:has([data-slot="image-frame-stage"])')
        .analyze()
      // Floor: the editor was actually scanned.
      expect(results.passes.length).toBeGreaterThan(0)
      expect(actionable(results), describeViolations(results)).toEqual([])
    })
  }
}
