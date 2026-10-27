import { isLanguageCode, type LanguageCode } from "./languages"

/**
 * The look an application asked for when it sent its user here: its language
 * (`ui_locales`, OpenID Connect) and its colour mode (`ui_theme`).
 *
 * The authorize and end-session endpoints resolve the two and add them to the
 * accounts page they redirect to; an application linking straight to an
 * accounts page may add them itself. They hold for the rest of that flow in
 * this tab — across the sign-in pages, a trip to Google and back, and an
 * "expired" page — and are written to nothing the user owns: not the stored
 * language or theme, not the profile. A choice the user makes here ends the
 * hint it overrides.
 *
 * `sessionStorage`, not `localStorage`: the hint dies with the tab, and never
 * reaches the next visit. The resume URL never carries the hints (the server
 * keeps them with the pending request), so `return-to.ts` is untouched.
 *
 * Resolution matches the server's `PresentationHints` row for row;
 * `docs/api/ui-locales-cases.json` holds the two to one table.
 */

/** The colour modes `ui_theme` may name, exactly as written. */
export const HINT_THEMES = ["light", "dark", "system"] as const

export type ThemeHint = (typeof HINT_THEMES)[number]

export interface FlowHints {
  language?: LanguageCode
  theme?: ThemeHint
}

export const UI_LOCALES_PARAM = "ui_locales"
export const UI_THEME_PARAM = "ui_theme"

const STORAGE_KEY = "auth.flow.hints"

/** Tags read from `ui_locales`; the rest are ignored (the server's MaxLocaleTags). */
const MAX_LOCALE_TAGS = 10

/** The longest tag considered (the server's MaxLocaleTagLength). */
const MAX_LOCALE_TAG_LENGTH = 35

/**
 * How long a hint outlives its last capture: the default lifetime of a pending
 * authorize request (`IdentityProvider:AuthorizationRequestLifetimeMinutes`).
 * Past it there is no flow left for the hint to belong to.
 */
export const FLOW_HINTS_LIFETIME_MS = 60 * 60 * 1000

interface StoredHints extends FlowHints {
  /** The flow the hints came with, or null for a direct link. */
  flow: string | null
  expiresAt: number
}

/**
 * The first supported language among the space-separated tags, in the order
 * sent; `en` when tags were sent and none is supported; null when the
 * parameter is absent or blank.
 */
export function resolveUiLocales(raw: string | null | undefined): LanguageCode | null {
  if (raw == null || raw.trim() === "") return null

  const tags = raw
    .split(" ")
    .filter((tag) => tag !== "")
    .slice(0, MAX_LOCALE_TAGS)
  for (const tag of tags) {
    if (tag.length > MAX_LOCALE_TAG_LENGTH) continue
    const language = primaryLanguage(tag)
    if (language) return language
  }

  return "en"
}

/** A tag's language part when it is a supported language (the server's `Languages.Normalize`). */
function primaryLanguage(tag: string): LanguageCode | null {
  const lower = tag.trim().toLowerCase()
  const dash = lower.indexOf("-")
  const primary = dash > 0 ? lower.slice(0, dash) : lower
  return isLanguageCode(primary) ? primary : null
}

/** The colour mode when `raw` names one exactly; null otherwise. */
export function resolveUiTheme(raw: string | null | undefined): ThemeHint | null {
  return HINT_THEMES.find((theme) => theme === raw) ?? null
}

/**
 * FNV-1a, 32 bits: a short, stable name for the pending request a page serves.
 * Enough to tell the flows of one tab apart, and it keeps the request's handle
 * itself out of storage.
 */
function digest(value: string): string {
  let hash = 0x811c9dc5
  for (let index = 0; index < value.length; index++) {
    hash ^= value.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193)
  }
  return (hash >>> 0).toString(16)
}

function read(now: number): StoredHints | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null

    const value = JSON.parse(raw) as Partial<StoredHints>
    if (typeof value.expiresAt !== "number" || value.expiresAt <= now) return null

    return {
      flow: typeof value.flow === "string" ? value.flow : null,
      expiresAt: value.expiresAt,
      language: isLanguageCode(value.language) ? value.language : undefined,
      theme: resolveUiTheme(value.theme) ?? undefined,
    }
  } catch {
    return null
  }
}

function write(value: StoredHints): void {
  try {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value))
  } catch {
    /* storage refused: the hint lasts for this page only */
  }
}

/**
 * Reads the hints off the address the tab arrived at, and removes them from it.
 * Call once, before the first render.
 *
 * `pendingRequest` is the authorize request this page serves, as
 * `return-to.ts` (the one module that knows its shapes) reads it off the same
 * address; null when the page serves none.
 *
 * - A URL that names hints, or opens a flow other than the kept one, replaces
 *   what was kept with what it says (nothing, when it names none): an
 *   application that sends no hint never inherits another's.
 * - A URL of the flow already kept leaves it alone. The server adds the stored
 *   hints again on every page of the flow, and a choice the user made since
 *   must not be undone by the next redirect.
 * - The two parameters leave the address bar, so a reload after the user
 *   changed the language does not apply the application's again.
 */
export function captureEntryHints(pendingRequest: string | null, now: number = Date.now()): void {
  let url: URL
  try {
    url = new URL(window.location.href)
  } catch {
    return
  }

  const params = url.searchParams
  const namesHints = params.has(UI_LOCALES_PARAM) || params.has(UI_THEME_PARAM)
  const flow = pendingRequest ? digest(pendingRequest) : null
  const kept = read(now)

  const sameFlow = flow !== null && kept?.flow === flow
  if (!sameFlow && (namesHints || flow !== null)) {
    write({
      flow,
      expiresAt: now + FLOW_HINTS_LIFETIME_MS,
      language: resolveUiLocales(params.get(UI_LOCALES_PARAM)) ?? undefined,
      theme: resolveUiTheme(params.get(UI_THEME_PARAM)) ?? undefined,
    })
  }

  if (namesHints) {
    params.delete(UI_LOCALES_PARAM)
    params.delete(UI_THEME_PARAM)
    try {
      window.history.replaceState(window.history.state, "", url.toString())
    } catch {
      /* a sandboxed frame may refuse; the hints are kept either way */
    }
  }
}

/** The hints in force in this tab; empty when none is, or the last one expired. */
export function readFlowHints(now: number = Date.now()): FlowHints {
  const kept = read(now)
  return kept ? { language: kept.language, theme: kept.theme } : {}
}

/**
 * Ends one hint because the user chose for themself. The flow stays kept, so
 * the next redirect of the same flow does not bring the hint back.
 */
export function dropFlowHint(kind: keyof FlowHints, now: number = Date.now()): void {
  const kept = read(now)
  if (!kept || kept[kind] === undefined) return

  write({ ...kept, [kind]: undefined })
}
