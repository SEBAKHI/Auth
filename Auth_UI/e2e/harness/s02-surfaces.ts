import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { fileURLToPath } from "node:url"
import { crc32, deflateSync } from "node:zlib"

import { expect, type Page } from "@playwright/test"

import { fulfillJson } from "../isolated/mock-authenticated-api"
import type { HarnessApi } from "./fixtures"
import { ORIGINS } from "./topology"

/**
 * The pages S02 (inline styles out of style-src) has to prove, shared by its
 * two specs: s02-visual-parity.spec.ts (pixels before and after) and
 * s02-csp-styles.spec.ts (computed styles and zero violations).
 *
 * One console template page carries most of them: the CodeMirror editor
 * (style-mod), the email preview frame, the Test send dialog with its language
 * Select (Radix Select plus the dialog's scroll lock, react-style-singleton),
 * and a toast after Publish (sonner). The dashboard's people tab carries the
 * charts (ChartStyle), accounts' two-factor page the code field (input-otp),
 * and /privacy the renderer's golden document.
 */

export const TEMPLATE_ID = "51515151-5151-5151-5151-515151515151"
const DRAFT_VERSION_ID = "52525252-5252-5252-5252-525252525252"
const PUBLISHED_VERSION_ID = "53535353-5353-5353-5353-535353535353"
const REVISION = "2026-10-01T09:00:00Z"

/** Pinned so dates the pages print, and the chart's day buckets, never move. */
export const NOW = new Date("2026-10-12T12:00:00Z")

/** The logo the email layout shows, served by the API host like an upload. */
export const LOGO_PATH = "/uploads/images/s02-logo.png"
const LOGO = solidPng(120, 32, [37, 99, 235])

/** A valid one-colour PNG, built here so no binary file sits in the repository. */
function solidPng(width: number, height: number, [r, g, b]: [number, number, number]) {
  const chunk = (type: string, data: Buffer) => {
    const body = Buffer.concat([Buffer.from(type, "ascii"), data])
    const length = Buffer.alloc(4)
    length.writeUInt32BE(data.length)
    const crc = Buffer.alloc(4)
    crc.writeUInt32BE(crc32(body))
    return Buffer.concat([length, body, crc])
  }
  const header = Buffer.alloc(13)
  header.writeUInt32BE(width, 0)
  header.writeUInt32BE(height, 4)
  header.set([8, 2, 0, 0, 0], 8)
  const row = Buffer.concat([Buffer.from([0]), Buffer.from(Array.from({ length: width }, () => [r, g, b]).flat())])
  const pixels = deflateSync(Buffer.concat(Array.from({ length: height }, () => row)))
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", pixels),
    chunk("IEND", Buffer.alloc(0)),
  ])
}

/**
 * A rendered email in the shape the seeded layout has (11_NotificationLayouts.sql):
 * a doctype, a <style> block whose dark palette sits behind
 * prefers-color-scheme, inline style attributes, and the platform logo from
 * the API origin. Nothing here is a script; the negative checks add their own.
 */
export const EMAIL_HTML = `<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
<meta charset="UTF-8">
<meta name="color-scheme" content="light dark">
<title>Harness</title>
<style>
:root { color-scheme: light dark; }
html, body { margin: 0; padding: 0; background-color: #f1f1ef; font-family: Arial, sans-serif; }
.card { background-color: #ffffff; color: #18181b; margin: 24px; padding: 24px; border-radius: 8px; }
@media (prefers-color-scheme: dark) {
  html, body { background-color: #111113; }
  .card { background-color: #1c1c1f; color: #f4f4f5; }
}
</style>
</head>
<body>
<div class="card">
<img src="${ORIGINS.api}${LOGO_PATH}" alt="Logo" width="120" height="32" style="display:block;margin:0 0 16px 0">
<p style="margin:0 0 12px 0;font-size:18px;font-weight:bold">Welcome to the harness</p>
<p style="margin:0;font-size:14px;line-height:1.5">Hello Isolated, this is the rendered body.</p>
</div>
</body>
</html>`

/** A non-default platform appearance (Blue theme), as the API returns it. */
export const BLUE_THEME = {
  base: { preset: "neutral" },
  theme: { preset: "blue" },
  chart: { preset: "cyan" },
  radius: "default",
  menuAccent: "subtle",
}
/** --primary under BLUE_THEME in light mode (build-theme, engine 3; branding.provider.test.tsx). */
export const BLUE_PRIMARY_LIGHT = "oklch(0.488 0.243 264.376)"

