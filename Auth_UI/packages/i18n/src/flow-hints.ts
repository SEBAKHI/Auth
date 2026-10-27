import { isLanguageCode, type LanguageCode } from "./languages"

/**
 * The look an application asked for when it sent its user here: its language
 * (`ui_locales`, OpenID Connect) and its colour mode (`ui_theme`).
 *
 * The authorize and end-session endpoints resolve the two and add them to the
 * accounts page they redirect to; an application linking straight to an
 * accounts page may add them itself. They hold for the rest of that flow in
 * this tab — across the sign-in pages and their interstitials, a reload, and the
 * "expired" page — and are written to nothing the user owns: not the stored
 * language or theme, not the profile. A choice the user makes here ends the
 * hint it overrides, and a page of another flow ends them all.
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

/** A language tag's characters: ASCII letters, digits and hyphens (RFC 5646). */
const TAG_PATTERN = /^[A-Za-z0-9-]+$/

/**
 * How long hints last after the entry that wrote them: the default lifetime of a
 * pending authorize request (`IdentityProvider:AuthorizationRequestLifetimeMinutes`).
 * Past it there is no flow left for them to belong to. Later pages of the same
 * flow keep them without extending this.
 */
export const FLOW_HINTS_LIFETIME_MS = 60 * 60 * 1000

interface StoredHints extends FlowHints {
  /** The flow the hints came with, or null for a direct link or a sign-out. */
  flow: string | null
  /** The application the entry named (`client_id`), or null when it named none. */
  client: string | null
  /**
   * The flow's pages' address, without the two hints, as a digest: every page
   * of one flow carries the same query once the hints are taken off. Read by the
   * boot script in <head> (`packages/ui/src/theme/boot-theme.ts`), which cannot
   * read the request's shape, to tell a page of the kept flow from any other.
   */
  marker?: string
  expiresAt: number
}

/**
 * The first supported language among the space-separated tags, in the order
 * sent; `en` when tags were sent and none is supported; null when the
 * parameter is absent or holds no tag.
 */
export function resolveUiLocales(raw: string | null | undefined): LanguageCode | null {
  if (raw == null) return null

  const tags = raw.split(" ").filter((tag) => tag !== "")
  if (tags.length === 0) return null

  for (const tag of tags.slice(0, MAX_LOCALE_TAGS)) {
    if (tag.length > MAX_LOCALE_TAG_LENGTH) continue
    const language = primaryLanguage(tag)
    if (language) return language
  }

  return "en"
}

/**
 * A tag's language part when it is a well-formed tag of a supported language:
 * the part before the first hyphen, lower-cased (the server's
 * `PresentationHints` over `Languages.Normalize`).
 */
function primaryLanguage(tag: string): LanguageCode | null {
  if (!TAG_PATTERN.test(tag)) return null

  const lower = tag.toLowerCase()
  const dash = lower.indexOf("-")
  const primary = dash > 0 ? lower.slice(0, dash) : lower
  return isLanguageCode(primary) ? primary : null
}

/** The colour mode when `raw` names one exactly; null otherwise. */
export function resolveUiTheme(raw: string | null | undefined): ThemeHint | null {
  return HINT_THEMES.find((theme) => theme === raw) ?? null
}

/**
 * FNV-1a, 32 bits: a short, stable name for the pending request a page serves,
 * and for the address of its pages. Enough to tell the flows of one tab apart,
 * and it keeps the request's handle itself out of storage. The boot script
 * computes the same digest of the address (`boot-theme.ts`); a test holds the two.
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
      client: typeof value.client === "string" ? value.client : null,
      marker: typeof value.marker === "string" ? value.marker : undefined,
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
    // Storage refused: nothing is kept, so the page shows as it would without
    // hints. Never an error the person sees.
  }
}

function clear(): void {
  try {
    sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // Nothing could be kept either.
  }
}

/**
 * Whether an accounts page, reached with no hint and no pending request in its
 * address, continues the flow whose hints are kept:
 *
 * - `"always"`: a page that exists only inside a sign-in already under way,
 *   reached from another page of it with the request in router state (the
 *   second factor, a forced password change, the email check); a reload of it
 *   is still that flow.
 * - `"same-client"`: a page an endpoint sends the browser to with the
 *   application's `client_id` (the expired request, the sign-out pages); it
 *   continues the kept flow only when it names the same application.
 * - `"never"`: any other page: a direct visit, a link from an email. It ends
 *   the kept hints, so an application that sends none never inherits another's.
 */
export type EntryContinuation = "always" | "same-client" | "never"

/** What the accounts app knows about the page the tab arrived at. */
export interface EntryContext {
  /**
   * The pending authorize request the page serves, as `return-to.ts` (the one
   * module that knows its shapes) reads it off the address; null when none.
   */
  pendingRequest: string | null
  /** The `client_id` the address names, or null. */
  clientId: string | null
  /** How a page with no hint and no pending request relates to the kept flow. */
  continuation: EntryContinuation
}

/**
 * Reads the hints off the address the tab arrived at, and removes them from it.
 * Call once, before anything reads the address: the router snapshots it when
 * it is created, so this must run before the router module is evaluated.
 *
 * - A page of the flow already kept leaves it alone, whatever its address
 *   names: the server adds the stored hints to every page of the flow, and a
 *   choice the user made since must not be undone by the next redirect.
 * - A page of another flow, or one that names hints and serves no flow,
 *   replaces what was kept with what it says (nothing, when it names none).
 * - Any other page keeps or ends the kept hints by its `continuation`.
 * - The two parameters leave the address bar, so a reload after the user
 *   changed the language does not apply the application's again.
 */
export function captureEntryHints(entry: EntryContext, now: number = Date.now()): void {
  let url: URL
  try {
    url = new URL(window.location.href)
  } catch {
    return
  }

  const params = url.searchParams
  const namesHints = params.has(UI_LOCALES_PARAM) || params.has(UI_THEME_PARAM)
  const flow = entry.pendingRequest ? digest(entry.pendingRequest) : null
  const kept = read(now)
  const rest = new URLSearchParams(params)
  rest.delete(UI_LOCALES_PARAM)
  rest.delete(UI_THEME_PARAM)

  if (flow !== null || namesHints) {
    if (flow === null || kept?.flow !== flow) {
      const language = resolveUiLocales(params.get(UI_LOCALES_PARAM)) ?? undefined
      const theme = resolveUiTheme(params.get(UI_THEME_PARAM)) ?? undefined
      // A flow is kept even without hints, so a choice made on its pages
      // survives its later redirects; a direct link without a usable hint
      // keeps nothing.
      if (flow !== null || language || theme) {
        write({
          flow,
          client: entry.clientId,
          marker: flow !== null ? digest(rest.toString()) : undefined,
          expiresAt: now + FLOW_HINTS_LIFETIME_MS,
          language,
          theme,
        })
      } else {
        clear()
      }
    }
  } else if (kept && !continuesKeptFlow(entry, kept)) {
    clear()
  }

  if (namesHints) {
    params.delete(UI_LOCALES_PARAM)
    params.delete(UI_THEME_PARAM)
    try {
      window.history.replaceState(window.history.state, "", url.toString())
    } catch {
      // A sandboxed frame may refuse. The address keeps the parameters, and a
      // reload reads them again as this entry did.
    }
  }
}

function continuesKeptFlow(entry: EntryContext, kept: StoredHints): boolean {
  switch (entry.continuation) {
    case "always":
      return true
    case "same-client":
      return kept.client === entry.clientId
    case "never":
      return false
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
