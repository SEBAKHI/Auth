import type { ComponentType } from "react"

import { Spinner } from "@authsystem/ui/spinner"

/**
 * What the router shows while it resolves a `lazy` route on a cold load.
 *
 * Without it react-router warns and renders nothing, so opening a deep link
 * straight from the address bar flashed a blank page until the chunk arrived.
 * Deliberately just a centred spinner: a skeleton here would guess at a layout the
 * router does not know yet.
 */
export function RouteFallback() {
  return (
    <div className="flex min-h-svh items-center justify-center">
      <Spinner className="size-6 text-muted-foreground" />
    </div>
  )
}

/**
 * Build a react-router `lazy` route from a dynamic import.
 *
 * Uses the router's own `lazy` property rather than `React.lazy` + `Suspense`: the
 * router awaits the module before it commits the navigation, so the previous screen
 * stays up instead of being replaced by a spinner — no layout flash on every click.
 *
 * The picker keeps this type-safe with named exports (this codebase has no default
 * page exports) and keeps each call to one line. Pass the import as a thunk so the
 * bundler still sees a static specifier and can split the chunk.
 */
export function lazyRoute<T>(
  load: () => Promise<T>,
  pick: (module: T) => ComponentType
) {
  return async () => ({ Component: pick(await load()) })
}
