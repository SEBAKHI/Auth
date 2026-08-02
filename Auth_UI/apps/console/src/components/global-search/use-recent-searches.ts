import * as React from "react"

/**
 * What the panel remembers about somewhere you went.
 *
 * `label` is the one piece of display text stored rather than resolved. Pages
 * and settings are looked up in the live index by id, so they follow a language
 * switch and disappear when a permission is revoked. A record has no index to
 * look up — re-resolving one would mean a request per remembered row on every
 * open — so its name is kept here, and it is the only kind whose title can go
 * stale after a rename.
 */
export interface RecentEntry {
  /** The index entry id, so a stale one can be dropped on read. */
  id: string
  route: string
  /** Set for records only; ignored when the id resolves in the index. */
  label?: string
  sublabel?: string
}

const MAX_RECENT = 5

/**
 * Per-account, because two admins sharing a browser profile should not read
 * each other's history out of the search panel.
 *
 * The name still says `settingsSearch` after the palette outgrew settings, and
 * deliberately so: `label` was added to an existing shape rather than replacing
 * it, so every entry written by the settings-only version still resolves. A
 * tidier string would buy nothing and silently empty everyone's history.
 */
function storageKey(userId: string | undefined): string {
  return `authsystem.settingsSearch.recent.${userId ?? "anonymous"}`
}

/**
 * Storage may be unavailable — private mode, a blocked third-party context, a
 * test environment with no `localStorage` at all. Losing the history is not
 * worth throwing over, so every access is best-effort.
 */
function read(key: string): RecentEntry[] {
  try {
    const raw = globalThis.localStorage?.getItem(key)
    if (!raw) return []
    const parsed: unknown = JSON.parse(raw)
    if (!Array.isArray(parsed)) return []
    return parsed
      .filter(
        (item): item is RecentEntry =>
          typeof item === "object" &&
          item !== null &&
          typeof (item as RecentEntry).id === "string" &&
          typeof (item as RecentEntry).route === "string"
      )
      .slice(0, MAX_RECENT)
  } catch {
    return []
  }
}

function write(key: string, value: RecentEntry[]): void {
  try {
    globalThis.localStorage?.setItem(key, JSON.stringify(value))
  } catch {
    // Quota or no storage: the list is a convenience, not state we own.
  }
}

/**
 * The five places this admin opened from the search most recently, newest
 * first.
 *
 * An empty search panel that only says "type to search" teaches nothing and
 * offers nothing; every palette worth copying opens onto what you last did.
 * Ordering is plain most-recently-used rather than a frecency score — the list
 * is five items long, so a weighting scheme would be unobservable.
 */
export function useRecentSearches(userId: string | undefined) {
  const key = storageKey(userId)
  // Storage is the single copy; React holds only a revision counter to re-read
  // after a write. Mirroring the list into state would need an effect to
  // re-sync it whenever the account changes, and that effect is the classic
  // cascading render.
  const [revision, setRevision] = React.useState(0)
  const recent = React.useMemo(
    () => read(key),
    // The counter is the dependency: it is what says the store moved.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [key, revision]
  )

  const remember = React.useCallback(
    (entry: RecentEntry) => {
      const next = [
        entry,
        ...read(key).filter((item) => item.id !== entry.id),
      ].slice(0, MAX_RECENT)
      write(key, next)
      setRevision((previous) => previous + 1)
    },
    [key]
  )

  const clear = React.useCallback(() => {
    write(key, [])
    setRevision((previous) => previous + 1)
  }, [key])

  return { recent, remember, clear }
}
