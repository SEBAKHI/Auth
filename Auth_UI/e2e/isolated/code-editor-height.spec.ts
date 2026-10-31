import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Every source editor shows 28 lines, whatever it holds, and scrolls inside
 * (owner, 2026-10-31). Measured here on an email template's body and on an
 * email layout, the two editors that sit in no font-size context of their own
 * (the sign-in design editor is measured in login-designs.spec.ts). Counted
 * in the code's own line height, so a box sized in the page's font, which
 * held about 34 code lines here, fails.
 */

const TEMPLATE_ID = "11111111-1111-1111-1111-111111111111"
const LAYOUT_ID = "44444444-4444-4444-4444-444444444444"
const REVISION = "2026-08-21T07:00:00Z"

const version = (id: string, bodyHtml: string) => ({
  id,
  versionNumber: 1,
  changeNote: null,
  createdAt: REVISION,
  createdBy: "99999999-9999-9999-9999-999999999999",
  translations: [{ languageCode: "en", subject: "Reset", bodyHtml }],
})

const template = (bodyHtml: string) => ({
  id: TEMPLATE_ID,
  notificationTypeId: "55555555-5555-5555-5555-555555555555",
  typeCode: "password-reset",
  typeName: "Password reset",
  typeIsSystem: false,
  typeVariablesJson: "[]",
  typeSampleDataJson: "{}",
  applicationId: null,
  applicationName: null,
  channel: "Email",
  defaultLanguage: "en",
  draftVersionId: "22222222-2222-2222-2222-222222222222",
  publishedVersionId: null,
  draftVersion: version("22222222-2222-2222-2222-222222222222", bodyHtml),
  publishedVersion: null,
  versions: [],
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: REVISION,
})

const layout = (draftContent: string) => ({
  id: LAYOUT_ID,
  applicationId: null,
  applicationName: null,
  channel: "Email",
  name: "Default email layout",
  draftContent,
  draftStringsJson: "{}",
  isPublished: false,
  hasUnpublishedChanges: true,
  publishedAt: null,
  createdAt: "2026-08-20T07:00:00Z",
  modifiedAt: REVISION,
})

const SHORT = "<p>{{ content | raw }}</p>"
const LONG = Array.from({ length: 120 }, (_, i) => `<p>line ${i}</p>`).join("\n")

async function install(page: Page, source: string) {
  await installAuthenticatedApi(
    page,
    ["notification-templates:read", "notification-templates:manage", "notification-layouts:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/notification-templates/${TEMPLATE_ID}`) {
        await fulfillJson(route, template(source))
        return true
      }
      if (path === `/api/v1/notification-layouts/${LAYOUT_ID}`) {
        await fulfillJson(route, layout(source))
        return true
      }
      return false
    }
  )
}

const measure = (page: Page) =>
  page.locator(".cm-scroller").evaluate((el) => ({
    height: el.clientHeight,
    lineHeight: parseFloat(getComputedStyle(el).lineHeight),
    overflows: el.scrollHeight > el.clientHeight,
  }))

for (const surface of [
  { name: "an email template's body", url: `/notifications/templates/${TEMPLATE_ID}` },
  { name: "an email layout", url: `/notifications/layouts/${LAYOUT_ID}` },
]) {
  test(`${surface.name}: 28 lines, short or long, scrolling inside`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1100 })

    await install(page, SHORT)
    await page.goto(surface.url)
    await expect(page.locator(".cm-scroller")).toBeVisible()
    const short = await measure(page)
    // The 8px is CodeMirror's own padding around the content.
    expect(Math.round((short.height - 8) / short.lineHeight)).toBe(28)
    expect(short.overflows).toBe(false)

    await page.unrouteAll({ behavior: "ignoreErrors" })
    await install(page, LONG)
    await page.goto(surface.url)
    await expect(page.locator(".cm-scroller")).toBeVisible()
    const long = await measure(page)
    expect(long.height).toBe(short.height)
    expect(long.overflows).toBe(true)
    await page.screenshot({
      path: testInfo.outputPath(`editor-28-lines-${surface.url.split("/")[2]}.png`),
      fullPage: true,
    })
  })
}
