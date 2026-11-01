/*
 * Puts the platform's own fonts on the page (Platform settings › Fonts): a heading font and a
 * body font, each a WOFF2 file served by the API. Small and free of React, like
 * apply-theme.ts: the entry points call `applyCachedFonts` before React mounts, so a returning
 * visitor's first frame is already set in the platform's fonts.
 *
 * Each file is registered with the CSS Font Loading API under a family name of this module's
 * own (never the name inside the file, which an uploader controls), and the two variables
 * the preset's utilities read (`--font-sans`, `--font-heading`, preset.css) are set by an
 * adopted stylesheet: no inline <style>, which the CSP refuses (card S02). The fetch is a
 * CORS request the API answers with `Access-Control-Allow-Origin: *`, and both apps' `font-src`
 * names the API origin.
 *
 * The shipped stack stays behind the custom family: a font without Arabic glyphs leaves Arabic
 * text to IBM Plex Sans Arabic, per glyph, and a file the browser refuses (damaged, wrong
 * format) leaves everything to IBM Plex Sans.
 */

import { API_BASE_URL } from "@authsystem/api/env"
import { readCachedBranding } from "@authsystem/ui/branding-cache"
import { setDocumentStyleSheet } from "@authsystem/ui/hooks/use-adopted-style-sheet"
import { THEME_APPLIED_EVENT } from "@authsystem/ui/theme/apply-theme"

/**
 * The largest font file the server accepts, mirrored so a larger one fails before it is sent.
 * Held to `PlatformFonts.MaxSizeBytes` (Auth.Domain/Constants/PlatformFonts.cs) by a test
 * that reads that file.
 */
export const FONT_MAX_SIZE_BYTES = 2 * 1024 * 1024

export type FontRole = "heading" | "body"

export const FONT_ROLES = ["heading", "body"] as const satisfies readonly FontRole[]

/** The family names the files are registered under, here and in the sign-in page design frame. */
export const PLATFORM_FONT_FAMILY: Record<FontRole, string> = {
  heading: "AuthSystem Platform Heading",
  body: "AuthSystem Platform Body",
}

/**
 * The shipped stack, as preset.css declares `--font-sans` (held to it by
 * platform-fonts.test.ts): it follows the custom family, so whatever the platform's font
 * lacks still comes from IBM Plex.
 */
export const DEFAULT_FONT_STACK = "'IBM Plex Sans Variable', 'IBM Plex Sans Arabic', sans-serif"

/** The fonts in force: each role's absolute file URL, or null for the shipped one. */
export type PlatformFontUrls = Record<FontRole, string | null>

/** One registered font, as the sign-in page design frame needs it. */
export interface PlatformFontFace {
  family: string
  url: string
}

/** The fonts part of the branding answer (`PlatformBrandingDto.fonts`), as cached or fetched. */
interface FontsPayload {
  heading?: { url?: string | null } | null
  body?: { url?: string | null } | null
}

const STYLE_ID = "platform-fonts"

/** What this page has registered, by role. */
const registered = new Map<FontRole, { url: string; face: FontFace | null }>()

/**
 * An absolute http(s) URL, or null. A relative URL is the API's (as the logos' are). Anything
 * else is dropped rather than handed to `url()`.
 */
function absolute(url: string | null | undefined): string | null {
  if (!url) return null
  try {
    const parsed = new URL(url.startsWith("/") ? `${API_BASE_URL}${url}` : url)
    return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed.href : null
  } catch {
    return null
  }
}

/** The fonts of a branding answer; none when it has no fonts (an older API, a cold cache). */
export function fontUrlsFrom(fonts: FontsPayload | null | undefined): PlatformFontUrls {
  return { heading: absolute(fonts?.heading?.url), body: absolute(fonts?.body?.url) }
}

/** The stylesheet that points the preset's variables at the registered families; null for none. */
export function platformFontsCss(urls: PlatformFontUrls): string | null {
  const declarations = [
    urls.body ? `--font-sans: "${PLATFORM_FONT_FAMILY.body}", ${DEFAULT_FONT_STACK};` : "",
    // A heading without its own font follows the body's, as the preset does.
    urls.heading ? `--font-heading: "${PLATFORM_FONT_FAMILY.heading}", var(--font-sans);` : "",
  ].join("")
  return declarations ? `:root{${declarations}}` : null
}

function register(role: FontRole, url: string | null): Promise<unknown> | null {
  const current = registered.get(role)
  if ((current?.url ?? null) === url) return null
  if (current?.face) document.fonts.delete(current.face)
  registered.delete(role)
  if (!url) return null
  // jsdom and very old browsers have no Font Loading API: the variables still apply, and the
  // browser's own @font-face matching is what they lack.
  if (typeof FontFace !== "function" || !document.fonts) {
    registered.set(role, { url, face: null })
    return null
  }
  const face = new FontFace(PLATFORM_FONT_FAMILY[role], `url("${url}") format("woff2")`, { display: "swap" })
  document.fonts.add(face)
  registered.set(role, { url, face })
  // Fetched now rather than when text first asks for it: the entry waits for it briefly, and
  // a file that fails (damaged, refused) leaves the stack's next family in place.
  return face.load().catch(() => undefined)
}

/**
 * Applies the platform's fonts; a role set to null goes back to the shipped one. Returns once
 * the newly registered files have loaded or failed. Applying what is already applied does
 * nothing; a change tells the sign-in page design frames (THEME_APPLIED_EVENT).
 */
export function applyPlatformFonts(urls: PlatformFontUrls): Promise<void> {
  const changed = FONT_ROLES.some((role) => (registered.get(role)?.url ?? null) !== urls[role])
  const loads = FONT_ROLES.map((role) => register(role, urls[role]))
  setDocumentStyleSheet(STYLE_ID, platformFontsCss(urls))
  if (changed) document.dispatchEvent(new Event(THEME_APPLIED_EVENT))
  return Promise.all(loads).then(() => undefined)
}

/** The registered fonts, for the sign-in page design frame, which registers them itself. */
export function platformFontFaces(): PlatformFontFace[] {
  return [...registered.entries()].map(([role, { url }]) => ({ family: PLATFORM_FONT_FAMILY[role], url }))
}

/**
 * How long the entry waits for the cached fonts before it draws anything. The files are
 * immutable and cached for a year, so a returning visitor's load is a disk read; past this a
 * first draw in the shipped font beats a blank page, and `font-display: swap` takes over.
 */
export const CACHED_FONTS_BUDGET_MS = 600

/**
 * Applies the fonts of the branding this browser cached on its last visit, before React
 * mounts, and resolves when they have loaded or `budgetMs` has passed, whichever is first.
 * Cosmetic, so a stale copy is harmless: BrandingProvider applies the current answer.
 */
export function applyCachedFonts(budgetMs: number = CACHED_FONTS_BUDGET_MS): Promise<void> {
  const fonts = readCachedBranding()?.fonts
  const urls = fontUrlsFrom(fonts)
  if (!urls.heading && !urls.body) return Promise.resolve()
  return Promise.race([
    applyPlatformFonts(urls),
    new Promise<void>((resolve) => window.setTimeout(resolve, budgetMs)),
  ])
}
