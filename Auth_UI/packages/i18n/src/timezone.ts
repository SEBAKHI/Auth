import * as React from "react"

/**
 * Active display time zone for all date/time rendering.
 *
 * The profile column defaults to "UTC" (NOT NULL), so that stored value is
 * treated as "automatic": dates render in the browser's time zone. A user who
 * wants literal UTC picks "Etc/UTC" from the IANA list instead.
 */
let activeTimeZone: string | null = null

const listeners = new Set<() => void>()

/** True when the identifier is a time zone the runtime can format with. */
export function isValidTimeZone(timeZone: string): boolean {
  try {
    new Intl.DateTimeFormat("en", { timeZone })
    return true
  } catch {
    return false
  }
}

/**
 * Maps the stored profile value to an explicit zone, or null for automatic.
 * "UTC" (the column default) and invalid legacy free-text values both fall
 * back to automatic so formatting never breaks on bad data.
 */
function normalize(timeZone: string | null | undefined): string | null {
  if (!timeZone || timeZone === "UTC") return null
  return isValidTimeZone(timeZone) ? timeZone : null
}

export function setActiveTimeZone(timeZone: string | null | undefined): void {
  const next = normalize(timeZone)
  if (next === activeTimeZone) return
  activeTimeZone = next
  listeners.forEach((listener) => listener())
}

/** The zone all dates render in: the explicit profile zone or the browser's. */
export function getActiveTimeZone(): string {
  return activeTimeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Reactive variant for components that must re-render on zone changes. */
export function useActiveTimeZone(): string {
  return React.useSyncExternalStore(subscribe, getActiveTimeZone)
}
