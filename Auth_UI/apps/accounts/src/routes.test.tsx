import { describe, expect, it } from "vitest"

import { MyApplicationsPage } from "@authsystem/account/pages/home/my-applications-page"
import { RequireAnonymous } from "@authsystem/auth/require-auth"
import { RequireMfaSatisfied } from "@authsystem/auth/require-mfa-satisfied"

import { router } from "./routes"

type RouteNode = {
  path?: string
  index?: boolean
  element?: { type?: unknown }
  handle?: { crumb?: { titleKey: string; href: string } }
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
describe("accounts routes", () => {
  it("has lazy routes to check", () => {
    expect(lazyRoutes.length).toBeGreaterThan(8)
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

  /**
   * Where the three sign-up screens sit relative to the anonymous guard.
   *
   * The first two are for visitors without a session and belong behind it.
   * The last one signs the person in as its outcome, and a guard that bounces
   * the authenticated would race that very transition — so it stands outside,
   * guarding itself. Moving any of the three is a one-line change that the
   * build would not notice.
   */
  it("keeps the first two sign-up screens behind the anonymous guard and the last one outside it", () => {
    const all = walk(router.routes as RouteNode[])
    const anonymous = all.find(
      ([, route]) => route.element?.type === RequireAnonymous
    )?.[1]
    expect(anonymous, "the RequireAnonymous layout route").toBeDefined()

    const guarded = (anonymous!.children ?? []).map((route) => route.path)
    expect(guarded).toContain("/register")
    expect(guarded).toContain("/register/verify")
    expect(guarded).not.toContain("/register/complete")

    const root = (router.routes as RouteNode[])[0]
    expect(root.children?.map((route) => route.path)).toContain(
      "/register/complete"
    )
  })

  /**
   * UI batch item 12: `/` is Home, the person's applications, and no longer a
   * redirect to the profile. It sits behind the second-factor guard like every
   * page but the profile, and its crumb is the trail's home.
   */
  it("opens on Home, the person's applications", async () => {
    const all = walk(router.routes as RouteNode[])
    const guard = all.find(([, route]) => route.element?.type === RequireMfaSatisfied)?.[1]
    expect(guard, "the RequireMfaSatisfied layout route").toBeDefined()

    const inside = walk(guard!.children ?? [])
    const home = inside.find(([, route]) => route.index)?.[1]
    expect(home, "an index route inside the guard").toBeDefined()
    expect(home!.handle?.crumb).toEqual(expect.objectContaining({ titleKey: "home", href: "/" }))

    const load = lazyRoutes.find(([path]) => path === "(index)")?.[1]
    expect(load, "the index route is lazy").toBeDefined()
    expect((await load!()).Component).toBe(MyApplicationsPage)
  })
})
