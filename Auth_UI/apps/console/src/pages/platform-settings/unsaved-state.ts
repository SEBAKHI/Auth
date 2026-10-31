import * as React from "react"

/** What a platform settings tab tells the page about edits it has not saved. */
export interface UnsavedState {
  isDirty: boolean
  isSaving: boolean
}

/**
 * Reports a tab's unsaved state to the page, which holds the one leave-prompt for every tab:
 * a router honours a single blocker at a time (the last one registered), so a prompt per tab
 * would silently ignore every tab but one.
 */
export function useReportUnsaved(
  report: ((state: UnsavedState) => void) | undefined,
  isDirty: boolean,
  isSaving: boolean
) {
  React.useEffect(() => {
    report?.({ isDirty, isSaving })
  }, [report, isDirty, isSaving])
  // A tab that goes away leaves nothing unsaved behind it.
  React.useEffect(() => () => report?.({ isDirty: false, isSaving: false }), [report])
}
