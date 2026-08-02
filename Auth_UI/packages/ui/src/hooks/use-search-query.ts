import * as React from "react"
import { useSearchParams } from "react-router-dom"

/** The parameter a search hands a list page on the way in. */
export const SEARCH_QUERY_PARAM = "q"

/**
 * The search term a list page was opened with, consumed once.
 *
 * The command palette shows the first few matches for a query and then hands
 * the rest to the page that owns them — without this, "and 132 more" is a
 * number with nowhere to go. The page seeds its own search box from the return
 * value and owns the term from then on.
 *
 * The parameter is stripped as it is read, for two reasons. A URL still saying
 * `?q=ahmed` while the box has been retyped to something else is a lie about
 * what is on screen. And leaving it would make Back re-apply a filter the user
 * had already cleared.
 *
 * Returns a value on the first render, not after an effect: a page that seeds
 * `useState` from it must have the term before its first query goes out.
 */
export function useSearchHandoff(
  param: string = SEARCH_QUERY_PARAM
): string {
  const [params, setParams] = useSearchParams()
  // Captured once. `params` changes as the parameter is stripped, and re-reading
  // it would hand the page an empty string on the very next render.
  const [initial] = React.useState(() => params.get(param) ?? "")

  React.useEffect(() => {
    if (!initial) return
    setParams(
      (current) => {
        const merged = new URLSearchParams(current)
        merged.delete(param)
        return merged
      },
      { replace: true }
    )
    // Runs once: the term is already captured, and re-running on every
    // `setParams` identity change would fight any other parameter the page owns.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return initial
}
