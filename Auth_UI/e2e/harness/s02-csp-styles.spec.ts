import type { Page } from "@playwright/test"

import { HOSTS, ORIGINS, expect, test } from "./fixtures"
import type { CspDisposition, CspObserver } from "./csp-observer"
import {
  BLUE_PRIMARY_LIGHT,
  BLUE_THEME,
  openAccountsTwoFactor,
  openDashboardPeople,
  openTemplate,
  pinClock,
  previewFrame,
  privacyFolder,
  serveConsoleSurfaces,
  TEMPLATE_ID,
  withoutSchemeEmulation,
} from "./s02-surfaces"
import { clickPageAction } from "../isolated/page-actions"
import { headerValue, headersFor, type WebConfigModel } from "./web-config-model"

/**
 * Card S02 (inline styles out of style-src), checked in a browser under the
 * headers each built web.config sends (target 15). Every surface where a style
 * injector draws is opened, its styles are read back with getComputedStyle,
 * and the CSP observer fails the test on any violation nobody declared - from
 * the enforced policy AND from the Report-Only target policy of stage A. So
 * "zero report-only style violations" is not asserted here test by test: it is
 * what every test's verdict already requires.
 *
 * The negative checks read which stage the build is in from the web.config
 * itself: while the enforced style-src still carries 'unsafe-inline' an
 * injected style is reported (and applies); once it does not, it is refused.
 */

function stageOf(model: WebConfigModel) {
  const enforced = headerValue(model, "Content-Security-Policy") ?? ""
  const styleSrc = /(?:^|;)\s*style-src\s+([^;]*)/.exec(enforced)?.[1] ?? ""
  const enforcing = !styleSrc.split(/\s+/).includes("'unsafe-inline'")
  return {
    /** How the target policy's refusal of an inline style is reported. */
    styleDisposition: (enforcing ? "enforce" : "report") as CspDisposition,
    /** Whether an injected inline style still applies. */
    inlineApplies: !enforcing,
    /** Each disposition a violation of a directive both headers share is reported with. */
    shared: (headerValue(model, "Content-Security-Policy-Report-Only")
      ? ["enforce", "report"]
      : ["enforce"]) as CspDisposition[],
  }
}

/** No <style> in the document carries `marker`, and an adopted sheet does - the floor. */
async function adoptedNotInline(page: Page, marker: string) {
  return page.evaluate((needle) => {
    const inline = [...document.querySelectorAll("style")].some((style) => style.textContent?.includes(needle))
    const adopted = document.adoptedStyleSheets.some((sheet) =>
      [...sheet.cssRules].some((rule) => rule.cssText.includes(needle))
    )
    return { inline, adopted }
  }, marker)
}

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" })
})

test("a toast is styled (sonner's stylesheet, allowed by its hash)", async ({ page, api }) => {
  await serveConsoleSurfaces(api)
  await openTemplate(page)
  await page.getByRole("button", { name: "Publish", exact: true }).click()
  await page.getByRole("alertdialog").getByRole("button", { name: /Publish$/ }).click()
  await expect(page.locator("[data-sonner-toast]")).toContainText("Published")
  await expect(page.locator("[data-sonner-toaster]")).toHaveCSS("position", "fixed")
})

test("the code field completes and hides its input (input-otp: empty <style> by hash, rules by insertRule)", async ({
  page,
  api,
}) => {
  const verifies: unknown[] = []
  await openAccountsTwoFactor(page, api, verifies)
  await page.locator('[data-slot="input-otp"]').click()
  await page.keyboard.type("123456")
  await expect.poll(() => verifies.length).toBe(1)
  await expect(page.locator("[data-input-otp]")).toHaveCSS("color", "rgba(0, 0, 0, 0)")
  // The rules input-otp writes through the sheet of its empty <style> exist,
  // so the element was not refused (a refused <style> has no sheet).
  const rules = await page.evaluate(
    () => (document.getElementById("input-otp-style") as HTMLStyleElement | null)?.sheet?.cssRules.length ?? -1
  )
  expect(rules).toBeGreaterThan(0)
})

