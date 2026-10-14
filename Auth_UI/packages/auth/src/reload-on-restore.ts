import * as React from "react"

/**
 * A page that holds a step of a sign-in asks the server again when the browser
 * brings it back from the back/forward cache, instead of coming back alive with
 * whatever it held when it was left (OI-150).
 *
 * The cache keeps the whole page, its scripts' memory included, and shows it
 * again on Back without a request. On a shared computer that is the previous
 * person's sign-in step, with their organizations listed and its buttons
 * working. Reloading sends the page through the server, which decides whether
 * the step may still be finished (the finish window after a fresh sign-in) or
 * the person must sign in again. It also clears every in-memory latch the page
 * left with.
 *
 * `Cache-Control: no-store` used to keep such pages out of the cache, but
 * browsers are relaxing that exclusion, so the reload is the guarantee.
 */
export function useReloadOnRestore(): void {
  React.useEffect(() => {
    const restored = (event: PageTransitionEvent) => {
      if (event.persisted) window.location.reload()
    }
    window.addEventListener("pageshow", restored)
    return () => window.removeEventListener("pageshow", restored)
  }, [])
}
