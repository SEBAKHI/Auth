import * as React from "react"

/** Returns a debounced copy of `value` that updates after `delay` ms of quiet. */
export function useDebouncedValue<T>(value: T, delay = 350): T {
  const [debounced, setDebounced] = React.useState(value)

  React.useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay)
    return () => window.clearTimeout(timer)
  }, [value, delay])

  return debounced
}
