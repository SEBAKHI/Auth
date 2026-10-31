import { expect, test, type FrameLocator, type Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { en } from "../../packages/i18n/src/locales/en"
import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Sign-in page designs in the console (phase 6): an application › Templates, and the
 * platform settings card. A new design opens in the editor; the preview is the server's
 * rendering in the same isolated frame the sign-in pages use, following the chosen mode;
 * Save sends the editor's fields with the revision it loaded; Activate is confirmed.
 * Arabic and English, 1440px and 375px, each with a screenshot.
 */

const catalogs = { en, ar } as const
const APP_ID = "5d1b7f2e-8c4a-4e6b-9f3d-2a7c1e9b4d60"
const REVISION = "2026-10-29T10:00:00.1234567Z"

const design = (overrides: Record<string, unknown> = {}) => ({
  id: "d1",
  applicationId: APP_ID,
  name: "Autumn",
  content: '<div id="design" style="padding:2rem">{{ strings.headline | raw }}</div>',
  stringsJson: JSON.stringify({ en: { headline: "Hello {{ App.Name }}" }, ar: { headline: "أهلًا {{ App.Name }}" } }),
  showPlatformLogo: true,
  isActive: false,
  contentHash: "a".repeat(64),
  revision: REVISION,
  createdAt: REVISION,
  modifiedAt: null,
  warnings: [],
  ...overrides,
})

interface Seen {
  method: string
  path: string
  body: unknown
}

async function install(page: Page, language: keyof typeof catalogs, seen: Seen[], scope: "application" | "platform") {
  const base = scope === "application" ? `/api/v1/applications/${APP_ID}/login-designs` : "/api/v1/admin/platform-settings/login-designs"
  let designs = [design({ applicationId: scope === "application" ? APP_ID : null })]
  await installAuthenticatedApi(
    page,
    ["applications:read", "applications:update", "platform-settings:manage"],
    async (route, url) => {
      const request = route.request()
      const path = url.pathname.toLowerCase()
      const body = request.postData() ? request.postDataJSON() : undefined
      if (path.startsWith(base.toLowerCase())) seen.push({ method: request.method(), path, body })

      if (path === `${base.toLowerCase()}/preview`) {
        const { content, language: lang } = body as { content: string; language: string }
        await fulfillJson(route, {
          html: content.replace("{{ strings.headline | raw }}", lang === "ar" ? "أهلًا Example CRM" : "Hello Example CRM"),
          language: lang,
          dir: lang === "ar" ? "rtl" : "ltr",
          warnings: content.includes("<script") ? ["Script"] : [],
        })
        return true
      }
      if (path === base.toLowerCase() && request.method() === "GET") {
        await fulfillJson(route, designs)
        return true
      }
      if (path === base.toLowerCase() && request.method() === "POST") {
        const created = design({ id: "d2", name: (body as { name: string }).name })
        designs = [...designs, created]
        await fulfillJson(route, created, 201)
        return true
      }
      if (path.endsWith("/activate")) {
        designs = designs.map((d) => ({ ...d, isActive: path.includes(`/${d.id}/`) }))
        await fulfillJson(route, designs.find((d) => d.isActive))
        return true
      }
      const match = /\/login-designs\/(d\d)$/.exec(path)
      if (match) {
        const current = designs.find((d) => d.id === match[1])!
        if (request.method() === "PUT") {
          const next = { ...current, ...(body as object), modifiedAt: "2026-10-29T11:00:00Z", revision: "2026-10-29T11:00:00Z" }
          designs = designs.map((d) => (d.id === current.id ? next : d))
          await fulfillJson(route, next)
          return true
        }
        await fulfillJson(route, current)
        return true
      }
      if (path === "/api/v1/admin/platform-settings") {
        await fulfillJson(route, { platformName: "AuthSystem", logoUrl: null, logoUrlDark: null, faviconUrl: null, theme: null })
        return true
      }
      if (path === `/api/v1/applications/${APP_ID}`) {
        await fulfillJson(route, {
          id: APP_ID,
          code: "crm",
          name: "Example CRM",
          isActive: true,
          accessMode: "Restricted",
          themeEnabled: false,
          theme: null,
          redirectUris: [],
          postLogoutRedirectUris: [],
          confidentialRedirectUris: [],
          allowedScopes: [],
          createdAt: "2026-08-01T09:00:00Z",
        })
        return true
      }
      if (path.endsWith("/roles") || path.endsWith("/permissions")) {
        await fulfillJson(route, [])
        return true
      }
      await fulfillJson(route, { items: [], users: [], organizations: [], totalCount: 0, totalPages: 0 })
      return true
    },
    { preferredLanguage: language }
  )
}

/** The preview's frame once it has rendered; re-resolved on every use, as a new design is a new frame. */
async function previewFrame(page: Page): Promise<FrameLocator> {
  const host = page.locator('[data-design-state="rendered"]')
  await expect(host).toBeVisible()
  return host.frameLocator("iframe")
}

const frameEval = <T,>(page: Page, fn: () => T) =>
  page
    .locator('[data-design-state="rendered"] iframe')
    .elementHandle()
    .then((handle) => handle!.contentFrame())
    .then((frame) => frame!.evaluate(fn))

for (const language of ["en", "ar"] as const) {
  test(`an application's Templates: create, edit, preview, save, activate (${language})`, async ({ page }, testInfo) => {
    const t = catalogs[language]
    const seen: Seen[] = []
    await page.setViewportSize({ width: 1440, height: 1200 })
    await install(page, language, seen, "application")
    await page.goto(`/applications/${APP_ID}?tab=templates`)

    await expect(page.getByRole("tab", { name: t.loginDesigns.tab })).toHaveAttribute("aria-selected", "true")
    await expect(page.getByText(t.loginDesigns.visitorsSeePlatform)).toBeVisible()

    // A new design opens in the editor, inactive.
    await page.getByRole("button", { name: t.loginDesigns.newDesign }).click()
    const dialog = page.getByRole("dialog")
    await dialog.getByRole("textbox", { name: t.loginDesigns.name }).fill("Winter")
    await dialog.getByRole("button", { name: t.loginDesigns.create }).click()
    expect(seen.find((entry) => entry.method === "POST")?.body).toEqual({
      name: "Winter",
      content: null,
      stringsJson: null,
      showPlatformLogo: true,
    })
    await expect(page.getByRole("button", { name: t.loginDesigns.back })).toBeVisible()

    // The preview is the server's rendering in the isolated frame, in the chosen mode.
    const frame = await previewFrame(page)
    await expect(frame.locator("#design")).toHaveText("Hello Example CRM")
    await page.getByRole("radio", { name: t.notifications.previewDark }).click()
    await expect.poll(() => frameEval(page, () => getComputedStyle(document.documentElement).colorScheme)).toBe("dark")

    // Arabic texts drive the preview's language.
    await page.getByRole("tab", { name: "ar", exact: true }).click()
    await expect((await previewFrame(page)).locator("#design")).toHaveText("أهلًا Example CRM")
    await expect.poll(() => frameEval(page, () => document.documentElement.dir)).toBe("rtl")

    // Save sends the fields with the revision the editor loaded.
    await page.getByRole("textbox", { name: t.loginDesigns.name }).fill("Winter campaign")
    await page.getByRole("switch", { name: t.loginDesigns.showPlatformLogo }).click()
    await page.getByRole("button", { name: t.loginDesigns.save }).click()
    await expect(page.getByText(t.loginDesigns.saved)).toBeVisible()
    const put = seen.find((entry) => entry.method === "PUT")
    expect(put?.body).toMatchObject({ name: "Winter campaign", showPlatformLogo: false, expectedRevision: REVISION })

    await expectNoShellOverflow(page)
    await page.screenshot({ path: testInfo.outputPath(`templates-editor-${language}.png`), fullPage: true })

    // Back to the list, then Activate with its confirmation.
    await page.getByRole("button", { name: t.loginDesigns.back }).click()
    await page.getByRole("button", { name: t.loginDesigns.actions.replace("{{name}}", "Winter campaign") }).click()
    await page.getByRole("menuitem", { name: t.loginDesigns.activate }).click()
    const confirm = page.getByRole("alertdialog")
    await expect(confirm).toContainText(t.loginDesigns.activateDescriptionApp)
    await confirm.getByRole("button", { name: t.loginDesigns.activate }).click()
    await expect(page.getByText(t.loginDesigns.visitorsSee.replace("{{name}}", "Winter campaign"))).toBeVisible()
    expect(seen.some((entry) => entry.method === "POST" && entry.path.endsWith("/d2/activate"))).toBe(true)
    await page.screenshot({ path: testInfo.outputPath(`templates-list-${language}.png`), fullPage: true })
  })

  test(`the Templates tab at 375px (${language})`, async ({ page }, testInfo) => {
    const t = catalogs[language]
    await page.setViewportSize({ width: 375, height: 812 })
    await install(page, language, [], "application")
    await page.goto(`/applications/${APP_ID}?tab=templates`)
    await expect(page.getByText(t.loginDesigns.visitorsSeePlatform)).toBeVisible()
    await page.getByRole("tabpanel").getByRole("button", { name: t.loginDesigns.edit }).click()
    await previewFrame(page)
    await expectNoShellOverflow(page)
    await page.screenshot({ path: testInfo.outputPath(`templates-375-${language}.png`), fullPage: true })
  })

  // Every source editor (this one, an email template's body, an email layout) is one
  // component with one fixed window: 28 lines of its own line height, whatever it holds.
  test(`the source editor shows 28 lines, short or long, and scrolls inside (${language})`, async ({ page }, testInfo) => {
    const t = catalogs[language]
    await page.setViewportSize({ width: 1440, height: 1200 })
    await install(page, language, [], "application")
    await page.goto(`/applications/${APP_ID}?tab=templates`)
    await page.getByRole("tabpanel").getByRole("button", { name: t.loginDesigns.edit }).click()
    await previewFrame(page)

    const scroller = page.locator(".cm-scroller")
    const measure = () =>
      scroller.evaluate((el) => ({
        height: el.clientHeight,
        lineHeight: parseFloat(getComputedStyle(el).lineHeight),
        overflows: el.scrollHeight > el.clientHeight,
      }))

    const short = await measure()
    // The 8px is CodeMirror's own padding around the content.
    expect(Math.round((short.height - 8) / short.lineHeight)).toBe(28)
    expect(short.overflows).toBe(false)

    await page.locator(".cm-content").click()
    await page.keyboard.press("ControlOrMeta+End")
    for (let line = 0; line < 60; line++) await page.keyboard.press("Enter")

    const long = await measure()
    expect(long.height).toBe(short.height)
    expect(long.overflows).toBe(true)
    await page.screenshot({ path: testInfo.outputPath(`editor-28-lines-${language}.png`), fullPage: true })
  })
}

test("the platform settings card manages the platform's designs, with the platform's fallback named", async ({ page }, testInfo) => {
  const seen: Seen[] = []
  await page.setViewportSize({ width: 1440, height: 1200 })
  await install(page, "en", seen, "platform")
  await page.goto("/admin/platform-settings")

  const card = page.locator('[data-slot="card"]').filter({ hasText: en.loginDesigns.cardTitle })
  await expect(card).toBeVisible()
  await expect(card.getByText(en.loginDesigns.visitorsSeePlain)).toBeVisible()
  await card.getByRole("button", { name: en.loginDesigns.edit }).click()
  const frame = await previewFrame(page)
  await expect(frame.locator("#design")).toHaveText("Hello Example CRM")
  expect(seen.some((entry) => entry.path.endsWith("/admin/platform-settings/login-designs/preview"))).toBe(true)
  await page.screenshot({ path: testInfo.outputPath("platform-designs-card.png"), fullPage: true })
})
