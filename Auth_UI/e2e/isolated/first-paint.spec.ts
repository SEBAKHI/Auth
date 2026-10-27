import { expect, test, type Page } from "@playwright/test"

import {
  cspViolations,
  enforceSealedCsp,
  framesBeforeContent,
  holdAppScripts,
  sampleFrames,
} from "./first-paint"
import { fulfillJson } from "./mock-authenticated-api"

/**
 * A dark user's first frames on the console. The same blank page as the
 * accounts app (e2e/isolated/accounts/first-paint.spec.ts) was painted white
 * until React ran; the boot script in <head> now stamps the mode first.
 */

const HOLD_MS = 800
const MIN_FRAMES = 10
const DARK = "oklch(0.145 0 0)"

async function arrive(page: Page, stored: string | null) {
  await page.emulateMedia({ colorScheme: "dark" })
  if (stored) {
    await page.addInitScript((theme) => localStorage.setItem("theme", theme), stored)
  }
  await sampleFrames(page)
  await holdAppScripts(page, HOLD_MS)
  // Signed out: whatever the console asks the API is refused, and it shows sign-in.
  await page.route("**/api/v1/**", (route) => fulfillJson(route, { code: "Http.Unauthorized" }, 401))
  await page.goto("/")
  return framesBeforeContent(page)
}

for (const [name, stored] of [
  ["a stored dark choice", "dark"],
  ["system on a dark device", null],
] as const) {
  test(`console, ${name}: no frame before the content is painted light`, async ({ page }) => {
    const frames = await arrive(page, stored)

    expect(frames.length).toBeGreaterThan(MIN_FRAMES)
    expect(frames.filter((frame) => !frame.dark || frame.background !== DARK)).toEqual([])
  })
}

test("console: the boot script runs under the sealed production CSP, with no violation", async ({ page }) => {
  const policy = await enforceSealedCsp(page, "console")
  expect(policy).toContain("script-src 'self'")

  const frames = await arrive(page, "dark")

  expect(frames.length).toBeGreaterThan(MIN_FRAMES)
  expect(frames.every((frame) => frame.dark)).toBe(true)
  expect(await cspViolations(page)).toEqual([])
})