const PERMISSIONS = [
  "notification-templates:read",
  "notification-templates:manage",
  "notification-templates:publish",
  "users:read",
]

const translation = {
  languageCode: "en",
  subject: "Welcome",
  bodyHtml: "<p>Hello {{ User.FirstName }}, this is the rendered body.</p>",
  bodyText: null,
  modifiedAt: REVISION,
}

const version = (id: string, versionNumber: number) => ({
  id,
  versionNumber,
  changeNote: null,
  createdAt: REVISION,
  createdBy: "99999999-9999-9999-9999-999999999999",
  translations: [translation],
})

const template = {
  id: TEMPLATE_ID,
  notificationTypeId: "54545454-5454-5454-5454-545454545454",
  typeCode: "welcome",
  typeName: "Welcome",
  typeIsSystem: false,
  typeVariablesJson: "[]",
  typeSampleDataJson: "{}",
  applicationId: null,
  applicationName: null,
  channel: "Email",
  defaultLanguage: "en",
  draftVersionId: DRAFT_VERSION_ID,
  publishedVersionId: PUBLISHED_VERSION_ID,
  draftVersion: version(DRAFT_VERSION_ID, 2),
  publishedVersion: version(PUBLISHED_VERSION_ID, 1),
  versions: [],
  createdAt: "2026-09-20T09:00:00Z",
  modifiedAt: REVISION,
}

/** Seven days of signups ending on NOW, so the bars chart has something to draw. */
function signupsPerDay() {
  return [3, 5, 2, 8, 4, 6, 7].map((count, index) => ({
    date: new Date(NOW.getTime() - (6 - index) * 86_400_000).toISOString().slice(0, 10) + "T00:00:00Z",
    count,
  }))
}

export interface SurfaceOptions {
  /** What the email preview endpoint returns; EMAIL_HTML by default. */
  emailHtml?: string
  branding?: Record<string, unknown>
}

/** The signed-in API every S02 console surface needs. */
export async function serveConsoleSurfaces(api: HarnessApi, options: SurfaceOptions = {}) {
  let current = template
  await api.useAuthenticated(
    PERMISSIONS,
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      const method = route.request().method()
      if (path === LOGO_PATH) {
        await route.fulfill({ status: 200, contentType: "image/png", body: LOGO })
        return true
      }
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}` && method === "GET") {
        await fulfillJson(route, current)
        return true
      }
      if (path === "/api/v1/notification-templates/preview") {
        await fulfillJson(route, {
          subject: "Welcome",
          html: options.emailHtml ?? EMAIL_HTML,
          text: "Hello Isolated, this is the rendered body.",
          languageCode: "en",
          direction: "ltr",
        })
        return true
      }
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}/publish`) {
        current = {
          ...template,
          draftVersionId: null as unknown as string,
          draftVersion: null as unknown as typeof template.draftVersion,
          publishedVersionId: DRAFT_VERSION_ID,
          publishedVersion: template.draftVersion,
        }
        await fulfillJson(route, current)
        return true
      }
      if (path === "/api/v1/dashboard/user-stats") {
        await fulfillJson(route, {
          days: 7,
          totalUsers: 120,
          byStatus: [
            { status: 1, count: 90 },
            { status: 0, count: 20 },
            { status: 2, count: 10 },
          ],
          activeUsers: 90,
          mfaEnabled: 30,
          newInWindow: 35,
          signupsPerDay: signupsPerDay(),
          cohortCreated: 35,
          cohortEmailConfirmed: 28,
          cohortLoggedIn: 20,
          dormantOver30Days: 5,
          usersByOrganization: [
            { organizationName: "North", count: 40 },
            { organizationName: "South", count: 25 },
          ],
          totalActiveMemberships: 70,
        })
        return true
      }
      return false
    },
    { branding: options.branding }
  )
}

/** Pins the page clock (timers keep running), so printed dates and buckets never move. */
export async function pinClock(page: Page) {
  await page.clock.setFixedTime(NOW)
}

export async function openTemplate(page: Page) {
  await pinClock(page)
  await page.goto(`${ORIGINS.console}/notifications/templates/${TEMPLATE_ID}`)
  // The editor is a lazy chunk of its own (CodeMirror, ~360 kB): under a full
  // parallel run its first paint can take longer than the default 5 s.
  await expect(page.locator(".cm-content")).toContainText("Hello", { timeout: 15_000 })
  // The preview renders after a debounce; a page is settled once the email,
  // logo included, is in the frame.
  await expect(page.frameLocator('iframe[title="Preview"]').locator(".card")).toBeVisible()
  await expect.poll(() => logoLoaded(page)).toBe(true)
}

