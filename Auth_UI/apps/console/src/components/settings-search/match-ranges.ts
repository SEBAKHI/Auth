/**
 * Finds every occurrence of every query word in `text`, merged and ordered.
 *
 * `indexOf` rather than a `RegExp`: the query is user input, so a regex would
 * have to escape metacharacters — a query containing "(" would throw and one
 * containing "." would quietly match everything.
 */
export function matchRanges(text: string, query: string): [number, number][] {
  const haystack = text.toLocaleLowerCase()
  const tokens = [
    ...new Set(
      query
        .toLocaleLowerCase()
        .split(/\s+/)
        .filter((token) => token.length > 0)
    ),
  ]

  const found: [number, number][] = []
  for (const token of tokens) {
    let from = 0
    for (;;) {
      const at = haystack.indexOf(token, from)
      if (at === -1) break
      found.push([at, at + token.length])
      from = at + token.length
    }
  }
  if (found.length === 0) return found

  found.sort((a, b) => a[0] - b[0] || a[1] - b[1])
  const merged: [number, number][] = [found[0]]
  for (const [start, end] of found.slice(1)) {
    const last = merged[merged.length - 1]
    if (start <= last[1]) last[1] = Math.max(last[1], end)
    else merged.push([start, end])
  }
  return merged
}
