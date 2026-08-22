/**
 * Unwraps an openapi-fetch result, throwing the API error so it can be caught
 * by React Query / try-catch and surfaced via `getErrorMessage`.
 *
 * The status is checked as well as `error`, because a failed response whose
 * body is empty or is not JSON - a gateway 502 serving an HTML page, a proxy
 * cutting a connection - leaves `error` unset. Returning that as data made the
 * failure silent: the caller received `undefined`, and a list rendered "no
 * results" for what was actually a broken request.
 */
export async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response?: Response }>
): Promise<T> {
  const { data, error, response } = await call
  if (error) throw error
  if (response && !response.ok) {
    // Shaped like ProblemDetails so it classifies the same way everything else
    // does; the reason text is never rendered, only the status is read.
    throw { status: response.status, title: response.statusText }
  }
  return data as T
}

/** Coerce the API's `number | string` numerics to a number for display/use. */
export function toNumber(value: number | string | null | undefined): number {
  if (value === null || value === undefined) return 0
  return typeof value === "string" ? Number(value) : value
}

/**
 * Mirrors the API's `SortDirection` enum, which the OpenAPI schema types as a
 * bare number (Asc = 0, Desc = 1) even though the binder also accepts names.
 */
export const SORT_ASC = 0
export const SORT_DESC = 1

/** Map a TanStack sorting entry to the API's sortBy/sortDirection params. */
export function toSortParams(
  sorting: ReadonlyArray<{ id: string; desc: boolean }>
): { sortBy?: string; sortDirection?: number } {
  const first = sorting[0]
  if (!first) return {}
  return { sortBy: first.id, sortDirection: first.desc ? SORT_DESC : SORT_ASC }
}

/** Page size used when walking every page for a full-dataset CSV export. */
export const EXPORT_PAGE_SIZE = 100
/** Safety ceiling so a runaway export can never lock the browser. */
export const EXPORT_MAX_ROWS = 50_000

/**
 * Walk a paginated list endpoint and collect every row, honoring whatever
 * filters `fetchPage` closes over. Stops on the last (short) page, once the
 * reported total is reached, or at {@link EXPORT_MAX_ROWS} as a safety cap.
 * Used to back the DataTable's full-dataset CSV export on server-paginated pages.
 */
export async function collectAllPages<T>(
  fetchPage: (
    pageNumber: number,
    pageSize: number
  ) => Promise<{ items: T[]; totalCount: number }>,
  options?: { pageSize?: number; maxRows?: number }
): Promise<T[]> {
  const pageSize = options?.pageSize ?? EXPORT_PAGE_SIZE
  const maxRows = options?.maxRows ?? EXPORT_MAX_ROWS
  const all: T[] = []
  let pageNumber = 1
  for (;;) {
    const { items, totalCount } = await fetchPage(pageNumber, pageSize)
    all.push(...items)
    if (
      items.length === 0 ||
      items.length < pageSize ||
      all.length >= totalCount ||
      all.length >= maxRows
    ) {
      break
    }
    pageNumber += 1
  }
  return all
}
