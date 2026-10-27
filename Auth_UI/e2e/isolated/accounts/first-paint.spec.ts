import { expect, test, type Page } from "@playwright/test"

import {
  cspViolations,
  enforceSealedCsp,
  framesBeforeContent,
  holdAppScripts,
  sampleFrames,
} from "../first-paint"
import { installAnonymousApi } from "./mock-anonymous-api"

/**
 * A dark user's first frames on the accounts pages. The app's entry is a module
 * script, which runs after the browser paints the empty page; before the boot
 * script in <head>, every one of those frames was white in a dark browser
 * (measured 2026-10-27: ~92 frames with the scripts held 1.5 s, 1–2 on
 * localhost). Each case holds the app's scripts back and samples every frame
 * until the first content.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const SIGN_IN = `/login?client_id=app&auth_request=${HANDLE}`
const HOLD_MS = 800
/** A floor, so a run that sampled nothing cannot pass. */
const MIN_FRAMES = 10
const DARK = "oklch(0.145 0 0)"
const LIGHT = "oklch(1 0 0)"

async function arrive(page: Page, path: string, stored: string | null) {
  await page.emulateMedia({ colorScheme: "dark" })
  if (stored) {
    await page.addInitScript((theme) => localStorage.setItem("theme", theme), stored)
  }
  await sampleFrames(page)
  await holdAppScripts(page, HOLD_MS)
  await installAnonymousApi(page, async () => false)
  await page.goto(path)
  return framesBeforeContent(page)
}

for (const [name, path, stored] of [
  ["an application's ui_theme=dark", `${SIGN_IN}&ui_theme=dark`, null],
  ["a stored dark choice", SIGN_IN, "dark"],
  ["system on a dark device", SIGN_IN, null],
] as const) {
  test(`${name}: no frame before the content is painted light`, async ({ page }) => {
    const frames = await arrive(page, path, stored)

    expect(frames.length).toBeGreaterThan(MIN_FRAMES)
    expect(frames.filter((frame) => !frame.dark || frame.background !== DARK)).toEqual([])
  })
}

test("a mode the user picked holds on the flow's next page, though its address names the application's", async ({
  page,
}) => {
  // The application sends light; on the sign-in page the user picks dark.
  await arrive(page, `${SIGN_IN}&ui_theme=light`, null)
  await page.getByRole("button", { name: "Theme" }).click()
  await page.getByRole("menuitem", { name: "Dark" }).click()
  await expect(page.locator("html")).toHaveClass(/\bdark\b/)

  // The flow's next accounts page: the server names light again.
  await page.goto(`/create-organization?client_id=app&auth_request=${HANDLE}&ui_theme=light`)
  const frames = await framesBeforeContent(page)

  expect(frames.length).toBeGreaterThan(MIN_FRAMES)
  expect(frames.filter((frame) => !frame.dark || frame.background !== DARK)).toEqual([])
})

test("a stored light choice on a dark device: no frame before the content is painted dark", async ({ page }) => {
  const frames = await arrive(page, SIGN_IN, "light")

  expect(frames.length).toBeGreaterThan(MIN_FRAMES)
  expect(frames.filter((frame) => frame.dark || frame.background !== LIGHT)).toEqual([])
})

test("the boot script runs under the sealed production CSP, with no violation", async ({ page }) => {
  const policy = await enforceSealedCsp(page, "accounts")
  expect(policy).toContain("script-src 'self'")

  const frames = await arrive(page, `${SIGN_IN}&ui_theme=dark`, null)

  expect(frames.length).toBeGreaterThan(MIN_FRAMES)
  expect(frames.every((frame) => frame.dark)).toBe(true)
  // One violation predates the boot script and is not about loading a script:
  // zod probes for eval support with `Function("")` inside a try, from the entry
  // chunk, and falls back when the CSP refuses it. Everything else must be clean.
  const violations = await cspViolations(page)
  const zodEvalProbe = /^script-src eval \S*\/assets\/index-[^/]+\.js:\d+$/
  expect(violations.filter((violation) => !zodEvalProbe.test(violation))).toEqual([])
  expect(violations.some((violation) => violation.includes("theme-boot"))).toBe(false)
})
