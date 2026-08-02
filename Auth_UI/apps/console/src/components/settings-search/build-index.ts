import type { TFunction } from "i18next"

import {
  SECTION_I18N,
  fieldI18nKey,
  formFieldName,
  type SystemSettingsSection,
} from "@/pages/system-settings/lib/sections"
import { STATIC_SURFACES, type StaticSurface } from "./static-surfaces"

/** Which visible text produced the hit. Drives the match explanation. */
export type MatchVia = "title" | "description" | "keywords"

interface BaseEntry {
  id: string
  title: string
  description: string
  route: string
  /** Extra words to match on that are not shown, e.g. the raw config path. */
  keywords: string
  /** Set by the search, not the index: how this row came to be here. */
  via?: MatchVia
}

/**
 * A page or section — something with its own place in the navigation.
 */
export interface SurfaceEntry extends BaseEntry {
  kind: "surface"
  /**
   * Where the result lives, shown under its title. Two entries can share a
   * name — "Sessions" is both a profile tab and a system-settings section —
   * and the title alone gives no way to tell them apart.
   *
   * Kept as separate crumbs rather than a joined string: rendered as one
   * string, a trail whose crumbs are all Latin resolves to LTR inside an
   * Arabic row and comes out back-to-front.
   */
  trail: string[]
}

/**
 * A single setting inside a section, grouped under its parent section.
 */
export interface FieldEntry extends BaseEntry {
  kind: "field"
  /** Section title, shown as the group heading above the field. */
  sectionTitle: string
  /** Full trail to the section, used as the group heading. */
  sectionTrail: string[]
  sectionId: string
  /** The raw config path, shown when that is the only reason this row matched. */
  configPath: string
}

export type SearchEntry = SurfaceEntry | FieldEntry

/**
 * Joins a trail for matching, not for display — the rendered trail keeps its
 * crumbs apart so each is its own bidi paragraph.
 */
export function joinPath(parts: (string | undefined)[]): string {
  return parts.filter(Boolean).join(" ")
}

/**
 * Splits a config path into searchable words: "Password:Argon2MemorySize"
 * becomes "password argon2 memory size".
 *
 * This is what makes the index usable in every language without shipping
 * every language. Only the active locale's labels are loaded at runtime — the
 * other five are code-split — so an admin running the console in Arabic who
 * knows the key name can still find the setting by typing it.
 */
export function pathKeywords(path: string): string {
  return path
    .split(":")
    .flatMap((segment) => segment.split(/(?=[A-Z])/))
    .join(" ")
    .toLowerCase()
}

/**
 * Builds the searchable index from the settings payload and the active
 * translation bundle.
 *
 * Runtime rather than generated: a build-time index needs a build step, goes
 * stale against the backend registry the moment a field is added, and cannot
 * carry per-field state. The payload is already in the query cache.
 */
export function buildSearchIndex(
  sections: SystemSettingsSection[],
  t: TFunction,
  hasPermission: (permission: string | undefined) => boolean
): SearchEntry[] {
  const entries: SearchEntry[] = []
  const settingsRoot = t("nav.systemSettings", { defaultValue: "" })

  for (const surface of STATIC_SURFACES as StaticSurface[]) {
    if (!hasPermission(surface.permission)) continue
    const title = t(surface.titleKey, { defaultValue: "" })
    if (!title) continue

    const trail = surface.pathKeys
      .map((key) => t(key, { defaultValue: "" }))
      .filter(Boolean)

    entries.push({
      kind: "surface",
      id: surface.id,
      title,
      description: surface.descriptionKey
        ? t(surface.descriptionKey, { defaultValue: "" })
        : "",
      route: surface.route,
      trail,
      // The trail is searchable too: typing "profile" should surface the tabs
      // that live on it, not only the page itself.
      keywords:
        `${surface.id.replace(/-/g, " ")} ${joinPath(trail)}`.toLowerCase(),
    })
  }

  // Registry results need no permission filter of their own: the payload is
  // only returned to holders of system-settings:manage, so a user without it
  // never has sections to index in the first place.
  for (const section of sections) {
    const sectionKey = section.key ?? ""
    const sectionI18n = SECTION_I18N[sectionKey]
    const sectionTitle = sectionI18n
      ? t(`systemSettings.${sectionI18n}.title`, { defaultValue: sectionKey })
      : sectionKey
    const route = `/admin/system-settings/${sectionKey}`
    const groupLabel = section.group
      ? t(`systemSettings.groups.${section.group}`, { defaultValue: "" })
      : ""
    const sectionOwnTrail = [settingsRoot, groupLabel].filter(Boolean)

    entries.push({
      kind: "surface",
      id: `section:${sectionKey}`,
      title: sectionTitle,
      description: sectionI18n
        ? t(`systemSettings.${sectionI18n}.description`, { defaultValue: "" })
        : "",
      route,
      trail: sectionOwnTrail,
      keywords:
        `${pathKeywords(sectionKey)} ${joinPath(sectionOwnTrail)}`.toLowerCase(),
    })

    for (const field of section.fields ?? []) {
      const path = field.path ?? ""
      if (!path) continue
      const base = sectionI18n
        ? `systemSettings.${sectionI18n}.${fieldI18nKey(path)}`
        : null

      entries.push({
        kind: "field",
        id: `${sectionKey}:${path}`,
        title: base ? t(base, { defaultValue: path }) : path,
        description: base ? t(`${base}Hint`, { defaultValue: "" }) : "",
        // The anchor the target row carries; see setting-field.tsx.
        route: `${route}?field=${encodeURIComponent(formFieldName(path))}`,
        keywords: pathKeywords(path),
        sectionTitle,
        // The group heading carries the whole trail, so each field row does
        // not have to repeat it.
        sectionTrail: [settingsRoot, sectionTitle].filter(Boolean),
        sectionId: sectionKey,
        configPath: `${sectionKey}:${path}`,
      })
    }
  }

  return entries
}

