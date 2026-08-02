import { describe, expect, it } from "vitest"

import { en } from "@authsystem/i18n/locales/en"
import { PROFILE_TABS } from "@authsystem/account/pages/profile/profile-tabs"
import { TAB_QUERY_PARAM } from "@authsystem/ui/hooks/use-tab-param"

import { DASHBOARD_TABS } from "@/pages/dashboard/use-dashboard-window"
import { RECORD_SOURCES, recordIcon } from "./record-sources"
import { STATIC_SURFACES } from "./static-surfaces"

/**
 * The index is addressed by i18n key, and a wrong key fails silently.
 *
 * `buildSearchIndex` drops any surface whose title resolves to "" — which is
 * exactly what a mistyped or renamed key produces. The entry then becomes
 * unsearchable with no compile error, no runtime error and no failing test:
 * the palette simply never offers that page again. Nothing else catches this.
 * The catalog's own parity test only sees *literal* `t("…")` calls in source,
 * and every key here is read out of a table at runtime.
 */

type Tree = Record<string, unknown>

function leafAt(path: string): string | undefined {
  let node: unknown = en
  for (const part of path.split(".")) {
    if (typeof node !== "object" || node === null) return undefined
    node = (node as Tree)[part]
  }
  return typeof node === "string" ? node : undefined
}

/** Every key the index reads, paired with where it came from. */
const surfaceKeys = STATIC_SURFACES.flatMap((surface) => [
  { id: surface.id, key: surface.titleKey },
  ...(surface.descriptionKey
    ? [{ id: surface.id, key: surface.descriptionKey }]
    : []),
  ...(surface.altTitleKeys ?? []).map((key) => ({ id: surface.id, key })),
  ...surface.pathKeys.map((key) => ({ id: surface.id, key })),
])

describe("static surfaces", () => {
  it("names every title, description, alias and trail crumb with a real key", () => {
    const broken = surfaceKeys
      .filter(({ key }) => !leafAt(key))
      .map(({ id, key }) => `${id} → ${key}`)

    expect(broken).toEqual([])
  })

  it("gives every surface a unique id", () => {
    // cmdk keys its rows on the id, and two rows sharing one collapse into a
    // single selectable item.
    const ids = STATIC_SURFACES.map((surface) => surface.id)
    expect(ids).toEqual([...new Set(ids)])
  })

  it("points every surface at an absolute route", () => {
    const relative = STATIC_SURFACES.filter(
      (surface) => !surface.route.startsWith("/")
    ).map((surface) => surface.id)

    expect(relative).toEqual([])
  })

  it("sends one destination to one row", () => {
    // Two rows one click apart, differing only in wording, is the failure this
    // guards: a tab's label belongs in `altTitleKeys`, not in an entry of its
    // own. The exception is a page and a tab of that page, which are genuinely
    // different destinations and carry different routes.
    const routes = STATIC_SURFACES.map((surface) => surface.route)
    expect(routes).toEqual([...new Set(routes)])
  })

  it("only opens tabs the profile page will accept", () => {
    // The route is built here and validated there. A tab renamed on one side
    // produces a link that silently falls back to the first tab — the exact
    // half-truth this whole change set out to remove.
    const named = STATIC_SURFACES.map(
      (surface) => new URL(surface.route, "http://x").searchParams.get(TAB_QUERY_PARAM)
    ).filter((tab): tab is string => Boolean(tab))

    expect(named.length).toBeGreaterThan(0)
    for (const tab of named) {
      expect([...PROFILE_TABS, ...DASHBOARD_TABS]).toContain(tab)
    }
  })
})

describe("record sources", () => {
  it("heads every group with a real key", () => {
    const broken = RECORD_SOURCES.filter(
      (source) => !leafAt(source.headingKey)
    ).map((source) => `${source.key} → ${source.headingKey}`)

    expect(broken).toEqual([])
  })

  it("gives every source a unique key", () => {
    const keys = RECORD_SOURCES.map((source) => source.key)
    expect(keys).toEqual([...new Set(keys)])
  })

  it("hands every source somewhere to send the rest of its matches", () => {
    const broken = RECORD_SOURCES.filter(
      (source) => !source.listRoute.startsWith("/")
    ).map((source) => source.key)

    expect(broken).toEqual([])
  })

  it("recovers a row's icon from its id", () => {
    // A remembered record is re-rendered from storage, with no source to ask.
    expect(recordIcon("user:0d1f…")).toBe(
      RECORD_SOURCES.find((source) => source.key === "user")?.icon
    )
    expect(recordIcon("notification-layout:0d1f…")).toBe(
      RECORD_SOURCES.find((source) => source.key === "notification-layout")
        ?.icon
    )
    expect(recordIcon("nonsense")).toBeUndefined()
  })

  it("never runs two sources for the same entity at once", () => {
    // Organizations are reached two ways — every tenant for a platform admin,
    // your own for everyone else — and both live in the same list. If a
    // permission ever satisfied both, an admin who is also a member would see
    // their organizations twice.
    const platformAdmin = () => true
    const member = (permission: string | undefined) => permission === undefined

    for (const hasPermission of [platformAdmin, member]) {
      const active = RECORD_SOURCES.filter(
        (source) =>
          hasPermission(source.permission) &&
          !(source.deniedPermission && hasPermission(source.deniedPermission))
      )
      const headings = active.map((source) => source.headingKey)
      expect(headings).toEqual([...new Set(headings)])
    }
  })
})
