import { describe, expect, it } from "vitest"

import { router } from "./routes"

type RouteNode = {
  path?: string
  index?: boolean
  lazy?: () => Promise<{ Component: unknown }>
  children?: RouteNode[]
}

function walk(
  routes: RouteNode[],
  trail: string[] = []
): Array<[string, RouteNode]> {
  return routes.flatMap((route) => {
    const here = [...trail, route.path ?? (route.index ? "(index)" : "")]
    return [
      [here.filter(Boolean).join("/") || "/", route] as [string, RouteNode],
      ...walk(route.children ?? [], here),
    ]
  })
}

/**
 * The thunk is captured here, not read off the route when the case runs.
 *
 * `router.routes` is live and the router consumes it: once it resolves a route
 * it assigns `Component` and clears `lazy`. In jsdom the document URL matches
 * the root, so the router resolves that chain on its own while these cases are
 * running, and a case that reached for `route.lazy` later found it gone.
 */
const lazyRoutes = walk(router.routes as RouteNode[])
  .filter(([, route]) => typeof route.lazy === "function")
  .map(([path, route]) => [path, route.lazy!] as const)

/**
 * Every lazy route resolves to a component that still exists.
 *
 * `lazyRoute` pairs a dynamic import with a named export, and nothing checks
 * that pairing until someone opens the page: rename the export or move the file
 * and the build stays green while the route renders undefined. Loading each one
 * here turns that into a test failure.
 */
describe("console routes", () => {
  it("has lazy routes to check", () => {
    expect(lazyRoutes.length).toBeGreaterThan(20)
  })

  // Generous, and only here: the first case pays for compiling a page chunk
  // and everything it imports, which is slow under coverage instrumentation.
  it.each(lazyRoutes)(
    "%s resolves to a component",
    async (_path, load) => {
      const resolved = await load()
      expect(resolved.Component).toBeTypeOf("function")
    },
    20_000
  )
})
