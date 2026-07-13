import * as React from "react"

/**
 * Route metadata for the header breadcrumb bar. Attached to routes via the
 * react-router `handle` field and read with `useMatches()`.
 */
export interface CrumbHandle {
  crumb: {
    /** i18n key under `nav.*` for the section label. */
    titleKey: string
    /** List-page path the section label links to. */
    href: string
    /** True for `:id` detail routes — the record name comes from the override. */
    detail?: boolean
  }
}

/** Breadcrumb metadata: list pages label themselves, `:id` pages add a record crumb. */
export function crumb(
  titleKey: string,
  href: string,
  detail = false
): CrumbHandle {
  return { crumb: { titleKey, href, detail } }
}

/**
 * Detail pages publish their record's display name here once loaded; the
 * breadcrumb renders it as the final crumb. Cleared on unmount so a previous
 * page's name never leaks into the next one.
 */
let override: string | null = null

const listeners = new Set<() => void>()

function setOverride(name: string | null): void {
  if (name === override) return
  override = name
  listeners.forEach((listener) => listener())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useBreadcrumbOverride(): string | null {
  return React.useSyncExternalStore(subscribe, () => override)
}

/** Called by detail pages with the loaded record name (undefined while loading). */
export function usePageBreadcrumb(name: string | null | undefined): void {
  React.useEffect(() => {
    setOverride(name ?? null)
    return () => setOverride(null)
  }, [name])
}
