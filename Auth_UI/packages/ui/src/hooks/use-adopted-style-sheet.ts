import * as React from "react"

/*
 * Stylesheets whose text is computed at run time, without an inline <style>.
 *
 * Both applications' CSP refuses inline styles (card S02: a report-only header
 * first, then the enforced one, with no 'unsafe-inline' in style-src). A
 * constant stylesheet can be allowed by its hash; one whose text depends on
 * a chart's configuration or the platform's appearance cannot. A constructable
 * stylesheet adopted by the document is not inline markup, and CSP does not
 * check it: under style-src 'self', replaceSync and insertRule raised no
 * violation in Chromium (S02 spike item 1, 2026-10-12). So every first-party
 * stylesheet with computed text goes through this module.
 *
 * Adopted sheets come after the document's own sheets in the cascade, so a
 * rule here outranks an equally specific rule in preset.css - as a <style>
 * appended to <head> did.
 *
 * Where the document has no adoptedStyleSheets (jsdom; Firefox before 101,
 * Safari before 16.4) a <style> element is the fallback. In a browser under
 * the strict policy that fallback is blocked, so those browsers lose these
 * styles: the residual risk card S02 names.
 */

function canAdopt() {
  return (
    typeof document !== "undefined" &&
    "adoptedStyleSheets" in document &&
    typeof CSSStyleSheet === "function" &&
    "replaceSync" in CSSStyleSheet.prototype
  )
}

function attach(sheet: CSSStyleSheet) {
  if (!document.adoptedStyleSheets.includes(sheet)) {
    document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet]
  }
}

function detach(sheet: CSSStyleSheet) {
  document.adoptedStyleSheets = document.adoptedStyleSheets.filter((other) => other !== sheet)
}

/** Adds a stylesheet of its own to the document; the returned function takes it off again. */
export function adoptStyleSheet(cssText: string): () => void {
  if (!canAdopt()) {
    const style = document.createElement("style")
    style.textContent = cssText
    document.head.appendChild(style)
    return () => style.remove()
  }
  const sheet = new CSSStyleSheet()
  sheet.replaceSync(cssText)
  attach(sheet)
  return () => detach(sheet)
}

const keyed = new Map<string, CSSStyleSheet>()

/**
 * The document's one stylesheet for `key`: replaced, never stacked, by every
 * call; null takes it off. The fallback <style> carries `key` as its id and is
 * found by it on every call, so nothing here holds an element someone else
 * may have removed.
 */
export function setDocumentStyleSheet(key: string, cssText: string | null): void {
  if (!canAdopt()) {
    let style = document.getElementById(key)
    if (cssText === null) {
      style?.remove()
      return
    }
    if (!style) {
      style = document.createElement("style")
      style.id = key
      document.head.appendChild(style)
    }
    if (style.textContent !== cssText) style.textContent = cssText
    return
  }
  const existing = keyed.get(key)
  if (cssText === null) {
    if (existing) detach(existing)
    keyed.delete(key)
    return
  }
  const sheet = existing ?? new CSSStyleSheet()
  keyed.set(key, sheet)
  sheet.replaceSync(cssText)
  attach(sheet)
}

/**
 * Adopts `cssText` while the calling component is mounted, and replaces it
 * when the text changes. An insertion effect runs before layout, so the first
 * paint already has the rules. Null adopts nothing.
 */
export function useAdoptedStyleSheet(cssText: string | null): void {
  React.useInsertionEffect(() => {
    if (cssText === null) return
    return adoptStyleSheet(cssText)
  }, [cssText])
}
