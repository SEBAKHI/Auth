import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

import { launchApplication, launchTarget } from "./launch-application"

/**
 * OI-192: the one check between an accepted invitation's launch address and the
 * location bar. The server builds the address from the application's
 * registration; this is the second lock, so anything that is not an absolute
 * https address (or http on the development loopback host) is never followed.
 */

const assign = vi.fn()

beforeEach(() => {
  assign.mockClear()
  vi.stubGlobal("location", { assign, href: "https://accounts.example.com/" })
})

afterEach(() => {
  vi.unstubAllGlobals()
})

const followed = [
  "https://crm.example.com/signin/start?iss=https%3A%2F%2Fid.example.com",
  "https://crm.example.com/",
  "http://localhost:5173/login",
  "http://127.0.0.1:8080/login",
  "http://[::1]:8080/login",
]

const refused = [
  "https://user@crm.example.com/",
  "https://a:b@crm.example.com/",
  "https://crm.example.com@evil.example.com/signin/start",
  "https://crm.example.com/signin/start#fragment",
  "javascript:alert(1)",
  "JavaScript:alert(1)",
  "data:text/html,<script>alert(1)</script>",
  "http://crm.example.com/login",
  "//crm.example.com/login",
  "/organizations/1",
  "crm.example.com/login",
  "",
  null,
  undefined,
]

describe("launchTarget", () => {
  it("has cases on both sides", () => {
    expect(followed.length).toBeGreaterThan(0)
    expect(refused.length).toBeGreaterThan(0)
  })

  it.each(followed)("follows %s", (url) => {
    expect(launchTarget(url)).not.toBeNull()
  })

  it.each(refused)("refuses %s", (url) => {
    expect(launchTarget(url)).toBeNull()
  })
})

describe("launchApplication", () => {
  it("navigates the whole page to a followed address", () => {
    expect(launchApplication(followed[0])).toBe(true)
    expect(assign).toHaveBeenCalledWith(followed[0])
  })

  it.each(refused)("does nothing for %s", (url) => {
    expect(launchApplication(url)).toBe(false)
    expect(assign).not.toHaveBeenCalled()
  })
})
