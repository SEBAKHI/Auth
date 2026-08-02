import type { ColumnSizingState, VisibilityState } from "@tanstack/react-table"

import {
  fetchUiPreferences,
  putUiPreference,
} from "@authsystem/api/ui-preferences"

/** Everything a user can rearrange about one table. */
export interface TableLayout {
  cols?: VisibilityState
  size?: ColumnSizingState
  order?: string[]
}

/** Server key namespace; the API only accepts keys under this prefix. */
const SERVER_KEY_PREFIX = "table:"

/** One-shot marker for the pre-scoping key format. */
const MIGRATION_FLAG = "dt:migrated:v1"

const LEGACY_FIELDS = [
  ["dt:cols:", "cols"],
  ["dt:size:", "size"],
  ["dt:order:", "order"],
] as const

/** Debounce for the server write. A resize drag emits dozens of updates. */
const SYNC_DELAY_MS = 800

/**
 * The signed-in user, or null when nobody is.
 *
 * A module-level setter rather than a React context because `packages/auth`
 * imports `packages/ui`, so the dependency cannot run the other way. It also
 * keeps key composition in exactly one place instead of at each of the ~30
 * call sites that pass a `tableId`.
 */
let scope: string | null = null

/** Layouts the server has, applied over the local cache once they arrive. */
let hydrated = false

const listeners = new Map<string, Set<() => void>>()
const pending = new Map<string, TableLayout>()
let flushTimer: ReturnType<typeof setTimeout> | null = null

function storage(): Storage | null {
  try {
    return typeof window === "undefined" ? null : window.localStorage
  } catch {
    // Private mode can throw on access, not just on write.
    return null
  }
}

function localKey(tableId: string): string | null {
  return scope ? `dt:${scope}:${tableId}` : null
}

function parse(raw: string | null): TableLayout {
  if (!raw) return {}
  try {
    const parsed = JSON.parse(raw) as unknown
    return parsed && typeof parsed === "object" ? (parsed as TableLayout) : {}
  } catch {
    return {}
  }
}

function notify(tableId: string): void {
  for (const listener of listeners.get(tableId) ?? []) listener()
}

/**
 * Folds the pre-scoping keys into the first scope that reads after the upgrade,
 * then deletes them.
 *
 * Deleting is the point. The old keys were not namespaced by user, so two
 * accounts on one browser shared a layout; leaving them in place would preserve
 * exactly the leak this scoping exists to close. Attributing them to the first
 * user to sign in afterwards is the only attribution available, and it is a
 * column layout, not data.
 */
function migrateLegacyKeys(store: Storage, activeScope: string): void {
  if (store.getItem(MIGRATION_FLAG)) return

  const merged = new Map<string, TableLayout>()
  const stale: string[] = []

  for (let i = 0; i < store.length; i += 1) {
    const key = store.key(i)
    if (!key) continue
    for (const [prefix, field] of LEGACY_FIELDS) {
      if (!key.startsWith(prefix)) continue
      const tableId = key.slice(prefix.length)
      const value = parse(store.getItem(key))
      if (Object.keys(value).length > 0 || Array.isArray(value)) {
        const layout = merged.get(tableId) ?? {}
        merged.set(tableId, { ...layout, [field]: value })
      }
      stale.push(key)
    }
  }

  try {
    for (const [tableId, layout] of merged) {
      store.setItem(`dt:${activeScope}:${tableId}`, JSON.stringify(layout))
    }
    for (const key of stale) store.removeItem(key)
    store.setItem(MIGRATION_FLAG, "1")
  } catch {
    // Out of quota: leave the legacy keys alone and try again next session.
  }
}

/**
 * Binds table layouts to a user. Called wherever the auth context settles on
 * (or clears) the current user; passing null stops all persistence.
 *
 * Nothing is cleared on sign-out: the layout is meant to survive it. What the
 * scope prevents is the *next* account inheriting it.
 */
export function setDataTableScope(userId: string | null): void {
  if (scope === userId) return
  scope = userId
  hydrated = false
  pending.clear()

  const store = storage()
  if (store && userId) migrateLegacyKeys(store, userId)

  // Every mounted table re-reads under the new scope.
  for (const tableId of listeners.keys()) notify(tableId)

  if (userId) void hydrateFromServer()
}

/**
 * Pulls the server's copy and applies it over the local cache.
 *
 * localStorage stays the first-paint source because reads have to be
 * synchronous — waiting on the network would render the default layout and
 * then visibly rearrange it. The server is the cross-device source of truth,
 * so its values win once they arrive.
 */
export async function hydrateFromServer(): Promise<void> {
  const activeScope = scope
  if (!activeScope || hydrated) return
  hydrated = true

  const preferences = await fetchUiPreferences()
  // A slow response must not overwrite a different account's layouts.
  if (scope !== activeScope) return

  const store = storage()
  for (const [key, raw] of Object.entries(preferences)) {
    if (!key.startsWith(SERVER_KEY_PREFIX)) continue
    const tableId = key.slice(SERVER_KEY_PREFIX.length)
    // A table the user has already touched this session has a pending write
    // in flight; that edit is newer than what the server answered with.
    if (pending.has(tableId)) continue
    try {
      store?.setItem(`dt:${activeScope}:${tableId}`, raw)
    } catch {
      // Storage full; the layout still applies in memory for this session.
    }
    notify(tableId)
  }
}

function flush(): void {
  flushTimer = null
  if (!scope) return
  const writes = [...pending.entries()]
  pending.clear()
  for (const [tableId, layout] of writes) {
    void putUiPreference(`${SERVER_KEY_PREFIX}${tableId}`, JSON.stringify(layout))
  }
}

/** Reads the stored layout for one table. Returns {} when nothing applies. */
export function readTableLayout(tableId?: string): TableLayout {
  if (!tableId) return {}
  const key = localKey(tableId)
  const store = storage()
  if (!key || !store) return {}
  return parse(store.getItem(key))
}

/**
 * Records a layout change: written to localStorage immediately so a reload is
 * instant, and pushed to the server on a debounce so a resize drag does not
 * emit one request per pixel.
 */
export function writeTableLayout(
  tableId: string | undefined,
  layout: TableLayout
): void {
  if (!tableId) return
  const key = localKey(tableId)
  const store = storage()
  if (!key || !store) return

  try {
    store.setItem(key, JSON.stringify(layout))
  } catch {
    // Ignore storage failures (private mode / quota); the server copy still
    // gets the change, and this session keeps it in memory.
  }

  pending.set(tableId, layout)
  if (flushTimer) clearTimeout(flushTimer)
  flushTimer = setTimeout(flush, SYNC_DELAY_MS)
}

/** Subscribes to external layout changes (scope switch, server hydration). */
export function subscribeTableLayout(
  tableId: string,
  listener: () => void
): () => void {
  const set = listeners.get(tableId) ?? new Set()
  set.add(listener)
  listeners.set(tableId, set)
  return () => {
    set.delete(listener)
    if (set.size === 0) listeners.delete(tableId)
  }
}

if (typeof window !== "undefined") {
  // A tab closed mid-debounce would otherwise drop the last change.
  window.addEventListener("pagehide", () => {
    if (flushTimer) {
      clearTimeout(flushTimer)
      flush()
    }
  })
}

/** Test seam: resets module state between cases. */
export function __resetDataTableStorage(): void {
  scope = null
  hydrated = false
  pending.clear()
  listeners.clear()
  if (flushTimer) clearTimeout(flushTimer)
  flushTimer = null
}
