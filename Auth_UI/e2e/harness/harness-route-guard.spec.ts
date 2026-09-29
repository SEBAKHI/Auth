import { expect, test } from "./fixtures"

/**
 * Route interception anywhere on a harness page would make Playwright answer
 * CORS preflights itself and add permissive CORS headers to fulfilled responses,
 * silently turning every CSRF check into a pass. So the harness refuses it.
 */
test("route interception is refused by name on the page and the context", async ({ page, context }) => {
  const named = (owner: string, name: string) =>
    new RegExp(
      `${owner}\\.${name}\\(\\) was called in a harness test: harness pages must not use route interception; use the api fixture`
    )
  expect(() => page.route("**/*", async (route) => route.continue())).toThrow(named("page", "route"))
  expect(() => page.routeFromHAR("unused.har")).toThrow(named("page", "routeFromHAR"))
  expect(() => context.route("**/*", async (route) => route.continue())).toThrow(named("context", "route"))
  expect(() => context.routeFromHAR("unused.har")).toThrow(named("context", "routeFromHAR"))

  const later = await context.newPage()
  expect(() => later.route("**/*", async (route) => route.continue())).toThrow(named("page", "route"))
})
