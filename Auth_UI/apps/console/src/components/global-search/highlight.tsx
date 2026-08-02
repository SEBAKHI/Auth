import * as React from "react"

import { matchRanges } from "./match-ranges"

/**
 * Marks the part of a result that answers what was typed.
 *
 * Without it a row that matched on its hint — or worse, on the invisible config
 * key — looks like it has no business being in the list. The tint is an alpha
 * so it composites over both the resting and the selected row background, and
 * the text keeps the colour of the line it sits in so it follows that line when
 * the row is selected. Weight is deliberately not used: it is already spent on
 * the title, and bold Arabic at this size closes its counters.
 *
 * Slices come from the original string, so casing is preserved, and the pieces
 * stay inline — an option's accessible name is the concatenation either way,
 * and bidi resolution is unchanged.
 */
export function Highlight({ text, query }: { text: string; query: string }) {
  const ranges = React.useMemo(() => matchRanges(text, query), [text, query])
  if (ranges.length === 0) return <>{text}</>

  const parts: React.ReactNode[] = []
  let cursor = 0
  ranges.forEach(([start, end], index) => {
    if (start > cursor) parts.push(text.slice(cursor, start))
    parts.push(
      <mark key={index} className="rounded-xs bg-primary/15 text-inherit">
        {text.slice(start, end)}
      </mark>
    )
    cursor = end
  })
  if (cursor < text.length) parts.push(text.slice(cursor))

  return <>{parts}</>
}