test("the editor is styled by an adopted sheet, not a <style> (style-mod, patched)", async ({ page, api }) => {
  await serveConsoleSurfaces(api)
  await openTemplate(page)
  await expect(page.locator(".cm-gutters")).toHaveCSS("display", "flex")
  expect(await adoptedNotInline(page, ".cm-")).toEqual({ inline: false, adopted: true })
})

test("the charts get their colours from an adopted sheet (ChartStyle)", async ({ page, api }) => {
  await serveConsoleSurfaces(api)
  await openDashboardPeople(page)
  await expect(page.locator(".recharts-bar-rectangle").first()).toBeVisible()
  // Every --color-* a chart's sheet defines resolves on that chart's element.
  const charts = await page.evaluate(() =>
    [...document.querySelectorAll<HTMLElement>("[data-chart]")].map((element) => {
      const id = element.dataset.chart!
      const names = document.adoptedStyleSheets
        .flatMap((sheet) => [...sheet.cssRules])
        .filter((rule): rule is CSSStyleRule => rule instanceof CSSStyleRule && rule.selectorText.includes(id))
        .flatMap((rule) => [...rule.style].filter((name) => name.startsWith("--color-")))
      const style = getComputedStyle(element)
      return { id, names, unresolved: names.filter((name) => !style.getPropertyValue(name).trim()) }
    })
  )
  expect(charts.filter((chart) => chart.names.length > 0).length).toBeGreaterThan(0)
  expect(charts.flatMap((chart) => chart.unresolved)).toEqual([])
  expect(await adoptedNotInline(page, "[data-chart=")).toEqual({ inline: false, adopted: true })
})

test("a dialog locks the page's scroll and its Select opens (react-style-singleton, patched; Radix Select by hash)", async ({
  page,
  api,
}) => {
  await serveConsoleSurfaces(api)
  await openTemplate(page)
  await clickPageAction(page, "Send test")
  const dialog = page.getByRole("alertdialog", { name: "Send test message" })
  await expect(dialog).toBeVisible()
  await expect(page.locator("body[data-scroll-locked]")).toHaveCSS("overflow", "hidden")
  expect(await adoptedNotInline(page, "data-scroll-locked")).toEqual({ inline: false, adopted: true })
  await dialog.locator("#test-send-language").click()
  await expect(page.getByRole("listbox")).toBeVisible()
  await expect(page.locator("[data-radix-select-viewport]")).toHaveCSS("scrollbar-width", "none")
})

test("a non-default platform appearance applies (apply-theme: one adopted sheet)", async ({ page, api }) => {
  await serveConsoleSurfaces(api, { branding: { platformName: "AuthSystem", theme: BLUE_THEME } })
  await openTemplate(page)
  await expect
    .poll(() => page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue("--primary").trim()))
    .toBe(BLUE_PRIMARY_LIGHT)
  expect(await page.locator("style#platform-theme").count()).toBe(0)
  expect(await adoptedNotInline(page, "--primary")).toEqual({ inline: false, adopted: true })
})

