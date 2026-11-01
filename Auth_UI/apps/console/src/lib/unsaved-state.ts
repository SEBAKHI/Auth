import * as React from "react"

/** What a part of a page (a tab, a card) tells the page about edits it has not saved. */
export interface UnsavedState {
  isDirty: boolean
  isSaving: boolean
}

/**
 * Reports a part's unsaved state to the page, which holds the one leave-prompt for every part:
 * a router honours a single blocker at a time (the last one registered), so a prompt per part
 * would silently ignore every part but one.
 */
export function useReportUnsaved(
  report: ((state: UnsavedState) => void) | undefined,
  isDirty: boolean,
  isSaving: boolean
) {
  React.useEffect(() => {
    report?.({ isDirty, isSaving })
  }, [report, isDirty, isSaving])
  // A part that goes away leaves nothing unsaved behind it.
  React.useEffect(() => () => report?.({ isDirty: false, isSaving: false }), [report])
}

/**
 * The unsaved state of several parts, one reporter per part and their union: dirty while any
 * part is, saving while any part is. For the one owner of a prompt (or of a report upwards)
 * that several parts feed.
 */
export function useUnsavedParts<K extends string>(
  parts: readonly K[]
): { reporters: Record<K, (state: UnsavedState) => void>; combined: UnsavedState } {
  const [states, setStates] = React.useState<Partial<Record<K, UnsavedState>>>({})
  // The parts are a fixed list per owner: their reporters are made once and stay stable.
  const [reporters] = React.useState(
    () =>
      Object.fromEntries(
        parts.map((part) => [
          part,
          (state: UnsavedState) =>
            setStates((current) =>
              current[part]?.isDirty === state.isDirty && current[part]?.isSaving === state.isSaving
                ? current
                : { ...current, [part]: state }
            ),
        ])
      ) as Record<K, (state: UnsavedState) => void>
  )
  const values = Object.values(states) as (UnsavedState | undefined)[]
  const isDirty = values.some((state) => state?.isDirty)
  const isSaving = values.some((state) => state?.isSaving)
  const combined = React.useMemo(() => ({ isDirty, isSaving }), [isDirty, isSaving])
  return { reporters, combined }
}
