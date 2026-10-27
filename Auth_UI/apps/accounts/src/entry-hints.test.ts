import { describe, expect, it } from "vitest"

import { entryContextFor } from "./entry-hints"

/**
 * Every accounts route, and what an arrival on it without a hint or a pending
 * request in its address does to the hints kept for this tab (flow-hints.ts):
 * "always" keeps them (an interstitial of the sign-in under way), "same-client"
 * keeps them for the same application only, "never" ends them.
 */
const ROUTES: [string, "always" | "same-client" | "never"][] = [
  ["/", "never"],
  ["/profile", "never"],
  ["/organizations", "never"],
  ["/organizations/abc", "never"],
  ["/login", "never"],
  ["/register", "never"],
  ["/register/verify", "never"],
  ["/register/complete", "always"],
  ["/forgot-password", "never"],
  ["/reset-password", "never"],
  ["/delete-account", "never"],
  ["/deletion-scheduled", "never"],
  ["/force-password-change", "always"],
  ["/two-factor", "always"],
  ["/two-factor/required", "always"],
  ["/verify-email", "always"],
  ["/account-recovery", "always"],
  ["/accept-invitation", "never"],
  ["/create-organization", "never"],
  ["/sign-in-expired", "same-client"],
  ["/logout", "same-client"],
  ["/signed-out", "same-client"],
  ["/no-such-page", "never"],
]

describe("entryContextFor", () => {
  it("covers every route the accounts app declares", () => {
    expect(ROUTES.length).toBeGreaterThan(20)
  })

  it.each(ROUTES)("%s continues the kept flow: %s", (pathname, continuation) => {
    expect(entryContextFor({ pathname, search: "" }).continuation).toBe(continuation)
    expect(entryContextFor({ pathname: `${pathname}/`, search: "" }).continuation).toBe(
      pathname === "/" ? "never" : continuation
    )
  })

  it("reads the pending request through return-to.ts, and the application it names", () => {
    const handle = "a".repeat(64)

    const context = entryContextFor({
      pathname: "/login",
      search: `?client_id=CRM&auth_request=${handle}&ui_locales=ar`,
    })

    expect(context.pendingRequest).toMatch(/\/api\/v1\/auth\/authorize\/resume\?client_id=CRM&auth_request=a{64}$/)
    expect(context.clientId).toBe("CRM")
  })

  it("names no request for a page that carries none", () => {
    expect(entryContextFor({ pathname: "/signed-out", search: "?client_id=CRM&state=s" })).toEqual({
      pendingRequest: null,
      clientId: "CRM",
      continuation: "same-client",
    })
  })
})
