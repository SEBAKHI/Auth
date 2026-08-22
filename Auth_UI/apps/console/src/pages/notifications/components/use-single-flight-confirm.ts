import * as React from "react"

/**
 * Closes the event-to-render race in destructive confirmations. React Query's
 * isPending disables the button on the next render; two native click events can
 * arrive before that render and would otherwise start two mutations.
 */
export function useSingleFlightConfirm() {
  const active = React.useRef(false)

  const run = React.useCallback((submit: () => void) => {
    if (active.current) return false
    active.current = true
    submit()
    return true
  }, [])

  const release = React.useCallback(() => {
    active.current = false
  }, [])

  return { run, release }
}
