/** The trailing control cell. It is never data, so it never moves. */
export const ACTIONS_COLUMN_ID = "actions"

/** Drops the pinned actions column, which no reordering operation may touch. */
function movableOf(order: string[]): string[] {
  return order.filter((id) => id !== ACTIONS_COLUMN_ID)
}

/** Re-appends the actions column, if the input had one, after a rearrangement. */
function withActions(order: string[], hadActions: boolean): string[] {
  return hadActions ? [...order, ACTIONS_COLUMN_ID] : order
}

/**
 * Reconciles a persisted order with the columns the table actually has.
 *
 * A stored order is *always* partial: `buildDisplayColumns` discovers columns
 * from the API payload, so the set changes whenever the response shape does,
 * and a stored order can also name columns a later release removed. Two
 * invariants keep a stale entry from corrupting the grid:
 *
 * 1. Ids that no longer exist are dropped, and ids the store has never seen
 *    keep their natural slot — only the slots held by known columns are
 *    rearranged among themselves. A newly discovered field therefore appears
 *    where the page put it, not bolted onto the end.
 * 2. `actions` is forced last.
 */
export function resolveColumnOrder(
  naturalIds: string[],
  storedOrder: string[]
): string[] {
  const natural = new Set(naturalIds)
  const known = storedOrder.filter((id) => natural.has(id))
  if (known.length === 0) return withActions(movableOf(naturalIds), naturalIds.includes(ACTIONS_COLUMN_ID))

  const knownIds = new Set(known)
  let next = 0
  const merged = naturalIds.map((id) => (knownIds.has(id) ? known[next++] : id))
  return withActions(movableOf(merged), merged.includes(ACTIONS_COLUMN_ID))
}

/**
 * Moves one column by `delta` slots — the keyboard and screen-reader path,
 * driven by the up/down buttons in the columns menu. Out-of-range moves are
 * no-ops rather than clamps, so a button at either end simply does nothing.
 */
export function moveColumn(
  order: string[],
  columnId: string,
  delta: number
): string[] {
  const movable = movableOf(order)
  const from = movable.indexOf(columnId)
  if (from === -1) return order

  const to = from + delta
  if (to < 0 || to >= movable.length) return order

  const next = [...movable]
  next.splice(to, 0, next.splice(from, 1)[0])
  return withActions(next, order.includes(ACTIONS_COLUMN_ID))
}

/**
 * Drops `draggedId` onto the slot `targetId` occupies — the pointer path.
 *
 * Deliberately expressed as "take this column out, put it back at that column's
 * index" rather than as a before/after decision from pointer geometry: index
 * arithmetic has no left or right, so the same code is correct under RTL.
 */
export function reorderColumn(
  order: string[],
  draggedId: string,
  targetId: string
): string[] {
  const movable = movableOf(order)
  const from = movable.indexOf(draggedId)
  const to = movable.indexOf(targetId)
  if (from === -1 || to === -1 || from === to) return order

  const next = [...movable]
  next.splice(to, 0, next.splice(from, 1)[0])
  return withActions(next, order.includes(ACTIONS_COLUMN_ID))
}

/** 1-based position of a column among the movable ones, for announcements. */
export function columnPosition(
  order: string[],
  columnId: string
): { position: number; total: number } {
  const movable = movableOf(order)
  return { position: movable.indexOf(columnId) + 1, total: movable.length }
}
