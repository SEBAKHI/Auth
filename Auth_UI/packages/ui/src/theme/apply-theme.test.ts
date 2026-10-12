import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { applyThemeCss } from "./apply-theme"

/**
 * The platform appearance is an adopted stylesheet, not a <style> (card S02,
 * M1): its text is the administrator's appearance, computed at run time, so no
 * hash can allow it under a style-src without 'unsafe-inline'. One sheet for
 * the document, replaced and never stacked. (Without adoptedStyleSheets the
 * <style id="platform-theme"> fallback stands in; branding.provider.test.tsx
 * covers that path, which is the one jsdom takes.)
 */

let uninstall: () => void

beforeEach(() => {
  document.head.innerHTML = ""
  let sheets: CSSStyleSheet[] = []
  Object.defineProperty(document, "adoptedStyleSheets", {
    configurable: true,
    get: () => sheets,
    set: (next: CSSStyleSheet[]) => {
      sheets = [...next]
    },
  })
  uninstall = () => delete (document as { adoptedStyleSheets?: unknown }).adoptedStyleSheets
})

afterEach(() => {
  applyThemeCss(null)
  uninstall()
})

const adopted = () =>
  document.adoptedStyleSheets.map((sheet) => [...sheet.cssRules].map((rule) => rule.cssText).join(" "))

describe("applyThemeCss", () => {
  it("adopts one sheet with the appearance, and leaves no <style> in the document", () => {
    applyThemeCss("html:root { --primary: red; }")
    expect(adopted()).toEqual(["html:root { --primary: red; }"])
    expect(document.querySelectorAll("style")).toHaveLength(0)
  })

  it("replaces the sheet on a second call instead of stacking a second one", () => {
    applyThemeCss("html:root { --primary: red; }")
    applyThemeCss("html:root { --primary: blue; }")
    expect(adopted()).toEqual(["html:root { --primary: blue; }"])
  })

  it("removes the sheet on null, so preset.css shows again", () => {
    applyThemeCss("html:root { --primary: red; }")
    applyThemeCss(null)
    expect(document.adoptedStyleSheets).toEqual([])
    expect(document.querySelectorAll("style")).toHaveLength(0)
  })
})
