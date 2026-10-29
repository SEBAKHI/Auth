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

/**
 * An application whose own appearance is switched on replaces the platform's
 * on the accounts pages it sends people to, so a browser keeps that one too,
 * beside the platform's, under a key of its own per application.
 */
const APP_CACHE_PREFIX = `${CACHE_KEY}:app:`

/**
 * How many applications' appearances a browser keeps. A person goes through
 * the sign-in pages of a few applications, not dozens; past this, the one used
 * longest ago is forgotten and its next visit starts from the platform's.
 */
export const MAX_CACHED_APPLICATIONS = 5

/**
 * Whose appearance: the platform's (null), or one application's, by its
 * client id. Every read names the scope it is for, and reads that key alone,
 * so a page never paints with the colours of an application it is not for.
 */
export type ThemeScope = string | null

function storageKey(scope: ThemeScope): string {
  return scope === null ? CACHE_KEY : `${APP_CACHE_PREFIX}${encodeURIComponent(scope)}`
}

/** Replaces the platform stylesheet; null removes it (preset.css shows). */
export function applyThemeCss(css: string | null): void {
  // One sheet for the document, replaced on every call. Adopted sheets follow
  // the document's own in the cascade, and the selectors outrank preset.css
  // anyway (build-theme.ts). Where the document cannot adopt a sheet (jsdom),
  // a <style id="platform-theme"> stands in.
  setDocumentStyleSheet(STYLE_ID, css)
}

/**
 * A computed stylesheet, and the appearance it was computed from. An empty
 * stylesheet is an application whose own appearance is the shipped preset:
 * preset.css alone draws it, where the platform's might not.
 */
export type CachedTheme = { key: string; css: string }

type StoredTheme = CachedTheme & { at?: number }

function readStored(key: string): StoredTheme | null {
  try {
    const raw = window.localStorage.getItem(key)
    if (!raw) return null
    const value = JSON.parse(raw) as Partial<StoredTheme>
    return typeof value.key === "string" && typeof value.css === "string"
      ? { key: value.key, css: value.css, at: typeof value.at === "number" ? value.at : 0 }
      : null
  } catch {
    // Storage is absent in privacy-restricted contexts and in jsdom, and a
    // value written by an older bundle may not parse.
    return null
  }
}

export function readCachedTheme(scope: ThemeScope = null): CachedTheme | null {
  const stored = readStored(storageKey(scope))
  return stored ? { key: stored.key, css: stored.css } : null
}

/** The applications' entries, most recently used first. */
function cachedApplications(): { key: string; at: number }[] {
  const entries: { key: string; at: number }[] = []
  for (let index = 0; index < window.localStorage.length; index++) {
    const key = window.localStorage.key(index)
    if (key?.startsWith(APP_CACHE_PREFIX)) entries.push({ key, at: readStored(key)?.at ?? 0 })
  }
  return entries.sort((a, b) => b.at - a.at || a.key.localeCompare(b.key))
}

/**
 * Remembers the computed stylesheet for the next visit's first frame, and
 * lets a visit whose appearance has not changed skip loading the registry.
 * Only what is saved is cached — never an administrator's preview. Null
 * forgets it: for the platform, it is back on the shipped preset; for an
 * application, its own appearance is off and the platform's shows.
 *
 * Writing an application's entry marks it as the most recently used, and
 * forgets the oldest past `MAX_CACHED_APPLICATIONS`.
 */
export function writeCachedTheme(value: CachedTheme | null, scope: ThemeScope = null): void {
  const key = storageKey(scope)
  try {
    if (!value) {
      window.localStorage.removeItem(key)
      return
    }
    if (scope === null) {
      window.localStorage.setItem(key, JSON.stringify(value))
      return
    }
    // Later than every entry already kept, even within one millisecond.
    const others = cachedApplications().filter((entry) => entry.key !== key)
    const at = Math.max(Date.now(), (others[0]?.at ?? 0) + 1)
    window.localStorage.setItem(key, JSON.stringify({ key: value.key, css: value.css, at }))
    for (const entry of others.slice(MAX_CACHED_APPLICATIONS - 1)) {
      window.localStorage.removeItem(entry.key)
    }
  } catch {
    // Losing the cache only costs one frame of the shipped preset.
  }
}

/**
 * Applies the last known appearance of `scope`: an application's own when this
 * browser has seen it switched on, otherwise the platform's. Cosmetic, so a
 * stale copy is harmless: BrandingProvider replaces it as soon as the current
 * one is fetched. Never another application's: only the scope's key is read.
 */
export function applyCachedTheme(scope: ThemeScope = null): void {
  const cached = (scope === null ? null : readCachedTheme(scope)) ?? readCachedTheme(null)
  if (cached) applyThemeCss(cached.css || null)
}
