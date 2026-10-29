import { afterEach, beforeEach, describe, expect, it } from "vitest"

import {
  applyCachedTheme,
  applyThemeCss,
  MAX_CACHED_APPLICATIONS,
  readCachedTheme,
  writeCachedTheme,
} from "./apply-theme"

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

/** A Storage with real enumeration (`length`, `key`), which the eviction walks. */
function stubStorage(seed: Record<string, string> = {}) {
  const entries = new Map(Object.entries(seed))
  Object.defineProperty(window, "localStorage", {
    configurable: true,
    value: {
      getItem: (key: string) => entries.get(key) ?? null,
      setItem: (key: string, value: string) => void entries.set(key, String(value)),
      removeItem: (key: string) => void entries.delete(key),
      clear: () => entries.clear(),
      key: (index: number) => [...entries.keys()][index] ?? null,
      get length() {
        return entries.size
      },
    },
  })
  return entries
}

const PLATFORM = "auth.ui.theme-css"
const app = (clientId: string) => `auth.ui.theme-css:app:${clientId}`
/** A stylesheet that names its scope, so a test can tell which one was painted. */
const css = (name: string) => `html:root{--scope:${name};}`
const sheetOf = (text: string) => JSON.stringify({ key: `k-${text}`, css: text })

describe("the cache, per scope", () => {
  it("keeps the platform's under its own key, as before, and each application's beside it", () => {
    const storage = stubStorage()

    writeCachedTheme({ key: "p", css: css("platform") })
    writeCachedTheme({ key: "a", css: css("crm") }, "crm")

    expect(JSON.parse(storage.get(PLATFORM)!)).toEqual({ key: "p", css: css("platform") })
    expect(JSON.parse(storage.get(app("crm"))!)).toMatchObject({ key: "a", css: css("crm") })
    expect(readCachedTheme()).toEqual({ key: "p", css: css("platform") })
    expect(readCachedTheme("crm")).toEqual({ key: "a", css: css("crm") })
  })

  it("reads only the scope it is asked for: never another application's, never the platform's in its place", () => {
    stubStorage({ [PLATFORM]: sheetOf(css("platform")), [app("crm")]: sheetOf(css("crm")) })

    expect(readCachedTheme("desk")).toBeNull()
    expect(readCachedTheme("crm")?.css).toBe(css("crm"))
  })

  it("names a client id safely: one that looks like another key stays its own", () => {
    const storage = stubStorage()

    writeCachedTheme({ key: "a", css: css("odd") }, "a:b/c")

    expect([...storage.keys()]).toEqual([app(encodeURIComponent("a:b/c"))])
    expect(readCachedTheme("a:b/c")?.css).toBe(css("odd"))
  })

  it(`keeps at most ${MAX_CACHED_APPLICATIONS} applications, forgetting the one used longest ago`, () => {
    const storage = stubStorage({ [PLATFORM]: sheetOf(css("platform")) })
    const clients = ["a", "b", "c", "d", "e", "f", "g"]

    for (const client of clients.slice(0, 5)) writeCachedTheme({ key: client, css: css(client) }, client)
    // "a" is used again: "b" is now the oldest.
    writeCachedTheme({ key: "a", css: css("a") }, "a")
    writeCachedTheme({ key: "f", css: css("f") }, "f")
    writeCachedTheme({ key: "g", css: css("g") }, "g")

    const kept = [...storage.keys()].filter((key) => key !== PLATFORM).sort()
    expect(kept).toEqual(["a", "d", "e", "f", "g"].map(app))
    expect(kept.length).toBe(MAX_CACHED_APPLICATIONS)
    expect(storage.has(PLATFORM)).toBe(true)
  })

  it("forgets one application without touching the platform's or another's", () => {
    const storage = stubStorage({
      [PLATFORM]: sheetOf(css("platform")),
      [app("crm")]: sheetOf(css("crm")),
      [app("desk")]: sheetOf(css("desk")),
    })

    writeCachedTheme(null, "crm")

    expect([...storage.keys()].sort()).toEqual([PLATFORM, app("desk")].sort())
  })
})

describe("applyCachedTheme — the first frame", () => {
  const sheet = () => document.getElementById("platform-theme")?.textContent ?? null

  beforeEach(() => {
    // The <style> fallback, so the applied text can be read back.
    uninstall()
  })

  it("paints an application's page with that application's own appearance", () => {
    stubStorage({
      [PLATFORM]: sheetOf(css("platform")),
      [app("crm")]: sheetOf(css("crm")),
      [app("desk")]: sheetOf(css("desk")),
    })

    applyCachedTheme("crm")

    expect(sheet()).toBe(css("crm"))
  })

  it("paints with the platform's when this browser has not seen the application's switched on", () => {
    stubStorage({ [PLATFORM]: sheetOf(css("platform")), [app("desk")]: sheetOf(css("desk")) })

    applyCachedTheme("crm")

    expect(sheet()).toBe(css("platform"))
  })

  it("paints the platform's pages with the platform's, whatever applications are kept", () => {
    stubStorage({ [PLATFORM]: sheetOf(css("platform")), [app("crm")]: sheetOf(css("crm")) })

    applyCachedTheme(null)

    expect(sheet()).toBe(css("platform"))
  })

  it("draws an application whose own appearance is the shipped preset with preset.css alone", () => {
    stubStorage({ [PLATFORM]: sheetOf(css("platform")), [app("crm")]: JSON.stringify({ key: "k", css: "" }) })

    applyCachedTheme("crm")

    expect(sheet()).toBeNull()
  })
})
