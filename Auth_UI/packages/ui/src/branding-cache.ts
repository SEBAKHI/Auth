import type { Schemas } from "@authsystem/api/types"

/**
 * The branding this browser saw last, kept between visits. Two readers: BrandingProvider
 * seeds its first answer with it, and the entry applies the platform's fonts from it before
 * React mounts (`applyCachedFonts`), so it lives apart from either.
 */
export const BRANDING_CACHE_KEY = "auth.ui.branding"

/**
 * Last known branding, so a returning visitor's first frame is the real mark
 * rather than a placeholder.
 *
 * Legitimate here precisely because branding is cosmetic: a one-frame-stale
 * logo costs nothing, and the revalidation lands in the same second. The same
 * trick is forbidden for anything a user could act on or rely upon.
 */
export function readCachedBranding(): Schemas["PlatformBrandingDto"] | undefined {
  try {
    const raw = window.localStorage.getItem(BRANDING_CACHE_KEY)
    return raw ? (JSON.parse(raw) as Schemas["PlatformBrandingDto"]) : undefined
  } catch {
    // Storage is absent in privacy-restricted contexts and in jsdom.
    return undefined
  }
}

export function writeCachedBranding(value: Schemas["PlatformBrandingDto"]): void {
  try {
    window.localStorage.setItem(BRANDING_CACHE_KEY, JSON.stringify(value))
  } catch {
    // Losing the cache only costs the placeholder frame back.
  }
}