test.describe("the email preview", () => {
  test("is /email-preview.html with its own headers, in an opaque allow-scripts frame", async ({
    page,
    api,
    harness,
    request,
  }) => {
    await serveConsoleSurfaces(api)
    await openTemplate(page)
    const frame = page.locator('iframe[title="Preview"]')
    await expect(frame).toHaveAttribute("src", "/email-preview.html")
    await expect(frame).toHaveAttribute("sandbox", "allow-scripts")
    expect(await frame.getAttribute("srcdoc")).toBeNull()

    const served = await request.get(`${ORIGINS.console}/email-preview.html`)
    const expected = Object.fromEntries(
      headersFor(harness.models.console, "/email-preview.html").map(([name, value]) => [name.toLowerCase(), value])
    )
    const headers = served.headers()
    expect(headers["content-security-policy"]).toBe(expected["content-security-policy"])
    expect(headers["content-security-policy"]).toMatch(/^default-src 'none'; script-src 'sha256-/)
    expect(headers["content-security-policy-report-only"]).toBeUndefined()
    expect(headers["x-frame-options"]).toBe("SAMEORIGIN")

    // The document inside has an opaque origin and no access to the console.
    const inside = await page.frameLocator('iframe[title="Preview"]').locator("body").evaluate(() => {
      let storage: string
      try {
        localStorage.getItem("x")
        storage = "readable"
      } catch {
        storage = "refused"
      }
      return { origin: self.origin, storage, parent: (() => { try { return parent.document ? "reachable" : "none" } catch { return "refused" } })() }
    })
    expect(inside).toEqual({ origin: "null", storage: "refused", parent: "refused" })
  })

  for (const scheme of ["light", "dark"] as const) {
    test(`renders the email's own styles and logo, ${scheme}`, async ({ page, api }) => {
      await withoutSchemeEmulation(page)
      await serveConsoleSurfaces(api)
      await openTemplate(page)
      await previewFrame(page, scheme)
      const inside = await page.frameLocator('iframe[title="Preview"]').locator(".card").evaluate((card) => {
        const logo = document.querySelector("img")!
        return {
          sheets: document.styleSheets.length,
          background: getComputedStyle(card).backgroundColor,
          logo: logo.currentSrc,
          loaded: logo.complete && logo.naturalWidth > 0,
        }
      })
      expect(inside.sheets).toBeGreaterThan(0)
      expect(inside.background).toBe(scheme === "light" ? "rgb(255, 255, 255)" : "rgb(28, 28, 31)")
      expect(inside.logo.startsWith(`${ORIGINS.api}/`)).toBe(true)
      expect(inside.loaded).toBe(true)
    })
  }

  // D-S02-1 (b), the owner's decision of 2026-10-12: the old inline-document
  // frame was never in quirks mode, whatever the email's doctype, and the new
  // preview must render the same way.
  for (const [label, prologue] of [
    ["no doctype", ""],
    ["a legacy doctype that would mean quirks mode", '<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN">'],
  ] as const) {
    test(`renders an email with ${label} in standards mode, as the old preview did`, async ({ page, api }) => {
      await serveConsoleSurfaces(api, {
        emailHtml: `${prologue}<html><head><title>Mode</title></head><body><div class="card">mode</div></body></html>`,
      })
      await pinClock(page)
      await page.goto(`${ORIGINS.console}/notifications/templates/${TEMPLATE_ID}`)
      const card = page.frameLocator('iframe[title="Preview"]').locator(".card")
      await expect(card).toHaveText("mode")
      expect(await card.evaluate(() => document.compatMode)).toBe("CSS1Compat")
    })
  }

  test("runs none of the email's scripts or handlers", async ({ page, api, csp }) => {
    await serveConsoleSurfaces(api, {
      emailHtml: `<!DOCTYPE html><html><head><title>Untouched</title></head><body>
<div class="card">body</div>
<script>document.title = "script ran"; parent.postMessage({ type: "authsystem.email-preview.ready" }, "*")</script>
<img src="data:image/png;base64,AAAA" onerror="document.body.dataset.handler = 'ran'">
</body></html>`,
    })
    await pinClock(page)
    await page.goto(`${ORIGINS.console}/notifications/templates/${TEMPLATE_ID}`)
    const card = page.frameLocator('iframe[title="Preview"]').locator(".card")
    await expect(card).toHaveText("body")
    await csp.settle()
    const state = await card.evaluate(() => {
      const image = document.querySelector("img")!
      return {
        title: document.title,
        handler: document.body.dataset.handler ?? null,
        // Floor: the image really failed, so its onerror had its chance to run.
        imageFailed: image.complete && image.naturalWidth === 0,
      }
    })
    expect(state).toEqual({ title: "Untouched", handler: null, imageFailed: true })
    const refused = csp
      .violations()
      .filter((violation) => violation.directive === "script-src" && violation.blocked === "inline")
    // Two refusals: the <script> element and the onerror handler (Audits names each once).
    expect(refused.filter((violation) => violation.channel === "audits").length).toBeGreaterThanOrEqual(2)
    expect(refused.every((violation) => violation.disposition === "enforce")).toBe(true)
    csp.expect({
      directive: "script-src-elem",
      blocked: "inline",
      reason: "S02 negative check: the email's <script> and on* handler, refused by the preview's own policy",
    })
  })

  test("cannot be navigated away by the email (meta refresh), and nothing reaches the other origin", async ({
    page,
    api,
    csp,
    harness,
    requests,
  }) => {
    const target = `${ORIGINS.attacker}/s02-meta-refresh`
    await serveConsoleSurfaces(api, {
      emailHtml: `<!DOCTYPE html><html><head><meta http-equiv="refresh" content="0;url=${target}"></head><body><div class="card">moving</div></body></html>`,
    })
    await pinClock(page)
    await page.goto(`${ORIGINS.console}/notifications/templates/${TEMPLATE_ID}`)
    // Chromium names only the refused origin of a cross-origin navigation.
    const blocked = /^https:\/\/attacker\.example\.net\/?$/
    await expect
      .poll(() => csp.violations().some((violation) => violation.directive === "frame-src" && blocked.test(violation.blocked)))
      .toBe(true)
    await csp.settle()
    expect(requests.to(HOSTS.attacker)).toEqual([])
    for (const disposition of stageOf(harness.models.console).shared) {
      csp.expect({
        directive: "frame-src",
        blocked,
        disposition,
        reason: "S02 negative check: console's frame-src refuses the email's navigation",
      })
    }
  })
})

test.describe("/privacy", () => {
  for (const lineEnding of ["lf", "crlf"] as const) {
    test(`current and archived documents are styled, with zero violations (${lineEnding} copy)`, async ({
      page,
      api,
      privacy,
    }) => {
      await api.useAnonymous()
      const folder = privacyFolder(lineEnding)
      try {
        privacy.mount(folder.dir)
        for (const path of ["/privacy/en", "/privacy/v2026.09/en"]) {
          await page.goto(`${ORIGINS.accounts}${path}`)
          const state = await page.evaluate(() => ({
            // "!": a document without its <style> must fail here, not pass.
            sheet: document.querySelector("style")!.sheet !== null,
            width: getComputedStyle(document.querySelector("main")!).maxWidth,
          }))
          expect(state, path).toEqual({ sheet: true, width: "736px" })
        }
      } finally {
        folder.dispose()
      }
    })
  }
})

test.describe("negative checks: an injected style", () => {
  for (const app of ["console", "accounts"] as const) {
    test(`${app}: a <style> element and a style attribute are refused by the target policy`, async ({
      page,
      api,
      csp,
      harness,
    }) => {
      const stage = stageOf(harness.models[app])
      await api.useAnonymous()
      await page.goto(`${ORIGINS[app]}/login`)
      await expect(page.locator('input[type="email"]')).toBeVisible()
      const applied = await page.evaluate(() => {
        const style = document.createElement("style")
        style.textContent = "body{outline:5px solid red}"
        document.head.append(style)
        document.body.setAttribute("style", "outline-offset: 9px")
        const computed = getComputedStyle(document.body)
        // outline-style, not outline-width: Chromium reports the initial width
        // (3px, medium) even while the style is none.
        return { style: computed.outlineStyle, offset: computed.outlineOffset }
      })
      await csp.settle()
      expect(applied).toEqual(stage.inlineApplies ? { style: "solid", offset: "9px" } : { style: "none", offset: "0px" })
      expectStyleRefusals(csp, stage.styleDisposition)
    })
  }
})

/** Both the element (style-src-elem) and the attribute (style-src-attr) were reported, on every channel. */
function expectStyleRefusals(csp: CspObserver, disposition: CspDisposition) {
  const styles = csp.violations().filter((violation) => violation.directive === "style-src")
  expect(styles.filter((violation) => violation.disposition !== disposition)).toEqual([])
  expect(new Set(styles.map((violation) => violation.channel))).toEqual(new Set(["event", "console", "audits"]))
  // Both of them: the event channel names the effective directive, so the
  // element (style-src-elem) and the attribute (style-src-attr) are told apart.
  expect(new Set(styles.filter((violation) => violation.channel === "event").map((violation) => violation.raw))).toEqual(
    new Set(["style-src-elem", "style-src-attr"])
  )
  csp.expect({
    directive: "style-src-elem",
    blocked: "inline",
    disposition,
    reason: "S02 negative check: an injected <style> and style attribute",
  })
}