/**
 * Ranks one entry against a query. Higher is better; 0 means no match.
 *
 * Exact and prefix matches on the visible label outrank a hit buried in a
 * hint, and a hint outranks the invisible path keywords — so typing a word
 * that is literally a setting's name puts that setting first.
 */
export function scoreEntry(
  entry: SearchEntry,
  query: string
): { score: number; via: MatchVia } {
  const needle = query.trim().toLowerCase()
  if (!needle) return { score: 0, via: "title" }

  const title = entry.title.toLowerCase()
  if (title === needle) return { score: 100, via: "title" }
  if (title.startsWith(needle)) return { score: 80, via: "title" }
  if (title.includes(needle)) return { score: 60, via: "title" }
  if (entry.description.toLowerCase().includes(needle)) {
    return { score: 40, via: "description" }
  }
  if (entry.keywords.includes(needle)) return { score: 20, via: "keywords" }
  return { score: 0, via: "title" }
}

export interface FieldGroup {
  sectionId: string
  sectionTitle: string
  sectionTrail: string[]
  /** Capped unless the group was expanded. */
  fields: FieldEntry[]
  /** How many matched in total, so a capped group can say so. */
  totalFields: number
}

export interface SearchResults {
  surfaces: SurfaceEntry[]
  /** How many surfaces matched before the cap. */
  totalSurfaces: number
  /** Field hits grouped under their section, in score order. */
  fieldGroups: FieldGroup[]
  /** How many field groups matched before the cap. */
  totalFieldGroups: number
  /** Every match, capped or not — what the screen reader is told. */
  total: number
}

/**
 * Caps exist so the panel stays scannable, not to hide things: whatever they
 * drop is counted and offered behind a "show all" row, because a silently
 * truncated list is indistinguishable from an exhaustive one.
 */
export const MAX_SURFACES = 5
export const MAX_FIELD_GROUPS = 3
export const MAX_FIELDS_PER_GROUP = 5

const EMPTY: SearchResults = {
  surfaces: [],
  totalSurfaces: 0,
  fieldGroups: [],
  totalFieldGroups: 0,
  total: 0,
}

/** Filters and groups the index for one query. */
export function searchSettings(
  index: SearchEntry[],
  query: string,
  /** Section ids whose cap the user lifted for this query. */
  expandedGroups: ReadonlySet<string> = new Set()
): SearchResults {
  if (!query.trim()) return EMPTY

  const scored = index
    .map((entry) => ({ entry, ...scoreEntry(entry, query) }))
    .filter((hit) => hit.score > 0)
    .sort(
      (a, b) =>
        b.score - a.score ||
        a.entry.title.localeCompare(b.entry.title) ||
        // Two entries can share a score and a title; without a final tiebreak
        // they can swap places between keystrokes that changed nothing.
        a.entry.id.localeCompare(b.entry.id)
    )
    .map((hit) => ({ ...hit, entry: { ...hit.entry, via: hit.via } }))

  const matchedSurfaces = scored
    .filter((hit) => hit.entry.kind === "surface")
    .map((hit) => hit.entry as SurfaceEntry)

  const matchedFields = scored
    .filter((hit) => hit.entry.kind === "field")
    .map((hit) => hit.entry as FieldEntry)

  // Grouping happens before the cap so a group's count is the true total, and
  // insertion order follows the best-scoring field in each section.
  const groups = new Map<string, FieldGroup>()
  for (const field of matchedFields) {
    const group = groups.get(field.sectionId) ?? {
      sectionId: field.sectionId,
      sectionTitle: field.sectionTitle,
      sectionTrail: field.sectionTrail,
      fields: [],
      totalFields: 0,
    }
    group.fields.push(field)
    group.totalFields += 1
    groups.set(field.sectionId, group)
  }

  const fieldGroups = [...groups.values()]
    .slice(0, MAX_FIELD_GROUPS)
    .map((group) => ({
      ...group,
      fields: expandedGroups.has(group.sectionId)
        ? group.fields
        : group.fields.slice(0, MAX_FIELDS_PER_GROUP),
    }))

  return {
    surfaces: matchedSurfaces.slice(0, MAX_SURFACES),
    totalSurfaces: matchedSurfaces.length,
    fieldGroups,
    totalFieldGroups: groups.size,
    total: scored.length,
  }
}
