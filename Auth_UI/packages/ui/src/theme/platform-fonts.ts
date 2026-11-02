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

/**
 * Tailwind's line height for each text size (tailwindcss/theme.css), as a multiple of the size.
 * A platform font taller than these is given its own (`lineHeightCss`), never a shorter one.
 */
const TEXT_LINE_HEIGHTS = {
  xs: "calc(1 / 0.75)",
  sm: "calc(1.25 / 0.875)",
  base: "calc(1.5 / 1)",
  lg: "calc(1.75 / 1.125)",
  xl: "calc(1.75 / 1.25)",
  "2xl": "calc(2 / 1.5)",
  "3xl": "calc(2.25 / 1.875)",
  "4xl": "calc(2.5 / 2.25)",
} as const

/**
 * Letters that reach furthest above and below the line in the scripts the platform writes:
 * Arabic tails (ح ع ي), Latin capitals with accents and descenders. Not stacked vowel marks,
 * which the interface never writes and which would open every line for them.
 */
const TALL_SAMPLE = "حعيجخغقÁÉÇgjpqy"

/**
 * The line height, as a multiple of the font size, a loaded family needs so that no letter is
 * cut: the larger of the font's own ascent and descent (what `line-height: normal` would give)
 * and the ink of its tallest letters. Null where it cannot be measured.
 */
export function measureLineHeight(family: string): number | null {
  const context = document.createElement("canvas").getContext("2d")
  if (!context) return null
  context.font = `100px "${family}"`
  const metrics = context.measureText(TALL_SAMPLE)
  const height = Math.max(
    (metrics.fontBoundingBoxAscent ?? 0) + (metrics.fontBoundingBoxDescent ?? 0),
    (metrics.actualBoundingBoxAscent ?? 0) + (metrics.actualBoundingBoxDescent ?? 0)
  )
  return Number.isFinite(height) && height > 0 ? Math.ceil(height) / 100 : null
}

/**
 * Every text size's line height raised to `lineHeight` where the font needs more than Tailwind
 * gives (owner, 2026-11-02: Arabic letters such as ح were cut at the bottom in the sidebar,
 * whose labels clip what leaves their line). Raised, never lowered.
 */
function lineHeightCss(lineHeight: number | null): string {
  if (!lineHeight) return ""
  return Object.entries(TEXT_LINE_HEIGHTS)
    .map(([step, base]) => `--text-${step}--line-height: max(${base}, ${lineHeight});`)
    .join("")
}

/** The stylesheet that points the preset's variables at the registered families; null for none. */
export function platformFontsCss(urls: PlatformFontUrls, lineHeight: number | null = null): string | null {
  const declarations = [
    urls.body ? `--font-sans: "${PLATFORM_FONT_FAMILY.body}", ${DEFAULT_FONT_STACK};` : "",
    // A heading without its own font follows the body's, as the preset does.
    urls.heading ? `--font-heading: "${PLATFORM_FONT_FAMILY.heading}", var(--font-sans);` : "",
    urls.body || urls.heading ? lineHeightCss(lineHeight) : "",
  ].join("")
  return declarations ? `:root{${declarations}}` : null
}

/** The tallest line height the loaded platform fonts need; null when none is loaded. */
function loadedLineHeight(): number | null {
  const heights = [...registered.entries()]
    .filter(([, { face }]) => face?.status === "loaded")
    .map(([role]) => measureLineHeight(PLATFORM_FONT_FAMILY[role]))
    .filter((height): height is number => height !== null)
  return heights.length ? Math.max(...heights) : null
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
 *
 * The line heights follow once the files have loaded, since a font is measured only then.
 */
export function applyPlatformFonts(urls: PlatformFontUrls): Promise<void> {
  const changed = FONT_ROLES.some((role) => (registered.get(role)?.url ?? null) !== urls[role])
  const loads = FONT_ROLES.map((role) => register(role, urls[role]))
  setDocumentStyleSheet(STYLE_ID, platformFontsCss(urls, loadedLineHeight()))
  if (changed) document.dispatchEvent(new Event(THEME_APPLIED_EVENT))
  return Promise.all(loads).then(() => {
    // A later call has since put other fonts in place: its own load sets their heights.
    if (FONT_ROLES.some((role) => (registered.get(role)?.url ?? null) !== urls[role])) return
    setDocumentStyleSheet(STYLE_ID, platformFontsCss(urls, loadedLineHeight()))
  })
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
