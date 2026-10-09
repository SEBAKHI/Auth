/*
 * Puts the platform appearance on the page. Deliberately tiny and free of the
 * colour registry: the entry points call `applyCachedTheme` before React
 * mounts, so a returning visitor's first frame already carries the platform's
 * colours, and the ~45 KB registry (build-theme.ts) loads only when the
 * appearance has to be computed.
 *
 * The CSP allows inline styles (`style-src 'unsafe-inline'`) and forbids
 * inline scripts, which is why this runs from the bundle instead of a script
 * in index.html.
 */

const STYLE_ID = "platform-theme"
const CACHE_KEY = "auth.ui.theme-css"

/** Replaces the platform stylesheet; null removes it (preset.css shows). */
export function applyThemeCss(css: string | null): void {
  let style = document.getElementById(STYLE_ID) as HTMLStyleElement | null
  if (css === null) {
    style?.remove()
    return
  }
  if (!style) {
    style = document.createElement("style")
    style.id = STYLE_ID
    // Last in <head>; the selectors outrank preset.css anyway (build-theme.ts).
    document.head.appendChild(style)
  }
  if (style.textContent !== css) style.textContent = css
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
