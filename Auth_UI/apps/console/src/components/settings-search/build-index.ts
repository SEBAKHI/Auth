import type { TFunction } from "i18next"

import {
  SECTION_I18N,
  fieldI18nKey,
  formFieldName,
  type SystemSettingsSection,
} from "@/pages/system-settings/lib/sections"
import { STATIC_SURFACES, type StaticSurface } from "./static-surfaces"

/**
 * A page or section — something with its own place in the navigation.
 * Rendered full-size.
 */
export interface SurfaceEntry {
  kind: "surface"
  id: string
  title: string
  description: string
  route: string
  /**
   * Where the result lives, shown beside the title. Two entries can share a
   * name — "Sessions" is both a profile tab and a system-settings section —
   * and the title alone gives no way to tell them apart.
   */
  path: string
  /** Extra words to match on that are not shown, e.g. the raw config path. */
  keywords: string
}

/**
 * A single setting inside a section. Rendered smaller and indented under its
 * parent, which is the distinction between "a page" and "one option on it".
 */
export interface FieldEntry {
  kind: "field"
  id: string
  title: string
  description: string
  route: string
  keywords: string
  /** Section title, shown as the group heading above the field.  */
  sectionTitle: string
  /** Full trail to the section, used as the group heading. */
  sectionPath: string
  sectionId: string
}

export type SearchEntry = SurfaceEntry | FieldEntry

/** Joins a trail for display. `›` reads correctly in both text directions. */
export function joinPath(parts: (string | undefined)[]): string {
  return parts.filter(Boolean).join(" › ")
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

  for (const surface of STATIC_SURFACES as StaticSurface[]) {
    if (!hasPermission(surface.permission)) continue
    const title = t(surface.titleKey, { defaultValue: "" })
    if (!title) continue

    const path = joinPath(
      surface.pathKeys.map((key) => t(key, { defaultValue: "" }))
    )

    entries.push({
      kind: "surface",
      id: surface.id,
      title,
      description: surface.descriptionKey
        ? t(surface.descriptionKey, { defaultValue: "" })
        : "",
      route: surface.route,
      path,
      // The trail is searchable too: typing "profile" should surface the tabs
      // that live on it, not only the page itself.
      keywords: `${surface.id.replace(/-/g, " ")} ${path}`.toLowerCase(),
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
    const settingsRoot = t("nav.systemSettings", { defaultValue: "" })
    const groupLabel = section.group
      ? t(`systemSettings.groups.${section.group}`, { defaultValue: "" })
      : ""
    const sectionPath = joinPath([settingsRoot, groupLabel])

    entries.push({
      kind: "surface",
      id: `section:${sectionKey}`,
      title: sectionTitle,
      description: sectionI18n
        ? t(`systemSettings.${sectionI18n}.description`, { defaultValue: "" })
        : "",
      route,
      path: sectionPath,
      keywords: `${pathKeywords(sectionKey)} ${sectionPath}`.toLowerCase(),
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
        sectionPath: joinPath([settingsRoot, sectionTitle]),
        sectionId: sectionKey,
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
export function scoreEntry(entry: SearchEntry, query: string): number {
  const needle = query.trim().toLowerCase()
  if (!needle) return 0

  const title = entry.title.toLowerCase()
  if (title === needle) return 100
  if (title.startsWith(needle)) return 80
  if (title.includes(needle)) return 60
  if (entry.description.toLowerCase().includes(needle)) return 40
  if (entry.keywords.includes(needle)) return 20
  return 0
}

export interface SearchResults {
  surfaces: SurfaceEntry[]
  /** Field hits grouped under their section, in score order. */
  fieldGroups: {
    sectionId: string
    sectionTitle: string
    sectionPath: string
    fields: FieldEntry[]
  }[]
}

const MAX_SURFACES = 8
const MAX_FIELDS = 20

/** Filters and groups the index for one query. */
export function searchSettings(
  index: SearchEntry[],
  query: string
): SearchResults {
  const scored = index
    .map((entry) => ({ entry, score: scoreEntry(entry, query) }))
    .filter((hit) => hit.score > 0)
    .sort((a, b) => b.score - a.score || a.entry.title.localeCompare(b.entry.title))

  const surfaces = scored
    .filter((hit) => hit.entry.kind === "surface")
    .slice(0, MAX_SURFACES)
    .map((hit) => hit.entry as SurfaceEntry)

  const fields = scored
    .filter((hit) => hit.entry.kind === "field")
    .slice(0, MAX_FIELDS)
    .map((hit) => hit.entry as FieldEntry)

  const groups = new Map<string, SearchResults["fieldGroups"][number]>()
  for (const field of fields) {
    const group = groups.get(field.sectionId) ?? {
      sectionId: field.sectionId,
      sectionTitle: field.sectionTitle,
      sectionPath: field.sectionPath,
      fields: [],
    }
    group.fields.push(field)
    groups.set(field.sectionId, group)
  }

  return { surfaces, fieldGroups: [...groups.values()] }
}
