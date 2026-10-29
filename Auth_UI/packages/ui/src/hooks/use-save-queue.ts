import * as React from "react"

/**
 * Runs a page's saves one after another, in the order they were asked for.
 *
 * A full-replace save resends every field it does not change, read when it is
 * sent. Two in flight at once would both read the stored copy before either
 * answered, and the second would send back what the first changed, or reach
 * the server first. Here each save starts only once the one before it has
 * settled, whatever its outcome, so it reads the answer the previous one wrote.
 */
export function useSaveQueue(): <T>(save: () => Promise<T>) => Promise<T> {
  const tail = React.useRef<Promise<unknown>>(Promise.resolve())
  return React.useCallback(<T,>(save: () => Promise<T>): Promise<T> => {
    const run = tail.current.then(save, save)
    tail.current = run.catch(() => undefined)
    return run
  }, [])
}