function logoLoaded(page: Page) {
  return page
    .frameLocator('iframe[title="Preview"]')
    .locator("img")
    .evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth > 0)
}

/**
 * The preview's scheme is `color-scheme` on the frame element, which Chromium
 * turns into the frame's prefers-color-scheme - but only while no scheme is
 * emulated: an emulated one (Playwright's default is "light") overrides every
 * frame, and both previews would render light. So the emulation is switched
 * off, and the console's own theme is pinned to light through its storage key.
 */
export async function withoutSchemeEmulation(page: Page) {
  await page.emulateMedia({ colorScheme: null })
  await page.addInitScript(() => localStorage.setItem("theme", "light"))
}

/** The preview frame, once the email inside it has rendered. */
export async function previewFrame(page: Page, scheme: "light" | "dark") {
  await page.getByRole("radio", { name: scheme === "light" ? "Light mode" : "Dark mode" }).click()
  const frame = page.locator('iframe[title="Preview"]')
  await expect(frame).toHaveCSS("color-scheme", scheme)
  const card = page.frameLocator('iframe[title="Preview"]').locator(".card")
  await expect(card).toBeVisible()
  // The scheme reached the email itself, not just the frame element.
  await expect
    .poll(() => card.evaluate(() => matchMedia("(prefers-color-scheme: dark)").matches))
    .toBe(scheme === "dark")
  await expect.poll(() => logoLoaded(page)).toBe(true)
  return frame
}

export async function openDashboardPeople(page: Page) {
  await pinClock(page)
  await page.goto(`${ORIGINS.console}/?tab=people&days=7`)
  await expect(page.locator('[data-slot="chart"]').first()).toBeVisible()
}

/** Accounts' second-factor page, reached the way a user reaches it: a password, then a challenge. */
export async function openAccountsTwoFactor(page: Page, api: HarnessApi, verifies: unknown[] = []) {
  await api.useAnonymous(async (route, url, body) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/auth/login") {
      await fulfillJson(route, { twoFactorChallengeToken: "harness-challenge", requiresTwoFactor: true })
      return true
    }
    if (path === "/api/v1/auth/2fa/verify") {
      verifies.push(body)
      await route.fulfill({
        status: 400,
        contentType: "application/problem+json",
        body: JSON.stringify({ status: 400, code: "TwoFactor.InvalidCode", detail: "Invalid code." }),
      })
      return true
    }
    return false
  })
  await page.goto(`${ORIGINS.accounts}/login`)
  await page.locator('input[type="email"]').fill("styles@example.test")
  await page.locator('input[type="password"]').fill("Harness1!")
  await page.locator('button[type="submit"]').click()
  await expect(page).toHaveURL(/\/two-factor$/)
  await expect(page.locator('[data-slot="input-otp"]')).toBeAttached()
}

const AUTH_UI = fileURLToPath(new URL("../..", import.meta.url))
/** The renderer's own output for a fixed request (PolicyDocumentRendererTests pins it). */
export const PRIVACY_GOLDEN = join(AUTH_UI, "apps", "accounts", "csp", "privacy-golden.html")

/**
 * A /privacy folder the way publishing writes it (FileSystemPolicyPublicationStore):
 * the current document as index.html and en.html, and an archived copy under
 * v2026.09/. `lineEnding: "crlf"` writes the same document with CRLF, which the
 * HTML parser folds back to LF before CSP hashes the stylesheet.
 */
export function privacyFolder(lineEnding: "lf" | "crlf" = "lf") {
  const golden = readFileSync(PRIVACY_GOLDEN, "utf8").replace(/\r\n/g, "\n")
  const html = lineEnding === "crlf" ? golden.replace(/\n/g, "\r\n") : golden
  const dir = mkdtempSync(join(tmpdir(), "s02-privacy-"))
  mkdirSync(join(dir, "v2026.09"))
  for (const file of ["index.html", "en.html", "v2026.09/index.html", "v2026.09/en.html"]) {
    writeFileSync(join(dir, file), html)
  }
  return { dir, dispose: () => rmSync(dir, { recursive: true, force: true }) }
}
