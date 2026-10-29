import { beforeEach, describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

import { themeScopeFor } from "./theme-scope"

/**
 * The first frame of an accounts page is painted with one scope's cached
 * appearance: the application of the address's pending request, validated as
 * every page validates it, or the platform's. An address that names an
 * application any other way (a client id alone, a request that fails
 * validation) is the platform's.
 */

const HANDLE = "0123456789abcdef".repeat(4)

beforeEach(() => {
  vi.spyOn(console, "warn").mockImplementation(() => {})
})

describe("themeScopeFor", () => {
  it("is the application of a stored request", () => {
    expect(themeScopeFor(`?client_id=crm&auth_request=${HANDLE}`)).toBe("crm")
  })

  it("is the application of a legacy authorize request on the API's origin", () => {
    const returnTo = "https://api.example.com/api/v1/auth/authorize?client_id=crm&response_type=code"
    expect(themeScopeFor(`?returnTo=${encodeURIComponent(returnTo)}`)).toBe("crm")
  })

  it.each([
    ["no request at all", ""],
    ["a client id alone (a sign-out page)", "?client_id=crm"],
    ["a malformed handle", "?client_id=crm&auth_request=not-a-handle"],
    ["a request on another origin", `?returnTo=${encodeURIComponent("https://evil.example/api/v1/auth/authorize?client_id=crm")}`],
    ["another path on the API's origin", `?returnTo=${encodeURIComponent("https://api.example.com/elsewhere?client_id=crm")}`],
  ])("is the platform's for %s", (_name, search) => {
    expect(themeScopeFor(search)).toBeNull()
  })
})
