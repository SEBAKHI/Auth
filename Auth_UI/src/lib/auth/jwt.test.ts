import { describe, expect, it } from "vitest"

import { claimToArray, decodeJwt, isTokenExpired } from "./jwt"

function makeToken(payload: Record<string, unknown>): string {
  const encode = (obj: unknown) =>
    btoa(JSON.stringify(obj))
      .replace(/=/g, "")
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
  return `${encode({ alg: "RS256" })}.${encode(payload)}.signature`
}

describe("decodeJwt", () => {
  it("decodes a valid token payload", () => {
    const token = makeToken({ sub: "123", email: "a@b.com" })
    const claims = decodeJwt(token)
    expect(claims?.sub).toBe("123")
    expect(claims?.email).toBe("a@b.com")
  })

  it("returns null for a malformed token", () => {
    expect(decodeJwt("not-a-token")).toBeNull()
  })
})

describe("claimToArray", () => {
  it("wraps a single value", () => {
    expect(claimToArray("users:read")).toEqual(["users:read"])
  })
  it("passes arrays through", () => {
    expect(claimToArray(["a", "b"])).toEqual(["a", "b"])
  })
  it("returns empty for undefined", () => {
    expect(claimToArray(undefined)).toEqual([])
  })
})

describe("isTokenExpired", () => {
  it("is true for a past expiry", () => {
    expect(isTokenExpired({ exp: Math.floor(Date.now() / 1000) - 100 })).toBe(
      true
    )
  })
  it("is false for a future expiry", () => {
    expect(isTokenExpired({ exp: Math.floor(Date.now() / 1000) + 1000 })).toBe(
      false
    )
  })
})
