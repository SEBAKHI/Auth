import * as React from "react"
import { useSearchParams } from "react-router-dom"

/** The parameter every tabbed page names its open tab with. */
export const TAB_QUERY_PARAM = "tab"

/**
 * The active tab, held in the URL rather than in component state.
 *
 * A tab that lives in state is a place with no address: it cannot be linked,
 * Back walks past it to the previous page, a reload drops the reader back on
 * the first tab, and — the reason this exists — a search result cannot point at
 * it. The command palette indexes tabs as destinations, and a destination has
 * to be reachable by URL or the result is a half-truth: it opens the page and
 * leaves the reader to find the tab themselves.
 *
 * The default tab writes no parameter, so the canonical URL of a page is still
 * the bare path and the palette does not offer two rows for one destination.
 * Written with `replace`, because flicking through tabs is looking around, not
 * navigating — nobody wants six Back presses to leave a page they visited once.
 *
 * `tabs` is the allow-list. A URL naming a tab that does not exist — a stale
 * link, a renamed tab, a hand-typed guess — falls back to the default instead
 * of rendering a page with every panel closed.
 */
export function useTabParam(
  tabs: readonly string[],
  defaultTab: string = tabs[0],
  param: string = TAB_QUERY_PARAM
): [string, (next: string) => void] {
  const [params, setParams] = useSearchParams()
  const requested = params.get(param)
  const active = requested && tabs.includes(requested) ? requested : defaultTab

  const setActive = React.useCallback(
    (next: string) => {
      setParams(
        (current) => {
          const merged = new URLSearchParams(current)
          if (next === defaultTab) merged.delete(param)
          else merged.set(param, next)
          return merged
        },
        { replace: true }
      )
    },
    [defaultTab, param, setParams]
  )

  return [active, setActive]
}
