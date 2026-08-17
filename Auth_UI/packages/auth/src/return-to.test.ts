import { beforeEach, describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

import { getReturnToClientId, getValidReturnTo, validateReturnToUrl } from "./return-to"

const AUTHORIZE = "https://api.example.com/api/v1/auth/authorize"

function search(returnTo: string): string {
  return `?returnTo=${encodeURIComponent(returnTo)}`
}

beforeEach(() => {
  vi.spyOn(console, "warn").mockImplementation(() => {})
})

describe("validateReturnToUrl", () => {
  it("accepts the authorize endpoint on the API origin", () => {
    expect(validateReturnToUrl(`${AUTHORIZE}?client_id=app`)).toBe(
      `${AUTHORIZE}?client_id=app`
    )
  })

  it("accepts any API version", () => {
    const versioned = "https://api.example.com/api/v7/auth/authorize?client_id=app"
    expect(validateReturnToUrl(versioned)).toBe(versioned)
  })

  it.each([
    ["a foreign origin", "https://evil.example.com/api/v1/auth/authorize"],
    ["a lookalike origin", "https://api.example.com.evil.test/api/v1/auth/authorize"],
    ["a different port", "https://api.example.com:8443/api/v1/auth/authorize"],
    ["another endpoint on the API", "https://api.example.com/api/v1/auth/login"],
    ["a path below authorize", "https://api.example.com/api/v1/auth/authorize/evil"],
    ["a protocol-relative URL", "//evil.example.com/api/v1/auth/authorize"],
    ["a javascript: URL", "javascript:alert(1)"],
    ["a data: URL", "data:text/html,<script>alert(1)</script>"],
    ["a relative path", "/api/v1/auth/authorize"],
    ["nonsense", "not a url at all"],
  ])("rejects %s", (_label, raw) => {
    expect(validateReturnToUrl(raw)).toBeNull()
  })

  it.each([null, undefined, ""])("rejects the empty value %s", (raw) => {
    expect(validateReturnToUrl(raw)).toBeNull()
  })

  it("strips the step-up parameters that would loop the browser", () => {
    // The authorize endpoint sends prompt=login back in returnTo and re-demands
    // step-up on sight of it. The interactive sign-in about to happen IS the
    // fresh authentication it wants, so carrying them home would loop forever.
    const result = validateReturnToUrl(
      `${AUTHORIZE}?client_id=app&prompt=login&max_age=0&state=xyz`
    )

    expect(result).not.toBeNull()
    const params = new URL(result!).searchParams
    expect(params.get("prompt")).toBeNull()
    expect(params.get("max_age")).toBeNull()
    expect(params.get("state")).toBe("xyz")
  })

  it("preserves every parameter the relying party depends on", () => {
    // Deleting a search param re-serializes the whole query string, so this
    // pins that the values a relying party will match on survive the trip.
    const result = validateReturnToUrl(
      `${AUTHORIZE}?response_type=code&client_id=app` +
        `&redirect_uri=${encodeURIComponent("https://rp.example.com/cb?tenant=1")}` +
        `&code_challenge=abc-_123&code_challenge_method=S256` +
        `&state=${encodeURIComponent("a b/c~d")}&scope=openid+profile`
    )

    const params = new URL(result!).searchParams
    expect(params.get("response_type")).toBe("code")
    expect(params.get("client_id")).toBe("app")
    expect(params.get("redirect_uri")).toBe("https://rp.example.com/cb?tenant=1")
    expect(params.get("code_challenge")).toBe("abc-_123")
    expect(params.get("code_challenge_method")).toBe("S256")
    expect(params.get("state")).toBe("a b/c~d")
  })

  it("is idempotent, so re-validating a threaded value is safe", () => {
    const once = validateReturnToUrl(`${AUTHORIZE}?client_id=app&prompt=login`)
    expect(validateReturnToUrl(once)).toBe(once)
  })
})

describe("getValidReturnTo", () => {
  it("reads the returnTo parameter of the login page", () => {
    expect(getValidReturnTo(search(`${AUTHORIZE}?client_id=app`))).toBe(
      `${AUTHORIZE}?client_id=app`
    )
  })

  it("returns null when there is no pending request", () => {
    expect(getValidReturnTo("?foo=bar")).toBeNull()
    expect(getValidReturnTo("")).toBeNull()
  })

  it("returns null for a rejected destination", () => {
    expect(getValidReturnTo(search("https://evil.example.com/"))).toBeNull()
  })

  it("says out loud in dev why a returnTo was dropped", () => {
    // A silent rejection is indistinguishable from no pending request at all,
    // which is exactly what the resume bug looked like from the outside.
    getValidReturnTo(search("https://evil.example.com/"))
    expect(console.warn).toHaveBeenCalledWith(
      expect.stringContaining("returnTo rejected")
    )
  })
})

describe("getReturnToClientId", () => {
  it("extracts the client id for branding lookup", () => {
    expect(getReturnToClientId(`${AUTHORIZE}?client_id=my-app`)).toBe("my-app")
  })

  it("returns null without a pending request", () => {
    expect(getReturnToClientId(null)).toBeNull()
  })
})
