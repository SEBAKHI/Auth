import { QueryClient } from "@tanstack/react-query"
import { describe, expect, it } from "vitest"

import authContextSource from "@authsystem/auth/auth-context.tsx?raw"

import { resetUserScopedCache } from "./query"

/**
 * The query client is a module singleton mounted ABOVE the auth provider, so it
 * survives every sign-out on the tab. Nothing about that is visible from any
 * single screen, which is why the leak lived: the symptom is the previous
 * account's avatar in the header, and the damage is the profile form seeding
 * its defaults from the previous account's row and writing them back on save.
 */
function seed(client: QueryClient) {
  client.setQueryData(["me"], { id: "alice", firstName: "Alice" })
  client.setQueryData(["users", { page: 1 }], [{ id: "alice" }])
  client.setQueryData(["sessions"], [{ id: "s1" }])
  client.setQueryData(["platform-branding"], { platformName: "Acme" })
  client.setQueryData(["external-providers"], { googleEnabled: true })
  client.setQueryData(["privacy-policy-version"], { version: 3 })
}

describe("resetUserScopedCache", () => {
  it("removes every query that belongs to the outgoing user", async () => {
    const client = new QueryClient()
    seed(client)

    await resetUserScopedCache(client)

    expect(client.getQueryData(["me"])).toBeUndefined()
    expect(client.getQueryData(["users", { page: 1 }])).toBeUndefined()
    expect(client.getQueryData(["sessions"])).toBeUndefined()
  })

  it("spares the anonymous queries, which belong to nobody", async () => {
    const client = new QueryClient()
    seed(client)

    await resetUserScopedCache(client)

    // Their providers sit above AuthProvider and stay mounted across a session
    // boundary, so a removed entry is refetched at once. Measured in a browser:
    // with this list emptied, one session-expired event re-requested branding,
    // the provider list and the branding logo image; with it restored, three
    // events produced zero requests. (It is NOT protection against a visual
    // flash — branding seeds itself from localStorage and never goes blank.)
    expect(client.getQueryData(["platform-branding"])).toEqual({
      platformName: "Acme",
    })
    expect(client.getQueryData(["external-providers"])).toBeDefined()
    expect(client.getQueryData(["privacy-policy-version"])).toBeDefined()
  })

  it("removes rather than invalidates, so no stale frame can render", async () => {
    const client = new QueryClient()
    client.setQueryData(["me"], { id: "alice" })

    await resetUserScopedCache(client)

    // An invalidated entry is still SERVED while it refetches; that frame is
    // precisely the one showing the wrong person.
    expect(client.getQueryCache().find({ queryKey: ["me"] })).toBeUndefined()
  })

  it("treats an unknown key as user-scoped", async () => {
    const client = new QueryClient()
    client.setQueryData(["something-added-later"], { secret: true })

    await resetUserScopedCache(client)

    // Fail closed: a new query is private until someone deliberately adds it to
    // the public list, so forgetting to classify one leaks nothing.
    expect(client.getQueryData(["something-added-later"])).toBeUndefined()
  })
})

/**
 * Source pin, following console-login-surface.test.ts.
 *
 * There is no runtime check that can catch a missing reset: the app behaves
 * correctly for one user, and the defect needs two accounts, one browser and
 * less than thirty seconds between them. Each of the four call sites is a
 * distinct way a session can end, and three of them are easy to overlook.
 */
describe("auth-context wiring", () => {
  it("resets the cache at all four session-boundary call sites", () => {
    const calls = authContextSource.match(/resetUserScopedCache\(queryClient\)/g)
    expect(calls).toHaveLength(4)
  })

  it("reads the client from context rather than importing the singleton", () => {
    // A test that renders AuthProvider with its own client must be able to
    // observe the reset; importing the singleton would silently bypass it.
    expect(authContextSource).toContain("useQueryClient()")
  })
})
