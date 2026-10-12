import { render } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { adoptStyleSheet, setDocumentStyleSheet, useAdoptedStyleSheet } from "./use-adopted-style-sheet"

/**
 * jsdom has constructable stylesheets (CSSStyleSheet with replaceSync,
 * insertRule and cssRules) but no document.adoptedStyleSheets. This file -
 * and only this file, not test/setup.ts - gives the document one, so the
 * adopted path runs as in a browser; the last block runs without it, as in
 * jsdom and old browsers.
 */
function installAdoption() {
  let sheets: CSSStyleSheet[] = []
  Object.defineProperty(document, "adoptedStyleSheets", {
    configurable: true,
    get: () => sheets,
    set: (next: CSSStyleSheet[]) => {
      sheets = [...next]
    },
  })
  return () => {
    delete (document as { adoptedStyleSheets?: unknown }).adoptedStyleSheets
  }
}

const texts = () => document.adoptedStyleSheets.map((sheet) => [...sheet.cssRules].map((rule) => rule.cssText).join(" "))
const inlineStyles = () => document.querySelectorAll("style").length

function Styled({ css }: { css: string | null }) {
  useAdoptedStyleSheet(css)
  return null
}

describe("with adoptedStyleSheets (every supported browser)", () => {
  let uninstall: () => void
  beforeEach(() => {
    document.head.innerHTML = ""
    uninstall = installAdoption()
  })
  afterEach(() => uninstall())

  it("mounting adds one sheet; unmounting takes it off; no <style> anywhere", () => {
    const { unmount } = render(<Styled css="p { color: red; }" />)
    expect(texts()).toEqual(["p { color: red; }"])
    expect(inlineStyles()).toBe(0)
    unmount()
    expect(document.adoptedStyleSheets).toEqual([])
  })

  it("a new text replaces the sheet rather than adding one; null adopts nothing", () => {
    const { rerender } = render(<Styled css="p { color: red; }" />)
    rerender(<Styled css="p { color: blue; }" />)
    expect(texts()).toEqual(["p { color: blue; }"])
    rerender(<Styled css={null} />)
    expect(document.adoptedStyleSheets).toEqual([])
  })

  it("keeps sheets adopted by others in place", () => {
    const other = new CSSStyleSheet()
    document.adoptedStyleSheets = [other]
    const remove = adoptStyleSheet("b { color: green; }")
    expect(document.adoptedStyleSheets).toHaveLength(2)
    expect(document.adoptedStyleSheets[0]).toBe(other)
    remove()
    expect(document.adoptedStyleSheets).toEqual([other])
  })

  it("setDocumentStyleSheet keeps one sheet per key: replaced, never stacked; null removes it", () => {
    setDocumentStyleSheet("k", "i { color: red; }")
    setDocumentStyleSheet("k", "i { color: blue; }")
    expect(texts()).toEqual(["i { color: blue; }"])
    setDocumentStyleSheet("other", "u { color: red; }")
    expect(document.adoptedStyleSheets).toHaveLength(2)
    setDocumentStyleSheet("k", null)
    expect(texts()).toEqual(["u { color: red; }"])
    setDocumentStyleSheet("other", null)
    expect(document.adoptedStyleSheets).toEqual([])
    expect(inlineStyles()).toBe(0)
  })

  it("setDocumentStyleSheet puts its sheet back if someone dropped every adopted sheet", () => {
    setDocumentStyleSheet("k", "i { color: red; }")
    document.adoptedStyleSheets = []
    setDocumentStyleSheet("k", "i { color: blue; }")
    expect(texts()).toEqual(["i { color: blue; }"])
    setDocumentStyleSheet("k", null)
  })
})

describe("without adoptedStyleSheets (jsdom, old browsers): a <style> fallback", () => {
  beforeEach(() => {
    document.head.innerHTML = ""
  })

  it("the hook and adoptStyleSheet fall back to an element they remove again", () => {
    const { unmount } = render(<Styled css="p { color: red; }" />)
    expect([...document.head.querySelectorAll("style")].map((style) => style.textContent)).toEqual([
      "p { color: red; }",
    ])
    unmount()
    expect(inlineStyles()).toBe(0)
  })

  it("setDocumentStyleSheet uses one element per key, found by id each time", () => {
    setDocumentStyleSheet("k", "i { color: red; }")
    setDocumentStyleSheet("k", "i { color: blue; }")
    expect(document.querySelectorAll("style#k")).toHaveLength(1)
    expect(document.getElementById("k")?.textContent).toBe("i { color: blue; }")
    setDocumentStyleSheet("k", null)
    expect(document.getElementById("k")).toBeNull()
  })
})
