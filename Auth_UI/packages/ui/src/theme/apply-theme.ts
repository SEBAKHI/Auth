/*
 * Puts the platform appearance on the page. Deliberately tiny and free of the
 * colour registry: the entry points call `applyCachedTheme` before React
 * mounts, so a returning visitor's first frame already carries the platform's
 * colours, and the ~45 KB registry (build-theme.ts) loads only when the
 * appearance has to be computed.
 *
 * The stylesheet's text is the administrator's appearance, computed at run
 * time, so no hash can allow it, and the CSP refuses inline styles (card S02):
 * it is a constructable stylesheet adopted by the document, not a <style>
 * element. The CSP forbids inline scripts too, which
 * is why this runs from the bundle instead of a script in index.html.
 */

import { setDocumentStyleSheet } from "@authsystem/ui/hooks/use-adopted-style-sheet"

const STYLE_ID = "platform-theme"
const CACHE_KEY = "auth.ui.theme-css"

/** Replaces the platform stylesheet; null removes it (preset.css shows). */
export function applyThemeCss(css: string | null): void {
  // One sheet for the document, replaced on every call. Adopted sheets follow
  // the document's own in the cascade, and the selectors outrank preset.css
  // anyway (build-theme.ts). Where the document cannot adopt a sheet (jsdom),
  // a <style id="platform-theme"> stands in.
  setDocumentStyleSheet(STYLE_ID, css)
}

/** A computed stylesheet, and the appearance it was computed from. */
export type CachedTheme = { key: string; css: string }

export function readCachedTheme(): CachedTheme | null {
  try {
    const raw = window.localStorage.getItem(CACHE_KEY)
    if (!raw) return null
    const value = JSON.parse(raw) as Partial<CachedTheme>
    return typeof value.key === "string" && typeof value.css === "string"
      ? { key: value.key, css: value.css }
      : null
  } catch {
    // Storage is absent in privacy-restricted contexts and in jsdom, and a
    // value written by an older bundle may not parse.
    return null
  }
}

/**
 * Remembers the computed stylesheet for the next visit's first frame, and
 * lets a visit whose appearance has not changed skip loading the registry.
 * Only what the platform saved is cached — never an administrator's preview.
 * Null forgets it (the platform is back on the shipped preset).
 */
export function writeCachedTheme(value: CachedTheme | null): void {
  try {
    if (value) window.localStorage.setItem(CACHE_KEY, JSON.stringify(value))
    else window.localStorage.removeItem(CACHE_KEY)
  } catch {
    // Losing the cache only costs one frame of the shipped preset.
  }
}

/**
 * Applies the last known appearance. Cosmetic, so a stale copy is harmless:
 * BrandingProvider replaces it as soon as the current one is fetched.
 */
export function applyCachedTheme(): void {
  const cached = readCachedTheme()
  if (cached) applyThemeCss(cached.css)
}
